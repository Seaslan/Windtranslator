using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Credentials;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private bool _isFetchingProviderModels;

    private string ManagedProvider => SettingsState.Providers.ElementAtOrDefault(SettingsState.ManagedProviderIndex) ?? AiProviders[0];

    private void MigrateProviderCredentials()
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in AiProviders)
        {
            if (_settings.ProviderKeysMigrated && !_settings.LegacyProviderAliases.ContainsKey(provider))
            {
                continue;
            }
            var key = GetProviderApiKey(provider);
            if (string.IsNullOrWhiteSpace(key))
            {
                var imported = _settings.LegacyProviderAliases.TryGetValue(provider, out var legacyProvider);
                key = GetLegacyProviderApiKey(imported ? legacyProvider! : provider,
                    _settings.ImageSelectedModels.GetValueOrDefault(provider), preferImage: imported);
            }
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys[provider] = key;
            }
        }
        foreach (var (provider, key) in keys)
        {
            SetProviderApiKey(provider, key);
        }
        _settings.ProviderKeysMigrated = true;
        _settings.LegacyProviderAliases.Clear();
    }

    private string GetLegacyProviderApiKey(string provider, string? imageModel, bool preferImage)
    {
        var models = new[] {
            preferImage ? imageModel : _settings.Models.GetValueOrDefault(provider),
            imageModel
        }.Concat(GetAvailableAiModels(GetProviderProfile(provider)))
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var credentials = models.SelectMany(model => preferImage
            ? new[] { GetImageModelCredentialKey(provider, model!), GetModelCredentialKey(provider, model!) }
            : new[] { GetModelCredentialKey(provider, model!), GetImageModelCredentialKey(provider, model!) });
        foreach (var credential in credentials)
        {
            if (_apiKeys.TryGetValue(credential, out var key) && !string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
            key = _settings.RememberKeys ? LoadKeyFromVault(credential) : null;
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
        }
        return GetProviderApiKey(provider);
    }

    private void RefreshManagedProviderOptions(string? preferredProvider = null)
    {
        var selected = preferredProvider ?? SettingsState.Providers.ElementAtOrDefault(SettingsState.ManagedProviderIndex) ?? _currentProvider;
        var provider = AiProviders.FirstOrDefault(item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase)) ?? AiProviders[0];
        _settingsStore!.Update(state => state with { Providers = AiProviders.ToArray(), ManagedProviderIndex = AiProviders.IndexOf(provider) });
        RefreshManagedProvider();
    }

    private void RefreshManagedProvider()
    {
        _settingsStore!.Update(state => state with
        {
            ProviderEndpoint = GetProviderEndpoint(ManagedProvider),
            ProviderEndpointPlaceholder = GetProviderProfile(ManagedProvider).DefaultEndpoint,
            ManagedModels = GetAvailableAiModels(GetProviderProfile(ManagedProvider)).ToArray(),
            CredentialRevision = state.CredentialRevision + 1,
        });
    }

    private void RefreshProviderConsumers(string? managedProvider = null, bool resetSelections = false)
    {
        var suppressed = _suppressEvents;
        _suppressEvents = true;
        try
        {
            var imageProvider = resetSelections ? _settings.ImageProviderName : GetImageProvider();
            var aiModel = resetSelections ? _settings.Models.GetValueOrDefault(_currentProvider) : GetSelectedAiModel();
            var imageModel = resetSelections ? _settings.ImageModel : ImageTranslationState.Models.ElementAtOrDefault(ImageTranslationState.ModelIndex);
            _settingsStore!.Update(state => state with { Providers = AiProviders.ToArray() });
            _currentProvider = AiProviders.FirstOrDefault(provider =>
                string.Equals(provider, _currentProvider, StringComparison.OrdinalIgnoreCase)) ?? AiProviders[0];
            _settingsStore!.Update(state => state with { AiProviderIndex = AiProviders.IndexOf(_currentProvider) });
            RefreshAiModelOptions(GetProviderProfile(_currentProvider), aiModel);
            _settings.Models[_currentProvider] = GetSelectedAiModel();

            ImageProviderComboBox.ItemsSource = AiProviders.ToList();
            ImageProviderComboBox.SelectedItem = AiProviders.FirstOrDefault(provider =>
                string.Equals(provider, imageProvider, StringComparison.OrdinalIgnoreCase)) ?? AiProviders[0];
            _settings.ImageProviderName = GetImageProvider();
            RefreshImageModelOptions(imageModel);
            RefreshManagedProviderOptions(managedProvider);
        }
        finally
        {
            _suppressEvents = suppressed;
        }
        SaveSettings();
        UpdateFileModeAvailability();
    }



    private bool CanChangeProviderCatalog()
    {
        if (_isFetchingProviderModels)
        {
            ShowInfo(Localization.Text("请等待模型列表获取完成后再修改供应商或模型。"), InfoBarSeverity.Warning);
            return false;
        }
        if (_isTranslating || _isSelectionTranslating || _isImageTranslating || _isFileTranslating || IsMiniTranslating)
        {
            ShowInfo(Localization.Text("请等待当前翻译完成后再修改供应商或模型。"), InfoBarSeverity.Warning);
            return false;
        }
        return true;
    }

    private void ResetProvidersButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeProviderCatalog()) return;
        try
        {
            // Include legacy model credentials and credentials of providers already deleted.
            // Machine translation AccessKeys belong to the separate API translation card.
            var vault = new PasswordVault();
            foreach (var credential in vault.RetrieveAll().Where(credential =>
                string.Equals(credential.Resource, KeyVaultResource, StringComparison.Ordinal)
                && !string.Equals(credential.UserName, AliyunAccessKeyIdResource, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(credential.UserName, AliyunAccessKeySecretResource, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                vault.Remove(credential);
            }
            _apiKeys.Clear();
            ProviderCatalog.ResetToDefaults(_settings);
            _currentProvider = _settings.ProviderName;
            RefreshProviderConsumers(_currentProvider, resetSelections: true);
            ShowInfo(Localization.Text("已恢复内置供应商和模型，并清除 API Key。"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("重置供应商失败：{0}", ex.Message), InfoBarSeverity.Error);
        }
    }





    private async Task FetchProviderModelsAsync()
    {
        if (!CanChangeProviderCatalog()) return;
        var provider = ManagedProvider;
        var endpoint = GetProviderEndpoint(provider);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            ShowInfo(Localization.Text("请填写有效的 HTTP/HTTPS 接口地址。"), InfoBarSeverity.Warning);
            return;
        }

        _isFetchingProviderModels = true;
        _settingsStore!.Update(state => state with { IsFetchingModels = true });
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var fetched = await _translationService.GetModelsAsync(endpoint, GetProviderApiKey(provider), timeout.Token);
            if (fetched.Count == 0)
            {
                ShowInfo(Localization.Text("供应商没有返回可用的模型。"), InfoBarSeverity.Warning);
                return;
            }

            var available = fetched.Except(GetAvailableAiModels(GetProviderProfile(provider)), StringComparer.OrdinalIgnoreCase).ToList();
            if (available.Count == 0)
            {
                ShowInfo(Localization.Text("获取到的模型均已保存。"), InfoBarSeverity.Informational);
                return;
            }

            if (await ShowProviderDialogAsync("fetched-models", provider, available) != ContentDialogResult.Primary) return;

            // Another translation may have started while the model list was loading.
            _isFetchingProviderModels = false;
            if (!CanChangeProviderCatalog()) return;
            var added = ProviderCatalog.AddModels(_settings, provider, SettingsState.SelectedFetchedModels);
            if (added == 0) return;
            RefreshProviderConsumers(provider);
            ShowInfo(Localization.Text("已添加 {0} 个模型。", added), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo(Localization.Text("获取模型列表超时，请稍后重试。"), InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isFetchingProviderModels = false;
            _settingsStore!.Update(state => state with { IsFetchingModels = false });
            UpdateUiState();
        }
    }

    private void DeleteProviderModel(string model)
    {
        if (!CanChangeProviderCatalog()) return;
        var provider = ManagedProvider;
        var models = GetAvailableAiModels(GetProviderProfile(provider));
        models.RemoveAll(item => string.Equals(item, model, StringComparison.OrdinalIgnoreCase));
        // Clean up old model credentials while retaining the shared provider key.
        RemoveProviderModelCredentials(provider, model);
        if (string.Equals(_settings.Models.GetValueOrDefault(provider), model, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Models[provider] = models.FirstOrDefault() ?? string.Empty;
        }
        if (string.Equals(_settings.ImageSelectedModels.GetValueOrDefault(provider), model, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ImageSelectedModels[provider] = models.FirstOrDefault() ?? string.Empty;
        }
        RefreshProviderConsumers(provider);
    }

    private void RemoveProviderModelCredentials(string provider, string model)
    {
        foreach (var credential in new[] { GetModelCredentialKey(provider, model), GetImageModelCredentialKey(provider, model) })
        {
            _apiKeys.Remove(credential);
            RemoveKeyFromVault(credential);
        }
    }

    private void ProviderKeyChanged(string value)
    {
        if (_settings is null || _suppressEvents || !SettingsState.CanEditProviderKey || value.Trim() == GetProviderApiKey(ManagedProvider)) return;
        SetProviderApiKey(ManagedProvider, value.Trim());
        UpdateFileModeAvailability();
    }

    private async Task AddProviderAsync()
    {
        if (!CanChangeProviderCatalog()) return;
        if (await ShowProviderDialogAsync("add-provider") != ContentDialogResult.Primary) return;
        var provider = SettingsState.DialogName.Trim();
        var model = SettingsState.DialogModel.Trim();
        AiProviders.Add(provider);
        _settings.Endpoints[provider] = SettingsState.DialogEndpoint.Trim();
        _settings.AvailableModels[provider] = new() { model };
        _settings.Models[provider] = model;
        _settings.ImageSelectedModels[provider] = model;
        if (!string.IsNullOrWhiteSpace(_providerDraftKey))
        {
            SetProviderApiKey(provider, _providerDraftKey.Trim());
        }
        _providerDraftKey = string.Empty;
        RefreshProviderConsumers(provider);
    }

    private async Task DeleteProviderAsync()
    {
        if (!CanChangeProviderCatalog()) return;
        if (AiProviders.Count <= 1)
        {
            ShowInfo(Localization.Text("至少需要保留一个供应商。"), InfoBarSeverity.Warning);
            return;
        }
        var provider = ManagedProvider;
        if (await ShowProviderDialogAsync("delete-provider", provider) != ContentDialogResult.Primary || !CanChangeProviderCatalog()) return;
        foreach (var model in GetAvailableAiModels(GetProviderProfile(provider)))
        {
            RemoveProviderModelCredentials(provider, model);
        }
        _apiKeys.Remove(provider);
        RemoveKeyFromVault(provider);
        AiProviders.Remove(provider);
        _settings.AvailableModels.Remove(provider);
        _settings.Endpoints.Remove(provider);
        _settings.Models.Remove(provider);
        _settings.ImageEndpoints.Remove(provider);
        _settings.ImageSelectedModels.Remove(provider);
        RefreshProviderConsumers();
    }

    private async Task AddProviderModelAsync()
    {
        if (!CanChangeProviderCatalog()) return;
        var provider = ManagedProvider;
        if (await ShowProviderDialogAsync("add-model", provider) != ContentDialogResult.Primary || !CanChangeProviderCatalog()) return;
        ProviderCatalog.AddModels(_settings, provider, new[] { SettingsState.DialogModel });
        RefreshProviderConsumers(provider);
    }

    private string _providerDraftKey = string.Empty;
    private TaskCompletionSource<ContentDialogResult>? _providerDialogCompletion;

    private Task<ContentDialogResult> ShowProviderDialogAsync(string kind, string? provider = null, IReadOnlyList<string>? models = null)
    {
        if (_providerDialogCompletion is not null) return Task.FromResult(ContentDialogResult.None);
        _providerDraftKey = string.Empty;
        _providerDialogCompletion = new TaskCompletionSource<ContentDialogResult>();
        _settingsStore!.Update(state => state with
        {
            ProviderDialog = kind, DialogProvider = provider ?? ManagedProvider,
            DialogName = string.Empty, DialogEndpoint = string.Empty, DialogModel = string.Empty,
            FetchedModels = models?.ToArray() ?? Array.Empty<string>(), SelectedFetchedModels = Array.Empty<string>(),
        });
        return _providerDialogCompletion.Task;
    }

    private void CloseProviderDialog(ContentDialogResult result)
    {
        var completion = _providerDialogCompletion;
        _providerDialogCompletion = null;
        _settingsStore!.Update(state => state with { ProviderDialog = string.Empty });
        completion?.TrySetResult(result);
        if (result != ContentDialogResult.Primary) _providerDraftKey = string.Empty;
    }

    private void HandleProviderAction(string action)
    {
        switch (action)
        {
            case "add-provider": _ = AddProviderAsync(); break;
            case "delete-provider": _ = DeleteProviderAsync(); break;
            case "add-model": _ = AddProviderModelAsync(); break;
            case "fetch-models": _ = FetchProviderModelsAsync(); break;
            case "reset": ResetProvidersButton_Click(this, new RoutedEventArgs()); break;
        }
    }
}
