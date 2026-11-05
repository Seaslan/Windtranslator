using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windtranslator.Models;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private static readonly TimeSpan RealtimeTranslationDelay = TimeSpan.FromSeconds(1);
    private RealtimeTranslationOperation? _realtimeOperation;
    private RealtimeTranslationInput? _realtimeInput;
    private bool _isRealtimeTranslating;

    private sealed class RealtimeTranslationOperation
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Task CancellationTask { get; set; } = Task.CompletedTask;
    }

    // Compare the entire request context so changing languages or settings invalidates old results.
    private sealed record RealtimeTranslationInput(
        string Text, int Mode, LanguageOption Source, LanguageOption Target,
        int LocalSource, string Endpoint, string Model, string ModelPath,
        string AccessKeyId, string AccessKeySecret, int PromptStyle,
        bool LanguageDetails, string CustomPrompt, bool Streaming);

    private RealtimeTranslationInput? CaptureRealtimeInput(bool includeEmptyText = false)
    {
        if (_settings is null || _suppressEvents || _isWindowClosed
            || !_settings.RealtimeTranslationEnabled || _isMiniMode || _currentPageTag != "Home")
            return null;

        var mode = HomeState.ModeIndex;
        if (mode != 0 && mode != 2) return null;
        var text = HomeState.Source.Trim();
        if ((!includeEmptyText && text.Length == 0)
            || HomeSourceLanguage is not { } source
            || HomeTargetLanguage is not { } target
            || GetTranslationConfigurationError(mode) is not null)
            return null;

        var local = mode == 2;
        return new RealtimeTranslationInput(text, mode, source, target,
            local ? LocalTranslationSourceComboBox.SelectedIndex : 0,
            local ? LocalEndpointTextBox.Text.Trim() : string.Empty,
            local ? LocalModelTextBox.Text.Trim() : string.Empty,
            local ? _selectedOfflineModelPath : string.Empty,
            local ? string.Empty : _aliyunAccessKeyId,
            local ? string.Empty : _aliyunAccessKeySecret,
            local ? SettingsState.PromptStyle : 0,
            local && SettingsState.LanguageDetails,
            local ? SettingsState.CustomPrompt : string.Empty,
            local && _settings.AiStreamingEnabled);
    }

    private void RefreshRealtimeTranslation()
    {
        if (_suppressEvents || _settings is null) return;
        var input = CaptureRealtimeInput(includeEmptyText: _isRealtimeTranslating);
        if (_isTranslating || _isSelectionTranslating)
        {
            CancelRealtimeTranslation();
            // Input edited during manual translation should be translated after it finishes.
            if (!Equals(input, _realtimeInput)) _realtimeInput = null;
            return;
        }

        // Let an active request finish when only its text changes. Keep the latest text in
        // the input box; completion will refresh and schedule it without parallel requests.
        if (_isRealtimeTranslating && input is not null && _realtimeInput is not null
            && Equals(input with { Text = _realtimeInput.Text }, _realtimeInput)) return;
        if (Equals(input, _realtimeInput)) return;
        _realtimeInput = input;
        CancelRealtimeTranslation();
        if (input is null) return;
        var operation = new RealtimeTranslationOperation();
        _realtimeOperation = operation;
        _ = RunRealtimeTranslationAsync(input, operation);
    }

    private void CancelRealtimeTranslation()
    {
        var operation = _realtimeOperation;
        _realtimeOperation = null;
        _isRealtimeTranslating = false;
        // Killing the offline process or cancelling HTTP must not block the next keystroke.
        // The running task waits for these callbacks before disposing its token source.
        if (operation is not null)
            operation.CancellationTask = operation.Cancellation.CancelAsync();
    }

    private void SuspendRealtimeTranslationForCurrentInput()
    {
        CancelRealtimeTranslation();
        _realtimeInput = CaptureRealtimeInput();
        UpdateUiState();
    }

    private bool IsCurrentRealtimeTranslation(RealtimeTranslationInput input, RealtimeTranslationOperation operation) =>
        ReferenceEquals(operation, _realtimeOperation) && !operation.Cancellation.IsCancellationRequested
        && !_isTranslating && !_isSelectionTranslating && Equals(input, CaptureRealtimeInput());

    private async Task RunRealtimeTranslationAsync(RealtimeTranslationInput input, RealtimeTranslationOperation operation)
    {
        var cts = operation.Cancellation;
        try
        {
            await Task.Delay(RealtimeTranslationDelay, cts.Token);
            if (!IsCurrentRealtimeTranslation(input, operation)) return;
            _isRealtimeTranslating = true;
            UpdateUiState();
            var result = await TranslateTextAsync(input.Text, input.Mode, input.Source, input.Target,
                cts.Token, onTextUpdated: text =>
                {
                    if (IsCurrentRealtimeTranslation(input, operation)) SetHomeOutput(text);
                });
            if (IsCurrentRealtimeTranslation(input, operation)) SetHomeOutput(result);
            // Only the manual translation flow adds history.
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentRealtimeTranslation(input, operation))
                ShowInfo(Localization.Text("实时翻译失败：{0}", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(operation, _realtimeOperation))
            {
                _realtimeOperation = null;
                _isRealtimeTranslating = false;
                if (!_isWindowClosed) UpdateUiState();
            }
            try
            {
                await operation.CancellationTask;
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
