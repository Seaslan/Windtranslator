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
    private const int MinWindowWidth = 960;
    private const int MinWindowHeight = 660;
    private const double SettingsPanelMaxWidth = 760d;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmXButtonUp = 0x020C;
    private const uint WmSysKeyDown = 0x0104;
    private const int VkLeft = 0x25;
    private const int XButton1 = 1;
    private const int GwlWndProc = -4;
    private static readonly int[] TranslationHistoryLimits = { 0, 5, 20, -1 };

    private static readonly List<string> CloudProviders = new() { "DeepSeek", "千问", "Kimi", "智谱" };

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
    private readonly SpeechInputService _speechInput = new();
    private readonly SpeechOutputService _speechOutput = new();
    private readonly Dictionary<string, string> _apiKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings;

    private string _currentProvider = "DeepSeek";
    private string _aliyunAccessKeyId = string.Empty;
    private string _aliyunAccessKeySecret = string.Empty;
    private string _selectedOfflineModelPath = string.Empty;
    private bool _isTranslating;
    private bool _isImageTranslating;
    private bool _isStartingSpeechInput;
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
        AppWindow.Resize(new SizeInt32(MinWindowWidth, MinWindowHeight));

        _settings = AppSettingsStore.Load();
        _translationService = new TranslationService(_httpClient);
        _machineTranslationService = new AliyunMachineTranslationService(_httpClient);
        _offlineModelService = new OfflineModelService(_httpClient);
        NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
        _speechOutput.PlaybackEnded += (_, _) => DispatcherQueue.TryEnqueue(() => SetSpeakingState(false));
        Closed += (_, _) =>
        {
            NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
            _ = _speechInput.StopAsync();
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
    }

    private void InitializeUi()
    {
        _suppressEvents = true;

        ModeComboBox.ItemsSource = new[] { "API 翻译", "AI 翻译", "本地翻译" };
        ModeComboBox.SelectedIndex = Math.Clamp(_settings.ModeIndex, 0, 2);
        LocalTranslationSourceComboBox.ItemsSource = new[] { "本地接口", "Mozilla Translations 模型" };
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
        RefreshTargetLanguageOptions(
            TargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);
        RefreshTargetLanguageOptions(
            ImageTargetLanguageComboBox,
            Languages[sourceIndex],
            Languages[targetIndex]);

        CustomPromptTextBox.Text = _settings.CustomPrompt;
        AiPromptStyleComboBox.ItemsSource = new[] { "标准", "正式", "自然", "简洁" };
        AiPromptStyleComboBox.SelectedIndex = Math.Clamp(_settings.AiPromptStyleIndex, 0, 3);
        AiIncludeLanguageDetailsCheckBox.IsChecked = _settings.AiIncludeLanguageDetails;
        AiRememberKeyCheckBox.IsChecked = _settings.RememberKeys;
        ImageRememberKeyCheckBox.IsChecked = _settings.RememberImageKeys;
        AliyunRememberKeyCheckBox.IsChecked = _settings.RememberAliyunKeys;
        ThemeComboBox.ItemsSource = new[] { "跟随系统", "浅色", "深色" };
        ThemeComboBox.SelectedIndex = Math.Clamp(_settings.ThemeIndex, 0, 2);
        MicaBackdropCheckBox.IsChecked = _settings.MicaBackdropEnabled;
        TranslationHistoryLimitComboBox.ItemsSource = new[] { "不保存翻译历史", "5 条", "20 条", "无上限" };
        _settings.TranslationHistoryLimit = NormalizeTranslationHistoryLimit(_settings.TranslationHistoryLimit);
        TranslationHistoryLimitComboBox.SelectedIndex = Array.IndexOf(
            TranslationHistoryLimits,
            _settings.TranslationHistoryLimit);
        TrimTranslationHistory();
        RefreshTranslationHistory();
        RefreshOfflineModelList();
        UpdateOfflineModelDownloadAvailability();

        _suppressEvents = false;

        var aiPromptSettingsEnabled = ModeComboBox.SelectedIndex != 0;
        CustomPromptTextBox.IsEnabled = aiPromptSettingsEnabled;
        AiPromptStyleComboBox.IsEnabled = aiPromptSettingsEnabled;
        AiIncludeLanguageDetailsCheckBox.IsEnabled = aiPromptSettingsEnabled;
        ApplyTheme();
        ApplyMicaBackdrop();
        AboutVersionTextBlock.Text = "版本 " + GetApplicationVersion();
        SidebarNavigationView.SelectedItem = HomeNavigationItem;
        NavigateTo("Home", addBackEntry: false);
        InitializeProviderSettings();
        UpdateCounts();
        UpdateUiState();
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var isApi = ModeComboBox.SelectedIndex == 0;
        CustomPromptTextBox.IsEnabled = !isApi;
        AiPromptStyleComboBox.IsEnabled = !isApi;
        AiIncludeLanguageDetailsCheckBox.IsEnabled = !isApi;
        SaveSettings();
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

        AiProviderComboBox.ItemsSource = CloudProviders;
        _currentProvider = CloudProviders.Contains(_settings.ProviderName)
            ? _settings.ProviderName
            : "DeepSeek";
        AiProviderComboBox.SelectedItem = _currentProvider;

        var profile = GetProviderProfile(_currentProvider);
        AiEndpointTextBox.Text = _settings.Endpoints.TryGetValue(_currentProvider, out var endpoint)
            && !string.IsNullOrWhiteSpace(endpoint)
                ? endpoint
                : profile.DefaultEndpoint;
        AiEndpointTextBox.PlaceholderText = profile.DefaultEndpoint;

        var savedModel = _settings.Models.TryGetValue(_currentProvider, out var model)
            ? NormalizeProviderModelName(_currentProvider, model)
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
                : profile.DefaultEndpoint;
        AiEndpointTextBox.PlaceholderText = profile.DefaultEndpoint;

        var savedModel = _settings.Models.TryGetValue(provider, out var model)
            ? NormalizeProviderModelName(provider, model)
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
            Header = "模型名称",
            PlaceholderText = "输入模型名称",
        };
        var apiKeyPasswordBox = new PasswordBox
        {
            Header = "API Key",
            PlaceholderText = "可稍后通过列表右侧按钮修改",
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(modelTextBox);
        content.Children.Add(apiKeyPasswordBox);
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "添加 AI 模型",
            Content = content,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var model = modelTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowInfo("请输入要添加的模型名称。", InfoBarSeverity.Warning);
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
            ShowInfo("至少需要保留一个 AI 翻译模型。", InfoBarSeverity.Warning);
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
            PlaceholderText = "输入 API Key",
        };
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = $"修改 {model} 的 API Key",
            Content = passwordBox,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            SetApiKeyForModel(_currentProvider, model, passwordBox.Password.Trim());
            ShowInfo("API Key 已更新。", InfoBarSeverity.Success);
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
                ShowInfo("网络不可用，无法获取离线语言模型。", InfoBarSeverity.Warning);
            }

            return;
        }

        _isLoadingOfflineModels = true;
        UpdateOfflineModelDownloadAvailability();
        try
        {
            _onlineOfflineModels = (await _offlineModelService.GetAvailableModelsAsync(CancellationToken.None)).ToList();
            OfflineModelsStatusTextBlock.Text = $"已获取 {_onlineOfflineModels.Count} 个可下载模型";
            RefreshOfflineModelList();
        }
        catch (Exception ex)
        {
            OfflineModelsStatusTextBlock.Text = "无法获取可下载模型";
            if (showError)
            {
                ShowInfo("获取离线模型失败：" + ex.Message, InfoBarSeverity.Error);
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
            ShowInfo("网络不可用，无法下载离线语言模型。", InfoBarSeverity.Warning);
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

            ShowInfo("离线语言模型下载完成。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo("下载离线语言模型失败：" + ex.Message, InfoBarSeverity.Error);
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
            ShowInfo("当前离线模型正在翻译，完成后再删除。", InfoBarSeverity.Warning);
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
            ShowInfo("已删除离线语言模型。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo("删除离线语言模型失败：" + ex.Message, InfoBarSeverity.Error);
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
        ShowInfo($"已选择 {model.Title} 离线模型。", InfoBarSeverity.Informational);
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
        OfflineModelsHeaderTextBlock.Text = $"离线语言模型（已下载 {installed.Count} 个）";
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
            model.Details = "正在下载 " + (int)(_offlineModelDownloadProgress * 100) + "% · " + size;
        }
        else
        {
            model.Details = (model.IsInstalled ? "已下载" : "可下载") + " · " + size;
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
            OfflineModelsStatusTextBlock.Text = $"网络不可用，已下载 {_offlineModelService.GetInstalledModels().Count} 个模型仍可使用";
        }
        else if (_isLoadingOfflineModels)
        {
            OfflineModelsStatusTextBlock.Text = "正在获取可下载模型...";
        }
        else if (!IsBergamotRuntimeAvailable())
        {
            OfflineModelsStatusTextBlock.Text = "已可下载模型；本地翻译引擎尚未随当前应用发布";
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
        "zh" => "简体中文",
        "zh-tw" => "繁体中文",
        "en" => "英语",
        "ja" => "日语",
        "ko" => "韩语",
        "fr" => "法语",
        "de" => "德语",
        "es" => "西班牙语",
        "ru" => "俄语",
        "pt" => "葡萄牙语",
        "it" => "意大利语",
        "ar" => "阿拉伯语",
        "th" => "泰语",
        "vi" => "越南语",
        "id" => "印尼语",
        _ => code,
    };

    private static string FormatModelSize(long bytes) => bytes switch
    {
        <= 0 => "大小未知",
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
            Header = "模型名称",
            PlaceholderText = "输入支持图片的模型名称",
        };
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = "添加图片翻译模型",
            Content = modelTextBox,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var model = modelTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowInfo("请输入要添加的图片模型名称。", InfoBarSeverity.Warning);
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
            ShowInfo("至少需要保留一个图片翻译模型。", InfoBarSeverity.Warning);
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
            PlaceholderText = "输入 API Key",
        };
        var dialog = new ContentDialog
        {
            XamlRoot = WindowRoot.XamlRoot,
            Title = $"修改 {model} 的 API Key",
            Content = passwordBox,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            SetApiKeyForImageModel(provider, model, passwordBox.Password.Trim());
            ShowInfo("API Key 已更新。", InfoBarSeverity.Success);
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
        ShowInfo("已清除翻译历史。", InfoBarSeverity.Success);
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

        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
        ShowInfo("译文已复制到剪贴板。", InfoBarSeverity.Success);
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

    private void SidebarNavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        var backButton = FindVisualChild<Button>(SidebarNavigationView, "NavigationViewBackButton");
        if (backButton is not null)
        {
            backButton.Width = TitleBarDragRegion.ActualHeight;
            ToolTipService.SetToolTip(backButton, null);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            var result = FindVisualChild<T>(child, name);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

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
        if (_backStack.Count > 0)
        {
            ShowPage(_backStack.Pop());
        }
    }

    private void ShowPage(string? tag)
    {
        tag ??= "Home";
        _currentPageTag = tag;
        var pageTitle = tag switch
        {
            "Home" => "Windtranslator",
            "Image" => "图片翻译",
            "History" => "翻译历史",
            "Settings" => "设置",
            "About" => "关于",
            _ => "Windtranslator",
        };
        PageTitleTextBlock.Text = pageTitle;
        Title = pageTitle;

        TranslationToolbar.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        HomePageGrid.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        TranslationHistoryPageGrid.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageScrollViewer.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ImagePageGrid.Visibility = tag == "Image" ? Visibility.Visible : Visibility.Collapsed;
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
            minMaxInfo.MinimumTrackSize.X = (int)Math.Ceiling(MinWindowWidth * scale);
            minMaxInfo.MinimumTrackSize.Y = (int)Math.Ceiling(MinWindowHeight * scale);
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
            ? "已选择 1 张图片"
            : $"已选择 {_selectedImageFiles.Count} 张图片（将按文件名排序）";
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
            ShowInfo("请填写图片翻译接口地址。", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            ShowInfo("请填写支持图片的模型名称。", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowInfo($"图片翻译使用{providerKey} API，请先在设置中为对应模型填写 API Key。", InfoBarSeverity.Warning);
            return;
        }

        _isImageTranslating = true;
        ImageOutputTextBox.Text = string.Empty;
        ShowInfo($"正在翻译 {_selectedImageFiles.Count} 张图片...", InfoBarSeverity.Informational);
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
                    results.Add($"翻译失败：{ex.Message}");
                }
            }

            ImageOutputTextBox.Text = string.Join(
                Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine,
                results);
            ImageTranslationProgressText.Text = $"已完成 {_selectedImageFiles.Count} 张";
            ShowInfo("图片翻译完成。", InfoBarSeverity.Success);
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

        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
        ShowInfo("图片译文已复制到剪贴板。", InfoBarSeverity.Success);
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranslating)
        {
            _cts?.Cancel();
            return;
        }

        var sourceText = SourceTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            ShowInfo("请输入需要翻译的内容。", InfoBarSeverity.Warning);
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
            ShowInfo("请填写接口地址。", InfoBarSeverity.Warning);
            return;
        }

        if (!useLocalModel && string.IsNullOrWhiteSpace(model))
        {
            ShowInfo("请填写模型名称。", InfoBarSeverity.Warning);
            return;
        }

        if (useLocalModel)
        {
            if (!IsBergamotRuntimeAvailable())
            {
                ShowInfo("当前发布包未包含 Mozilla Translations 本地翻译引擎。请构建并重新发布 Runtime\\Bergamot 运行库。", InfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedOfflineModelPath))
            {
                ShowInfo("请先在设置中下载并选择离线语言模型。", InfoBarSeverity.Warning);
                return;
            }
        }

        if (!isLocalMode && string.IsNullOrWhiteSpace(apiKey))
        {
            ShowInfo("请在设置中为当前模型填写 API Key。", InfoBarSeverity.Warning);
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
        TranslateButtonText.Text = "取消";
        ProgressRing.IsActive = true;
        UpdateUiState();
        ShowInfo(useLocalModel ? "正在使用本地模型翻译..." : "正在翻译...", InfoBarSeverity.Informational);

        try
        {
            var result = useLocalModel
                ? await _bergamotTranslationService.TranslateAsync(
                    _selectedOfflineModelPath,
                    sourceText,
                    _cts.Token)
                : await _translationService.TranslateAsync(
                    new TranslationRequest(
                        endpoint,
                        apiKey,
                        model,
                        systemPrompt,
                        sourceText),
                    _cts.Token);
            OutputTextBox.Text = result;
            UpdateCounts();
            AddTranslationHistory(sourceText);
            ShowInfo("翻译完成。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo("已取消翻译。", InfoBarSeverity.Warning);
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
            TranslateButtonText.Text = "翻译";
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
            ShowInfo("请选择有效的源语言和目标语言。", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_aliyunAccessKeyId))
        {
            ShowInfo("请填写阿里云机器翻译 AccessKey ID。", InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_aliyunAccessKeySecret))
        {
            ShowInfo("请填写阿里云机器翻译 AccessKey Secret。", InfoBarSeverity.Warning);
            return;
        }

        var request = new MachineTranslationRequest(
            AliyunServiceUrl,
            _aliyunAccessKeyId,
            _aliyunAccessKeySecret,
            sourceLanguage.ApiCode,
            targetLanguage.ApiCode,
            sourceText);

        _cts = new CancellationTokenSource();
        _isTranslating = true;
        TranslateButtonText.Text = "取消";
        ProgressRing.IsActive = true;
        UpdateUiState();
        ShowInfo("正在调用机器翻译...", InfoBarSeverity.Informational);

        try
        {
            var result = await _machineTranslationService.TranslateAsync(request, _cts.Token);
            OutputTextBox.Text = result;
            UpdateCounts();
            AddTranslationHistory(sourceText);
            ShowInfo("翻译完成。", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo("已取消翻译。", InfoBarSeverity.Warning);
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
            TranslateButtonText.Text = "翻译";
            ProgressRing.IsActive = false;
            UpdateUiState();
        }
    }

    private async void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isStartingSpeechInput)
        {
            return;
        }

        if (_speechInput.IsListening)
        {
            await _speechInput.StopAsync();
            SetMicListeningState(false);
            ShowInfo("已停止语音输入。", InfoBarSeverity.Informational);
            return;
        }

        try
        {
            _isStartingSpeechInput = true;
            UpdateUiState();
            var languageTag = (SourceLanguageComboBox.SelectedItem as LanguageOption)?.SpeechTag;
            await _speechInput.StartAsync(
                languageTag,
                text => DispatcherQueue.TryEnqueue(() => AppendSpeechResult(text)),
                text => DispatcherQueue.TryEnqueue(() => ShowInfo(text, InfoBarSeverity.Informational)),
                error => DispatcherQueue.TryEnqueue(() => HandleSpeechRecognitionCompleted(error)));
            SetMicListeningState(true);
            ShowInfo("正在聆听，请开始说话。", InfoBarSeverity.Informational);
        }
        catch (UnauthorizedAccessException)
        {
            SetMicListeningState(false);
            ShowInfo(
                "无法使用麦克风，请在 Windows 隐私设置中允许 Windtranslator 和桌面应用访问麦克风。",
                InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            SetMicListeningState(false);
            ShowInfo("无法启动语音识别：" + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isStartingSpeechInput = false;
            UpdateUiState();
        }
    }

    private void HandleSpeechRecognitionCompleted(string? error)
    {
        SetMicListeningState(false);
        ShowInfo(
            error ?? "语音识别已结束。",
            error is null ? InfoBarSeverity.Informational : InfoBarSeverity.Error);
    }

    private void AppendSpeechResult(string text)
    {
        if (string.IsNullOrEmpty(SourceTextBox.Text))
        {
            SourceTextBox.Text = text;
        }
        else
        {
            var separator = SourceTextBox.Text.EndsWith(' ') || SourceTextBox.Text.EndsWith('\n')
                ? string.Empty
                : " ";
            SourceTextBox.Text += separator + text;
        }
    }

    private void SetMicListeningState(bool listening)
    {
        MicIcon.Symbol = listening ? Symbol.Stop : Symbol.Microphone;
        MicButton.Tag = listening;
        UpdateUiState();
    }

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speechOutput.IsSpeaking)
        {
            _speechOutput.Stop();
            SetSpeakingState(false);
            ShowInfo("已停止朗读。", InfoBarSeverity.Informational);
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
            ShowInfo("正在合成语音...", InfoBarSeverity.Informational);
            await _speechOutput.SpeakAsync(text, languageTag);
            SetSpeakingState(true);
            ShowInfo("正在朗读译文。", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetSpeakingState(false);
            ShowInfo("无法播放语音：" + ex.Message, InfoBarSeverity.Error);
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
            ShowInfo("请先填写本地接口地址。", InfoBarSeverity.Warning);
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
                ShowInfo("本地服务没有返回模型列表。", InfoBarSeverity.Warning);
                return;
            }

            _suppressEvents = true;
            LocalModelTextBox.Text = models[0];
            _suppressEvents = false;
            _settings.Endpoints["本地 AI"] = endpoint;
            _settings.Models["本地 AI"] = models[0];
            SaveSettings();
            ShowInfo($"找到 {models.Count} 个本地模型。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo("刷新模型失败：" + ex.Message, InfoBarSeverity.Error);
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
                && (sourceLanguage.PromptName == "自动检测"
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
        SourceCountText.Text = $"{SourceTextBox.Text.Length} 字";
        OutputCountText.Text = $"{OutputTextBox.Text.Length} 字";
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
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
        }
    }

    private void UpdateUiState()
    {
        TranslateButton.IsEnabled = !_isTranslating && SourceTextBox.Text.Trim().Length > 0;
        MicButton.IsEnabled = !_isTranslating && !_isStartingSpeechInput;
        SpeakButton.IsEnabled = _speechOutput.IsSpeaking || OutputTextBox.Text.Trim().Length > 0;
        PickImageButton.IsEnabled = !_isImageTranslating;
        TranslateImageButton.IsEnabled = !_isImageTranslating && _selectedImageFiles.Count > 0;
        CopyImageOutputButton.IsEnabled = ImageOutputTextBox.Text.Trim().Length > 0;
    }

    private void SaveSettings()
    {
        _settings.ModeIndex = Math.Max(0, ModeComboBox.SelectedIndex);
        _settings.ProviderName = _currentProvider;
        _settings.ThemeIndex = Math.Max(0, ThemeComboBox.SelectedIndex);
        _settings.MicaBackdropEnabled = MicaBackdropCheckBox.IsChecked == true;
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
                + "请使用 Markdown 格式组织输出，先给出译文，再简要给出必要的解释、读音和词性；如果某项不适用，可以省略。保持原文的格式和专有名词，不要输出原始 HTML。";
        }

        return $"你是一名专业的翻译引擎。{instruction}{styleInstruction}"
            + "只输出译文；如有多段内容，请使用 Markdown 段落或列表组织，不要添加解释、注释、读音、词性、代码块或任何额外内容。保持原文的格式和专有名词。";
    }

    private static ProviderProfile GetProviderProfile(string provider) => provider switch
    {
        "DeepSeek" => new ProviderProfile(
            "DeepSeek",
            "https://api.deepseek.com",
            new[] { "deepseek-v4-flash", "deepseek-v4-pro" },
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
        _ => new ProviderProfile(
            "本地 AI",
            "http://localhost:11434/v1",
            Array.Empty<string>(),
            false),
    };

    private static string NormalizeProviderModelName(string provider, string model) => provider == "DeepSeek"
        ? model switch
        {
            "v4flash" => "deepseek-v4-flash",
            "v4pro" => "deepseek-v4-pro",
            _ => model,
        }
        : model;

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
        "DeepSeek" => "deepseek-v4-flash-vision-exp",
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
