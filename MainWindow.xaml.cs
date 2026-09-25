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
    private const string CustomAiProviderName = "自定义模型";
    private const int DefaultWindowWidth = 960;
    private const int DefaultWindowHeight = 660;
    private const int MinimumWindowWidth = 420;
    private const int MinimumWindowHeight = 320;
    private const int MiniWindowWidth = 520;
    private const int MiniWindowHeight = 420;
    private const int MiniModeEnterWidth = 680;
    private const int MiniModeExitWidth = 760;
    private const int NavigationPaneCollapseWidth = 900;
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

    private static readonly List<string> CloudProviders = new() { "DeepSeek", "千问", "Kimi", "智谱" };
    private static readonly List<string> AiProviders = new(CloudProviders) { CustomAiProviderName };

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
    private StorageFile? _selectedTextFile;
    private CancellationTokenSource? _fileTranslationCts;
    private bool _isFileTranslating;
    private bool _isMiniMode;
    private bool _openNavigationPaneWhenSpaceReturns;
    private bool _isSelectionTranslating;
    private string? _pendingClipboardText;
    private SelectionTranslationOrigin? _selectionTranslationOrigin;
    private string? _pendingSelectionTranslationText;
    private CancellationTokenSource? _selectionTranslationTipCts;
    private int _selectionTranslationStart;
    private int _selectionTranslationLength;
    private bool _suppressSelectionTranslationEvents;
    private uint _lastHandledClipboardSequenceNumber;
    private readonly WindowProcedure _windowProcedure;
    private nint _windowHandle;
    private nint _previousWindowProcedure;

    public MainWindow()
    {
        InitializeComponent();
        _windowProcedure = WindowProcedureCallback;

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(TitleBarDragRegion);
        InstallMinimumSizeHandler();
        ResizeWindowInDips(DefaultWindowWidth, DefaultWindowHeight);
        AppWindow.Changed += AppWindow_Changed;

        _settings = AppSettingsStore.Load();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _translationService = new TranslationService(_httpClient);
        _machineTranslationService = new AliyunMachineTranslationService(_httpClient);
        _offlineModelService = new OfflineModelService(_httpClient);
        NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
        _speechOutput.PlaybackEnded += (_, _) => DispatcherQueue.TryEnqueue(() => SetSpeakingState(false));
        Closed += (_, _) =>
        {
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
        FileSourceLanguageComboBox.ItemsSource = Languages;
        FileTargetLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        FileSourceLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        InitializeImageModelSettings();
        SourceLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        ImageSourceLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        TargetLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);
        ImageTargetLanguageComboBox.DisplayMemberPath = nameof(LanguageOption.Display);

        var sourceIndex = Math.Clamp(_settings.SourceLanguageIndex, 0, Languages.Count - 1);
        var targetIndex = Math.Clamp(_settings.TargetLanguageIndex, 0, Languages.Count - 1);
        SourceLanguageComboBox.SelectedIndex = sourceIndex;
        ImageSourceLanguageComboBox.SelectedIndex = sourceIndex;
        FileSourceLanguageComboBox.SelectedIndex = sourceIndex;
        RefreshTargetLanguageOptions(
            TargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);
        RefreshTargetLanguageOptions(
            ImageTargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);
        RefreshTargetLanguageOptions(
            FileTargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);

        CustomPromptTextBox.Text = _settings.CustomPrompt;
        AiPromptStyleComboBox.ItemsSource = new[] { Localization.Text("标准"), Localization.Text("正式"), Localization.Text("自然"), Localization.Text("简洁") };
        AiPromptStyleComboBox.SelectedIndex = Math.Clamp(_settings.AiPromptStyleIndex, 0, 3);
        AiIncludeLanguageDetailsCheckBox.IsChecked = _settings.AiIncludeLanguageDetails;
        AiRememberKeyCheckBox.IsChecked = _settings.RememberKeys;
        ImageRememberKeyCheckBox.IsChecked = _settings.RememberImageKeys;
        AliyunRememberKeyCheckBox.IsChecked = _settings.RememberAliyunKeys;
        ThemeComboBox.ItemsSource = new[] { Localization.Text("跟随系统"), Localization.Text("浅色"), Localization.Text("深色") };
        ThemeComboBox.SelectedIndex = Math.Clamp(_settings.ThemeIndex, 0, 2);
        MicaBackdropCheckBox.IsChecked = _settings.MicaBackdropEnabled;
        EnterToTranslateToggleSwitch.IsOn = _settings.EnterToTranslate;
        UpdateEnterShortcutLabels();
        TranslationHistoryLimitComboBox.ItemsSource = new[] { Localization.Text("不保存翻译历史"), Localization.Text("5 条"), Localization.Text("20 条"), Localization.Text("无上限") };
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        TranslationHistoryLimitComboBox.SelectedIndex = Array.IndexOf(
            TranslationHistoryLimits,
            _settings.TranslationHistoryLimit);
        TrimTranslationHistory();
        RefreshTranslationHistory();
        RefreshOfflineModelList();
        UpdateOfflineModelDownloadAvailability();

        _suppressEvents = false;

        ApplyTheme();
        ApplyMicaBackdrop();
        AboutVersionTextBlock.Text = Localization.Text("版本 ") + GetApplicationVersion();
        SidebarNavigationView.SelectedItem = HomeNavigationItem;
        NavigateTo("Home", addBackEntry: false);
        InitializeProviderSettings();
        FileModeComboBox.SelectedIndex = ModeComboBox.SelectedIndex;
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

    private void EnterToTranslateToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.EnterToTranslate = EnterToTranslateToggleSwitch.IsOn;
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

        AiProviderComboBox.ItemsSource = AiProviders;
        _currentProvider = AiProviders.Contains(_settings.ProviderName)
            ? _settings.ProviderName
            : "DeepSeek";
        AiProviderComboBox.SelectedItem = _currentProvider;

        var profile = GetProviderProfile(_currentProvider);
        AiEndpointTextBox.Text = _settings.Endpoints.TryGetValue(_currentProvider, out var endpoint)
            && !string.IsNullOrWhiteSpace(endpoint)
                ? endpoint
                : _currentProvider == CustomAiProviderName ? string.Empty : profile.DefaultEndpoint;
        AiEndpointTextBox.PlaceholderText = _currentProvider == CustomAiProviderName
            ? "https://example.com/v1"
            : profile.DefaultEndpoint;
        UpdateCustomProviderHint();

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
        AiEndpointTextBox.Text = _settings.Endpoints.TryGetValue(provider, out var endpoint)
            && !string.IsNullOrWhiteSpace(endpoint)
                ? endpoint
                : provider == CustomAiProviderName ? string.Empty : profile.DefaultEndpoint;
        AiEndpointTextBox.PlaceholderText = provider == CustomAiProviderName
            ? "https://example.com/v1"
            : profile.DefaultEndpoint;
        UpdateCustomProviderHint();

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

    private void AiEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || string.IsNullOrEmpty(_currentProvider))
        {
            return;
        }

        _settings.Endpoints[_currentProvider] = AiEndpointTextBox.Text.Trim();
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

    private List<string> GetAvailableAiModels(ProviderProfile profile)
    {
        _settings.AvailableModels ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!_settings.AvailableModels.TryGetValue(profile.Name, out var models) || models is null)
        {
            models = profile.DefaultModels
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _settings.AvailableModels[profile.Name] = models;
        }

        return models;
    }

    private void RefreshAiModelOptions(ProviderProfile profile, string? preferredModel)
    {
        var models = GetAvailableAiModels(profile);
        if (!string.IsNullOrWhiteSpace(preferredModel)
            && !models.Contains(preferredModel, StringComparer.OrdinalIgnoreCase))
        {
            models.Add(preferredModel);
        }

        AiModelsListView.ItemsSource = models.ToList();
        AiModelsListView.SelectedItem = models.FirstOrDefault(model =>
            string.Equals(model, preferredModel, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault();
    }

    private async void AddAiModelButton_Click(object sender, RoutedEventArgs e)
    {
        var modelTextBox = new TextBox
        {
            Header = Localization.Text("模型名称"),
            PlaceholderText = Localization.Text("输入自定义模型名称"),
        };
        var apiKeyPasswordBox = new PasswordBox
        {
            Header = "API Key",
            PlaceholderText = Localization.Text("可稍后通过列表右侧按钮修改"),
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(modelTextBox);
        content.Children.Add(apiKeyPasswordBox);
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("添加自定义模型"),
            Content = content,
            PrimaryButtonText = Localization.Text("添加"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var model = modelTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowInfo(Localization.Text("请输入要添加的模型名称。"), InfoBarSeverity.Warning);
            return;
        }

        var profile = GetProviderProfile(_currentProvider);
        var models = GetAvailableAiModels(profile);
        var existingModel = models.FirstOrDefault(item =>
            string.Equals(item, model, StringComparison.OrdinalIgnoreCase));
        if (existingModel is null)
        {
            models.Add(model);
            existingModel = model;
        }

        _suppressEvents = true;
        RefreshAiModelOptions(profile, existingModel);
        _suppressEvents = false;
        _settings.Models[_currentProvider] = existingModel;

        var apiKey = apiKeyPasswordBox.Password.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            SetApiKeyForModel(_currentProvider, existingModel, apiKey);
        }

        SaveSettings();
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
            var isFileModeSelected = FileModeComboBox.SelectedIndex == modeIndex;

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
                FileModeComboBox.SelectedIndex = 2;
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
        UpdateModeItems(FileModeComboBox);

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

    private void UpdateCustomProviderHint()
    {
        CustomProviderHintText.Visibility = _currentProvider == CustomAiProviderName
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void DeleteAiModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string model })
        {
            return;
        }

        var profile = GetProviderProfile(_currentProvider);
        var models = GetAvailableAiModels(profile);
        if (models.Count <= 1)
        {
            ShowInfo(Localization.Text("至少需要保留一个 AI 翻译模型。"), InfoBarSeverity.Warning);
            return;
        }

        var selectedModel = GetSelectedAiModel();
        models.RemoveAll(item => string.Equals(item, model, StringComparison.OrdinalIgnoreCase));
        var preferredModel = string.Equals(selectedModel, model, StringComparison.OrdinalIgnoreCase)
            ? models.FirstOrDefault()
            : selectedModel;
        var credentialKey = GetModelCredentialKey(_currentProvider, model);
        _apiKeys.Remove(credentialKey);
        RemoveKeyFromVault(credentialKey);
        _suppressEvents = true;
        RefreshAiModelOptions(profile, preferredModel);
        _suppressEvents = false;
        _settings.Models[_currentProvider] = GetSelectedAiModel();
        SaveSettings();
    }

    private async void EditAiModelApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string model })
        {
            return;
        }

        var passwordBox = new PasswordBox
        {
            Header = "API Key",
            Password = GetApiKeyForModel(_currentProvider, model),
            PlaceholderText = Localization.Text("输入 API Key"),
        };
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("修改 {0} 的 API Key", model),
            Content = passwordBox,
            PrimaryButtonText = Localization.Text("保存"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            SetApiKeyForModel(_currentProvider, model, passwordBox.Password.Trim());
            ShowInfo(Localization.Text("API Key 已更新。"), InfoBarSeverity.Success);
        }
    }

    private static string GetModelCredentialKey(string provider, string model) =>
        $"{provider}:Model:{model}";

    private static string GetImageModelCredentialKey(string provider, string model) =>
        $"Image:{provider}:Model:{model}";

    private string GetApiKeyForModel(string provider, string model)
    {
        var credentialKey = GetModelCredentialKey(provider, model);
        if (_apiKeys.TryGetValue(credentialKey, out var apiKey))
        {
            return apiKey;
        }

        if (_settings.RememberKeys)
        {
            apiKey = LoadKeyFromVault(credentialKey) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _apiKeys[credentialKey] = apiKey;
                return apiKey;
            }
        }

        return _apiKeys.GetValueOrDefault(provider)
            ?? (_settings.RememberKeys ? LoadKeyFromVault(provider) : null)
            ?? string.Empty;
    }

    private void SetApiKeyForModel(string provider, string model, string apiKey)
    {
        var credentialKey = GetModelCredentialKey(provider, model);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _apiKeys.Remove(credentialKey);
            RemoveKeyFromVault(credentialKey);
            return;
        }

        _apiKeys[credentialKey] = apiKey;
        if (_settings.RememberKeys)
        {
            SaveKeyToVault(credentialKey, apiKey);
        }
    }

    private void AiRememberKeyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.RememberKeys = AiRememberKeyCheckBox.IsChecked == true;
        SaveSettings();

        var models = GetAvailableAiModels(GetProviderProfile(_currentProvider));
        if (_settings.RememberKeys)
        {
            foreach (var model in models)
            {
                var credentialKey = GetModelCredentialKey(_currentProvider, model);
                if (_apiKeys.TryGetValue(credentialKey, out var apiKey)
                    && !string.IsNullOrWhiteSpace(apiKey))
                {
                    SaveKeyToVault(credentialKey, apiKey);
                }
            }
        }
        else
        {
            foreach (var model in models)
            {
                RemoveKeyFromVault(GetModelCredentialKey(_currentProvider, model));
            }

            RemoveKeyFromVault(_currentProvider);
        }
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

    private string GetApiKeyForImageModel(string provider, string model)
    {
        var credentialKey = GetImageModelCredentialKey(provider, model);
        if (_apiKeys.TryGetValue(credentialKey, out var apiKey))
        {
            return apiKey;
        }

        if (_settings.RememberImageKeys)
        {
            apiKey = LoadKeyFromVault(credentialKey) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _apiKeys[credentialKey] = apiKey;
            }
        }

        return apiKey ?? string.Empty;
    }

    private void SetApiKeyForImageModel(string provider, string model, string apiKey)
    {
        var credentialKey = GetImageModelCredentialKey(provider, model);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _apiKeys.Remove(credentialKey);
            RemoveKeyFromVault(credentialKey);
            return;
        }

        _apiKeys[credentialKey] = apiKey;
        if (_settings.RememberImageKeys)
        {
            SaveKeyToVault(credentialKey, apiKey);
        }
    }

    private void ImageRememberKeyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.RememberImageKeys = ImageRememberKeyCheckBox.IsChecked == true;
        if (_settings.RememberImageKeys)
        {
            foreach (var provider in CloudProviders)
            {
                foreach (var model in _settings.ImageModels)
                {
                    var credentialKey = GetImageModelCredentialKey(provider, model);
                    if (_apiKeys.TryGetValue(credentialKey, out var apiKey)
                        && !string.IsNullOrWhiteSpace(apiKey))
                    {
                        SaveKeyToVault(credentialKey, apiKey);
                    }
                }
            }
        }
        else
        {
            foreach (var provider in CloudProviders)
            {
                foreach (var model in _settings.ImageModels)
                {
                    RemoveKeyFromVault(GetImageModelCredentialKey(provider, model));
                }
            }
        }

        SaveSettings();
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
            _onlineOfflineModels = (await _offlineModelService.GetAvailableModelsAsync(CancellationToken.None)).ToList();
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
            var installed = _offlineModelService.GetInstalledModels()
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
        var installed = _offlineModelService.GetInstalledModels()
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
            OfflineModelsStatusTextBlock.Text = Localization.Text("网络不可用，已下载 {0} 个模型仍可使用", _offlineModelService.GetInstalledModels().Count);
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
        SaveSettings();
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

    private void SourceTextBox_SelectionChanged(object sender, RoutedEventArgs e) =>
        UpdateSelectionTranslationTeachingTip(SourceTextBox, SelectionTranslationOrigin.Source);

    private void OutputTextBox_SelectionChanged(object sender, RoutedEventArgs e) =>
        UpdateSelectionTranslationTeachingTip(OutputTextBox, SelectionTranslationOrigin.Output);

    private void UpdateSelectionTranslationTeachingTip(
        TextBox textBox,
        SelectionTranslationOrigin origin)
    {
        if (_suppressSelectionTranslationEvents)
        {
            return;
        }

        CloseSelectionTranslationTeachingTip();
        if (_isMiniMode
            || _currentPageTag != "Home"
            || _isTranslating
            || _isSelectionTranslating
            || textBox.SelectionLength <= 0
            || string.IsNullOrWhiteSpace(textBox.SelectedText))
        {
            return;
        }

        _selectionTranslationOrigin = origin;
        _pendingSelectionTranslationText = textBox.SelectedText;
        _selectionTranslationStart = textBox.SelectionStart;
        _selectionTranslationLength = textBox.SelectionLength;
        _selectionTranslationTipCts = new CancellationTokenSource();
        _ = ShowSelectionTranslationTeachingTipAsync(
            textBox,
            origin,
            _selectionTranslationTipCts.Token);
    }

    private async Task ShowSelectionTranslationTeachingTipAsync(
        TextBox textBox,
        SelectionTranslationOrigin origin,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested
            || _selectionTranslationOrigin != origin
            || textBox.SelectionStart != _selectionTranslationStart
            || textBox.SelectionLength != _selectionTranslationLength
            || !string.Equals(textBox.SelectedText, _pendingSelectionTranslationText, StringComparison.Ordinal))
        {
            return;
        }

        SelectionTranslationTeachingTip.Target = textBox;
        SelectionTranslationTeachingTip.Subtitle = Localization.Text(
            "将选中文字翻译成{0}",
            origin == SelectionTranslationOrigin.Source
                ? TargetLanguageComboBox.SelectedItem is LanguageOption target ? target.Display : Localization.Text("英语")
                : SourceLanguageComboBox.SelectedItem is LanguageOption source && source.ApiCode != "auto"
                    ? source.Display
                    : Localization.Text("源语言"));
        SelectionTranslationTeachingTip.IsOpen = true;
        // TeachingTip can take focus after opening. Restore the selection on the next UI turn.
        _ = RestoreSelectionHighlightAsync(textBox, origin);
    }

    private async Task RestoreSelectionHighlightAsync(TextBox textBox, SelectionTranslationOrigin origin)
    {
        await Task.Delay(50);
        RestoreSelectionHighlight(textBox, origin);
    }

    private void RestoreSelectionHighlight(TextBox textBox, SelectionTranslationOrigin origin)
    {
        if (!SelectionTranslationTeachingTip.IsOpen
            || _selectionTranslationOrigin != origin
            || _selectionTranslationStart < 0
            || _selectionTranslationStart > textBox.Text.Length
            || _selectionTranslationLength > textBox.Text.Length - _selectionTranslationStart
            || !string.Equals(
                textBox.Text.Substring(_selectionTranslationStart, _selectionTranslationLength),
                _pendingSelectionTranslationText,
                StringComparison.Ordinal))
        {
            return;
        }

        _suppressSelectionTranslationEvents = true;
        try
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectionStart = _selectionTranslationStart;
            textBox.SelectionLength = _selectionTranslationLength;
        }
        finally
        {
            _suppressSelectionTranslationEvents = false;
        }
    }

    private void CloseSelectionTranslationTeachingTip()
    {
        _selectionTranslationTipCts?.Cancel();
        _selectionTranslationTipCts?.Dispose();
        _selectionTranslationTipCts = null;
        _selectionTranslationOrigin = null;
        _pendingSelectionTranslationText = null;
        SelectionTranslationTeachingTip.IsOpen = false;
    }

    private async void SelectionTranslationTeachingTip_ActionButtonClick(TeachingTip sender, object args)
    {
        var origin = _selectionTranslationOrigin;
        var selectedText = _pendingSelectionTranslationText;
        CloseSelectionTranslationTeachingTip();
        if (origin is null || string.IsNullOrWhiteSpace(selectedText) || _isTranslating)
        {
            return;
        }

        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
        if (origin == SelectionTranslationOrigin.Output)
        {
            if (sourceLanguage.ApiCode == "auto")
            {
                ShowInfo(Localization.Text("请先指定源语言，再翻译选中的译文。"), InfoBarSeverity.Warning);
                return;
            }

            (sourceLanguage, targetLanguage) = (targetLanguage, sourceLanguage);
        }

        try
        {
            _isSelectionTranslating = true;
            UpdateUiState();
            var translatedText = await TranslateTextAsync(
                selectedText,
                Math.Clamp(ModeComboBox.SelectedIndex, 0, 2),
                sourceLanguage,
                targetLanguage,
                CancellationToken.None);
            if (origin == SelectionTranslationOrigin.Source)
            {
                OutputTextBox.Text = translatedText;
                OutputTextBox.Focus(FocusState.Programmatic);
                OutputTextBox.SelectionStart = OutputTextBox.Text.Length;
                OutputTextBox.SelectionLength = 0;
            }
            else
            {
                var outputText = OutputTextBox.Text;
                if (_selectionTranslationStart < 0
                    || _selectionTranslationStart > outputText.Length
                    || _selectionTranslationLength > outputText.Length - _selectionTranslationStart
                    || !string.Equals(
                        outputText.Substring(_selectionTranslationStart, _selectionTranslationLength),
                        selectedText,
                        StringComparison.Ordinal))
                {
                    ShowInfo(Localization.Text("选中的译文已发生变化，请重新选择。"), InfoBarSeverity.Warning);
                    return;
                }

                OutputTextBox.Text = outputText[.._selectionTranslationStart]
                    + translatedText
                    + outputText[(_selectionTranslationStart + _selectionTranslationLength)..];
                OutputTextBox.Focus(FocusState.Programmatic);
                OutputTextBox.SelectionStart = _selectionTranslationStart;
                OutputTextBox.SelectionLength = translatedText.Length;
            }

            ShowInfo(Localization.Text("翻译完成。"), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo(Localization.Text("已取消翻译。"), InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("选词翻译失败：{0}", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            _isSelectionTranslating = false;
            UpdateUiState();
        }
    }

    private void SelectionTranslationTeachingTip_CloseButtonClick(TeachingTip sender, object args) =>
        CloseSelectionTranslationTeachingTip();

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
            _suppressEvents = true;
            ImageModelComboBox.SelectedItem = model;
            _suppressEvents = false;
            SaveSettings();
        }
    }

    private void ImageEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.ImageEndpoint = ImageEndpointTextBox.Text.Trim();
        _settings.ImageEndpoints[GetImageProvider()] = _settings.ImageEndpoint;
        SaveSettings();
    }

    private void ImageProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ImageProviderComboBox.SelectedItem is not string provider)
        {
            return;
        }

        _settings.ImageProviderName = provider;
        _suppressEvents = true;
        ImageEndpointTextBox.Text = GetImageEndpoint(provider);
        ImageEndpointTextBox.PlaceholderText = GetProviderProfile(provider).DefaultEndpoint;
        _suppressEvents = false;
        _settings.ImageEndpoint = ImageEndpointTextBox.Text.Trim();
        SaveSettings();
    }

    private void InitializeImageModelSettings()
    {
        _settings.ImageModels ??= new List<string>();
        _settings.ImageEndpoints ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var provider = CloudProviders.Contains(_settings.ImageProviderName)
            ? _settings.ImageProviderName
            : "DeepSeek";
        _settings.ImageProviderName = provider;
        if (!_settings.ImageEndpoints.ContainsKey(provider)
            && !string.IsNullOrWhiteSpace(_settings.ImageEndpoint))
        {
            _settings.ImageEndpoints[provider] = _settings.ImageEndpoint;
        }

        ImageProviderComboBox.ItemsSource = CloudProviders;
        ImageProviderComboBox.SelectedItem = provider;
        ImageEndpointTextBox.Text = GetImageEndpoint(provider);
        ImageEndpointTextBox.PlaceholderText = GetProviderProfile(provider).DefaultEndpoint;
        _settings.ImageEndpoint = ImageEndpointTextBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(_settings.ImageModel)
            && !_settings.ImageModels.Contains(_settings.ImageModel, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ImageModels.Add(_settings.ImageModel);
        }

        if (_settings.ImageModels.Count == 0)
        {
            _settings.ImageModels.Add(GetDefaultImageModel(provider));
        }

        RefreshImageModelOptions(_settings.ImageModel);
    }

    private void RefreshImageModelOptions(string? preferredModel)
    {
        var models = _settings.ImageModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.ImageModels = models;
        ImageModelsListView.ItemsSource = models.ToList();
        ImageModelComboBox.ItemsSource = models.ToList();
        var selectedModel = models.FirstOrDefault(model =>
            string.Equals(model, preferredModel, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault();
        ImageModelsListView.SelectedItem = selectedModel;
        ImageModelComboBox.SelectedItem = selectedModel;
    }

    private string GetImageProvider()
    {
        return ImageProviderComboBox.SelectedItem as string is { Length: > 0 } provider
            ? provider
            : _settings.ImageProviderName;
    }

    private string GetImageEndpoint(string provider)
    {
        return _settings.ImageEndpoints.TryGetValue(provider, out var endpoint)
            && !string.IsNullOrWhiteSpace(endpoint)
                ? endpoint
                : GetProviderProfile(provider).DefaultEndpoint;
    }

    private async void AddImageModelButton_Click(object sender, RoutedEventArgs e)
    {
        var modelTextBox = new TextBox
        {
            Header = Localization.Text("模型名称"),
            PlaceholderText = Localization.Text("输入支持图片的模型名称"),
        };
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("添加图片翻译模型"),
            Content = modelTextBox,
            PrimaryButtonText = Localization.Text("添加"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var model = modelTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowInfo(Localization.Text("请输入要添加的图片模型名称。"), InfoBarSeverity.Warning);
            return;
        }

        var existingModel = _settings.ImageModels.FirstOrDefault(item =>
            string.Equals(item, model, StringComparison.OrdinalIgnoreCase));
        if (existingModel is null)
        {
            _settings.ImageModels.Add(model);
            existingModel = model;
        }

        _suppressEvents = true;
        RefreshImageModelOptions(existingModel);
        _suppressEvents = false;
        _settings.ImageModel = existingModel;
        SaveSettings();
    }

    private void DeleteImageModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string model })
        {
            return;
        }

        if (_settings.ImageModels.Count <= 1)
        {
            ShowInfo(Localization.Text("至少需要保留一个图片翻译模型。"), InfoBarSeverity.Warning);
            return;
        }

        _settings.ImageModels.RemoveAll(item =>
            string.Equals(item, model, StringComparison.OrdinalIgnoreCase));
        var credentialKey = GetImageModelCredentialKey(GetImageProvider(), model);
        _apiKeys.Remove(credentialKey);
        RemoveKeyFromVault(credentialKey);
        var preferredModel = string.Equals(_settings.ImageModel, model, StringComparison.OrdinalIgnoreCase)
            ? _settings.ImageModels.FirstOrDefault()
            : _settings.ImageModel;
        _suppressEvents = true;
        RefreshImageModelOptions(preferredModel);
        _suppressEvents = false;
        _settings.ImageModel = ImageModelComboBox.SelectedItem?.ToString() ?? string.Empty;
        SaveSettings();
    }

    private async void EditImageModelApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string model })
        {
            return;
        }

        var provider = GetImageProvider();
        var passwordBox = new PasswordBox
        {
            Header = "API Key",
            Password = GetApiKeyForImageModel(provider, model),
            PlaceholderText = Localization.Text("输入 API Key"),
        };
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = Localization.Text("修改 {0} 的 API Key", model),
            Content = passwordBox,
            PrimaryButtonText = Localization.Text("保存"),
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            SetApiKeyForImageModel(provider, model, passwordBox.Password.Trim());
            ShowInfo(Localization.Text("API Key 已更新。"), InfoBarSeverity.Success);
        }
    }

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
        SourceTextBox.Text = string.Empty;
        SourceTextBox.Focus(FocusState.Programmatic);
    }

    private void TranslationHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("History");
    }

    private void ClearTranslationHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.TranslationHistory.Clear();
        RefreshTranslationHistory();
        SaveSettings();
        ShowInfo(Localization.Text("已清除翻译历史。"), InfoBarSeverity.Success);
    }

    private void TranslationHistoryListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string sourceText)
        {
            return;
        }

        SourceTextBox.Text = sourceText;
        NavigateTo("Home");
        SourceTextBox.Focus(FocusState.Programmatic);
        TranslateButton_Click(TranslateButton, new RoutedEventArgs());
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
            ClipboardPasteTeachingTip.Target = _isMiniMode ? MiniSourceTextBox : TranslationHistoryButton;
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
            MiniSourceTextBox.IsReadOnly = false;
            MiniSourceTextBox.Text = text;
            MiniTranslateButton.IsEnabled = true;
            MiniSourceTextBox.Focus(FocusState.Programmatic);
            MiniSourceTextBox.SelectionStart = text.Length;
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

    private void FileSourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var source = FileSourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        RefreshTargetLanguageOptions(FileTargetLanguageComboBox, source, FileTargetLanguageComboBox.SelectedItem as LanguageOption);
    }

    private void FileTargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFileModeAvailability();

    private void FileModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFileModeAvailability();

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
            CloseSelectionTranslationTeachingTip();
        }

        var pageTitle = tag switch
        {
            "Home" => Localization.AppName,
            "Image" => Localization.Text("图片翻译"),
            "File" => Localization.Text("文件翻译"),
            "History" => Localization.Text("翻译历史"),
            "Settings" => Localization.Text("设置"),
            "About" => Localization.Text("关于"),
            _ => Localization.AppName,
        };
        PageTitleTextBlock.Text = pageTitle;
        Title = tag == "Home" ? Localization.AppName : $"{pageTitle} — {Localization.AppName}";

        TranslationToolbar.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        HomePageGrid.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        TranslationHistoryPageGrid.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageScrollViewer.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ImagePageGrid.Visibility = tag == "Image" ? Visibility.Visible : Visibility.Collapsed;
        FilePageGrid.Visibility = tag == "File" ? Visibility.Visible : Visibility.Collapsed;
        AboutPagePanel.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;

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
        if (!args.DidSizeChange)
        {
            return;
        }

        var logicalWidth = sender.ClientSize.Width / GetWindowScale();
        if (!_isMiniMode && logicalWidth <= MiniModeEnterWidth)
        {
            SetMiniMode(true, resizeWindow: false);
            return;
        }

        if (_isMiniMode && logicalWidth >= MiniModeExitWidth)
        {
            SetMiniMode(false, resizeWindow: false);
        }

        UpdateNavigationPaneForWindowWidth(logicalWidth);
    }

    private void SetMiniMode(bool mini, bool resizeWindow)
    {
        if (_isMiniMode == mini)
        {
            if (resizeWindow)
            {
                ResizeWindowInDips(
                    mini ? MiniWindowWidth : DefaultWindowWidth,
                    mini ? MiniWindowHeight : DefaultWindowHeight);
            }

            return;
        }

        if (mini && SidebarNavigationView.IsPaneOpen)
        {
            _openNavigationPaneWhenSpaceReturns = true;
        }

        _isMiniMode = mini;
        _pendingClipboardText = null;
        ClipboardPasteTeachingTip.IsOpen = false;
        CloseSelectionTranslationTeachingTip();
        ClearNavigationHistory();
        MiniPageGrid.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        SidebarNavigationView.Visibility = Visibility.Visible;
        if (mini)
        {
            // Collapse every navigation surface before showing the mini overlay.
            SidebarNavigationView.IsPaneOpen = false;
            SidebarNavigationView.IsPaneToggleButtonVisible = false;
            SidebarNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
            SidebarNavigationView.CompactPaneLength = 0;
            SidebarNavigationView.IsPaneVisible = false;
        }
        else
        {
            SidebarNavigationView.IsPaneVisible = true;
            SidebarNavigationView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
            SidebarNavigationView.CompactPaneLength = 48;
            SidebarNavigationView.IsPaneToggleButtonVisible = true;
            SidebarNavigationView.IsPaneOpen = false;
        }
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
                mini ? MiniWindowWidth : DefaultWindowWidth,
                mini ? MiniWindowHeight : DefaultWindowHeight);
        }

        UpdateNavigationPaneForWindowWidth(AppWindow.ClientSize.Width / GetWindowScale());
        if (mini)
        {
            MiniTargetLanguageComboBox.ItemsSource = Languages.Skip(1).ToList();
            MiniTargetLanguageComboBox.SelectedIndex = Math.Clamp(
                _settings.TargetLanguageIndex - 1,
                0,
                Languages.Count - 2);
            if (!MiniSourceTextBox.IsReadOnly)
            {
                MiniSourceTextBox.Focus(FocusState.Programmatic);
            }
        }
    }

    private void MiniClearButton_Click(object sender, RoutedEventArgs e)
    {
        MiniSourceTextBox.IsReadOnly = false;
        MiniSourceTextBox.Text = string.Empty;
        MiniTranslateButton.IsEnabled = true;
        MiniSourceTextBox.Focus(FocusState.Programmatic);
    }

    private void MiniSourceTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !MiniSourceTextBox.IsReadOnly)
        {
            e.Handled = true;
            _ = MiniTranslateButton_ClickAsync();
        }
    }

    private void MiniTranslateButton_Click(object sender, RoutedEventArgs e) => _ = MiniTranslateButton_ClickAsync();

    private async Task MiniTranslateButton_ClickAsync()
    {
        var text = MiniSourceTextBox.Text.Trim();
        if (text.Length == 0 || MiniTargetLanguageComboBox.SelectedItem is not LanguageOption targetLanguage)
        {
            return;
        }

        try
        {
            MiniTranslateButton.IsEnabled = false;
            MiniClearButton.IsEnabled = false;
            MiniSourceTextBox.IsReadOnly = true;
            MiniProgressRing.IsActive = true;
            var result = await TranslateTextAsync(
                text,
                Math.Clamp(ModeComboBox.SelectedIndex, 0, 2),
                Languages[0],
                targetLanguage,
                CancellationToken.None);
            MiniSourceTextBox.Text = result;
            AddTranslationHistory(text);
        }
        catch (Exception ex)
        {
            MiniSourceTextBox.IsReadOnly = false;
            ShowInfo(Localization.Text("翻译失败：") + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            MiniProgressRing.IsActive = false;
            MiniClearButton.IsEnabled = true;
            MiniTranslateButton.IsEnabled = !MiniSourceTextBox.IsReadOnly;
        }
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

        var endpoint = ImageEndpointTextBox.Text.Trim();
        var model = ImageModelComboBox.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        var providerKey = GetImageProvider();
        var apiKey = GetApiKeyForImageModel(providerKey, model);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = GetApiKeyForModel(providerKey, model);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var selectedAiModel = GetSelectedAiModelForProvider(providerKey);
            apiKey = GetApiKeyForImageModel(providerKey, selectedAiModel);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = GetApiKeyForModel(providerKey, selectedAiModel);
            }
        }

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
            ShowInfo(Localization.Text("图片翻译使用{0} API，请先在设置中为对应模型填写 API Key。", Localization.ProviderName(providerKey)), InfoBarSeverity.Warning);
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
                    var result = await TranslateImageFileAsync(
                        file,
                        endpoint,
                        apiKey,
                        model,
                        systemPrompt);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(Localization.Text("翻译失败：{0}", ex.Message));
                }
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
        string systemPrompt)
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

        return await _translationService.TranslateImageAsync(request, CancellationToken.None);
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

    private async void PickTextFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".txt");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        const ulong maximumFileBytes = 5UL * 1024 * 1024;
        if (properties.Size > maximumFileBytes)
        {
            ShowInfo(Localization.Text("文件超过 5 MB 上限，无法进行文件翻译。"), InfoBarSeverity.Warning);
            return;
        }

        _selectedTextFile = file;
        SelectedTextFilePathText.Text = file.Path;
        SelectedTextFileSizeText.Text = $"{FormatFileSize(properties.Size)}";
        FileOutputTextBox.Text = string.Empty;
        FileProgressText.Text = string.Empty;
        UpdateFileModeAvailability();
    }

    private async void TranslateFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isFileTranslating || _selectedTextFile is null)
        {
            return;
        }

        var configurationError = GetTranslationConfigurationError(FileModeComboBox.SelectedIndex);
        if (configurationError is not null)
        {
            ShowInfo(configurationError, InfoBarSeverity.Warning);
            UpdateFileModeAvailability();
            return;
        }

        try
        {
            var sourceText = await ReadTextFileAsync(_selectedTextFile.Path);
            if (sourceText.Length == 0)
            {
                ShowInfo(Localization.Text("所选文本文件为空。"), InfoBarSeverity.Warning);
                return;
            }

            var chunks = SplitTextIntoChunks(sourceText);
            if (chunks.Count == 0)
            {
                ShowInfo(Localization.Text("所选文本文件为空。"), InfoBarSeverity.Warning);
                return;
            }

            var sourceLanguage = FileSourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
            var targetLanguage = FileTargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
            _fileTranslationCts = new CancellationTokenSource();
            _isFileTranslating = true;
            FileOutputTextBox.Text = string.Empty;
            FileProgressRing.IsActive = true;
            FileProgressText.Text = Localization.Text("第 0/{0} 块", chunks.Count);
            ShowInfo(Localization.Text("正在翻译文件..."), InfoBarSeverity.Informational);
            UpdateFileModeAvailability();

            var translatedChunks = new List<string>(chunks.Count);
            for (var index = 0; index < chunks.Count; index++)
            {
                _fileTranslationCts.Token.ThrowIfCancellationRequested();
                try
                {
                    translatedChunks.Add(await TranslateFileChunkWithRetryAsync(
                        chunks[index],
                        FileModeComboBox.SelectedIndex,
                        sourceLanguage,
                        targetLanguage,
                        _fileTranslationCts.Token));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ShowInfo(Localization.Text("第 {0}/{1} 块翻译失败：{2}", index + 1, chunks.Count, ex.Message), InfoBarSeverity.Error);
                    return;
                }

                FileProgressText.Text = Localization.Text("第 {0}/{1} 块", index + 1, chunks.Count);
            }

            FileOutputTextBox.Text = string.Concat(translatedChunks);
            ShowInfo(Localization.Text("文件翻译完成。"), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            FileOutputTextBox.Text = string.Empty;
            ShowInfo(Localization.Text("已取消文件翻译，未生成任何文件。"), InfoBarSeverity.Warning);
        }
        catch (InvalidDataException ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("读取或翻译文件失败：") + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isFileTranslating = false;
            _fileTranslationCts?.Dispose();
            _fileTranslationCts = null;
            FileProgressRing.IsActive = false;
            UpdateFileModeAvailability();
        }
    }

    private async void ClearTextFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isFileTranslating || _selectedTextFile is null)
        {
            return;
        }

        if (!await ConfirmClearSelectionAsync(Localization.Text("清除已选文件"), Localization.Text("将清除已选文件和当前译文。")))
        {
            return;
        }

        _selectedTextFile = null;
        SelectedTextFilePathText.Text = Localization.Text("尚未选择文件");
        SelectedTextFileSizeText.Text = string.Empty;
        FileOutputTextBox.Text = string.Empty;
        FileProgressText.Text = string.Empty;
        UpdateFileModeAvailability();
        ShowInfo(Localization.Text("已清除选择的文件。"), InfoBarSeverity.Success);
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

    private void CopyFileOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FileOutputTextBox.Text))
        {
            return;
        }

        SetClipboardText(FileOutputTextBox.Text);
        ShowInfo(Localization.Text("文件译文已复制到剪贴板。"), InfoBarSeverity.Success);
    }

    private async void ExportFileOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTextFile is null || string.IsNullOrEmpty(FileOutputTextBox.Text))
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_selectedTextFile.Name) + Localization.Text("_译文"),
        };
        picker.FileTypeChoices.Add(Localization.Text("文本文件"), new List<string> { ".txt" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(file.Path, FileOutputTextBox.Text, new UTF8Encoding(false));
            ShowInfo(Localization.Text("译文已导出到：") + file.Path, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("导出失败：") + ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task<string> TranslateFileChunkWithRetryAsync(
        string chunk,
        int mode,
        LanguageOption sourceLanguage,
        LanguageOption targetLanguage,
        CancellationToken cancellationToken)
    {
        try
        {
            return RestoreOriginalLineEndings(
                await TranslateTextAsync(chunk, mode, sourceLanguage, targetLanguage, cancellationToken),
                chunk);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return RestoreOriginalLineEndings(
                await TranslateTextAsync(chunk, mode, sourceLanguage, targetLanguage, cancellationToken),
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
        if (!_settings.EnterToTranslate
            || e.Key != Windows.System.VirtualKey.Enter
            || GetKeyState((int)Windows.System.VirtualKey.Shift) < 0
            || GetKeyState((int)Windows.System.VirtualKey.Control) < 0
            || GetKeyState((int)Windows.System.VirtualKey.Menu) < 0)
        {
            return;
        }

        e.Handled = true;
        if (!_isTranslating && !string.IsNullOrWhiteSpace(SourceTextBox.Text))
        {
            TranslateButton_Click(TranslateButton, new RoutedEventArgs());
        }
    }

    private void UpdateNavigationPaneForWindowWidth(double logicalWidth)
    {
        if (_isMiniMode || !SidebarNavigationView.IsPaneVisible)
        {
            return;
        }

        if (logicalWidth < NavigationPaneCollapseWidth && SidebarNavigationView.IsPaneOpen)
        {
            _openNavigationPaneWhenSpaceReturns = true;
            SidebarNavigationView.IsPaneOpen = false;
        }
        else if (logicalWidth >= DefaultWindowWidth && _openNavigationPaneWhenSpaceReturns)
        {
            SidebarNavigationView.IsPaneOpen = true;
            _openNavigationPaneWhenSpaceReturns = false;
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
            : AiEndpointTextBox.Text.Trim();
        var model = isLocalMode
            ? LocalModelTextBox.Text.Trim()
            : GetSelectedAiModel();
        var apiKey = isLocalMode
            ? null
            : GetApiKeyForModel(_currentProvider, model);
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
            ShowInfo(Localization.Text("请在设置中为当前模型填写 API Key。"), InfoBarSeverity.Warning);
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
        var systemPrompt = BuildDefaultPrompt(
            sourceLanguage,
            targetLanguage,
            AiPromptStyleComboBox.SelectedIndex,
            AiIncludeLanguageDetailsCheckBox.IsChecked == true);

        if (ModeComboBox.SelectedIndex != 0 && !string.IsNullOrWhiteSpace(CustomPromptTextBox.Text))
        {
            systemPrompt += "\n\n补充要求：" + CustomPromptTextBox.Text.Trim();
        }

        _cts = new CancellationTokenSource();
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
                _cts.Token);
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
                _cts.Token);
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
        CancellationToken cancellationToken)
    {
        if (mode == 0)
        {
            if (string.IsNullOrWhiteSpace(_aliyunAccessKeyId) || string.IsNullOrWhiteSpace(_aliyunAccessKeySecret))
            {
                throw new InvalidOperationException(Localization.Text("请填写阿里云机器翻译 AccessKey ID 和 AccessKey Secret。"));
            }

            return await _machineTranslationService.TranslateAsync(
                new MachineTranslationRequest(
                    AliyunServiceUrl,
                    _aliyunAccessKeyId,
                    _aliyunAccessKeySecret,
                    sourceLanguage.ApiCode,
                    targetLanguage.ApiCode,
                    text),
                cancellationToken);
        }

        var useLocalModel = mode == 2 && LocalTranslationSourceComboBox.SelectedIndex == 1;
        if (useLocalModel)
        {
            if (!IsBergamotRuntimeAvailable())
            {
                throw new InvalidOperationException(Localization.Text("当前发布包未包含 Mozilla Translations 本地翻译引擎。"));
            }

            if (string.IsNullOrWhiteSpace(_selectedOfflineModelPath))
            {
                throw new InvalidOperationException(Localization.Text("请先在设置中下载并选择离线语言模型。"));
            }

            return await _bergamotTranslationService.TranslateAsync(
                _selectedOfflineModelPath,
                text,
                cancellationToken);
        }

        var endpoint = mode == 2 ? LocalEndpointTextBox.Text.Trim() : AiEndpointTextBox.Text.Trim();
        var model = mode == 2 ? LocalModelTextBox.Text.Trim() : GetSelectedAiModel();
        var apiKey = mode == 2 ? null : GetApiKeyForModel(_currentProvider, model);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(Localization.Text("请填写接口地址。"));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(Localization.Text("请填写模型名称。"));
        }

        if (mode != 2 && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(Localization.Text("请在设置中为当前模型填写 API Key。"));
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

        return await _translationService.TranslateAsync(
            new TranslationRequest(endpoint, apiKey, model, systemPrompt, text),
            cancellationToken);
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

        var endpoint = mode == 2 ? LocalEndpointTextBox.Text : AiEndpointTextBox.Text;
        var model = mode == 2 ? LocalModelTextBox.Text : GetSelectedAiModel();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return mode == 2 ? Localization.Text("本地翻译未配置接口地址。") : Localization.Text("AI 翻译未配置接口地址。");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return mode == 2 ? Localization.Text("本地翻译未配置模型名称。") : Localization.Text("AI 翻译未配置模型名称。");
        }

        return mode == 2 || !string.IsNullOrWhiteSpace(GetApiKeyForModel(_currentProvider, model))
            ? null
            : Localization.Text("AI 翻译未配置当前模型的 API Key。");
    }

    private void UpdateFileModeAvailability()
    {
        if (FileModeComboBox is null)
        {
            return;
        }

        var selectedIndex = Math.Clamp(FileModeComboBox.SelectedIndex, 0, 2);
        var items = FileModeComboBox.Items.OfType<ComboBoxItem>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var isModeEnabled = IsTranslationModeEnabled(index);
            items[index].Visibility = isModeEnabled ? Visibility.Visible : Visibility.Collapsed;
            items[index].IsEnabled = isModeEnabled && GetTranslationConfigurationError(index) is null;
        }

        if (selectedIndex < items.Count && !items[selectedIndex].IsEnabled)
        {
            FileModeHintText.Text = GetTranslationConfigurationError(selectedIndex) ?? Localization.Text("当前模式未配置。");
        }
        else
        {
            FileModeHintText.Text = string.Empty;
        }

        TranslateFileButton.IsEnabled = !_isFileTranslating && _selectedTextFile is not null
            && selectedIndex < items.Count && items[selectedIndex].IsEnabled;
        ClearTextFileButton.IsEnabled = !_isFileTranslating && _selectedTextFile is not null;
        CopyFileOutputButton.IsEnabled = !_isFileTranslating && !string.IsNullOrEmpty(FileOutputTextBox.Text);
        ExportFileOutputButton.IsEnabled = !_isFileTranslating && !string.IsNullOrEmpty(FileOutputTextBox.Text);
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

    private enum SelectionTranslationOrigin
    {
        Source,
        Output,
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
            .ToList();
        TranslationHistoryListView.ItemsSource = history;
        TranslationHistoryEmptyText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearTranslationHistoryButton.IsEnabled = history.Count > 0;
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
        TranslateButton.IsEnabled = !_isTranslating && !_isSelectionTranslating && SourceTextBox.Text.Trim().Length > 0;
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
        _settings.EnterToTranslate = EnterToTranslateToggleSwitch.IsOn;
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption;
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption;
        _settings.SourceLanguageIndex = sourceLanguage is null ? 0 : Languages.IndexOf(sourceLanguage);
        _settings.TargetLanguageIndex = targetLanguage is null ? 3 : Languages.IndexOf(targetLanguage);
        _settings.CustomPrompt = CustomPromptTextBox.Text;
        _settings.AiPromptStyleIndex = Math.Clamp(AiPromptStyleComboBox.SelectedIndex, 0, 3);
        _settings.AiIncludeLanguageDetails = AiIncludeLanguageDetailsCheckBox.IsChecked == true;
        _settings.ImageEndpoint = ImageEndpointTextBox.Text.Trim();
        _settings.ImageProviderName = GetImageProvider();
        _settings.ImageEndpoints[_settings.ImageProviderName] = _settings.ImageEndpoint;
        _settings.ImageModel = ImageModelComboBox.SelectedItem?.ToString() ?? string.Empty;
        _settings.ImageModels = _settings.ImageModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.RememberImageKeys = ImageRememberKeyCheckBox.IsChecked == true;
        _settings.RememberKeys = AiRememberKeyCheckBox.IsChecked == true;
        _settings.RememberAliyunKeys = AliyunRememberKeyCheckBox.IsChecked == true;
        _settings.LocalTranslationSourceIndex = Math.Clamp(LocalTranslationSourceComboBox.SelectedIndex, 0, 1);
        _settings.LocalModelPath = _selectedOfflineModelPath;
        _settings.Endpoints["本地 AI"] = LocalEndpointTextBox.Text.Trim();
        _settings.Models["本地 AI"] = LocalModelTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(_currentProvider))
        {
            _settings.Endpoints[_currentProvider] = AiEndpointTextBox.Text.Trim();
            _settings.Models[_currentProvider] = GetSelectedAiModel();
        }

        AppSettingsStore.Save(_settings);
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
    {
        var instruction = source.PromptName == "自动检测"
            ? $"请自动识别用户输入的语言，并翻译成{target.PromptName}。"
            : $"请把用户输入的内容从{source.PromptName}翻译成{target.PromptName}。";
        var styleInstruction = styleIndex switch
        {
            1 => "使用正式、严谨、专业的表达方式。",
            2 => "使用自然、流畅、符合目标语言习惯的表达方式。",
            3 => "使用简洁、精炼的表达方式，在不遗漏信息的前提下避免冗余。",
            _ => "保持准确、清晰、自然的表达方式。",
        };

        if (includeLanguageDetails)
        {
            return $"你是一名专业的翻译引擎。{instruction}{styleInstruction}"
                + "先输出译文，必要时空一行，再用普通文字简要说明翻译选择、读音和词性；不适用的内容可以省略。只使用纯文本和自然换行，不使用 Markdown 或 HTML 语法，不添加标题、项目符号、编号、强调标记或代码块。保留原文的段落结构和专有名词。";
        }

        return $"你是一名专业的翻译引擎。{instruction}{styleInstruction}"
            + "只输出纯文本译文，保留原文的段落结构、换行和专有名词；不要添加解释、注释、读音、词性或任何额外内容。不要使用 Markdown 或 HTML 语法，不新增标题、项目符号、编号、强调标记或代码块。";
    }

    private static ProviderProfile GetProviderProfile(string provider) => provider switch
    {
        "DeepSeek" => new ProviderProfile(
            "DeepSeek",
            "https://api.deepseek.com",
            new[] { "deepseek-flash", "deepseek-v4-pro" },
            true),
        "千问" => new ProviderProfile(
            "千问",
            "https://ws-hb89wnirs0jbftdm.cn-beijing.maas.aliyuncs.com/compatible-mode/v1",
            new[] { "qwen3.7-plus", "qwen3.7-flash", "qwen3.7-max" },
            true),
        "Kimi" => new ProviderProfile(
            "Kimi",
            "https://api.moonshot.cn/v1",
            new[] { "kimi-k2.6" },
            true),
        "智谱" => new ProviderProfile(
            "智谱",
            "https://open.bigmodel.cn/api/paas/v4",
            new[] { "glm-5.3-flash" },
            true),
        CustomAiProviderName => new ProviderProfile(
            CustomAiProviderName,
            string.Empty,
            Array.Empty<string>(),
            true),
        _ => new ProviderProfile(
            "本地 AI",
            "http://localhost:11434/v1",
            Array.Empty<string>(),
            false),
    };

    private string GetSelectedAiModelForProvider(string provider)
    {
        if (_settings.Models.TryGetValue(provider, out var model)
            && !string.IsNullOrWhiteSpace(model))
        {
            return model.Trim();
        }

        return GetProviderProfile(provider).DefaultModels.FirstOrDefault() ?? string.Empty;
    }

    private static string GetDefaultImageModel(string provider) => provider switch
    {
        "DeepSeek" => "deepseek-flash",
        "Kimi" => "kimi-k2.6",
        "智谱" => "glm-5.3-flash",
        _ => "qwen3.5-ocr",
    };

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
