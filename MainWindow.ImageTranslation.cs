using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windtranslator.Models;
using Windtranslator.Services;
using WinRT.Interop;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private ImageTranslationPageStore? _imageTranslationStore;
    private IReadOnlyList<LanguageOption> _imageTargetLanguages = Array.Empty<LanguageOption>();
    private ImageTranslationPageState ImageTranslationState => _imageTranslationStore!.State;

    private void CreateImageTranslationStore()
    {
        _imageTranslationStore = new ImageTranslationPageStore
        {
            Pick = () => _ = PickImagesAsync(),
            Translate = () => _ = TranslateImagesAsync(),
            Copy = CopyImageOutput,
            Clear = () =>
            {
                if (ImageTranslationState.CanClear)
                    _imageTranslationStore!.Update(state => state with { IsClearConfirmationOpen = true });
            },
            ConfirmClear = ConfirmClearImages,
            SourceChanged = index =>
            {
                if (_suppressEvents || index < 0 || index >= Languages.Count || index == ImageTranslationState.SourceIndex) return;
                _imageTranslationStore!.Update(state => state with { SourceIndex = index });
                RefreshImageTargetLanguageOptions();
            },
            TargetChanged = index =>
            {
                if (_suppressEvents || index < 0 || index >= _imageTargetLanguages.Count || index == ImageTranslationState.TargetIndex) return;
                _imageTranslationStore!.Update(state => state with { TargetIndex = index });
            },
            ModelChanged = index =>
            {
                if (_settings is null || _suppressEvents || index < 0 || index >= ImageTranslationState.Models.Count
                    || index == ImageTranslationState.ModelIndex) return;
                SetImageModelSelection(ImageTranslationState.Models[index]);
                var previous = _suppressEvents;
                _suppressEvents = true;
                ImageModelsListView.SelectedItem = _settings.ImageModel;
                _suppressEvents = previous;
                SaveSettings();
            },
        };
        var host = new ReactorHostControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        ImagePageHost.Content = host;
        host.Mount(new ImageTranslationPageView(_imageTranslationStore));
    }

    private void SetImageModelSelection(string model)
    {
        _settings.ImageModel = model;
        _settings.ImageSelectedModels[GetImageProvider()] = model;
        var index = ImageTranslationState.Models.ToList().IndexOf(model);
        _imageTranslationStore!.Update(state => state with { ModelIndex = index });
    }

    private void InitializeImageTranslationLanguages(int sourceIndex, LanguageOption preferred)
    {
        _imageTranslationStore!.Update(state => state with
        {
            SourceNames = Languages.Select(language => language.Display).ToArray(),
            SourceIndex = sourceIndex,
        });
        RefreshImageTargetLanguageOptions(preferred);
    }

    private void RefreshImageTargetLanguageOptions(LanguageOption? preferred = null)
    {
        preferred ??= _imageTargetLanguages.ElementAtOrDefault(ImageTranslationState.TargetIndex);
        var source = Languages[ImageTranslationState.SourceIndex];
        var targets = Languages.Where(language => language.PromptName != "自动检测"
            && (source.ApiCode == "auto" || language.PromptName != source.PromptName)).ToArray();
        var target = targets.FirstOrDefault(language => language.PromptName == preferred?.PromptName)
            ?? targets.FirstOrDefault(language => language.PromptName == "英语") ?? targets.FirstOrDefault();
        _imageTargetLanguages = targets;
        _imageTranslationStore!.Update(state => state with
        {
            TargetNames = targets.Select(language => language.Display).ToArray(),
            TargetIndex = Array.IndexOf(targets, target),
        });
    }

    private void ConfirmClearImages(bool confirmed)
    {
        var open = ImageTranslationState.IsClearConfirmationOpen;
        _imageTranslationStore!.Update(state => state with { IsClearConfirmationOpen = false });
        if (!open || !confirmed || !ImageTranslationState.CanClear) return;
        _imageTranslationStore.Update(state => state with
        {
            Files = Array.Empty<StorageFile>(), Preview = null,
            Summary = Localization.Text("未选择图片"), Output = string.Empty, Progress = string.Empty,
        });
        UpdateUiState();
        ShowInfo(Localization.Text("已清除选择的图片。"), InfoBarSeverity.Success);
    }

    private void SetImageOutput(string text) =>
        _imageTranslationStore!.Update(state => state with { Output = text });

    private async Task PickImagesAsync()
    {
        if (_isImageTranslating) return;
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
        if (files.Count == 0 || _isImageTranslating)
        {
            return;
        }

        var existingPaths = ImageTranslationState.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = ImageTranslationState.Files.Concat(files.Where(file => existingPaths.Add(file.Path)))
            .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        _imageTranslationStore!.Update(state => state with
        {
            Files = selected,
            Summary = selected.Length == 1 ? Localization.Text("已选择 1 张图片")
                : Localization.Text("已选择 {0} 张图片（将按文件名排序）", selected.Length),
            Output = string.Empty, Progress = string.Empty, Preview = null,
        });
        using var stream = await selected[0].OpenReadAsync();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        if (ReferenceEquals(ImageTranslationState.Files, selected))
            _imageTranslationStore.Update(state => state with { Preview = bitmap });
        UpdateUiState();
    }

    private async Task TranslateImagesAsync()
    {
        if (ImageTranslationState.Files.Count == 0 || _isImageTranslating)
        {
            return;
        }

        var sourceLanguage = Languages[ImageTranslationState.SourceIndex];
        var targetLanguage = _imageTargetLanguages.ElementAtOrDefault(ImageTranslationState.TargetIndex) ?? Languages[3];
        var systemPrompt = BuildDefaultPrompt(
            sourceLanguage,
            targetLanguage,
            SettingsState.PromptStyle,
            SettingsState.LanguageDetails);
        if (!string.IsNullOrWhiteSpace(SettingsState.CustomPrompt))
        {
            systemPrompt += "\n\n补充要求：" + SettingsState.CustomPrompt.Trim();
        }

        var providerKey = GetImageProvider();
        var endpoint = GetProviderEndpoint(providerKey);
        var model = ImageTranslationState.Models.ElementAtOrDefault(ImageTranslationState.ModelIndex)?.Trim() ?? string.Empty;
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
        SetImageOutput(string.Empty);
        ShowInfo(Localization.Text("正在翻译 {0} 张图片...", ImageTranslationState.Files.Count), InfoBarSeverity.Informational);
        UpdateUiState();

        try
        {
            var results = new List<string>();
            for (var index = 0; index < ImageTranslationState.Files.Count; index++)
            {
                var file = ImageTranslationState.Files[index];
                _imageTranslationStore!.Update(state => state with { Progress = $"{index + 1} / {ImageTranslationState.Files.Count}" });

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
                        text => SetImageOutput(prefix + text));
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(Localization.Text("翻译失败：{0}", ex.Message));
                }
                SetImageOutput(string.Join(
                    Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine, results));
            }

            SetImageOutput(string.Join(
                Environment.NewLine + Environment.NewLine + "--------------------" + Environment.NewLine + Environment.NewLine,
                results));
            _imageTranslationStore!.Update(state => state with { Progress = Localization.Text("已完成 {0} 张", state.Files.Count) });
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

    private void CopyImageOutput()
    {
        var text = ImageTranslationState.Output;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SetClipboardText(text);
        ShowInfo(Localization.Text("图片译文已复制到剪贴板。"), InfoBarSeverity.Success);
    }


}

