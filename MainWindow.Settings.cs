using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private SettingsPageStore? _settingsStore;
    private SettingsPageState SettingsState => _settingsStore!.State;

    private void CreateSettingsStore()
    {
        _settingsStore = new SettingsPageStore { Edited = GeneralSettingsEdited,
            ReadAliyunKey = secret => secret ? _aliyunAccessKeySecret : _aliyunAccessKeyId,
            AliyunKeyChanged = AliyunKeyChanged,
            ReadProviderKey = () => _settings is null ? string.Empty : GetProviderApiKey(ManagedProvider),
            ProviderKeyChanged = ProviderKeyChanged,
            DraftKeyChanged = value => _providerDraftKey = value,
            ProviderAction = HandleProviderAction,
            DeleteModel = DeleteProviderModel,
            DialogClosed = CloseProviderDialog,
            RefreshLocalModels = () => RefreshLocalModelsButton_Click(this, new RoutedEventArgs()),
            RefreshOfflineModels = async () => await RefreshOfflineModelsAsync(showError: true),
            UseOrDownloadOfflineModel = async id => { if (_offlineModelItems.TryGetValue(id, out var model)) await UseOrDownloadOfflineModelAsync(model); },
            DeleteOfflineModel = id => { if (_offlineModelItems.TryGetValue(id, out var model)) DeleteOfflineModel(model); } };
        var host = CreateReactorHost();
        SettingsPageHost.Content = host;
        host.Mount(new SettingsPageView(_settingsStore));
    }

    private void InitializeGeneralSettings()
    {
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        _settingsStore!.Update(state => state with
        {
            AiEnabled = _settings.AiTranslationEnabled, RememberKeys = _settings.RememberKeys,
            Streaming = _settings.AiStreamingEnabled, PromptStyle = Math.Clamp(_settings.AiPromptStyleIndex, 0, 3),
            LanguageDetails = _settings.AiIncludeLanguageDetails, CustomPrompt = _settings.CustomPrompt,
            Providers = AiProviders.ToArray(), LocalSourceIndex = Math.Clamp(_settings.LocalTranslationSourceIndex, 0, 1),
            ApiEnabled = _settings.ApiTranslationEnabled, RememberAliyun = _settings.RememberAliyunKeys,
            UiLanguageIndex = _settings.UiLanguage switch { "zh-CN" => 1, "en-US" => 2, _ => 0 },
            ThemeIndex = Math.Clamp(_settings.ThemeIndex, 0, 2),
            Mica = _settings.MicaBackdropEnabled, AlwaysOnTop = _settings.AlwaysOnTop,
            EnterToTranslate = _settings.EnterToTranslate, Realtime = _settings.RealtimeTranslationEnabled,
            HistoryLimitIndex = Array.IndexOf(TranslationHistoryLimits, _settings.TranslationHistoryLimit),
        });
    }

    private void AliyunKeyChanged(string value, bool secret)
    {
        if (_settings is null || _suppressEvents) return;
        if (secret) _aliyunAccessKeySecret = value; else _aliyunAccessKeyId = value;
        UpdateUiState();
        if (SettingsState.RememberAliyun && !string.IsNullOrEmpty(value))
            SaveKeyToVault(secret ? AliyunAccessKeySecretResource : AliyunAccessKeyIdResource, value);
    }

    private void GeneralSettingsEdited(SettingsPageState before, SettingsPageState after)
    {
        if (_settings is null || _suppressEvents) return;
        _settings.UiLanguage = after.UiLanguageIndex switch { 1 => "zh-CN", 2 => "en-US", _ => "system" };
        _settings.TranslationHistoryLimit = TranslationHistoryLimits[Math.Clamp(after.HistoryLimitIndex, 0, 3)];
        _settings.EnterToTranslate = after.EnterToTranslate;
        ApplyEnterToTranslateInputMode();
        if (before.HistoryLimitIndex != after.HistoryLimitIndex)
        {
            TrimTranslationHistory();
            RefreshTranslationHistory();
        }
        if (before.ImageProviderIndex != after.ImageProviderIndex && after.ImageProviderIndex >= 0 && after.ImageProviderIndex < after.Providers.Count)
        {
            var provider = after.Providers[after.ImageProviderIndex];
            _settings.ImageProviderName = provider;
            RefreshImageModelOptions(_settings.ImageSelectedModels.GetValueOrDefault(provider));
        }
        else if (before.ImageModelIndex != after.ImageModelIndex && after.ImageModelIndex >= 0 && after.ImageModelIndex < after.ImageModels.Count)
            SetImageModelSelection(after.ImageModels[after.ImageModelIndex]);
        if (before.ManagedProviderIndex != after.ManagedProviderIndex) RefreshManagedProvider();
        if (before.ProviderEndpoint != after.ProviderEndpoint)
            _settings.Endpoints[ManagedProvider] = SettingsState.ProviderEndpoint.Trim();
        if (before.AiProviderIndex != after.AiProviderIndex && after.AiProviderIndex >= 0 && after.AiProviderIndex < after.Providers.Count)
            SelectAiProvider(after.Providers[after.AiProviderIndex]);
        if (before.RememberKeys != after.RememberKeys) AiRememberKeyCheckBox_Changed(this, new RoutedEventArgs());
        if (before.AiEnabled != after.AiEnabled) SetTranslationModeEnabled(1, after.AiEnabled);
        if (before.ApiEnabled != after.ApiEnabled) SetTranslationModeEnabled(0, after.ApiEnabled);
        if (before.RememberAliyun != after.RememberAliyun) AliyunRememberKeyCheckBox_Changed(this, new RoutedEventArgs());
        SaveSettings();
        if (before.ThemeIndex != after.ThemeIndex) ApplyTheme();
        if (before.Mica != after.Mica) ApplyMicaBackdrop();
        if (before.AlwaysOnTop != after.AlwaysOnTop) ApplyAlwaysOnTop();
        if (before.UiLanguageIndex != after.UiLanguageIndex)
            ShowInfo(Localization.Text("语言设置已保存，请重新启动应用以应用更改。"), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
        if (!before.EnterToTranslate && after.EnterToTranslate)
            ShowInfo(Localization.Text("已开启回车键翻译，按 Shift+Enter 换行。"), Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational);
    }
}

