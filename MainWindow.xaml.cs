using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windtranslator.Models;
using Windtranslator.Services;
using WinRT.Interop;

namespace Windtranslator;

public sealed partial class MainWindow : Window
{
    private const string KeyVaultResource = "Windtranslator/ProviderKeys";
    private const string AliyunProviderName = "阿里云机器翻译";
    private const string AliyunServiceUrl = "http://mt.cn-hangzhou.aliyuncs.com/api/translate/web/ecommerce";
    private const string AliyunAccessKeyIdResource = "阿里云机器翻译:AccessKeyId";
    private const string AliyunAccessKeySecretResource = "阿里云机器翻译:AccessKeySecret";
    private const int DefaultWindowWidth = 960;
    private const int DefaultWindowHeight = 660;
    private const int MinimumWindowWidth = 420;
    private const int MinimumWindowHeight = 320;
    private const int MiniWindowWidth = 520;
    private const int MiniWindowHeight = 420;
    private const int MiniModeEnterWidth = 680;
    private const int MiniModeExitWidth = 760;
    private const double SettingsPanelMaxWidth = 760d;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmXButtonUp = 0x020C;
    private const uint WmSysKeyDown = 0x0104;
    private const int VkLeft = 0x25;
    private const byte VkLeftWindows = 0x5B;
    private const byte VkH = 0x48;
    private const uint KeyEventKeyUp = 0x0002;
    private const int XButton1 = 1;
    private const int GwlWndProc = -4;
    private static readonly int[] TranslationHistoryLimits = { 0, 5, 20, -1 };

    private List<string> AiProviders => _settings.ProviderNames!;

    private static readonly List<LanguageOption> Languages = new()
    {
        new LanguageOption("自动检测", "自动检测", null, "auto"),
        new LanguageOption("简体中文", "简体中文", "zh-CN", "zh"),
        new LanguageOption("繁体中文", "繁体中文", "zh-TW", "zh-tw"),
        new LanguageOption("英语", "英语", "en-US", "en"),
        new LanguageOption("日语", "日语", "ja-JP", "ja"),
        new LanguageOption("韩语", "韩语", "ko-KR", "ko"),
        new LanguageOption("法语", "法语", "fr-FR", "fr"),
        new LanguageOption("德语", "德语", "de-DE", "de"),
        new LanguageOption("西班牙语", "西班牙语", "es-ES", "es"),
        new LanguageOption("俄语", "俄语", "ru-RU", "ru"),
        new LanguageOption("葡萄牙语", "葡萄牙语", "pt-PT", "pt"),
        new LanguageOption("意大利语", "意大利语", "it-IT", "it"),
        new LanguageOption("阿拉伯语", "阿拉伯语", "ar-SA", "ar"),
        new LanguageOption("泰语", "泰语", "th-TH", "th"),
        new LanguageOption("越南语", "越南语", "vi-VN", "vi"),
        new LanguageOption("印尼语", "印尼语", "id-ID", "id"),
    };

    private readonly HttpClient _httpClient = new();
    private readonly TranslationService _translationService;
    private readonly BergamotTranslationService _bergamotTranslationService = new();
    private readonly OfflineModelService _offlineModelService;
    private readonly AliyunMachineTranslationService _machineTranslationService;
    private readonly SpeechOutputService _speechOutput = new();
    private readonly Dictionary<string, string> _apiKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings;

    private string _currentProvider = "DeepSeek";
    private string _aliyunAccessKeyId = string.Empty;
    private string _aliyunAccessKeySecret = string.Empty;
    private string _selectedOfflineModelPath = string.Empty;
    private bool _isTranslating;
    private bool _isImageTranslating;
    private bool _isOpeningSystemDictation;
    private bool _suppressEvents;
    private bool _suppressSidebarSync;
    private bool _isLoadingOfflineModels;
    private bool _isDownloadingOfflineModel;
    private double _offlineModelDownloadProgress;
    private string? _downloadingOfflineModelId;
    private List<OfflineTranslationModel> _onlineOfflineModels = new();
    private string _currentPageTag = "Home";
    private readonly Stack<string> _backStack = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueueTimer? _infoBarTimer;
    private readonly List<StorageFile> _selectedImageFiles = new();
    private bool _isMiniMode;
    private bool _windowLayoutUpdateQueued;
    private bool _isWindowClosed;
    private bool _isSelectionTranslating;
    private string? _pendingClipboardText;
    private uint _lastHandledClipboardSequenceNumber;
    private readonly WindowProcedure _windowProcedure;
    private nint _windowHandle;
    private nint _previousWindowProcedure;

    public MainWindow()
    {
        InitializeComponent();
        CreateAboutStore();
        CreateTranslationHistoryStore();
        CreateFavoritesStore();
        CreateFileTranslationStore();
        InstallSelectionTranslationCommands();
        _windowProcedure = WindowProcedureCallback;

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(TitleBarDragRegion);
        InstallMinimumSizeHandler();
        ResizeWindowInDips(DefaultWindowWidth, DefaultWindowHeight);
        AppWindow.Changed += AppWindow_Changed;

        _settings = AppSettingsStore.Load();
        ProviderCatalog.Initialize(_settings);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _translationService = new TranslationService(_httpClient);
        _machineTranslationService = new AliyunMachineTranslationService(_httpClient);
        _offlineModelService = new OfflineModelService(_httpClient);
        NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
        _speechOutput.PlaybackEnded += (_, _) => DispatcherQueue.TryEnqueue(() => SetSpeakingState(false));
        Closed += (_, _) =>
        {
            _isWindowClosed = true;
            CancelRealtimeTranslation();
            AppWindow.Changed -= AppWindow_Changed;
            NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
            Clipboard.ContentChanged -= Clipboard_ContentChanged;
            _speechOutput.Stop();
            RestoreWindowProcedure();
        };
        _infoBarTimer = DispatcherQueue.CreateTimer();
        _infoBarTimer.Interval = TimeSpan.FromSeconds(3);
        _infoBarTimer.Tick += (_, _) =>
        {
            _infoBarTimer?.Stop();
            StatusInfoBar.IsOpen = false;
        };

        InitializeUi();
        _lastHandledClipboardSequenceNumber = GetClipboardSequenceNumber();
        Clipboard.ContentChanged += Clipboard_ContentChanged;
    }

    private void InitializeUi()
    {
        _suppressEvents = true;
        UiLanguageComboBox.ItemsSource = new[] { Localization.Text("跟随系统"), "简体中文", "English" };
        UiLanguageComboBox.SelectedIndex = _settings.UiLanguage switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };

        ApiTranslationToggleSwitch.IsOn = _settings.ApiTranslationEnabled;
        AiTranslationToggleSwitch.IsOn = _settings.AiTranslationEnabled;
        UpdateTranslationToggleStateText();
        var initialModeIndex = Math.Clamp(_settings.ModeIndex, 0, 2);
        ModeComboBox.SelectedIndex = IsTranslationModeEnabled(initialModeIndex) ? initialModeIndex : 2;
        LocalTranslationSourceComboBox.ItemsSource = new[] { Localization.Text("本地接口"), Localization.Text("Mozilla Translations 模型") };
        LocalTranslationSourceComboBox.SelectedIndex = Math.Clamp(_settings.LocalTranslationSourceIndex, 0, 1);

        SourceLanguageComboBox.ItemsSource = Languages;
        ImageSourceLanguageComboBox.ItemsSource = Languages;
        InitializeImageModelSettings();
        SourceLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        ImageSourceLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        TargetLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        ImageTargetLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);

        var sourceIndex = Math.Clamp(_settings.SourceLanguageIndex, 0, Languages.Count - 1);
        var targetIndex = Math.Clamp(_settings.TargetLanguageIndex, 0, Languages.Count - 1);
        SourceLanguageComboBox.SelectedIndex = sourceIndex;
        ImageSourceLanguageComboBox.SelectedIndex = sourceIndex;
        InitializeFileTranslationLanguages(sourceIndex, Languages[targetIndex]);
        RefreshTargetLanguageOptions(
            TargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);
        RefreshTargetLanguageOptions(
            ImageTargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);

        CustomPromptTextBox.Text = _settings.CustomPrompt;
        AiPromptStyleComboBox.ItemsSource = new[] { Localization.Text("标准"), Localization.Text("正式"), Localization.Text("自然"), Localization.Text("简洁") };
        AiPromptStyleComboBox.SelectedIndex = Math.Clamp(_settings.AiPromptStyleIndex, 0, 3);
        UpdateAiPromptStyleDescription();
        AiIncludeLanguageDetailsCheckBox.IsChecked = _settings.AiIncludeLanguageDetails;
        AiStreamingToggleSwitch.IsOn = _settings.AiStreamingEnabled;
        AiRememberKeyCheckBox.IsChecked = _settings.RememberKeys;
        AliyunRememberKeyCheckBox.IsChecked = _settings.RememberAliyunKeys;
        ThemeComboBox.ItemsSource = new[] { Localization.Text("跟随系统"), Localization.Text("浅色"), Localization.Text("深色") };
        ThemeComboBox.SelectedIndex = Math.Clamp(_settings.ThemeIndex, 0, 2);
        MicaBackdropCheckBox.IsChecked = _settings.MicaBackdropEnabled;
        AlwaysOnTopToggleSwitch.IsOn = _settings.AlwaysOnTop;
        EnterToTranslateToggleSwitch.IsOn = _settings.EnterToTranslate;
        RealtimeTranslationToggleSwitch.IsOn = _settings.RealtimeTranslationEnabled;
        ApplyEnterToTranslateInputMode();
        UpdateEnterShortcutLabels();
        TranslationHistoryLimitComboBox.ItemsSource = new[] { Localization.Text("不保存翻译历史"), Localization.Text("5 条"), Localization.Text("20 条"), Localization.Text("无上限") };
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        TranslationHistoryLimitComboBox.SelectedIndex = Array.IndexOf(
            TranslationHistoryLimits,
            _settings.TranslationHistoryLimit);
        TrimTranslationHistory();
        RefreshTranslationHistory();
        RefreshOfflineModelList();
        RefreshFavorites();
        UpdateOfflineModelDownloadAvailability();

        _suppressEvents = false;

        ApplyTheme();
        ApplyMicaBackdrop();
        ApplyAlwaysOnTop();
        SidebarNavigationView.SelectedItem = HomeNavigationItem;
        NavigateTo("Home", addBackEntry: false);
        InitializeProviderSettings();
        _fileTranslationStore!.Update(state => state with { ModeIndex = ModeComboBox.SelectedIndex });
        UpdateTranslationModeVisibility();
        UpdateFileModeAvailability();
        UpdateCounts();
        UpdateUiState();
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        SaveSettings();
    }

    private void UiLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        _settings.UiLanguage = UiLanguageComboBox.SelectedIndex switch { 1 => "zh-CN", 2 => "en-US", _ => "system" };
        SaveSettings();
        ShowInfo(Localization.Text("语言设置已保存，请重新启动应用以应用更改。"), InfoBarSeverity.Informational);
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.ThemeIndex = Math.Max(0, ThemeComboBox.SelectedIndex);
        SaveSettings();
        ApplyTheme();
    }

    private void MicaBackdropCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.MicaBackdropEnabled = MicaBackdropCheckBox.IsChecked == true;
        ApplyMicaBackdrop();
        SaveSettings();
    }

    private void AlwaysOnTopToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.AlwaysOnTop = AlwaysOnTopToggleSwitch.IsOn;
        ApplyAlwaysOnTop();
        SaveSettings();
    }

    private void EnterToTranslateToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.EnterToTranslate = EnterToTranslateToggleSwitch.IsOn;
        ApplyEnterToTranslateInputMode();
        UpdateEnterShortcutLabels();
        SaveSettings();
        if (_settings.EnterToTranslate)
        {
            ShowInfo(Localization.Text("已开启回车键翻译，按 Shift+Enter 换行。"), InfoBarSeverity.Informational);
        }
    }

    private void UpdateEnterShortcutLabels()
    {
        EnterShortcutActionText.Text = Localization.Text(
            EnterToTranslateToggleSwitch.IsOn ? "翻译文本" : "插入换行");
        ShiftEnterShortcutRow.Visibility = EnterToTranslateToggleSwitch.IsOn
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyEnterToTranslateInputMode()
    {
        // Prevent TextBox's built-in AcceptsReturn handling from inserting a newline
        // before SourceTextBox_KeyDown can dispatch the translation command. Shift+Enter
        // is inserted explicitly by the handler while this mode is enabled.
        SourceTextBox.AcceptsReturn = !_settings.EnterToTranslate;
    }

    private void TranslationHistoryLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.TranslationHistoryLimit = TranslationHistoryLimitComboBox.SelectedIndex switch
        {
            0 => 0,
            1 => 5,
            2 => 20,
            3 => -1,
            _ => 20,
        };
        TrimTranslationHistory();
        RefreshTranslationHistory();
        SaveSettings();
    }

    private void InitializeProviderSettings()
    {
        _suppressEvents = true;

        AiProviderComboBox.ItemsSource = AiProviders.ToList();
        _currentProvider = AiProviders.FirstOrDefault(provider =>
            string.Equals(provider, _settings.ProviderName, StringComparison.OrdinalIgnoreCase))
            ?? AiProviders[0];
        AiProviderComboBox.SelectedItem = _currentProvider;

        var profile = GetProviderProfile(_currentProvider);

        var savedModel = _settings.Models.TryGetValue(_currentProvider, out var model)
            ? model.Trim()
            : null;
        RefreshAiModelOptions(profile, savedModel);

        if (_settings.RememberKeys)
        {
            var rememberedKey = LoadKeyFromVault(_currentProvider);
            if (!string.IsNullOrEmpty(rememberedKey))
            {
                _apiKeys[_currentProvider] = rememberedKey;
            }
        }

        if (_settings.RememberAliyunKeys)
        {
            if (string.IsNullOrEmpty(_aliyunAccessKeyId))
            {
                _aliyunAccessKeyId = LoadKeyFromVault(AliyunAccessKeyIdResource) ?? string.Empty;
            }

            if (string.IsNullOrEmpty(_aliyunAccessKeySecret))
            {
                _aliyunAccessKeySecret = LoadKeyFromVault(AliyunAccessKeySecretResource) ?? string.Empty;
            }
        }

        AliyunAccessKeyIdPasswordBox.Password = _aliyunAccessKeyId;
        AliyunAccessKeySecretPasswordBox.Password = _aliyunAccessKeySecret;

        LocalEndpointTextBox.Text = _settings.Endpoints.TryGetValue("本地 AI", out var localEndpoint)
            && !string.IsNullOrWhiteSpace(localEndpoint)
                ? localEndpoint
                : "http://localhost:11434/v1";
        LocalModelTextBox.Text = _settings.Models.TryGetValue("本地 AI", out var localModel)
            && !string.IsNullOrWhiteSpace(localModel)
                ? localModel
                : "qwen2.5:7b";
        _selectedOfflineModelPath = _settings.LocalModelPath;
        UpdateLocalTranslationSourceUi();
        MigrateProviderCredentials();
        RefreshManagedProviderOptions();

        _suppressEvents = false;
        SaveSettings();
    }

    private void AiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || AiProviderComboBox.SelectedItem is not string provider)
        {
            return;
        }

        _currentProvider = provider;
        _settings.ProviderName = provider;
        var profile = GetProviderProfile(provider);

        _suppressEvents = true;
        var savedModel = _settings.Models.TryGetValue(provider, out var model)
            ? model.Trim()
            : null;
        RefreshAiModelOptions(profile, savedModel);

        if (AiRememberKeyCheckBox.IsChecked == true)
        {
            var legacyKey = LoadKeyFromVault(provider);
            if (!string.IsNullOrEmpty(legacyKey))
            {
                _apiKeys[provider] = legacyKey;
            }
        }

        _suppressEvents = false;
        SaveSettings();
    }

    private void AiModelsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || string.IsNullOrEmpty(_currentProvider))
        {
            return;
        }

        if (AiModelsListView.SelectedItem is string model)
        {
            _settings.Models[_currentProvider] = model;
            SaveSettings();
        }
    }

    private string GetSelectedAiModel()
    {
        return AiModelsListView.SelectedItem?.ToString()?.Trim() ?? string.Empty;
    }

    private List<string> GetAvailableAiModels(ProviderProfile profile) =>
        ProviderCatalog.GetModels(_settings, profile.Name);

    private void RefreshAiModelOptions(ProviderProfile profile, string? preferredModel)
    {
        var models = GetAvailableAiModels(profile);
        AiModelsListView.ItemsSource = models.ToList();
        AiModelsListView.SelectedItem = models.FirstOrDefault(model =>
            string.Equals(model, preferredModel, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault();
    }

    private void ApiTranslationToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        SetTranslationModeEnabled(0, ApiTranslationToggleSwitch.IsOn);
    }

    private void AiTranslationToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        SetTranslationModeEnabled(1, AiTranslationToggleSwitch.IsOn);
    }

    private void SetTranslationModeEnabled(int modeIndex, bool isEnabled)
    {
        UpdateTranslationToggleStateText();

        if (!isEnabled)
        {
            var isHomeModeSelected = ModeComboBox.SelectedIndex == modeIndex;
            var isFileModeSelected = FileTranslationState.ModeIndex == modeIndex;

            if (isHomeModeSelected && _isTranslating)
            {
                _cts?.Cancel();
            }

            if (isFileModeSelected && _isFileTranslating)
            {
                _fileTranslationCts?.Cancel();
            }

            if (isHomeModeSelected)
            {
                ModeComboBox.SelectedIndex = 2;
            }

            if (isFileModeSelected)
            {
                _fileTranslationStore!.Update(state => state with { ModeIndex = 2 });
            }
        }

        UpdateTranslationModeVisibility();
        UpdateFileModeAvailability();
        SaveSettings();
    }

    private void UpdateTranslationToggleStateText()
    {
        ApiTranslationStateText.Text = ApiTranslationToggleSwitch.IsOn ? Localization.Text("开") : Localization.Text("关");
        AiTranslationStateText.Text = AiTranslationToggleSwitch.IsOn ? Localization.Text("开") : Localization.Text("关");
    }

    private bool IsTranslationModeEnabled(int modeIndex) => modeIndex switch
    {
        0 => ApiTranslationToggleSwitch.IsOn,
        1 => AiTranslationToggleSwitch.IsOn,
        _ => true,
    };

    private void UpdateTranslationModeVisibility()
    {
        UpdateModeItems(ModeComboBox);
        UpdateFileModeAvailability();

        void UpdateModeItems(ComboBox comboBox)
        {
            var items = comboBox.Items.OfType<ComboBoxItem>().ToList();
            for (var index = 0; index < items.Count; index++)
            {
                var isEnabled = IsTranslationModeEnabled(index);
                items[index].Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                items[index].IsEnabled = isEnabled;
            }
        }
    }

    private static string GetModelCredentialKey(string provider, string model) =>
        $"{provider}:Model:{model}";

    private static string GetImageModelCredentialKey(string provider, string model) =>
        $"Image:{provider}:Model:{model}";

    private string GetProviderApiKey(string provider)
    {
        if (_apiKeys.TryGetValue(provider, out var key))
        {
            return key;
        }
        key = _settings.RememberKeys ? LoadKeyFromVault(provider) : null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _apiKeys[provider] = key;
        }
        return key ?? string.Empty;
    }

    private void SetProviderApiKey(string provider, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _apiKeys.Remove(provider);
            RemoveKeyFromVault(provider);
            return;
        }

        _apiKeys[provider] = apiKey;
        if (_settings.RememberKeys)
        {
            SaveKeyToVault(provider, apiKey);
        }
    }

    private void AiRememberKeyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        // Load provider credentials before turning persistence off, to keep this session working.
        foreach (var provider in AiProviders)
        {
            _ = GetProviderApiKey(provider);
        }
        _settings.RememberKeys = AiRememberKeyCheckBox.IsChecked == true;
        _settings.RememberImageKeys = _settings.RememberKeys;
        foreach (var provider in AiProviders)
        {
            if (_settings.RememberKeys)
            {
                if (_apiKeys.TryGetValue(provider, out var key) && !string.IsNullOrWhiteSpace(key))
                {
                    SaveKeyToVault(provider, key);
                }
            }
            else
            {
                RemoveKeyFromVault(provider);
                foreach (var model in GetAvailableAiModels(GetProviderProfile(provider)))
                {
                    RemoveProviderModelCredentials(provider, model);
                }
            }
        }
        SaveSettings();
    }

    private void AliyunRememberKeyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.RememberAliyunKeys = AliyunRememberKeyCheckBox.IsChecked == true;
        SaveSettings();

        if (_settings.RememberAliyunKeys)
        {
            if (!string.IsNullOrEmpty(_aliyunAccessKeyId))
            {
                SaveKeyToVault(AliyunAccessKeyIdResource, _aliyunAccessKeyId);
            }

            if (!string.IsNullOrEmpty(_aliyunAccessKeySecret))
            {
                SaveKeyToVault(AliyunAccessKeySecretResource, _aliyunAccessKeySecret);
            }
        }
        else
        {
            RemoveKeyFromVault(AliyunAccessKeyIdResource);
            RemoveKeyFromVault(AliyunAccessKeySecretResource);
        }
    }

    private void LocalEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.Endpoints["本地 AI"] = LocalEndpointTextBox.Text.Trim();
        SaveSettings();
    }

    private void LocalModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.Models["本地 AI"] = LocalModelTextBox.Text.Trim();
        SaveSettings();
    }

    private void LocalTranslationSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.LocalTranslationSourceIndex = Math.Clamp(LocalTranslationSourceComboBox.SelectedIndex, 0, 1);
        UpdateLocalTranslationSourceUi();
        SaveSettings();
    }

    private void UpdateLocalTranslationSourceUi()
    {
        var useModel = LocalTranslationSourceComboBox.SelectedIndex == 1;
        LocalEndpointPanel.Visibility = useModel ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NetworkInformation_NetworkStatusChanged(object sender)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateOfflineModelDownloadAvailability();
            if (HasInternetAccess() && _onlineOfflineModels.Count == 0 && !_isLoadingOfflineModels)
            {
                _ = RefreshOfflineModelsAsync(showError: false);
            }
        });
    }

    private async void RefreshOfflineModelsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshOfflineModelsAsync(showError: true);
    }

    private async Task RefreshOfflineModelsAsync(bool showError)
    {
        if (!HasInternetAccess())
        {
            UpdateOfflineModelDownloadAvailability();
            if (showError)
            {
                ShowInfo(Localization.Text("网络不可用，无法获取离线语言模型。"), InfoBarSeverity.Warning);
            }

            return;
        }

        _isLoadingOfflineModels = true;
        UpdateOfflineModelDownloadAvailability();
        try
        {
            _onlineOfflineModels = (await _offlineModelService.GetAvailableModelsAsync(CancellationToken.None))
                .Where(IsOfflineModelSupportedOnHomePage)
                .ToList();
            OfflineModelsStatusTextBlock.Text = Localization.Text("已获取 {0} 个可下载模型", _onlineOfflineModels.Count);
            RefreshOfflineModelList();
        }
        catch (Exception ex)
        {
            OfflineModelsStatusTextBlock.Text = Localization.Text("无法获取可下载模型");
            if (showError)
            {
                ShowInfo(Localization.Text("获取离线模型失败：") + ex.Message, InfoBarSeverity.Error);
            }
        }
        finally
        {
            _isLoadingOfflineModels = false;
            UpdateOfflineModelDownloadAvailability();
        }
    }

    private async void OfflineModelPrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: OfflineTranslationModel model })
        {
            return;
        }

        if (model.IsInstalled)
        {
            UseOfflineModel(model);
            return;
        }

        if (!HasInternetAccess())
        {
            ShowInfo(Localization.Text("网络不可用，无法下载离线语言模型。"), InfoBarSeverity.Warning);
            return;
        }

        _isDownloadingOfflineModel = true;
        _downloadingOfflineModelId = model.Id;
        _offlineModelDownloadProgress = 0;
        RefreshOfflineModelList();
        try
        {
            var lastReportedPercent = -1;
            var progress = new Progress<double>(value =>
            {
                var percent = (int)(value * 100);
                if (percent == lastReportedPercent)
                {
                    return;
                }

                lastReportedPercent = percent;
                _offlineModelDownloadProgress = value;
                RefreshOfflineModelList();
            });
            await _offlineModelService.DownloadAsync(model, progress, CancellationToken.None);
            RefreshOfflineModelList();
            var installed = GetSupportedInstalledOfflineModels()
                .FirstOrDefault(item => item.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
            if (installed is not null)
            {
                UseOfflineModel(installed);
            }

            ShowInfo(Localization.Text("离线语言模型下载完成。"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("下载离线语言模型失败：") + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isDownloadingOfflineModel = false;
            _downloadingOfflineModelId = null;
            _offlineModelDownloadProgress = 0;
            RefreshOfflineModelList();
        }
    }

    private void DeleteOfflineModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: OfflineTranslationModel model } || !model.IsInstalled)
        {
            return;
        }

        if (_isTranslating && PathsEqual(_selectedOfflineModelPath, model.LocalDirectory))
        {
            ShowInfo(Localization.Text("当前离线模型正在翻译，完成后再删除。"), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            _offlineModelService.Delete(model);
            if (PathsEqual(_selectedOfflineModelPath, model.LocalDirectory))
            {
                _selectedOfflineModelPath = string.Empty;
            }

            RefreshOfflineModelList();
            ShowInfo(Localization.Text("已删除离线语言模型。"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("删除离线语言模型失败：") + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void UseOfflineModel(OfflineTranslationModel model)
    {
        if (string.IsNullOrWhiteSpace(model.LocalDirectory))
        {
            return;
        }

        _suppressEvents = true;
        ModeComboBox.SelectedIndex = 2;
        LocalTranslationSourceComboBox.SelectedIndex = 1;
        _selectedOfflineModelPath = model.LocalDirectory;
        SelectModelLanguages(model);
        _suppressEvents = false;
        UpdateLocalTranslationSourceUi();
        SaveSettings();
        ShowInfo(Localization.Text("已选择 {0} 离线模型。", model.Title), InfoBarSeverity.Informational);
        NavigateTo("Home");
    }

    private void SelectModelLanguages(OfflineTranslationModel model)
    {
        var sourceCode = ToApplicationLanguageCode(model.SourceLanguageCode);
        var targetCode = ToApplicationLanguageCode(model.TargetLanguageCode);
        var source = Languages.FirstOrDefault(language => language.ApiCode == sourceCode);
        var target = Languages.FirstOrDefault(language => language.ApiCode == targetCode);
        if (source is null || target is null)
        {
            return;
        }

        SourceLanguageComboBox.SelectedItem = source;
        RefreshTargetLanguageOptions(TargetLanguageComboBox, source, target);
    }

    private void RefreshOfflineModelList()
    {
        var installed = GetSupportedInstalledOfflineModels()
            .ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var models = new List<OfflineTranslationModel>();
        foreach (var onlineModel in _onlineOfflineModels)
        {
            installed.TryGetValue(onlineModel.Id, out var installedModel);
            models.Add(CreateOfflineModelListItem(
                new OfflineTranslationModel(
                    onlineModel.Id,
                    onlineModel.SourceLanguageCode,
                    onlineModel.TargetLanguageCode,
                    onlineModel.SizeBytes,
                    onlineModel.Files,
                    installedModel?.LocalDirectory)));
        }

        models.AddRange(installed.Values
            .Where(model => _onlineOfflineModels.All(online => !online.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(CreateOfflineModelListItem));
        OfflineModelsHeaderTextBlock.Text = Localization.Text("离线语言模型（已下载 {0} 个）", installed.Count);
        OfflineModelsListView.ItemsSource = models
            .OrderByDescending(model => model.IsInstalled)
            .ThenBy(model => model.Title, StringComparer.CurrentCulture)
            .ToList();
    }

    private OfflineTranslationModel CreateOfflineModelListItem(OfflineTranslationModel model)
    {
        model.Title = GetLanguageDisplayName(model.SourceLanguageCode) + " -> " + GetLanguageDisplayName(model.TargetLanguageCode);
        var size = FormatModelSize(model.SizeBytes);
        if (_isDownloadingOfflineModel && model.Id.Equals(_downloadingOfflineModelId, StringComparison.OrdinalIgnoreCase))
        {
            model.Details = Localization.Text("正在下载 ") + (int)(_offlineModelDownloadProgress * 100) + "% · " + size;
        }
        else
        {
            model.Details = (model.IsInstalled ? Localization.Text("已下载") : Localization.Text("可下载")) + " · " + size;
        }

        model.CanDownload = model.IsInstalled || (HasInternetAccess() && !_isLoadingOfflineModels && !_isDownloadingOfflineModel);
        return model;
    }

    private void UpdateOfflineModelDownloadAvailability()
    {
        var online = HasInternetAccess();
        RefreshOfflineModelsButton.IsEnabled = online && !_isLoadingOfflineModels && !_isDownloadingOfflineModel;
        if (!online)
        {
            OfflineModelsStatusTextBlock.Text = Localization.Text("网络不可用，已下载 {0} 个模型仍可使用", GetSupportedInstalledOfflineModels().Count);
        }
        else if (_isLoadingOfflineModels)
        {
            OfflineModelsStatusTextBlock.Text = Localization.Text("正在获取可下载模型...");
        }
        else if (!IsBergamotRuntimeAvailable())
        {
            OfflineModelsStatusTextBlock.Text = Localization.Text("已可下载模型；本地翻译引擎尚未随当前应用发布");
        }

        RefreshOfflineModelList();
    }

    private static bool HasInternetAccess() => NetworkInformation.GetInternetConnectionProfile()?
        .GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

    private static bool IsBergamotRuntimeAvailable()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => string.Empty,
        };
        return !string.IsNullOrEmpty(architecture) && File.Exists(Path.Combine(
            AppContext.BaseDirectory,
            "Runtime",
            "Bergamot",
            architecture,
            "translator-cli.exe"));
    }

    private static string ToApplicationLanguageCode(string code) => code.ToLowerInvariant() switch
    {
        "zh_hant" => "zh-tw",
        _ => code.ToLowerInvariant(),
    };

    private static bool IsOfflineModelSupportedOnHomePage(OfflineTranslationModel model)
    {
        var sourceCode = ToApplicationLanguageCode(model.SourceLanguageCode);
        var targetCode = ToApplicationLanguageCode(model.TargetLanguageCode);
        return sourceCode != "auto"
            && targetCode != "auto"
            && !string.Equals(sourceCode, targetCode, StringComparison.OrdinalIgnoreCase)
            && Languages.Any(language => string.Equals(language.ApiCode, sourceCode, StringComparison.OrdinalIgnoreCase))
            && Languages.Any(language => string.Equals(language.ApiCode, targetCode, StringComparison.OrdinalIgnoreCase));
    }

    private List<OfflineTranslationModel> GetSupportedInstalledOfflineModels() => _offlineModelService
        .GetInstalledModels()
        .Where(IsOfflineModelSupportedOnHomePage)
        .ToList();

    private static string GetLanguageDisplayName(string code) => ToApplicationLanguageCode(code) switch
    {
        "zh" => Localization.Text("简体中文"),
        "zh-tw" => Localization.Text("繁体中文"),
        "en" => Localization.Text("英语"),
        "ja" => Localization.Text("日语"),
        "ko" => Localization.Text("韩语"),
        "fr" => Localization.Text("法语"),
        "de" => Localization.Text("德语"),
        "es" => Localization.Text("西班牙语"),
        "ru" => Localization.Text("俄语"),
        "pt" => Localization.Text("葡萄牙语"),
        "it" => Localization.Text("意大利语"),
        "ar" => Localization.Text("阿拉伯语"),
        "th" => Localization.Text("泰语"),
        "vi" => Localization.Text("越南语"),
        "id" => Localization.Text("印尼语"),
        _ => code,
    };

    private static string FormatModelSize(long bytes) => bytes switch
    {
        <= 0 => Localization.Text("大小未知"),
        < 1024 * 1024 => (bytes / 1024d).ToString("0.0") + " KB",
        _ => (bytes / (1024d * 1024d)).ToString("0.0") + " MB",
    };

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private void AliyunAccessKeyIdPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _aliyunAccessKeyId = AliyunAccessKeyIdPasswordBox.Password;
        UpdateUiState();
        if (AliyunRememberKeyCheckBox.IsChecked == true && !string.IsNullOrEmpty(_aliyunAccessKeyId))
        {
            SaveKeyToVault(AliyunAccessKeyIdResource, _aliyunAccessKeyId);
        }
    }

    private void AliyunAccessKeySecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _aliyunAccessKeySecret = AliyunAccessKeySecretPasswordBox.Password;
        UpdateUiState();
        if (AliyunRememberKeyCheckBox.IsChecked == true && !string.IsNullOrEmpty(_aliyunAccessKeySecret))
        {
            SaveKeyToVault(AliyunAccessKeySecretResource, _aliyunAccessKeySecret);
        }
    }

    private void CustomPromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.CustomPrompt = CustomPromptTextBox.Text;
        SaveSettings();
    }

    private void AiPromptStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.AiPromptStyleIndex = Math.Clamp(AiPromptStyleComboBox.SelectedIndex, 0, 3);
        UpdateAiPromptStyleDescription();
        SaveSettings();
    }

    private void UpdateAiPromptStyleDescription()
    {
        AiPromptStyleDescriptionText.Text = AiPromptStyleComboBox.SelectedIndex switch
        {
            1 => Localization.Text("适合商务与公文，使用严谨、完整的书面表达。"),
            2 => Localization.Text("适合日常交流，使用地道口语，可灵活调整句式。"),
            3 => Localization.Text("删减客套和重复表达，用更少的文字保留关键信息。"),
            _ => Localization.Text("忠实保留原文语气、句式和信息，不额外润色。"),
        };
    }

    private void AiIncludeLanguageDetailsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.AiIncludeLanguageDetails = AiIncludeLanguageDetailsCheckBox.IsChecked == true;
        SaveSettings();
    }

    private void AiStreamingToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _settings is null) return;
        _settings.AiStreamingEnabled = AiStreamingToggleSwitch.IsOn;
        SaveSettings();
    }

    private void SourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCounts();
        UpdateUiState();
    }

    private void OutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCounts();
        UpdateUiState();
    }

    private void ImageOutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUiState();
    }

    private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RefreshTargetLanguageOptions();
        SaveSettings();
    }

    private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RefreshTargetLanguageOptions();
        SaveSettings();
    }

    private void ImageSourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RefreshImageTargetLanguageOptions();
    }

    private void ImageModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (ImageModelComboBox.SelectedItem is string model)
        {
            _settings.ImageModel = model;
            _settings.ImageSelectedModels[GetImageProvider()] = model;
            _suppressEvents = true;
            ImageModelsListView.SelectedItem = model;
            _suppressEvents = false;
            SaveSettings();
        }
    }

    private void ImageModelsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (ImageModelsListView.SelectedItem is string model)
        {
            _settings.ImageModel = model;
            _settings.ImageSelectedModels[GetImageProvider()] = model;
            _suppressEvents = true;
            ImageModelComboBox.SelectedItem = model;
            _suppressEvents = false;
            SaveSettings();
        }
    }

    private void ImageProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ImageProviderComboBox.SelectedItem is not string provider)
        {
            return;
        }

        _settings.ImageProviderName = provider;
        _suppressEvents = true;
        RefreshImageModelOptions(_settings.ImageSelectedModels.GetValueOrDefault(provider));
        _suppressEvents = false;
        SaveSettings();
    }

    private void InitializeImageModelSettings()
    {
        var provider = AiProviders.FirstOrDefault(name =>
            string.Equals(name, _settings.ImageProviderName, StringComparison.OrdinalIgnoreCase))
            ?? AiProviders[0];
        _settings.ImageProviderName = provider;
        ImageProviderComboBox.ItemsSource = AiProviders.ToList();
        ImageProviderComboBox.SelectedItem = provider;
        RefreshImageModelOptions(_settings.ImageSelectedModels.GetValueOrDefault(provider));
    }

    private void RefreshImageModelOptions(string? preferredModel)
    {
        var provider = GetImageProvider();
        var models = GetAvailableAiModels(GetProviderProfile(provider));
        ImageModelsListView.ItemsSource = models.ToList();
        ImageModelComboBox.ItemsSource = models.ToList();
        var selectedModel = models.FirstOrDefault(model =>
            string.Equals(model, preferredModel, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault();
        ImageModelsListView.SelectedItem = selectedModel;
        ImageModelComboBox.SelectedItem = selectedModel;
        _settings.ImageModel = selectedModel ?? string.Empty;
        _settings.ImageSelectedModels[provider] = _settings.ImageModel;
    }

    private string GetImageProvider() =>
        ImageProviderComboBox.SelectedItem as string ?? _settings.ImageProviderName;

    private string GetProviderEndpoint(string provider) =>
        ProviderCatalog.GetEndpoint(_settings, provider);

    private void ImageTargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RefreshImageTargetLanguageOptions();
    }

    private void SwapLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        var newSource = TargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
        _suppressEvents = true;
        SourceLanguageComboBox.SelectedItem = newSource;
        ImageSourceLanguageComboBox.SelectedItem = newSource;
        _suppressEvents = false;

        RefreshTargetLanguageOptions();
        RefreshImageTargetLanguageOptions();
        SaveSettings();
    }

    private void ClearSourceButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRealtimeTranslation();
        _cts?.Cancel();
        _selectionTranslationCts?.Cancel();
        SourceTextBox.Text = string.Empty;
        OutputTextBox.Text = string.Empty;
        SuspendRealtimeTranslationForCurrentInput();
        SourceTextBox.Focus(FocusState.Programmatic);
    }

    private void TranslationHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("History");
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var text = OutputTextBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SetClipboardText(text);
        ShowInfo(Localization.Text("译文已复制到剪贴板。"), InfoBarSeverity.Success);
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        var sequenceNumber = GetClipboardSequenceNumber();
        if (sequenceNumber == _lastHandledClipboardSequenceNumber)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => _ = HandleClipboardContentChangedAsync(sequenceNumber));
    }

    private async Task HandleClipboardContentChangedAsync(uint sequenceNumber)
    {
        if (sequenceNumber == _lastHandledClipboardSequenceNumber
            || sequenceNumber != GetClipboardSequenceNumber())
        {
            return;
        }

        if (!_isMiniMode && _currentPageTag != "Home")
        {
            _lastHandledClipboardSequenceNumber = sequenceNumber;
            return;
        }

        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                _lastHandledClipboardSequenceNumber = sequenceNumber;
                return;
            }

            var text = await content.GetTextAsync();
            if (sequenceNumber != GetClipboardSequenceNumber())
            {
                return;
            }

            _lastHandledClipboardSequenceNumber = sequenceNumber;
            if (string.IsNullOrWhiteSpace(text) || (!_isMiniMode && _currentPageTag != "Home"))
            {
                return;
            }

            _pendingClipboardText = text;
            ClipboardPasteTeachingTip.Target = _isMiniMode
                ? MiniStore.SourceTextBox ?? (FrameworkElement)MiniPageHost
                : TranslationHistoryButton;
            ClipboardPasteTeachingTip.Subtitle = BuildClipboardPreview(text);
            ClipboardPasteTeachingTip.IsOpen = true;
        }
        catch (Exception)
        {
            // The clipboard can be temporarily locked by the application writing to it.
        }
    }

    private void ClipboardPasteTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        var text = _pendingClipboardText;
        _pendingClipboardText = null;
        sender.IsOpen = false;
        if (string.IsNullOrEmpty(text) || (!_isMiniMode && _currentPageTag != "Home"))
        {
            return;
        }

        if (_isMiniMode)
        {
            if (IsMiniTranslating) return;
            MiniStore.SetInput(text);
            MiniStore.RequestFocus();
            return;
        }

        SourceTextBox.Text = text;
        SourceTextBox.Focus(FocusState.Programmatic);
        SourceTextBox.SelectionStart = SourceTextBox.Text.Length;
    }

    private void ClipboardPasteTeachingTip_CloseButtonClick(TeachingTip sender, object args)
    {
        _pendingClipboardText = null;
        sender.IsOpen = false;
    }

    private static string BuildClipboardPreview(string text)
    {
        const int previewLength = 120;
        var preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return preview.Length <= previewLength
            ? preview
            : preview[..previewLength] + "...";
    }

    private void SetClipboardText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
        _lastHandledClipboardSequenceNumber = GetClipboardSequenceNumber();
    }

    private void SidebarNavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSidebarSync || args.SelectedItem is not NavigationViewItem selectedItem)
        {
            return;
        }

        NavigateTo(selectedItem.Tag?.ToString());
    }

    private void SidebarNavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) =>
        GoBack();

    private void WindowRoot_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(WindowRoot).Properties.IsXButton1Pressed)
        {
            GoBack();
            e.Handled = true;
        }
    }

    private void SettingsPageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SettingsCardsPanel.Width = Math.Min(SettingsPanelMaxWidth, Math.Max(0d, e.NewSize.Width));
    }

    private void NavigateTo(string? tag, bool addBackEntry = true)
    {
        tag ??= "Home";
        if (tag == _currentPageTag)
        {
            return;
        }

        if (addBackEntry)
        {
            _backStack.Push(_currentPageTag);
        }

        ShowPage(tag);
    }

    private void GoBack()
    {
        if (_isMiniMode)
        {
            return;
        }

        if (_backStack.Count > 0)
        {
            ShowPage(_backStack.Pop());
        }
    }

    private void ShowPage(string? tag)
    {
        tag ??= "Home";
        _currentPageTag = tag;
        if (tag != "Home" && !_isMiniMode)
        {
            _pendingClipboardText = null;
            ClipboardPasteTeachingTip.IsOpen = false;
        }

        var pageTitle = tag switch
        {
            "Home" => Localization.AppName,
            "Image" => Localization.Text("图片翻译"),
            "File" => Localization.Text("文件翻译"),
            "History" => Localization.Text("翻译历史"),
            "Favorites" => Localization.Text("收藏列表"),
            "Settings" => Localization.Text("设置"),
            "About" => Localization.Text("关于"),
            _ => Localization.AppName,
        };
        PageTitleTextBlock.Text = pageTitle;
        Title = tag == "Home" ? Localization.AppName : $"{pageTitle} — {Localization.AppName}";

        TranslationToolbar.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        HomePageGrid.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        TranslationHistoryPageHost.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        FavoritesPageHost.Visibility = tag == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageScrollViewer.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ImagePageGrid.Visibility = tag == "Image" ? Visibility.Visible : Visibility.Collapsed;
        FilePageHost.Visibility = tag == "File" ? Visibility.Visible : Visibility.Collapsed;
        AboutPageHost.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;

        // The online catalog is only needed when the user opens model settings.
        if (tag == "Settings" && HasInternetAccess() && _onlineOfflineModels.Count == 0 && !_isLoadingOfflineModels)
        {
            _ = RefreshOfflineModelsAsync(showError: false);
        }

        _suppressSidebarSync = true;
        SidebarNavigationView.SelectedItem = tag switch
        {
            "Home" => HomeNavigationItem,
            "Image" => ImageNavigationItem,
            "File" => FileNavigationItem,
            "Settings" => SettingsNavigationItem,
            "About" => AboutNavigationItem,
            _ => null,
        };
        _suppressSidebarSync = false;
        SidebarNavigationView.IsBackEnabled = _backStack.Count > 0;
        UpdateUiState();
    }

    private void InstallMinimumSizeHandler()
    {
        _windowHandle = WindowNative.GetWindowHandle(this);
        _previousWindowProcedure = SetWindowLongPtr(
            _windowHandle,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_windowProcedure));
    }

    private void RestoreWindowProcedure()
    {
        if (_windowHandle == 0 || _previousWindowProcedure == 0)
        {
            return;
        }

        SetWindowLongPtr(_windowHandle, GwlWndProc, _previousWindowProcedure);
        _previousWindowProcedure = 0;
    }

    private nint WindowProcedureCallback(nint windowHandle, uint message, nint wParam, nint lParam)
    {
        if (message == WmGetMinMaxInfo)
        {
            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var dpi = GetDpiForWindow(windowHandle);
            var scale = dpi == 0 ? 1d : dpi / 96d;
            minMaxInfo.MinimumTrackSize.X = (int)Math.Ceiling(MinimumWindowWidth * scale);
            minMaxInfo.MinimumTrackSize.Y = (int)Math.Ceiling(MinimumWindowHeight * scale);
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
        }

        if (message == WmXButtonUp && ((wParam.ToInt64() >> 16) & 0xffff) == XButton1)
        {
            DispatcherQueue.TryEnqueue(GoBack);
            return 0;
        }

        if (message == WmSysKeyDown && wParam.ToInt64() == VkLeft)
        {
            DispatcherQueue.TryEnqueue(GoBack);
            return 0;
        }

        return CallWindowProc(_previousWindowProcedure, windowHandle, message, wParam, lParam);
    }

    private delegate nint WindowProcedure(nint windowHandle, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaximumSize;
        public Point MaximumPosition;
        public Point MinimumTrackSize;
        public Point MaximumTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(
        nint previousProcedure,
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    private async void PickImageButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        var existingPaths = _selectedImageFiles
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedImageFiles.AddRange(files.Where(file => existingPaths.Add(file.Path)));
        _selectedImageFiles.Sort((left, right) =>
            StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
        SelectedImagesListView.ItemsSource = _selectedImageFiles.Select(file => file.Name).ToList();
        ImageSelectionSummaryText.Text = _selectedImageFiles.Count == 1
            ? Localization.Text("已选择 1 张图片")
            : Localization.Text("已选择 {0} 张图片（将按文件名排序）", _selectedImageFiles.Count);
        ImageOutputTextBox.Text = string.Empty;
        ImageTranslationProgressText.Text = string.Empty;

        var stream = await _selectedImageFiles[0].OpenReadAsync();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        ImagePreview.Source = bitmap;
        ImagePreview.Visibility = Visibility.Visible;
        ImagePreviewPlaceholderText.Visibility = Visibility.Collapsed;
        UpdateUiState();
    }

    private void MiniModeButton_Click(object sender, RoutedEventArgs e)
    {
        var mini = !_isMiniMode;
        SetMiniMode(mini, resizeWindow: true);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _windowLayoutUpdateQueued || _isWindowClosed)
        {
            return;
        }

        // Only switch the full/mini surface here. NavigationView owns its adaptive
        // pane state so resize notifications cannot interrupt its open/close animation.
        _windowLayoutUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _windowLayoutUpdateQueued = false;
            if (_isWindowClosed)
            {
                return;
            }

            var logicalWidth = AppWindow.ClientSize.Width / GetWindowScale();
            var logicalHeight = AppWindow.ClientSize.Height / GetWindowScale();
            if (!_isMiniMode && logicalWidth <= MiniModeEnterWidth)
            {
                SetMiniMode(true, resizeWindow: false);
                return;
            }

            if (_isMiniMode && logicalWidth >= MiniModeExitWidth && logicalWidth >= logicalHeight)
            {
                SetMiniMode(false, resizeWindow: false);
            }
            else if (_isMiniMode)
            {
                UpdateMiniLayout(logicalWidth, logicalHeight);
            }
        }))
        {
            _windowLayoutUpdateQueued = false;
        }
    }

    private void SetMiniMode(bool mini, bool resizeWindow)
    {
        var scale = GetWindowScale();
        var currentWidth = AppWindow.ClientSize.Width / scale;
        var currentHeight = AppWindow.ClientSize.Height / scale;
        // Keep a tall window tall when the user explicitly enters mini mode.
        // Landscape windows retain the original compact mini size.
        var useTallWindow = mini && ShouldUseStackedMiniLayout(currentWidth, currentHeight, false);
        var targetWidth = useTallWindow ? Math.Min((int)currentWidth, MiniWindowWidth) : MiniWindowWidth;
        var targetHeight = useTallWindow ? (int)currentHeight : MiniWindowHeight;
        if (_isMiniMode == mini)
        {
            if (resizeWindow)
            {
                ResizeWindowInDips(
                    mini ? targetWidth : DefaultWindowWidth,
                    mini ? targetHeight : DefaultWindowHeight);
            }

            return;
        }

        _isMiniMode = mini;
        _pendingClipboardText = null;
        ClipboardPasteTeachingTip.IsOpen = false;
        ClearNavigationHistory();
        SidebarNavigationView.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        MiniWindowGrid.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        SetTitleBar(mini ? MiniTitleBarDragRegion : TitleBarDragRegion);
        TranslationToolbar.Visibility = mini ? Visibility.Collapsed : (_currentPageTag == "Home" ? Visibility.Visible : Visibility.Collapsed);
        HomePageGrid.Visibility = mini ? Visibility.Collapsed : (_currentPageTag == "Home" ? Visibility.Visible : Visibility.Collapsed);
        if (mini)
        {
            PageTitleTextBlock.Text = Localization.AppName;
            Title = Localization.AppName;
        }
        else
        {
            ShowPage(_currentPageTag);
        }

        if (resizeWindow)
        {
            ResizeWindowInDips(
                mini ? targetWidth : DefaultWindowWidth,
                mini ? targetHeight : DefaultWindowHeight);
        }

        if (mini)
        {
            ShowMiniTranslator(
                resizeWindow ? targetWidth : currentWidth,
                resizeWindow ? targetHeight : currentHeight);
        }
        UpdateUiState();
    }

    private async void ClearImageSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isImageTranslating || _selectedImageFiles.Count == 0)
        {
            return;
        }

        if (!await ConfirmClearSelectionAsync(Localization.Text("清除已选图片"), Localization.Text("将清除已选图片、预览和当前译文。")))
        {
            return;
        }

        _selectedImageFiles.Clear();
        SelectedImagesListView.ItemsSource = null;
        ImageSelectionSummaryText.Text = Localization.Text("未选择图片");
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        ImagePreviewPlaceholderText.Visibility = Visibility.Visible;
        ImageOutputTextBox.Text = string.Empty;
        ImageTranslationProgressText.Text = string.Empty;
        UpdateUiState();
        ShowInfo(Localization.Text("已清除选择的图片。"), InfoBarSeverity.Success);
    }

    private async void TranslateImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImageFiles.Count == 0 || _isImageTranslating)
        {
            return;
        }

        var sourceLanguage = ImageSourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        var targetLanguage = ImageTargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
        var systemPrompt = BuildDefaultPrompt(
            sourceLanguage,
            targetLanguage,
            AiPromptStyleComboBox.SelectedIndex,
            AiIncludeLanguageDetailsCheckBox.IsChecked == true);
        if (!string.IsNullOrWhiteSpace(CustomPromptTextBox.Text))
        {
            systemPrompt += "\n\n补充要求：" + CustomPromptTextBox.Text.Trim();
        }

        var providerKey = GetImageProvider();
        var endpoint = GetProviderEndpoint(providerKey);
        var model = ImageModelComboBox.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        var apiKey = GetProviderApiKey(providerKey);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ShowInfo(Localization.Text("请填写图片翻译接口地址。"), InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            ShowInfo(Localization.Text("请填写支持图片的模型名称。"), InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowInfo(Localization.Text("图片翻译使用{0} API，请先在供应商卡片中设置 API Key。", Localization.ProviderName(providerKey)), InfoBarSeverity.Warning);
            return;
        }

        _isImageTranslating = true;
        ImageOutputTextBox.Text = string.Empty;
        ShowInfo(Localization.Text("正在翻译 {0} 张图片...", _selectedImageFiles.Count), InfoBarSeverity.Informational);
        UpdateUiState();

        try
        {
            var results = new List<string>();
            for (var index = 0; index < _selectedImageFiles.Count; index++)
            {
                var file = _selectedImageFiles[index];
                ImageTranslationProgressText.Text = $"{index + 1} / {_selectedImageFiles.Count}";

                try
                {
                    var completedImages = string.Join(
                        Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine, results);
                    var prefix = results.Count == 0 ? string.Empty : completedImages
                        + Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine;
                    var result = await TranslateImageFileAsync(
                        file,
                        endpoint,
                        apiKey,
                        model,
                        systemPrompt,
                        text => ImageOutputTextBox.Text = prefix + text);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(Localization.Text("翻译失败：{0}", ex.Message));
                }
                ImageOutputTextBox.Text = string.Join(
                    Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine, results);
            }

            ImageOutputTextBox.Text = string.Join(
                Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine,
                results);
            ImageTranslationProgressText.Text = Localization.Text("已完成 {0} 张", _selectedImageFiles.Count);
            ShowInfo(Localization.Text("图片翻译完成。"), InfoBarSeverity.Success);
        }
        finally
        {
            _isImageTranslating = false;
            UpdateUiState();
        }
    }

    private async Task<string> TranslateImageFileAsync(
        StorageFile file,
        string endpoint,
        string apiKey,
        string model,
        string systemPrompt,
        Action<string>? onTextUpdated = null)
    {
        byte[] imageBytes;
        using (var imageStream = await file.OpenStreamForReadAsync())
        using (var memoryStream = new MemoryStream())
        {
            await imageStream.CopyToAsync(memoryStream);
            imageBytes = memoryStream.ToArray();
        }

        var imageDataUrl = $"data:{GetMimeType(file.Path)};base64,{Convert.ToBase64String(imageBytes)}";
        var request = new ImageTranslationRequest(
            endpoint,
            apiKey,
            model,
            systemPrompt,
            "请识别图片中的文字，并翻译成目标语言。",
            imageDataUrl);

        return await _translationService.TranslateImageAsync(
            request, CancellationToken.None, _settings.AiStreamingEnabled, onTextUpdated);
    }

    private void CopyImageOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var text = ImageOutputTextBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SetClipboardText(text);
        ShowInfo(Localization.Text("图片译文已复制到剪贴板。"), InfoBarSeverity.Success);
    }

    private async Task<bool> ConfirmClearSelectionAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = Localization.Text("清除"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<string> TranslateFileChunkWithRetryAsync(
        string chunk,
        int mode,
        LanguageOption sourceLanguage,
        LanguageOption targetLanguage,
        CancellationToken cancellationToken,
        Action<string>? onTextUpdated = null)
    {
        try
        {
            return RestoreOriginalLineEndings(
                await TranslateTextAsync(chunk, mode, sourceLanguage, targetLanguage, cancellationToken,
                    requireApiKey: false, onTextUpdated: onTextUpdated),
                chunk);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            onTextUpdated?.Invoke(string.Empty);
            return RestoreOriginalLineEndings(
                await TranslateTextAsync(chunk, mode, sourceLanguage, targetLanguage, cancellationToken,
                    requireApiKey: false, onTextUpdated: onTextUpdated),
                chunk);
        }
    }

    private static async Task<string> ReadTextFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                    .GetString(bytes);
            }
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(Localization.Text("无法识别文件编码。请将文件保存为 UTF-8、UTF-16 或 GBK 后重试。"), ex);
        }
    }

    private static List<string> SplitTextIntoChunks(string text)
    {
        const int chunkSize = 1800;
        var chunks = new List<string>();
        var chunkStart = 0;
        var position = 0;

        while (position < text.Length)
        {
            var lineEnd = position;
            while (lineEnd < text.Length && text[lineEnd] != '\r' && text[lineEnd] != '\n')
            {
                lineEnd++;
            }

            var nextLineStart = lineEnd;
            if (nextLineStart < text.Length && text[nextLineStart] == '\r')
            {
                nextLineStart++;
            }

            if (nextLineStart < text.Length && text[nextLineStart] == '\n')
            {
                nextLineStart++;
            }

            if (lineEnd - chunkStart >= chunkSize && position > chunkStart)
            {
                chunks.Add(text[chunkStart..position]);
                chunkStart = position;
            }

            position = nextLineStart;
        }

        if (chunkStart < text.Length)
        {
            chunks.Add(text[chunkStart..]);
        }

        return chunks;
    }

    private static string FormatFileSize(ulong bytes) => bytes < 1024
        ? $"{bytes} B"
        : $"{bytes / 1024d:F1} KB";

    private static string RestoreOriginalLineEndings(string translatedText, string sourceText)
    {
        var sourceEndings = GetLineEndings(sourceText);
        var translatedLines = translatedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (sourceEndings.Count == 0)
        {
            return translatedText;
        }

        var expectedLineCount = sourceEndings.Count + 1;
        if (translatedLines.Length != expectedLineCount)
        {
            var adjustedLines = new string[expectedLineCount];
            if (translatedLines.Length < expectedLineCount)
            {
                adjustedLines[0] = translatedText.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                for (var index = 1; index < adjustedLines.Length; index++)
                {
                    adjustedLines[index] = string.Empty;
                }
            }
            else
            {
                Array.Copy(translatedLines, adjustedLines, expectedLineCount - 1);
                adjustedLines[^1] = string.Concat(translatedLines.Skip(expectedLineCount - 1));
            }

            translatedLines = adjustedLines;
        }

        var builder = new StringBuilder(translatedText.Length + sourceEndings.Count);
        for (var index = 0; index < sourceEndings.Count; index++)
        {
            builder.Append(translatedLines[index]);
            builder.Append(sourceEndings[index]);
        }

        builder.Append(translatedLines[^1]);
        return builder.ToString();
    }

    private static List<string> GetLineEndings(string text)
    {
        var endings = new List<string>();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                endings.Add(index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r");
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (text[index] == '\n')
            {
                endings.Add("\n");
            }
        }

        return endings;
    }

    private void SourceTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!_settings.EnterToTranslate || e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        var shiftDown = GetKeyState((int)Windows.System.VirtualKey.Shift) < 0;
        var controlDown = GetKeyState((int)Windows.System.VirtualKey.Control) < 0;
        var altDown = GetKeyState((int)Windows.System.VirtualKey.Menu) < 0;
        if (controlDown || altDown)
        {
            return;
        }

        if (shiftDown)
        {
            var start = SourceTextBox.SelectionStart;
            var length = SourceTextBox.SelectionLength;
            SourceTextBox.Text = SourceTextBox.Text.Remove(start, length)
                .Insert(start, Environment.NewLine);
            SourceTextBox.SelectionStart = start + Environment.NewLine.Length;
            SourceTextBox.SelectionLength = 0;
            return;
        }

        if (!_isTranslating && !string.IsNullOrWhiteSpace(SourceTextBox.Text))
        {
            TranslateButton_Click(TranslateButton, new RoutedEventArgs());
        }
    }

    private void ClearNavigationHistory()
    {
        _backStack.Clear();
        SidebarNavigationView.IsBackEnabled = false;
    }

    private double GetWindowScale()
    {
        var dpi = GetDpiForWindow(_windowHandle);
        return dpi == 0 ? 1d : dpi / 96d;
    }

    private void ResizeWindowInDips(int width, int height)
    {
        var scale = GetWindowScale();
        AppWindow.Resize(new SizeInt32(
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale)));
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranslating)
        {
            _cts?.Cancel();
            return;
        }

        if (_isSelectionTranslating)
        {
            return;
        }

        SuspendRealtimeTranslationForCurrentInput();
        var sourceText = SourceTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            ShowInfo(Localization.Text("请输入需要翻译的内容。"), InfoBarSeverity.Warning);
            return;
        }

        if (ModeComboBox.SelectedIndex == 0)
        {
            await TranslateWithMachineTranslationAsync(sourceText);
            return;
        }

        var isLocalMode = ModeComboBox.SelectedIndex == 2;
        var useLocalModel = isLocalMode && LocalTranslationSourceComboBox.SelectedIndex == 1;
        var endpoint = isLocalMode
            ? LocalEndpointTextBox.Text.Trim()
            : GetProviderEndpoint(_currentProvider);
        var model = isLocalMode
            ? LocalModelTextBox.Text.Trim()
            : GetSelectedAiModel();
        var apiKey = isLocalMode
            ? null
            : GetProviderApiKey(_currentProvider);
        if (!useLocalModel && string.IsNullOrWhiteSpace(endpoint))
        {
            ShowInfo(Localization.Text("请填写接口地址。"), InfoBarSeverity.Warning);
            return;
        }

        if (!useLocalModel && string.IsNullOrWhiteSpace(model))
        {
            ShowInfo(Localization.Text("请填写模型名称。"), InfoBarSeverity.Warning);
            return;
        }

        if (useLocalModel)
        {
            if (!IsBergamotRuntimeAvailable())
            {
                ShowInfo(Localization.Text("当前发布包未包含 Mozilla Translations 本地翻译引擎。请构建并重新发布 Runtime\\Bergamot 运行库。"), InfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedOfflineModelPath))
            {
                ShowInfo(Localization.Text("请先在设置中下载并选择离线语言模型。"), InfoBarSeverity.Warning);
                return;
            }
        }

        if (!isLocalMode && string.IsNullOrWhiteSpace(apiKey))
        {
            ShowInfo(Localization.Text("请在供应商卡片中设置 API Key。"), InfoBarSeverity.Warning);
            return;
        }

        if (!useLocalModel && !string.IsNullOrEmpty(model))
        {
            var settingsKey = isLocalMode ? "本地 AI" : _currentProvider;
            _settings.Models[settingsKey] = model;
            _settings.Endpoints[settingsKey] = endpoint;
            SaveSettings();
        }

        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
        _cts = new CancellationTokenSource();
        var translationToken = _cts.Token;
        _isTranslating = true;
        TranslateButtonText.Text = Localization.Text("取消");
        ProgressRing.IsActive = true;
        UpdateUiState();
        ShowInfo(useLocalModel ? Localization.Text("正在使用本地模型翻译...") : Localization.Text("正在翻译..."), InfoBarSeverity.Informational);

        try
        {
            var result = await TranslateTextAsync(
                sourceText,
                ModeComboBox.SelectedIndex,
                sourceLanguage,
                targetLanguage,
                translationToken,
                onTextUpdated: text =>
                {
                    if (!translationToken.IsCancellationRequested) OutputTextBox.Text = text;
                });
            translationToken.ThrowIfCancellationRequested();
            OutputTextBox.Text = result;
            UpdateCounts();
            AddTranslationHistory(sourceText);
            ShowInfo(Localization.Text("翻译完成。"), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo(Localization.Text("已取消翻译。"), InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isTranslating = false;
            _cts.Dispose();
            _cts = null;
            TranslateButtonText.Text = Localization.Text("翻译");
            ProgressRing.IsActive = false;
            UpdateUiState();
        }
    }

    private async Task TranslateWithMachineTranslationAsync(string sourceText)
    {
        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption;
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption;
        if (sourceLanguage is null
            || targetLanguage is null
            || string.IsNullOrEmpty(targetLanguage.ApiCode))
        {
            ShowInfo(Localization.Text("请选择有效的源语言和目标语言。"), InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_aliyunAccessKeyId))
        {
            ShowInfo(Localization.Text("请填写阿里云机器翻译 AccessKey ID。"), InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_aliyunAccessKeySecret))
        {
            ShowInfo(Localization.Text("请填写阿里云机器翻译 AccessKey Secret。"), InfoBarSeverity.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        var translationToken = _cts.Token;
        _isTranslating = true;
        TranslateButtonText.Text = Localization.Text("取消");
        ProgressRing.IsActive = true;
        UpdateUiState();
        ShowInfo(Localization.Text("正在调用机器翻译..."), InfoBarSeverity.Informational);

        try
        {
            var result = await TranslateTextAsync(
                sourceText,
                0,
                sourceLanguage,
                targetLanguage,
                translationToken);
            translationToken.ThrowIfCancellationRequested();
            OutputTextBox.Text = result;
            UpdateCounts();
            AddTranslationHistory(sourceText);
            ShowInfo(Localization.Text("翻译完成。"), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo(Localization.Text("已取消翻译。"), InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isTranslating = false;
            _cts.Dispose();
            _cts = null;
            TranslateButtonText.Text = Localization.Text("翻译");
            ProgressRing.IsActive = false;
            UpdateUiState();
        }
    }

    private async void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOpeningSystemDictation)
        {
            return;
        }

        try
        {
            _isOpeningSystemDictation = true;
            UpdateUiState();
            SourceTextBox.Focus(FocusState.Programmatic);
            SourceTextBox.SelectionStart = SourceTextBox.Text.Length;
            SourceTextBox.SelectionLength = 0;
            await Task.Delay(100);

            keybd_event(VkLeftWindows, 0, 0, 0);
            keybd_event(VkH, 0, 0, 0);
            keybd_event(VkH, 0, KeyEventKeyUp, 0);
            keybd_event(VkLeftWindows, 0, KeyEventKeyUp, 0);
            ShowInfo(Localization.Text("已打开 Windows 语音输入。"), InfoBarSeverity.Informational);
        }
        finally
        {
            _isOpeningSystemDictation = false;
            UpdateUiState();
        }
    }

    private async Task<string> TranslateTextAsync(
        string text,
        int mode,
        LanguageOption sourceLanguage,
        LanguageOption targetLanguage,
        CancellationToken cancellationToken,
        bool requireApiKey = true,
        Action<string>? onTextUpdated = null)
    {
        if (mode == 0)
        {
            if (string.IsNullOrWhiteSpace(_aliyunAccessKeyId) || string.IsNullOrWhiteSpace(_aliyunAccessKeySecret))
            {
                throw new InvalidOperationException(Localization.Text("请填写阿里云机器翻译 AccessKey ID 和 AccessKey Secret。"));
            }

            var request = new MachineTranslationRequest(
                AliyunServiceUrl,
                _aliyunAccessKeyId,
                _aliyunAccessKeySecret,
                sourceLanguage.ApiCode,
                targetLanguage.ApiCode,
                text);
            return await RunTranslationInBackgroundAsync(
                () => _machineTranslationService.TranslateAsync(request, cancellationToken), cancellationToken);
        }

        var useLocalModel = mode == 2 && LocalTranslationSourceComboBox.SelectedIndex == 1;
        if (useLocalModel)
        {
            if (string.IsNullOrWhiteSpace(_selectedOfflineModelPath))
            {
                throw new InvalidOperationException(Localization.Text("请先在设置中下载并选择离线语言模型。"));
            }

            var modelPath = _selectedOfflineModelPath;
            return await RunTranslationInBackgroundAsync(async () =>
            {
                if (!IsBergamotRuntimeAvailable())
                    throw new InvalidOperationException(Localization.Text("当前发布包未包含 Mozilla Translations 本地翻译引擎。"));
                return await _bergamotTranslationService.TranslateAsync(modelPath, text, cancellationToken);
            }, cancellationToken);
        }

        var endpoint = mode == 2 ? LocalEndpointTextBox.Text.Trim() : GetProviderEndpoint(_currentProvider);
        var model = mode == 2 ? LocalModelTextBox.Text.Trim() : GetSelectedAiModel();
        var apiKey = mode == 2 ? null : GetProviderApiKey(_currentProvider);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(Localization.Text("请填写接口地址。"));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(Localization.Text("请填写模型名称。"));
        }

        if (requireApiKey && mode != 2 && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(Localization.Text("请在供应商卡片中设置 API Key。"));
        }

        var systemPrompt = BuildDefaultPrompt(
            sourceLanguage,
            targetLanguage,
            AiPromptStyleComboBox.SelectedIndex,
            AiIncludeLanguageDetailsCheckBox.IsChecked == true);
        if (mode != 0 && !string.IsNullOrWhiteSpace(CustomPromptTextBox.Text))
        {
            systemPrompt += "\n\n补充要求：" + CustomPromptTextBox.Text.Trim();
        }

        // Capture controls/settings on the UI thread; service startup, parsing and streaming run
        // in the background. Wait for each UI update so chunks stay ordered and callback errors
        // (for example, a changed selection) still propagate to the translation error handler.
        var translationRequest = new TranslationRequest(endpoint, apiKey, model, systemPrompt, text);
        var streaming = _settings.AiStreamingEnabled;
        var progress = CreateTranslationProgressCallback(onTextUpdated, cancellationToken);
        return await RunTranslationInBackgroundAsync(
            () => _translationService.TranslateAsync(translationRequest, cancellationToken, streaming, progress),
            cancellationToken);
    }

    private static Action<string>? CreateTranslationProgressCallback(
        Action<string>? onTextUpdated, CancellationToken cancellationToken)
    {
        if (onTextUpdated is null) return null;
        var context = SynchronizationContext.Current;
        return text =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context is null || ReferenceEquals(context, SynchronizationContext.Current))
            {
                onTextUpdated(text);
                return;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(_ =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onTextUpdated(text);
                    completion.SetResult();
                }
                catch (OperationCanceledException)
                {
                    completion.SetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }, null);
            completion.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
        };
    }

    private static async Task<string> RunTranslationInBackgroundAsync(
        Func<Task<string>> translate, CancellationToken cancellationToken)
    {
        var result = await Task.Run(translate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private string? GetTranslationConfigurationError(int mode)
    {
        if (!IsTranslationModeEnabled(mode))
        {
            return mode == 0 ? Localization.Text("API 翻译已在设置中关闭。") : Localization.Text("AI 翻译已在设置中关闭。");
        }

        if (mode == 0)
        {
            return string.IsNullOrWhiteSpace(_aliyunAccessKeyId) || string.IsNullOrWhiteSpace(_aliyunAccessKeySecret)
                ? Localization.Text("API 翻译未配置阿里云 AccessKey。")
                : null;
        }

        if (mode == 2 && LocalTranslationSourceComboBox.SelectedIndex == 1)
        {
            return string.IsNullOrWhiteSpace(_selectedOfflineModelPath)
                ? Localization.Text("本地翻译未选择 Mozilla Translations 模型。")
                : !IsBergamotRuntimeAvailable()
                    ? Localization.Text("当前发布包未包含 Mozilla Translations 本地翻译引擎。")
                    : null;
        }

        var endpoint = mode == 2 ? LocalEndpointTextBox.Text : GetProviderEndpoint(_currentProvider);
        var model = mode == 2 ? LocalModelTextBox.Text : GetSelectedAiModel();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return mode == 2 ? Localization.Text("本地翻译未配置接口地址。") : Localization.Text("AI 翻译未配置接口地址。");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return mode == 2 ? Localization.Text("本地翻译未配置模型名称。") : Localization.Text("AI 翻译未配置模型名称。");
        }

        return null;
    }

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speechOutput.IsSpeaking)
        {
            _speechOutput.Stop();
            SetSpeakingState(false);
            ShowInfo(Localization.Text("已停止朗读。"), InfoBarSeverity.Informational);
            return;
        }

        var text = OutputTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var languageTag = (TargetLanguageComboBox.SelectedItem as LanguageOption)?.SpeechTag;
        SpeakButton.IsEnabled = false;

        try
        {
            ShowInfo(Localization.Text("正在合成语音..."), InfoBarSeverity.Informational);
            await _speechOutput.SpeakAsync(text, languageTag);
            SetSpeakingState(true);
            ShowInfo(Localization.Text("正在朗读译文。"), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetSpeakingState(false);
            ShowInfo(Localization.Text("无法播放语音：") + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void SetSpeakingState(bool speaking)
    {
        SpeakIcon.Symbol = speaking ? Symbol.Stop : Symbol.Audio;
        UpdateUiState();
    }

    private async void RefreshLocalModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = LocalEndpointTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ShowInfo(Localization.Text("请先填写本地接口地址。"), InfoBarSeverity.Warning);
            return;
        }

        RefreshLocalModelsButton.IsEnabled = false;
        try
        {
            var models = await _translationService.GetModelsAsync(
                endpoint,
                null,
                CancellationToken.None);

            if (models.Count == 0)
            {
                ShowInfo(Localization.Text("本地服务没有返回模型列表。"), InfoBarSeverity.Warning);
                return;
            }

            _suppressEvents = true;
            LocalModelTextBox.Text = models[0];
            _suppressEvents = false;
            _settings.Endpoints["本地 AI"] = endpoint;
            _settings.Models["本地 AI"] = models[0];
            SaveSettings();
            ShowInfo(Localization.Text("找到 {0} 个本地模型。", models.Count), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("刷新模型失败：") + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            RefreshLocalModelsButton.IsEnabled = true;
        }
    }

    private void RefreshTargetLanguageOptions()
    {
        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        RefreshTargetLanguageOptions(
            TargetLanguageComboBox,
            sourceLanguage,
            TargetLanguageComboBox.SelectedItem as LanguageOption);
    }

    private void RefreshImageTargetLanguageOptions()
    {
        var sourceLanguage = ImageSourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        RefreshTargetLanguageOptions(
            ImageTargetLanguageComboBox,
            sourceLanguage,
            ImageTargetLanguageComboBox.SelectedItem as LanguageOption);
    }

    private void RefreshTargetLanguageOptions(
        ComboBox targetComboBox,
        LanguageOption sourceLanguage,
        LanguageOption? preferredTarget)
    {
        var allowedTargets = Languages
            .Where(language => language.PromptName != "自动检测"
                && (sourceLanguage.ApiCode == "auto"
                    || language.PromptName != sourceLanguage.PromptName))
            .ToList();

        var target = allowedTargets.FirstOrDefault(
                language => preferredTarget is not null
                    && language.PromptName == preferredTarget.PromptName)
            ?? allowedTargets.FirstOrDefault(language => language.PromptName == "英语")
            ?? allowedTargets.FirstOrDefault();

        var previousSuppress = _suppressEvents;
        _suppressEvents = true;
        targetComboBox.ItemsSource = allowedTargets;
        targetComboBox.SelectedItem = target;
        _suppressEvents = previousSuppress;
    }

    private static string GetMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };
    }

    private void UpdateCounts()
    {
        SourceCountText.Text = Localization.Text("{0} 字", SourceTextBox.Text.Length);
        OutputCountText.Text = Localization.Text("{0} 字", OutputTextBox.Text.Length);
    }

    private void AddTranslationHistory(string sourceText)
    {
        if (_settings.TranslationHistoryLimit == 0 || string.IsNullOrWhiteSpace(sourceText))
        {
            return;
        }

        _settings.TranslationHistory.RemoveAll(historyText =>
            string.Equals(historyText, sourceText, StringComparison.Ordinal));
        _settings.TranslationHistory.Add(sourceText);
        TrimTranslationHistory();
        RefreshTranslationHistory();
        SaveSettings();
    }

    private void TrimTranslationHistory()
    {
        _settings.TranslationHistory ??= new List<string>();
        var seenTexts = new HashSet<string>(StringComparer.Ordinal);
        for (var index = _settings.TranslationHistory.Count - 1; index >= 0; index--)
        {
            if (!seenTexts.Add(_settings.TranslationHistory[index]))
            {
                _settings.TranslationHistory.RemoveAt(index);
            }
        }

        if (_settings.TranslationHistoryLimit == 0)
        {
            _settings.TranslationHistory.Clear();
            return;
        }

        if (_settings.TranslationHistoryLimit > 0
            && _settings.TranslationHistory.Count > _settings.TranslationHistoryLimit)
        {
            _settings.TranslationHistory.RemoveRange(
                0,
                _settings.TranslationHistory.Count - _settings.TranslationHistoryLimit);
        }
    }

    private void RefreshTranslationHistory()
    {
        var history = _settings.TranslationHistory
            .AsEnumerable()
            .Reverse()
            .ToArray();
        _translationHistoryStore?.Update(state => state with { Items = history });
    }

    private static int NormalizeTranslationHistoryLimit(int limit) =>
        TranslationHistoryLimits.Contains(limit) ? limit : 20;

    private static string GetApplicationVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? Localization.Text("未知");
        }
    }

    private void UpdateUiState()
    {
        RefreshRealtimeTranslation();
        ProgressRing.IsActive = _isTranslating || _isRealtimeTranslating;
        UpdateFavoritesState();
        ProviderApiKeyPasswordBox.IsEnabled = !_isTranslating && !_isSelectionTranslating
            && !_isImageTranslating && !_isFileTranslating && !IsMiniTranslating;
        TranslateButton.IsEnabled = _isTranslating || (!_isSelectionTranslating && SourceTextBox.Text.Trim().Length > 0);
        MicButton.IsEnabled = !_isTranslating && !_isSelectionTranslating && !_isOpeningSystemDictation;
        SpeakButton.IsEnabled = _speechOutput.IsSpeaking || OutputTextBox.Text.Trim().Length > 0;
        PickImageButton.IsEnabled = !_isImageTranslating;
        TranslateImageButton.IsEnabled = !_isImageTranslating && _selectedImageFiles.Count > 0;
        ClearImageSelectionButton.IsEnabled = !_isImageTranslating && _selectedImageFiles.Count > 0;
        CopyImageOutputButton.IsEnabled = ImageOutputTextBox.Text.Trim().Length > 0;
        UpdateFileModeAvailability();
    }

    private void SaveSettings()
    {
        _settings.ModeIndex = Math.Max(0, ModeComboBox.SelectedIndex);
        _settings.ApiTranslationEnabled = ApiTranslationToggleSwitch.IsOn;
        _settings.AiTranslationEnabled = AiTranslationToggleSwitch.IsOn;
        _settings.ProviderName = _currentProvider;
        _settings.ThemeIndex = Math.Max(0, ThemeComboBox.SelectedIndex);
        _settings.MicaBackdropEnabled = MicaBackdropCheckBox.IsChecked == true;
        _settings.AlwaysOnTop = AlwaysOnTopToggleSwitch.IsOn;
        _settings.EnterToTranslate = EnterToTranslateToggleSwitch.IsOn;
        _settings.RealtimeTranslationEnabled = RealtimeTranslationToggleSwitch.IsOn;
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption;
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption;
        _settings.SourceLanguageIndex = sourceLanguage is null ? 0 : Languages.IndexOf(sourceLanguage);
        _settings.TargetLanguageIndex = targetLanguage is null ? 3 : Languages.IndexOf(targetLanguage);
        _settings.CustomPrompt = CustomPromptTextBox.Text;
        _settings.AiPromptStyleIndex = Math.Clamp(AiPromptStyleComboBox.SelectedIndex, 0, 3);
        _settings.AiIncludeLanguageDetails = AiIncludeLanguageDetailsCheckBox.IsChecked == true;
        _settings.AiStreamingEnabled = AiStreamingToggleSwitch.IsOn;
        _settings.ImageEndpoint = GetProviderEndpoint(GetImageProvider());
        _settings.ImageProviderName = GetImageProvider();
        _settings.ImageEndpoints[_settings.ImageProviderName] = _settings.ImageEndpoint;
        _settings.ImageModel = ImageModelComboBox.SelectedItem?.ToString() ?? string.Empty;
        _settings.ImageSelectedModels[_settings.ImageProviderName] = _settings.ImageModel;
        _settings.RememberImageKeys = AiRememberKeyCheckBox.IsChecked == true;
        _settings.RememberKeys = AiRememberKeyCheckBox.IsChecked == true;
        _settings.RememberAliyunKeys = AliyunRememberKeyCheckBox.IsChecked == true;
        _settings.LocalTranslationSourceIndex = Math.Clamp(LocalTranslationSourceComboBox.SelectedIndex, 0, 1);
        _settings.LocalModelPath = _selectedOfflineModelPath;
        _settings.Endpoints["本地 AI"] = LocalEndpointTextBox.Text.Trim();
        _settings.Models["本地 AI"] = LocalModelTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(_currentProvider))
        {
            _settings.Models[_currentProvider] = GetSelectedAiModel();
        }

        AppSettingsStore.Save(_settings);
        UpdateUiState();
    }

    private void ApplyTheme()
    {
        var theme = ThemeComboBox.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        UpdateTitleBarButtonColors(theme);
    }

    private void ApplyMicaBackdrop()
    {
        if (MicaBackdropCheckBox.IsChecked != true)
        {
            SystemBackdrop = null;
            AcrylicFallbackLayer.Visibility = Visibility.Collapsed;
            return;
        }

        // Mica is available on Windows 11. Windows 10 receives the related Acrylic effect.
        var useMica = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        SystemBackdrop = useMica ? new MicaBackdrop() : new DesktopAcrylicBackdrop();
        AcrylicFallbackLayer.Visibility = useMica ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = AlwaysOnTopToggleSwitch.IsOn;
        }
    }

    private void UpdateTitleBarButtonColors(ElementTheme theme)
    {
        var titleBar = AppWindow.TitleBar;
        if (theme == ElementTheme.Dark)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 70, 70, 70);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 100, 100, 100);
        }
        else if (theme == ElementTheme.Light)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 220, 220, 220);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 190, 190, 190);
        }
        else
        {
            titleBar.ButtonForegroundColor = null;
            titleBar.ButtonHoverBackgroundColor = null;
            titleBar.ButtonPressedBackgroundColor = null;
        }
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        _infoBarTimer?.Stop();
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
        _infoBarTimer?.Start();
    }

    private string BuildDefaultPrompt(
        LanguageOption source,
        LanguageOption target,
        int styleIndex = 0,
        bool includeLanguageDetails = false)
        => TranslationPromptBuilder.Build(source, target, styleIndex, includeLanguageDetails);

    private static ProviderProfile GetProviderProfile(string provider) => ProviderCatalog.GetProfile(provider);

    private static void SaveKeyToVault(string provider, string key)
    {
        try
        {
            var vault = new PasswordVault();
            try
            {
                var existing = vault.Retrieve(KeyVaultResource, provider);
                vault.Remove(existing);
            }
            catch
            {
                // No existing credential is fine.
            }

            vault.Add(new PasswordCredential(KeyVaultResource, provider, key));
        }
        catch
        {
            // Credential manager failures should not interrupt the app.
        }
    }

    private static string? LoadKeyFromVault(string provider)
    {
        try
        {
            return new PasswordVault().Retrieve(KeyVaultResource, provider).Password;
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveKeyFromVault(string provider)
    {
        try
        {
            var vault = new PasswordVault();
            var existing = vault.Retrieve(KeyVaultResource, provider);
            vault.Remove(existing);
        }
        catch
        {
            // Nothing to remove.
        }
    }
}
