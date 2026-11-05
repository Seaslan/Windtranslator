using Microsoft.UI.Xaml;
using System;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private SettingsPageStore? _settingsStore;
    private SettingsPageState SettingsState => _settingsStore!.State;

    private void CreateSettingsStore()
    {
        var legacy = SettingsCardsPanel;
        foreach (var entry in SettingsPageScrollViewer.Resources)
            legacy.Resources[entry.Key] = entry.Value;
        SettingsPageScrollViewer.Content = null;
        _settingsStore = new SettingsPageStore { LegacySections = legacy, Edited = GeneralSettingsEdited,
            ReadAliyunKey = secret => secret ? _aliyunAccessKeySecret : _aliyunAccessKeyId,
            AliyunKeyChanged = AliyunKeyChanged };
        var host = CreateReactorHost();
        SettingsPageHost.Content = host;
        host.Mount(new SettingsPageView(_settingsStore));
    }

    private void InitializeGeneralSettings()
    {
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        _settingsStore!.Update(state => state with
        {
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

