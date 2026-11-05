using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windtranslator.Models;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private const string TranslateSelectionCommandTag = "TranslateSelection";
    private const string TranslateSelectionShortcutText = "Ctrl+T";

    private enum SelectionTranslationOrigin
    {
        Source,
        Output,
    }

    private readonly Dictionary<TextBox, SelectionTranslationOrigin> _selectionTranslationTargets = new();
    private readonly HashSet<FlyoutBase> _hookedSelectionFlyouts = new();
    private CancellationTokenSource? _selectionTranslationCts;

    private bool CanTranslateSelection =>
        !_isMiniMode && _currentPageTag == "Home" && !_isTranslating && !_isSelectionTranslating;

    private void InstallSelectionTranslationCommands(TextBox textBox, SelectionTranslationOrigin origin)
    {
        if (_selectionTranslationTargets.ContainsKey(textBox)) return;
        // Extend the built-in text menu rather than replacing it: the TextBox places
        // its own flyout at the pointer, while a replacement is placed by the control edge.
        // The default flyout may be shared, so resolve the box from the flyout's target.
        if (textBox.ContextFlyout is not TextCommandBarFlyout flyout)
        {
            flyout = new TextCommandBarFlyout();
            textBox.ContextFlyout = flyout;
        }

        _selectionTranslationTargets[textBox] = origin;
        if (_hookedSelectionFlyouts.Add(flyout))
        {
            flyout.Opening += (_, _) =>
            {
                if (flyout.Target is TextBox target && _selectionTranslationTargets.TryGetValue(target, out var targetOrigin))
                {
                    AddTranslateSelectionCommand(flyout, target, targetOrigin);
                }
                else
                {
                    RemoveTranslateSelectionCommand(flyout);
                }
            };
        }

        var accelerator = new KeyboardAccelerator { Key = VirtualKey.T, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (sender, args) =>
        {
            args.Handled = true;
            if (string.IsNullOrWhiteSpace(textBox.SelectedText))
            {
                ShowInfo(Localization.Text("请先选中要翻译的文本。"), InfoBarSeverity.Informational);
                return;
            }

            _ = TranslateSelectionAsync(origin, textBox.SelectedText, textBox.SelectionStart, textBox.SelectionLength);
        };
        textBox.KeyboardAccelerators.Add(accelerator);
        textBox.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private static void RemoveTranslateSelectionCommand(TextCommandBarFlyout flyout)
    {
        for (var i = flyout.SecondaryCommands.Count - 1; i >= 0; i--)
        {
            if (flyout.SecondaryCommands[i] is FrameworkElement { Tag: TranslateSelectionCommandTag })
            {
                flyout.SecondaryCommands.RemoveAt(i);
            }
        }
    }

    private void AddTranslateSelectionCommand(TextCommandBarFlyout flyout, TextBox textBox, SelectionTranslationOrigin origin)
    {
        RemoveTranslateSelectionCommand(flyout);
        var selectedText = textBox.SelectedText;
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        // Capture the selection now; focus moves to the menu before the item is clicked.
        var selectionStart = textBox.SelectionStart;
        var selectionLength = textBox.SelectionLength;
        var button = new AppBarButton
        {
            Tag = TranslateSelectionCommandTag,
            Label = Localization.Text("翻译选中文本"),
            Icon = new SymbolIcon(Symbol.Forward),
            KeyboardAcceleratorTextOverride = TranslateSelectionShortcutText,
            IsEnabled = CanTranslateSelection,
        };
        button.Click += (_, _) =>
        {
            flyout.Hide();
            _ = TranslateSelectionAsync(origin, selectedText, selectionStart, selectionLength);
        };
        flyout.SecondaryCommands.Insert(0, button);
        flyout.SecondaryCommands.Insert(1, new AppBarSeparator { Tag = TranslateSelectionCommandTag });
    }

    private async Task TranslateSelectionAsync(
        SelectionTranslationOrigin origin,
        string selectedText,
        int selectionStart,
        int selectionLength)
    {
        if (!CanTranslateSelection || string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        var sourceLanguage = HomeSourceLanguage ?? Languages[0];
        var targetLanguage = HomeTargetLanguage ?? Languages[3];
        if (origin == SelectionTranslationOrigin.Output)
        {
            if (sourceLanguage.ApiCode == "auto")
            {
                ShowInfo(Localization.Text("请先指定源语言，再翻译选中的译文。"), InfoBarSeverity.Warning);
                return;
            }

            (sourceLanguage, targetLanguage) = (targetLanguage, sourceLanguage);
        }

        var originalOutput = HomeState.Output;
        if (origin == SelectionTranslationOrigin.Output
            && (selectionStart < 0 || selectionStart > originalOutput.Length
                || selectionLength > originalOutput.Length - selectionStart
                || !string.Equals(originalOutput.Substring(selectionStart, selectionLength), selectedText, StringComparison.Ordinal)))
        {
            ShowInfo(Localization.Text("选中的译文已发生变化，请重新选择。"), InfoBarSeverity.Warning);
            return;
        }
        var expectedOutput = originalOutput;
        using var cancellation = new CancellationTokenSource();
        var selectionToken = cancellation.Token;
        _selectionTranslationCts = cancellation;
        void UpdateSelectedTranslation(string text)
        {
            selectionToken.ThrowIfCancellationRequested();
            if (origin == SelectionTranslationOrigin.Output)
            {
                if (!string.Equals(HomeState.Output, expectedOutput, StringComparison.Ordinal))
                    throw new InvalidOperationException(Localization.Text("选中的译文已发生变化，请重新选择。"));
                expectedOutput = originalOutput[..selectionStart] + text + originalOutput[(selectionStart + selectionLength)..];
                SetHomeOutput(expectedOutput);
            }
            else
            {
                SetHomeOutput(text);
            }
        }

        try
        {
            SuspendRealtimeTranslationForCurrentInput();
            _isSelectionTranslating = true;
            UpdateUiState();
            var translatedText = await TranslateTextAsync(
                selectedText,
                Math.Clamp(HomeState.ModeIndex, 0, 2),
                sourceLanguage,
                targetLanguage,
                selectionToken,
                onTextUpdated: UpdateSelectedTranslation);
            selectionToken.ThrowIfCancellationRequested();
            if (origin == SelectionTranslationOrigin.Source)
            {
                SetHomeOutput(translatedText);
                RequestHomeFocus(output: true);

            }
            else
            {
                UpdateSelectedTranslation(translatedText);
                RequestHomeFocus(output: true, start: selectionStart, length: translatedText.Length);
            }

            ShowInfo(Localization.Text("翻译完成。"), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowInfo(Localization.Text("已取消翻译。"), InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested
                && origin == SelectionTranslationOrigin.Output && HomeState.Output == expectedOutput)
                SetHomeOutput(originalOutput);
            ShowInfo(Localization.Text("选词翻译失败：{0}", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            _selectionTranslationCts = null;
            _isSelectionTranslating = false;
            UpdateUiState();
        }
    }
}
