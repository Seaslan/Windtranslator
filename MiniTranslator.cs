using Microsoft.UI.Input;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;
using Windows.UI.Core;
using Windtranslator.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

// The window owns this state, so re-rendering or remounting the Reactor view
// never discards source text, a streamed result, or the selected language.
public sealed record MiniTranslatorState(
    string Input,
    string Result,
    bool HasResult,
    bool IsTranslating,
    bool IsStacked,
    int TargetIndex,
    int FocusRequest)
{
    public static MiniTranslatorState Initial { get; } = new(string.Empty, string.Empty, false, false, false, 0, 0);

    // In the single-box layout a finished result replaces the source text.
    public bool ShowsResultInSource => !IsStacked && (HasResult || (IsTranslating && Result.Length > 0));

    public bool IsSourceReadOnly => IsTranslating || (!IsStacked && HasResult);

    public bool CanTranslate => !IsTranslating && (IsStacked || !HasResult);
}

public sealed class MiniTranslatorStore
{
    private event Action? Changed;

    public MiniTranslatorState State { get; private set; } = MiniTranslatorState.Initial;

    public string[] TargetLanguageNames { get; set; } = [];

    public Action Translate { get; init; } = () => { };

    public Action ExitMiniMode { get; init; } = () => { };

    public Action<string> CopyResult { get; init; } = _ => { };

    public TextBox? SourceTextBox { get; internal set; }

    public Action Subscribe(Action onChanged)
    {
        Changed += onChanged;
        return () => Changed -= onChanged;
    }

    public void Update(Func<MiniTranslatorState, MiniTranslatorState> change)
    {
        var next = change(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }

    public void SetInput(string text) =>
        Update(s => s with { Input = text, Result = string.Empty, HasResult = false });

    public void RequestFocus() => Update(s => s with { FocusRequest = s.FocusRequest + 1 });

    internal void EditSource(string text)
    {
        // A read-only box can still report programmatic text changes; only real edits
        // of the source should discard the current result.
        var state = State;
        if (state.IsSourceReadOnly || state.ShowsResultInSource || text == state.Input) return;
        SetInput(text);
    }

    internal void Clear()
    {
        if (State.IsTranslating) return;
        SetInput(string.Empty);
        RequestFocus();
    }
}

public sealed class MiniTranslatorView : Component
{
    private readonly MiniTranslatorStore _store;

    public MiniTranslatorView(MiniTranslatorStore store) => _store = store;

    public override Element Render()
    {
        var state = UseExternalStore(_store.Subscribe, () => _store.State);

        UseEffect(() =>
        {
            if (state.FocusRequest == 0 || _store.SourceTextBox is not { } box || box.IsReadOnly) return;
            box.Focus(FocusState.Programmatic);
            box.SelectionStart = box.Text.Length;
        }, state.FocusRequest);

        var sourceText = state.ShowsResultInSource ? state.Result : state.Input;
        var hasResultText = !string.IsNullOrWhiteSpace(state.Result);

        var header = Grid(
            [GridSize.Auto, GridSize.Auto, GridSize.Star()],
            [GridSize.Auto],
            [
                Button(Icon("FullScreen"), _store.ExitMiniMode)
                    .Width(36).Height(34).Padding(0)
                    .ToolTip(Localization.Get("S9ad30df5111b0ead"))
                    .AutomationName(Localization.Get("S9ad30df5111b0ead")),
                TextBlock(Localization.Get("S34f52662eeb52c89"))
                    .VAlign(VerticalAlignment.Center)
                    .Grid(column: 1),
                ComboBox(_store.TargetLanguageNames, state.TargetIndex,
                        index => { if (index >= 0) _store.Update(s => s with { TargetIndex = index }); })
                    .IsEnabled(!state.IsTranslating)
                    .HAlign(HorizontalAlignment.Stretch)
                    .AutomationName(Localization.Get("S34f52662eeb52c89"))
                    .Grid(column: 2),
            ]).Set(g => g.ColumnSpacing = 8);

        var source = Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Star()],
            [
                TextBlock(Localization.Get("Se8502c968c8ab89d"))
                    .FontWeight(Microsoft.UI.Text.FontWeights.SemiBold)
                    .IsVisible(state.IsStacked),
                MultilineBox(sourceText, _store.EditSource)
                    .IsReadOnly(state.IsSourceReadOnly)
                    .PlaceholderText(Localization.Get("S99a6de45d0cd7780"))
                    .AutomationName(Localization.Get("Se8502c968c8ab89d"))
                    .OnMount(element => _store.SourceTextBox = element as TextBox)
                    .OnKeyDown(OnSourceKeyDown)
                    .Grid(row: 1),
            ]).Set(g => g.RowSpacing = 6).Margin(0, 8, 0, 4).Grid(row: 1);

        var result = Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Star()],
            [
                Grid(
                    [GridSize.Star(), GridSize.Auto],
                    [GridSize.Auto],
                    [
                        TextBlock(Localization.Get("S80461c2decc549dc"))
                            .FontWeight(Microsoft.UI.Text.FontWeights.SemiBold)
                            .VAlign(VerticalAlignment.Center),
                        Button(Icon(FontIcon("\uE8C8", fontSize: 16)), () => _store.CopyResult(_store.State.Result))
                            .SubtleButton()
                            .Width(28).Height(28).Padding(0)
                            .MinWidth(0).MinHeight(0)
                            .IsEnabled(hasResultText)
                            .ToolTip(Localization.Get("S032eb48c7a431ec0"))
                            .AutomationName(Localization.Get("S032eb48c7a431ec0"))
                            .Grid(column: 1),
                    ]).Set(g => g.ColumnSpacing = 8),
                MultilineBox(state.Result, null)
                    .IsReadOnly()
                    .PlaceholderText(Localization.Get("Sd83fdf18df41c98d"))
                    .AutomationName(Localization.Get("S80461c2decc549dc"))
                    .Grid(row: 1),
            ]).Set(g => g.RowSpacing = 6).Margin(0, 4, 0, 8).IsVisible(state.IsStacked).Grid(row: 2);

        var footer = Grid(
            [GridSize.Auto, GridSize.Star(), GridSize.Auto],
            [GridSize.Auto],
            [
                Button(Icon("Clear"), _store.Clear)
                    .Width(36).Height(34).Padding(0)
                    .IsEnabled(!state.IsTranslating)
                    .ToolTip(Localization.Get("S8adc7488d992ff3b"))
                    .AutomationName(Localization.Get("S8adc7488d992ff3b")),
                ProgressRing().IsActive(state.IsTranslating)
                    .Width(22).Height(22)
                    .HAlign(HorizontalAlignment.Right).VAlign(VerticalAlignment.Center)
                    .Grid(column: 1),
                Button(HStack(8, Icon("Forward"), TextBlock(Localization.Get("Sa52a08335185f048"))), _store.Translate)
                    .AccentButton()
                    .MinWidth(96).Height(34)
                    .IsEnabled(state.CanTranslate)
                    .AutomationName(Localization.Get("Sa52a08335185f048"))
                    .Grid(column: 2),
            ]).Set(g => g.ColumnSpacing = 8).Margin(0, 4, 0, 0).Grid(row: 3);

        // A zero-height row keeps the source box filling the window in the single-box layout.
        return Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Star(), state.IsStacked ? GridSize.Star() : GridSize.Px(0), GridSize.Auto],
            [header, source, result, footer]);
    }

    // Callers set the accessible name; the analyzer cannot see through this helper.
#pragma warning disable REACTOR_A11Y_003
    private static TextBoxElement MultilineBox(string text, Action<string>? onChanged) =>
        TextBox(text, onChanged)
            .AcceptsReturn()
            .TextWrapping()
            .FontSize(16)
            .Set(box =>
            {
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            });
#pragma warning restore REACTOR_A11Y_003

    private void OnSourceKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _store.State.IsSourceReadOnly) return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shift.HasFlag(CoreVirtualKeyStates.Down)) return;
        e.Handled = true;
        _store.Translate();
    }
}
