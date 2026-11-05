using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    private FileTranslationPageStore? _fileTranslationStore;
    private IReadOnlyList<LanguageOption> _fileTargetLanguages = Array.Empty<LanguageOption>();
    private StorageFile? _selectedTextFile;
    private CancellationTokenSource? _fileTranslationCts;
    private bool _isFileTranslating;

    private FileTranslationPageState FileTranslationState => _fileTranslationStore!.State;

    private void CreateFileTranslationStore()
    {
        var store = new FileTranslationPageStore
        {
            PickFile = () => _ = PickTextFileAsync(),
            Translate = () => _ = TranslateFileAsync(),
            Clear = () =>
            {
                if (FileTranslationState.CanClear)
                    _fileTranslationStore!.Update(state => state with { IsClearConfirmationOpen = true });
            },
            ConfirmClear = ConfirmClearTextFile,
            Copy = CopyFileOutput,
            Export = () => _ = ExportFileOutputAsync(),
            SourceLanguageChanged = FileSourceLanguageChanged,
            TargetLanguageChanged = FileTargetLanguageChanged,
            ModeChanged = FileModeChanged,
        };
        _fileTranslationStore = store;
        var host = new ReactorHostControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        FilePageHost.Content = host;
        host.Mount(new FileTranslationPageView(store));
    }

    private void InitializeFileTranslationLanguages(int sourceIndex, LanguageOption target)
    {
        _fileTranslationStore!.Update(state => state with
        {
            SourceLanguageNames = Languages.Select(language => language.Display).ToArray(),
            SourceLanguageIndex = sourceIndex,
        });
        RefreshFileTargetLanguageOptions(target);
    }

    private void RefreshFileTargetLanguageOptions(LanguageOption? preferredTarget)
    {
        var source = Languages[FileTranslationState.SourceLanguageIndex];
        var targets = Languages.Where(language => language.PromptName != "自动检测"
            && (source.ApiCode == "auto" || language.PromptName != source.PromptName)).ToArray();
        var target = targets.FirstOrDefault(language => preferredTarget is not null
                && language.PromptName == preferredTarget.PromptName)
            ?? targets.FirstOrDefault(language => language.PromptName == "英语")
            ?? targets.FirstOrDefault();
        _fileTargetLanguages = targets;
        _fileTranslationStore!.Update(state => state with
        {
            TargetLanguageNames = targets.Select(language => language.Display).ToArray(),
            TargetLanguageIndex = Array.IndexOf(targets, target),
        });
    }

    private void FileSourceLanguageChanged(int index)
    {
        if (_settings is null || _suppressEvents || FileTranslationState.IsTranslating
            || index < 0 || index >= Languages.Count || index == FileTranslationState.SourceLanguageIndex) return;
        var preferredTarget = _fileTargetLanguages.ElementAtOrDefault(FileTranslationState.TargetLanguageIndex);
        _fileTranslationStore!.Update(state => state with { SourceLanguageIndex = index });
        RefreshFileTargetLanguageOptions(preferredTarget);
        UpdateFileModeAvailability();
    }

    private void FileTargetLanguageChanged(int index)
    {
        if (_settings is null || _suppressEvents || FileTranslationState.IsTranslating
            || index < 0 || index >= _fileTargetLanguages.Count || index == FileTranslationState.TargetLanguageIndex) return;
        _fileTranslationStore!.Update(state => state with { TargetLanguageIndex = index });
        UpdateFileModeAvailability();
    }

    private void FileModeChanged(int index)
    {
        if (_settings is null || _suppressEvents || FileTranslationState.IsTranslating
            || index < 0 || index >= FileTranslationState.Modes.Count || index == FileTranslationState.ModeIndex
            || !FileTranslationState.Modes[index].IsEnabled) return;
        _fileTranslationStore!.Update(state => state with { ModeIndex = index });
        UpdateFileModeAvailability();
    }

    private void UpdateFileModeAvailability()
    {
        if (_fileTranslationStore is null || _settings is null) return;
        var labels = new[]
        {
            Localization.Get("Sb1862e99d55f529f"),
            Localization.Get("S5239fbb2909810d6"),
            Localization.Get("S8f4e49aeafbade43"),
        };
        var modes = labels.Select((name, index) => new FileTranslationModeOption(
            name, IsTranslationModeEnabled(index),
            IsTranslationModeEnabled(index) && GetTranslationConfigurationError(index) is null)).ToArray();
        var modeIndex = Math.Clamp(FileTranslationState.ModeIndex, 0, 2);
        var hint = modes[modeIndex].IsEnabled ? string.Empty
            : GetTranslationConfigurationError(modeIndex) ?? Localization.Text("当前模式未配置。");
        _fileTranslationStore.Update(state => state with
        {
            Modes = state.Modes.SequenceEqual(modes) ? state.Modes : modes,
            ModeHint = hint,
            IsTranslating = _isFileTranslating,
            HasFile = _selectedTextFile is not null,
        });
    }

    private async Task PickTextFileAsync()
    {
        if (_isFileTranslating) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".txt");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null || _isFileTranslating) return;
        var properties = await file.GetBasicPropertiesAsync();
        const ulong maximumFileBytes = 5UL * 1024 * 1024;
        if (properties.Size > maximumFileBytes)
        {
            ShowInfo(Localization.Text("文件超过 5 MB 上限，无法进行文件翻译。"), InfoBarSeverity.Warning);
            return;
        }
        if (_isFileTranslating) return;
        _selectedTextFile = file;
        _fileTranslationStore!.Update(state => state with
        {
            FilePath = file.Path,
            FileSize = FormatFileSize(properties.Size),
            Output = string.Empty,
            ProgressText = string.Empty,
        });
        UpdateFileModeAvailability();
    }

    private async Task TranslateFileAsync()
    {
        if (_isFileTranslating || _selectedTextFile is null) return;
        var mode = FileTranslationState.ModeIndex;
        var configurationError = GetTranslationConfigurationError(mode);
        if (configurationError is not null)
        {
            ShowInfo(configurationError, InfoBarSeverity.Warning);
            UpdateFileModeAvailability();
            return;
        }

        var file = _selectedTextFile;
        var sourceLanguage = Languages.ElementAtOrDefault(FileTranslationState.SourceLanguageIndex) ?? Languages[0];
        var targetLanguage = _fileTargetLanguages.ElementAtOrDefault(FileTranslationState.TargetLanguageIndex) ?? Languages[3];
        using var cancellation = new CancellationTokenSource();
        _fileTranslationCts = cancellation;
        var token = cancellation.Token;
        // Mark busy before the first await so two clicks cannot start parallel reads/translations.
        _isFileTranslating = true;
        UpdateUiState();
        try
        {
            var sourceText = await ReadTextFileAsync(file.Path);
            token.ThrowIfCancellationRequested();
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
            _fileTranslationStore!.Update(state => state with
            {
                Output = string.Empty,
                ProgressText = Localization.Text("第 0/{0} 块", chunks.Count),
            });
            ShowInfo(Localization.Text("正在翻译文件..."), InfoBarSeverity.Informational);
            var translatedChunks = new List<string>(chunks.Count);
            for (var index = 0; index < chunks.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var completedChunks = string.Concat(translatedChunks);
                    translatedChunks.Add(await TranslateFileChunkWithRetryAsync(
                        chunks[index], mode, sourceLanguage, targetLanguage, token,
                        text => _fileTranslationStore.Update(state => state with { Output = completedChunks + text })));
                    token.ThrowIfCancellationRequested();
                    _fileTranslationStore.Update(state => state with { Output = string.Concat(translatedChunks) });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _fileTranslationStore.Update(state => state with { Output = string.Concat(translatedChunks) });
                    ShowInfo(Localization.Text("第 {0}/{1} 块翻译失败：{2}", index + 1, chunks.Count, ex.Message), InfoBarSeverity.Error);
                    return;
                }
                _fileTranslationStore.Update(state => state with
                {
                    ProgressText = Localization.Text("第 {0}/{1} 块", index + 1, chunks.Count),
                });
            }
            _fileTranslationStore.Update(state => state with { Output = string.Concat(translatedChunks) });
            ShowInfo(Localization.Text("文件翻译完成。"), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            _fileTranslationStore!.Update(state => state with { Output = string.Empty });
            ShowInfo(Localization.Text("已取消文件翻译，未生成任何文件。"), InfoBarSeverity.Warning);
        }
        catch (InvalidDataException ex) { ShowInfo(ex.Message, InfoBarSeverity.Error); }
        catch (Exception ex)
        {
            ShowInfo(Localization.Text("读取或翻译文件失败：") + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isFileTranslating = false;
            _fileTranslationCts = null;
            UpdateUiState();
        }
    }

    private void ConfirmClearTextFile(bool confirmed)
    {
        var wasOpen = FileTranslationState.IsClearConfirmationOpen;
        _fileTranslationStore!.Update(state => state with { IsClearConfirmationOpen = false });
        if (!wasOpen || !confirmed || _isFileTranslating || _selectedTextFile is null) return;
        _selectedTextFile = null;
        _fileTranslationStore.Update(state => state with
        {
            FilePath = Localization.Text("尚未选择文件"),
            FileSize = string.Empty,
            Output = string.Empty,
            ProgressText = string.Empty,
        });
        UpdateFileModeAvailability();
        ShowInfo(Localization.Text("已清除选择的文件。"), InfoBarSeverity.Success);
    }

    private void CopyFileOutput()
    {
        if (!FileTranslationState.CanCopy) return;
        SetClipboardText(FileTranslationState.Output);
        ShowInfo(Localization.Text("文件译文已复制到剪贴板。"), InfoBarSeverity.Success);
    }

    private async Task ExportFileOutputAsync()
    {
        if (_selectedTextFile is null || !FileTranslationState.CanExport) return;
        var output = FileTranslationState.Output;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_selectedTextFile.Name) + Localization.Text("_译文"),
        };
        picker.FileTypeChoices.Add(Localization.Text("文本文件"), new List<string> { ".txt" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await File.WriteAllTextAsync(file.Path, output, new UTF8Encoding(false));
            ShowInfo(Localization.Text("译文已导出到：") + file.Path, InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowInfo(Localization.Text("导出失败：") + ex.Message, InfoBarSeverity.Error); }
    }
}
