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

    private string ManagedProvider => ProviderManagementComboBox.SelectedItem as string ?? AiProviders[0];

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
        var selected = preferredProvider ?? ProviderManagementComboBox.SelectedItem as string ?? _currentProvider;
        ProviderManagementComboBox.ItemsSource = AiProviders.ToList();
        ProviderManagementComboBox.SelectedItem = AiProviders.FirstOrDefault(provider =>
            string.Equals(provider, selected, StringComparison.OrdinalIgnoreCase)) ?? AiProviders[0];
        RefreshManagedProvider();
    }

    private void RefreshManagedProvider()
    {
        var suppressed = _suppressEvents;
        _suppressEvents = true;
        try
        {
            ProviderEndpointTextBox.Text = GetProviderEndpoint(ManagedProvider);
            ProviderEndpointTextBox.PlaceholderText = GetProviderProfile(ManagedProvider).DefaultEndpoint;
            ProviderApiKeyPasswordBox.Password = GetProviderApiKey(ManagedProvider);
            ProviderModelsListView.ItemsSource = GetAvailableAiModels(GetProviderProfile(ManagedProvider)).ToList();
        }
        finally
        {
            _suppressEvents = suppressed;
        }
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
            AiProviderComboBox.ItemsSource = AiProviders.ToList();
            _currentProvider = AiProviders.FirstOrDefault(provider =>
                string.Equals(provider, _currentProvider, StringComparison.OrdinalIgnoreCase)) ?? AiProviders[0];
            AiProviderComboBox.SelectedItem = _currentProvider;
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

    private void ProviderManagementComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ProviderManagementComboBox.SelectedItem is not string)
        {
            return;
        }
        _suppressEvents = true;
        try { RefreshManagedProvider(); }
        finally { _suppressEvents = false; }
    }

    private void ProviderEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || ProviderManagementComboBox.SelectedItem is not string provider)
        {
            return;
        }
        _settings.Endpoints[provider] = ProviderEndpointTextBox.Text.Trim();
        SaveSettings();
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

    private async void AddProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeProviderCatalog()) return;
        var nameBox = new TextBox { Header = Localization.Text("供应商名称") };
        var endpointBox = new TextBox
        {
            Header = Localization.Text("接口地址"),
            PlaceholderText = "https://example.com/v1",
        };
        var modelBox = new TextBox { Header = Localization.Text("模型名称") };
        var keyBox = new PasswordBox { Header = "API Key" };
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(nameBox);
        panel.Children.Add(endpointBox);
        panel.Children.Add(modelBox);
        panel.Children.Add(keyBox);
        panel.Children.Add(errorText);
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("添加供应商"),
            Content = panel,
            PrimaryButtonText = Localization.Text("添加"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var name = nameBox.Text.Trim();
            var validName = name.Length > 0 && !string.Equals(name, "本地 AI", StringComparison.OrdinalIgnoreCase) && !name.Contains(':')
                && !AiProviders.Contains(name, StringComparer.OrdinalIgnoreCase);
            var validEndpoint = Uri.TryCreate(endpointBox.Text.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
            if (!validName || !validEndpoint || string.IsNullOrWhiteSpace(modelBox.Text))
            {
                args.Cancel = true;
                errorText.Text = Localization.Text("请填写唯一的供应商名称（不含冒号）、有效的 HTTP/HTTPS 接口地址和模型名称。");
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var provider = nameBox.Text.Trim();
        var model = modelBox.Text.Trim();
        AiProviders.Add(provider);
        _settings.Endpoints[provider] = endpointBox.Text.Trim();
        _settings.AvailableModels[provider] = new() { model };
        _settings.Models[provider] = model;
        _settings.ImageSelectedModels[provider] = model;
        if (!string.IsNullOrWhiteSpace(keyBox.Password))
        {
            SetProviderApiKey(provider, keyBox.Password.Trim());
        }
        RefreshProviderConsumers(provider);
    }

    private async void DeleteProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeProviderCatalog()) return;
        if (AiProviders.Count <= 1)
        {
            ShowInfo(Localization.Text("至少需要保留一个供应商。"), InfoBarSeverity.Warning);
            return;
        }
        var provider = ManagedProvider;
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("删除供应商"),
            Content = Localization.Text("删除 {0} 及其模型配置和保存的密钥？", Localization.ProviderName(provider)),
            PrimaryButtonText = Localization.Text("删除"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
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

    private async void AddProviderModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanChangeProviderCatalog()) return;
        var provider = ManagedProvider;
        var modelBox = new TextBox
        {
            Header = Localization.Text("模型名称"),
            PlaceholderText = Localization.Text("输入自定义模型名称"),
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(modelBox);
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("添加自定义模型"),
            Content = panel,
            PrimaryButtonText = Localization.Text("添加"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(modelBox.Text))
            {
                args.Cancel = true;
                modelBox.PlaceholderText = Localization.Text("请输入要添加的模型名称。");
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ProviderCatalog.AddModels(_settings, provider, new[] { modelBox.Text });
        RefreshProviderConsumers(provider);
    }

    private async void FetchProviderModelsButton_Click(object sender, RoutedEventArgs e)
        => await FetchProviderModelsAsync();

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
        ProviderCatalogExpander.IsEnabled = false;
        FetchProviderModelsButtonText.Text = Localization.Text("获取中…");
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

            var modelList = new ListView
            {
                ItemsSource = available,
                ItemTemplate = (DataTemplate)WindowRoot.Resources["ProviderModelSelectionTemplate"],
                SelectionMode = ListViewSelectionMode.Multiple,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 360,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(modelList, ScrollBarVisibility.Disabled);
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = Localization.Text("仅显示尚未保存的模型，可多选。现有模型会保留。"),
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(modelList);
            var dialog = new ContentDialog
            {
                XamlRoot = WindowRoot.XamlRoot,
                Title = Localization.Text("选择要保存的模型 — {0}", Localization.ProviderName(provider)),
                Content = panel,
                PrimaryButtonText = Localization.Text("保存"),
                CloseButtonText = Localization.Text("取消"),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
            };
            modelList.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = modelList.SelectedItems.Count > 0;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            // Another translation may have started while the model list was loading.
            _isFetchingProviderModels = false;
            if (!CanChangeProviderCatalog()) return;
            var added = ProviderCatalog.AddModels(_settings, provider, modelList.SelectedItems.Cast<string>());
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
            ProviderCatalogExpander.IsEnabled = true;
            FetchProviderModelsButtonText.Text = Localization.Text("获取模型");
            UpdateUiState();
        }
    }

    private void DeleteProviderModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string model } || !CanChangeProviderCatalog()) return;
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

    private void ProviderApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || ProviderManagementComboBox.SelectedItem is not string provider)
        {
            return;
        }
        SetProviderApiKey(provider, ProviderApiKeyPasswordBox.Password.Trim());
        UpdateFileModeAvailability();
    }
}
