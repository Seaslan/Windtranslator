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
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
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
    private const int MinWindowWidth = 1180;
    private const int MinWindowHeight = 780;

    private static readonly List<string> CloudProviders = new() { "DeepSeek", "千问", "Kimi" };

    private static readonly List<LanguageOption> Languages = new()
    {
        new LanguageOption("自动检测", "自动检测", null, "auto"),
        new LanguageOption("简体中文", "简体中文", "zh", "zh"),
        new LanguageOption("繁体中文", "繁体中文", "zh-Hant", "zh-tw"),
        new LanguageOption("英语", "英语", "en", "en"),
        new LanguageOption("日语", "日语", "ja", "ja"),
        new LanguageOption("韩语", "韩语", "ko", "ko"),
        new LanguageOption("法语", "法语", "fr", "fr"),
        new LanguageOption("德语", "德语", "de", "de"),
        new LanguageOption("西班牙语", "西班牙语", "es", "es"),
        new LanguageOption("俄语", "俄语", "ru", "ru"),
        new LanguageOption("葡萄牙语", "葡萄牙语", "pt", "pt"),
        new LanguageOption("意大利语", "意大利语", "it", "it"),
        new LanguageOption("阿拉伯语", "阿拉伯语", "ar", "ar"),
        new LanguageOption("泰语", "泰语", "th", "th"),
        new LanguageOption("越南语", "越南语", "vi", "vi"),
        new LanguageOption("印尼语", "印尼语", "id", "id"),
    };

    private readonly HttpClient _httpClient = new();
    private readonly TranslationService _translationService;
    private readonly LocalT5TranslationService _localT5TranslationService = new();
    private readonly AliyunMachineTranslationService _machineTranslationService;
    private readonly SpeechInputService _speechInput = new();
    private readonly SpeechOutputService _speechOutput = new();
    private readonly Dictionary<string, string> _apiKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings;

    private string _currentProvider = "DeepSeek";
    private string _aliyunAccessKeyId = string.Empty;
    private string _aliyunAccessKeySecret = string.Empty;
    private bool _isTranslating;
    private bool _isImageTranslating;
    private bool _isSidebarCollapsed;
    private bool _suppressWindowResize;
    private bool _suppressEvents;
    private bool _suppressSidebarSync;
    private CancellationTokenSource? _cts;
    private DispatcherQueueTimer? _infoBarTimer;
    private StorageFile? _selectedImageFile;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        SetTitleBar(TitleBarDragRegion);
        AppWindow.Resize(new SizeInt32(MinWindowWidth, MinWindowHeight));
        AppWindow.Changed += OnAppWindowChanged;

        _settings = AppSettingsStore.Load();
        _translationService = new TranslationService(_httpClient);
        _machineTranslationService = new AliyunMachineTranslationService(_httpClient);
        _speechOutput.PlaybackEnded += (_, _) => DispatcherQueue.TryEnqueue(() => SetSpeakingState(false));
        Closed += (_, _) => _localT5TranslationService.Dispose();
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

        ModeComboBox.ItemsSource = new[] { "API 翻译", "AI 翻译", "本地 AI 翻译" };
        ModeComboBox.SelectedIndex = Math.Clamp(_settings.ModeIndex, 0, 2);
        LocalTranslationSourceComboBox.ItemsSource = new[] { "本地接口", "本地模型" };
        LocalTranslationSourceComboBox.SelectedIndex = Math.Clamp(_settings.LocalTranslationSourceIndex, 0, 1);

        SourceLanguageComboBox.ItemsSource = Languages;
        ImageSourceLanguageComboBox.ItemsSource = Languages;
        ImageProviderComboBox.ItemsSource = new[] { "千问", "Kimi" };
        ImageProviderComboBox.SelectedIndex = 0;
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
        AiRememberKeyCheckBox.IsChecked = _settings.RememberKeys;
        AliyunRememberKeyCheckBox.IsChecked = _settings.RememberAliyunKeys;
        ThemeComboBox.ItemsSource = new[] { "跟随系统", "浅色", "深色" };
        ThemeComboBox.SelectedIndex = Math.Clamp(_settings.ThemeIndex, 0, 2);
        MicaBackdropCheckBox.IsChecked = _settings.MicaBackdropEnabled;

        _suppressEvents = false;

        ApplyTheme();
        ApplyMicaBackdrop();
        SidebarNavList.SelectedIndex = 0;
        InitializeProviderSettings();
        UpdatePromptPreview();
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
        ResetPromptButton.IsEnabled = !isApi;
        PromptHintText.Text = isApi
            ? "API 模式使用默认翻译提示词，不附加补充提示词。"
            : "AI 模式会保留默认翻译提示词，并把补充要求附加在后方。";
        UpdatePromptPreview();
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

        AiModelComboBox.ItemsSource = profile.DefaultModels;
        AiModelComboBox.IsEditable = true;
        var savedModel = _settings.Models.TryGetValue(_currentProvider, out var model) ? model : null;
        AiModelComboBox.Text = !string.IsNullOrWhiteSpace(savedModel)
            ? savedModel
            : profile.DefaultModels.Count > 0
                ? profile.DefaultModels[0]
                : "qwen2.5:7b";

        if (_settings.RememberKeys)
        {
            var rememberedKey = LoadKeyFromVault(_currentProvider);
            if (!string.IsNullOrEmpty(rememberedKey))
            {
                _apiKeys[_currentProvider] = rememberedKey;
            }
        }

        AiApiKeyPasswordBox.Password = _apiKeys.TryGetValue(_currentProvider, out var apiKey)
            ? apiKey
            : string.Empty;

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
        LocalT5ModelPathTextBox.Text = _settings.LocalModelPath;
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

        AiModelComboBox.ItemsSource = profile.DefaultModels;
        var savedModel = _settings.Models.TryGetValue(provider, out var model) ? model : null;
        AiModelComboBox.Text = !string.IsNullOrWhiteSpace(savedModel)
            ? savedModel
            : profile.DefaultModels.Count > 0
                ? profile.DefaultModels[0]
                : "qwen2.5:7b";

        var key = _apiKeys.TryGetValue(provider, out var memoryKey)
            ? memoryKey
            : AiRememberKeyCheckBox.IsChecked == true
                ? LoadKeyFromVault(provider) ?? string.Empty
                : string.Empty;
        AiApiKeyPasswordBox.Password = key;
        if (!string.IsNullOrEmpty(key))
        {
            _apiKeys[provider] = key;
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

    private void AiApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || string.IsNullOrEmpty(_currentProvider))
        {
            return;
        }

        var key = AiApiKeyPasswordBox.Password;
        _apiKeys[_currentProvider] = key;
        if (AiRememberKeyCheckBox.IsChecked == true && !string.IsNullOrEmpty(key))
        {
            SaveKeyToVault(_currentProvider, key);
        }
    }

    private void AiModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || string.IsNullOrEmpty(_currentProvider))
        {
            return;
        }

        if (AiModelComboBox.SelectedItem is string model && !string.IsNullOrWhiteSpace(model))
        {
            _settings.Models[_currentProvider] = model;
            SaveSettings();
        }
    }

    private void AiModelComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (string.IsNullOrEmpty(_currentProvider))
        {
            return;
        }

        var model = args.Text.Trim();
        if (!string.IsNullOrEmpty(model))
        {
            _settings.Models[_currentProvider] = model;
            SaveSettings();
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

        var key = AiApiKeyPasswordBox.Password;
        if (_settings.RememberKeys)
        {
            if (!string.IsNullOrEmpty(key))
            {
                SaveKeyToVault(_currentProvider, key);
            }
        }
        else
        {
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

    private void LocalT5ModelPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.LocalModelPath = LocalT5ModelPathTextBox.Text.Trim();
        SaveSettings();
    }

    private async void PickLocalT5ModelFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        LocalT5ModelPathTextBox.Text = folder.Path;
    }

    private void UpdateLocalTranslationSourceUi()
    {
        var useModel = LocalTranslationSourceComboBox.SelectedIndex == 1;
        LocalEndpointPanel.Visibility = useModel ? Visibility.Collapsed : Visibility.Visible;
        LocalT5ModelPanel.Visibility = useModel ? Visibility.Visible : Visibility.Collapsed;
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
        UpdatePromptPreview();
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

    private void SourceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RefreshTargetLanguageOptions();
        UpdatePromptPreview();
        SaveSettings();
    }

    private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        RefreshTargetLanguageOptions();
        UpdatePromptPreview();
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

    private void ImageProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
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
        UpdatePromptPreview();
        SaveSettings();
    }

    private void ClearSourceButton_Click(object sender, RoutedEventArgs e)
    {
        SourceTextBox.Text = string.Empty;
        SourceTextBox.Focus(FocusState.Programmatic);
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

    private void ResetPromptButton_Click(object sender, RoutedEventArgs e)
    {
        CustomPromptTextBox.Text = string.Empty;
        UpdatePromptPreview();
        ShowInfo("已恢复默认补充提示词。", InfoBarSeverity.Success);
    }

    private void SidebarNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = SidebarNavList.SelectedItem as ListViewItem;
        var tag = selectedItem?.Tag?.ToString();

        if (!_suppressSidebarSync && SidebarAboutNavList.SelectedIndex >= 0)
        {
            _suppressSidebarSync = true;
            SidebarAboutNavList.SelectedIndex = -1;
            _suppressSidebarSync = false;
        }

        ShowPage(tag);
    }

    private void SidebarAboutNavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSidebarSync || SidebarAboutNavList.SelectedIndex < 0)
        {
            return;
        }

        _suppressSidebarSync = true;
        SidebarNavList.SelectedIndex = -1;
        _suppressSidebarSync = false;

        ShowPage("About");
    }

    private void ShowPage(string? tag)
    {
        var pageTitle = tag switch
        {
            "Home" => "Windtranslator",
            "Image" => "图片翻译",
            "Settings" => "设置",
            "About" => "关于",
            _ => "Windtranslator",
        };
        PageTitleTextBlock.Text = pageTitle;
        Title = pageTitle;

        TranslationToolbar.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        HomePageGrid.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageScrollViewer.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ImagePageGrid.Visibility = tag == "Image" ? Visibility.Visible : Visibility.Collapsed;
        AboutPagePanel.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        SidebarColumn.Width = _isSidebarCollapsed ? new GridLength(64) : new GridLength(200);
        SidebarToggleIcon.Glyph = _isSidebarCollapsed ? "\uE8A0" : "\uE89F";
        SidebarToggleText.Text = _isSidebarCollapsed ? "展开侧边栏" : "收起侧边栏";
        SidebarToggleText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        HomeNavText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SettingsNavText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ImageNavText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        AboutNavText.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(
            SidebarToggleButton,
            _isSidebarCollapsed ? "展开侧边栏" : "收起侧边栏");
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_suppressWindowResize || !args.DidSizeChange)
        {
            return;
        }

        if ((AppWindow.Presenter as OverlappedPresenter)?.State == OverlappedPresenterState.Minimized)
        {
            return;
        }

        var size = sender.Size;
        if (size.Width >= MinWindowWidth && size.Height >= MinWindowHeight)
        {
            return;
        }

        _suppressWindowResize = true;
        sender.Resize(new SizeInt32(
            Math.Max(size.Width, MinWindowWidth),
            Math.Max(size.Height, MinWindowHeight)));
        _suppressWindowResize = false;
    }

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
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _selectedImageFile = file;
        ImageFileNameText.Text = file.Name;
        ImageOutputTextBox.Text = string.Empty;

        var stream = await file.OpenReadAsync();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        ImagePreview.Source = bitmap;
        ImagePreview.Visibility = Visibility.Visible;
        UpdateUiState();
    }

    private async void TranslateImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedImageFile is null || _isImageTranslating)
        {
            return;
        }

        var imageProvider = ImageProviderComboBox.SelectedItem?.ToString() ?? "千问";
        var providerKey = imageProvider == "Kimi" ? "Kimi" : "千问";
        var apiKey = _apiKeys.GetValueOrDefault(providerKey);
        if (string.IsNullOrWhiteSpace(apiKey) && AiRememberKeyCheckBox.IsChecked == true)
        {
            apiKey = LoadKeyFromVault(providerKey) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                _apiKeys[providerKey] = apiKey;
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowInfo($"图片翻译使用{providerKey} API，请先在设置中填写对应 API Key。", InfoBarSeverity.Warning);
            return;
        }

        var sourceLanguage = ImageSourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        var targetLanguage = ImageTargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
        var systemPrompt = BuildDefaultPrompt(sourceLanguage, targetLanguage);
        if (!string.IsNullOrWhiteSpace(CustomPromptTextBox.Text))
        {
            systemPrompt += "\n\n补充要求：" + CustomPromptTextBox.Text.Trim();
        }

        var endpoint = _settings.Endpoints.TryGetValue(providerKey, out var savedEndpoint)
            && !string.IsNullOrWhiteSpace(savedEndpoint)
                ? savedEndpoint
                : GetProviderProfile(providerKey).DefaultEndpoint;

        byte[] imageBytes;
        using (var imageStream = await _selectedImageFile.OpenStreamForReadAsync())
        using (var memoryStream = new MemoryStream())
        {
            await imageStream.CopyToAsync(memoryStream);
            imageBytes = memoryStream.ToArray();
        }

        var mimeType = GetMimeType(_selectedImageFile.Path);
        var imageDataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
        var request = new ImageTranslationRequest(
            endpoint,
            apiKey,
            providerKey == "Kimi" ? "kimi-k2.6" : "qwen3.5-ocr",
            systemPrompt,
            "请识别图片中的文字，并翻译成目标语言。",
            imageDataUrl);

        _isImageTranslating = true;
        TranslateImageButton.IsEnabled = false;
        ShowInfo("正在翻译图片...", InfoBarSeverity.Informational);

        try
        {
            var result = await _translationService.TranslateImageAsync(request, CancellationToken.None);
            ImageOutputTextBox.Text = result;
            ShowInfo("图片翻译完成。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isImageTranslating = false;
            UpdateUiState();
        }
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
            : AiModelComboBox.Text.Trim();
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

        var localT5TargetLanguage = string.Empty;
        if (useLocalModel)
        {
            localT5TargetLanguage = GetLocalT5TargetLanguageCode(
                TargetLanguageComboBox.SelectedItem as LanguageOption);
            if (string.IsNullOrWhiteSpace(localT5TargetLanguage))
            {
                ShowInfo("该本地模型仅支持翻译为简体中文、英语或俄语。", InfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(LocalT5ModelPathTextBox.Text))
            {
                ShowInfo("请选择本地 ONNX 模型文件夹。", InfoBarSeverity.Warning);
                return;
            }
        }

        if (!isLocalMode && string.IsNullOrWhiteSpace(_apiKeys.GetValueOrDefault(_currentProvider)))
        {
            ShowInfo("请手动输入 API Key。", InfoBarSeverity.Warning);
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
        var systemPrompt = BuildDefaultPrompt(sourceLanguage, targetLanguage);

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
                ? await _localT5TranslationService.TranslateAsync(
                    LocalT5ModelPathTextBox.Text.Trim(),
                    localT5TargetLanguage,
                    sourceText,
                    _cts.Token)
                : await _translationService.TranslateAsync(
                    new TranslationRequest(
                        endpoint,
                        isLocalMode ? null : _apiKeys.GetValueOrDefault(_currentProvider),
                        model,
                        systemPrompt,
                        sourceText),
                    _cts.Token);
            OutputTextBox.Text = result;
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
        if (_speechInput.IsListening)
        {
            await _speechInput.StopAsync();
            SetMicListeningState(false);
            ShowInfo("已停止语音输入。", InfoBarSeverity.Informational);
            return;
        }

        try
        {
            await _speechInput.StartAsync(
                text => DispatcherQueue.TryEnqueue(() => AppendSpeechResult(text)),
                text => DispatcherQueue.TryEnqueue(() => ShowInfo(text, InfoBarSeverity.Informational)));
            SetMicListeningState(true);
            ShowInfo("正在聆听，请开始说话。", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetMicListeningState(false);
            ShowInfo("无法启动语音识别：" + ex.Message, InfoBarSeverity.Error);
        }
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

    private void UpdatePromptPreview()
    {
        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption ?? Languages[0];
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption ?? Languages[3];
        DefaultPromptTextBox.Text = BuildDefaultPrompt(sourceLanguage, targetLanguage);
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

    private void UpdateUiState()
    {
        TranslateButton.IsEnabled = !_isTranslating && SourceTextBox.Text.Trim().Length > 0;
        MicButton.IsEnabled = !_isTranslating;
        SpeakButton.IsEnabled = _speechOutput.IsSpeaking || OutputTextBox.Text.Trim().Length > 0;
        PickImageButton.IsEnabled = !_isImageTranslating;
        TranslateImageButton.IsEnabled = !_isImageTranslating && _selectedImageFile is not null;
        CopyImageOutputButton.IsEnabled = ImageOutputTextBox.Text.Trim().Length > 0;
    }

    private void SaveSettings()
    {
        _settings.ModeIndex = Math.Max(0, ModeComboBox.SelectedIndex);
        _settings.ProviderName = _currentProvider;
        _settings.ThemeIndex = Math.Max(0, ThemeComboBox.SelectedIndex);
        _settings.MicaBackdropEnabled = MicaBackdropCheckBox.IsChecked == true;
        var sourceLanguage = SourceLanguageComboBox.SelectedItem as LanguageOption;
        var targetLanguage = TargetLanguageComboBox.SelectedItem as LanguageOption;
        _settings.SourceLanguageIndex = sourceLanguage is null ? 0 : Languages.IndexOf(sourceLanguage);
        _settings.TargetLanguageIndex = targetLanguage is null ? 3 : Languages.IndexOf(targetLanguage);
        _settings.CustomPrompt = CustomPromptTextBox.Text;
        _settings.RememberKeys = AiRememberKeyCheckBox.IsChecked == true;
        _settings.RememberAliyunKeys = AliyunRememberKeyCheckBox.IsChecked == true;
        _settings.LocalTranslationSourceIndex = Math.Clamp(LocalTranslationSourceComboBox.SelectedIndex, 0, 1);
        _settings.LocalModelPath = LocalT5ModelPathTextBox.Text.Trim();
        _settings.Endpoints["本地 AI"] = LocalEndpointTextBox.Text.Trim();
        _settings.Models["本地 AI"] = LocalModelTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(_currentProvider))
        {
            _settings.Endpoints[_currentProvider] = AiEndpointTextBox.Text.Trim();
            _settings.Models[_currentProvider] = AiModelComboBox.Text.Trim();
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

    private static string BuildDefaultPrompt(LanguageOption source, LanguageOption target)
    {
        if (source.PromptName == "自动检测")
        {
            return $"你是一名专业的翻译引擎。请自动识别用户输入的语言，并翻译成{target.PromptName}。"
                + "只输出译文，不要添加解释、注释、代码块或任何额外内容。保持原文的语气、格式和专有名词。";
        }

        return $"你是一名专业的翻译引擎。请把用户输入的内容从{source.PromptName}翻译成{target.PromptName}。"
            + "只输出译文，不要添加解释、注释、代码块或任何额外内容。保持原文的语气、格式和专有名词。";
    }

    private static string GetLocalT5TargetLanguageCode(LanguageOption? language) => language?.ApiCode switch
    {
        "zh" or "zh-tw" => "zh",
        "en" => "en",
        "ru" => "ru",
        _ => string.Empty,
    };

    private static ProviderProfile GetProviderProfile(string provider) => provider switch
    {
        "DeepSeek" => new ProviderProfile(
            "DeepSeek",
            "https://api.deepseek.com",
            new[] { "v4flash", "v4pro" },
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
        _ => new ProviderProfile(
            "本地 AI",
            "http://localhost:11434/v1",
            Array.Empty<string>(),
            false),
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
