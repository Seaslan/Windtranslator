using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Windtranslator.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

public sealed record HomePageState
{
    public string Source { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FileTranslationModeOption> Modes { get; init; } = Array.Empty<FileTranslationModeOption>();
    public int SourceIndex { get; init; }
    public int TargetIndex { get; init; } = -1;
    public int ModeIndex { get; init; }
    public bool IsTranslating { get; init; }
    public bool IsBusy { get; init; }
    public bool IsSelectionTranslating { get; init; }
    public bool IsOpeningDictation { get; init; }
    public bool IsSpeaking { get; init; }
    public bool IsSynthesizing { get; init; }
    public bool IsFavorite { get; init; }
    public bool CanFavorite { get; init; }
    public bool EnterToTranslate { get; init; }
    public bool IsMiniMode { get; init; }
    public bool IsClipboardTipOpen { get; init; }
    public string ClipboardPreview { get; init; } = string.Empty;
    public int FocusRequest { get; init; }
    public bool FocusOutput { get; init; }
    public int SelectionStart { get; init; } = -1;
    public int SelectionLength { get; init; }
}

public sealed class HomePageStore
{
    private event Action? Changed;
    public HomePageState State { get; private set; } = new();
    public ElementRef SourceRef { get; } = new();
    public Func<ElementRef> MiniClipboardTarget { get; init; } = () => new ElementRef();
    public Action Translate { get; init; } = () => { };
    public Action Clear { get; init; } = () => { };
    public Action Copy { get; init; } = () => { };
    public Action Speak { get; init; } = () => { };
    public Action Mic { get; init; } = () => { };
    public Action Favorite { get; init; } = () => { };
    public Action ShowFavorites { get; init; } = () => { };
    public Action ShowHistory { get; init; } = () => { };
    public Action Swap { get; init; } = () => { };
    public Action MiniMode { get; init; } = () => { };
    public Action PasteClipboard { get; init; } = () => { };
    public Action CloseClipboard { get; init; } = () => { };
    public Action<string> EditSource { get; init; } = _ => { };
    public Action<int> SourceChanged { get; init; } = _ => { };
    public Action<int> TargetChanged { get; init; } = _ => { };
    public Action<int> ModeChanged { get; init; } = _ => { };
    public Action<TextBox, bool> MountTextBox { get; init; } = (_, _) => { };
    public Action<TextBox> UnmountTextBox { get; init; } = _ => { };
    public Action<object, KeyRoutedEventArgs> SourceKeyDown { get; init; } = (_, _) => { };
    public Action Subscribe(Action callback) { Changed += callback; return () => Changed -= callback; }
    public void Update(Func<HomePageState, HomePageState> update)
    {
        var next = update(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
}

public sealed class HomeToolbarView(HomePageStore store) : Component
{
    public override Element Render()
    {
        var state = UseExternalStore(store.Subscribe, () => store.State);
        var sources = UseMemo(() => state.Sources.ToArray(), state.Sources);
        var targets = UseMemo(() => state.Targets.ToArray(), state.Targets);
        var modes = UseMemo(() => state.Modes.Select(mode => mode.Name).ToArray(), state.Modes);
        var style = UseMemo(() => new FileTranslationPageView.FileModeStyleSelector(state.Modes), state.Modes);
        return Grid([GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star()],
            [GridSize.Auto],
            ComboBox(modes, state.ModeIndex, store.ModeChanged).Set(combo => combo.ItemContainerStyleSelector = style)
                .AutomationName(Localization.Get("Sd37804040d141ab0")).MinWidth(130).MinHeight(34).VAlign(VerticalAlignment.Center),
            ComboBox(sources, state.SourceIndex, store.SourceChanged)
                .AutomationName(Localization.Get("S4968bbcf1aff9e7a")).MinWidth(104).MinHeight(34)
                .VAlign(VerticalAlignment.Center).Grid(column: 1),
            Button(Icon("Switch"), store.Swap).AutomationName(Localization.Get("S72046b05a1d96e97"))
                .ToolTip(Localization.Get("S72046b05a1d96e97")).Padding(10, 0).MinHeight(34)
                .VAlign(VerticalAlignment.Center).Grid(column: 2),
            ComboBox(targets, state.TargetIndex, store.TargetChanged)
                .AutomationName(Localization.Get("S966a23e84aedeea5")).MinWidth(104).MinHeight(34)
                .VAlign(VerticalAlignment.Center).Grid(column: 3),
            Button(HStack(8, Icon("Forward"), TextBlock(Localization.Text(state.IsTranslating ? "取消" : "翻译"))), store.Translate)
                .AccentButton().AutomationName(Localization.Text(state.IsTranslating ? "取消" : "翻译"))
                .IsEnabled(state.IsTranslating || (!state.IsSelectionTranslating && !string.IsNullOrWhiteSpace(state.Source)))
                .MinWidth(104).MinHeight(34).Padding(16, 0).VAlign(VerticalAlignment.Center).Grid(column: 4),
            Grid([GridSize.Star()], [GridSize.Auto],
                ProgressRing().IsActive(state.IsBusy).Width(22).Height(22).Margin(8, 0, 0, 0).VAlign(VerticalAlignment.Center),
                Button(Icon("BackToWindow"), store.MiniMode).MinHeight(34).Padding(10, 0)
                    .AutomationName(Localization.Get("Sd99a4b7799b40e4b")).ToolTip(Localization.Get("Sd99a4b7799b40e4b")))
                .Grid(column: 5))
            .Set(grid => grid.ColumnSpacing = 8).Padding(16, 4, 16, 4);
    }
}

public sealed class HomePageView(HomePageStore store) : Component
{
    public override Element Render()
    {
        var state = UseExternalStore(store.Subscribe, () => store.State);
        var outputRef = this.UseElementRef<TextBox>();
        UseEffect(() =>
        {
            if (state.FocusRequest == 0) return;
            var box = state.FocusOutput ? outputRef.Current : store.SourceRef.Current as TextBox;
            if (box is null) return;
            box.Focus(FocusState.Programmatic);
            box.SelectionStart = state.SelectionStart < 0 ? box.Text.Length : Math.Clamp(state.SelectionStart, 0, box.Text.Length);
            box.SelectionLength = Math.Clamp(state.SelectionLength, 0, box.Text.Length - box.SelectionStart);
        }, state.FocusRequest);
        var favoriteLabel = Localization.Text(state.IsFavorite ? "取消收藏" : "收藏源文本和译文");
        var source = Surface(Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star(), GridSize.Auto],
            Grid([GridSize.Auto, GridSize.Star(), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                BodyStrong(Localization.Get("Se8502c968c8ab89d")).VAlign(VerticalAlignment.Center),
                TextBlock(Localization.Text("{0} 字", state.Source.Length)).Foreground(Theme.SecondaryText)
                    .Margin(12, 0, 0, 0).VAlign(VerticalAlignment.Center).Grid(column: 1),
                ToolButton("Clear", Localization.Get("S1ef3de06b32e521b"), store.Clear).Grid(column: 2),
                ToolButton("Microphone", Localization.Get("S2fdc91e671fde5aa"), store.Mic)
                    .IsEnabled(!state.IsTranslating && !state.IsSelectionTranslating && !state.IsOpeningDictation).Grid(column: 3))
                .Set(grid => grid.ColumnSpacing = 8),
            Multiline(state.Source, store.EditSource, Localization.Get("Sd7c905a34a658bb7"))
                .AcceptsReturn(!state.EnterToTranslate)
                .AutomationName(Localization.Get("Se8502c968c8ab89d"))
                .Ref(store.SourceRef).OnMount(element => store.MountTextBox((TextBox)element, false))
                .OnUnmount(element => store.UnmountTextBox((TextBox)element))
                .OnKeyDown(store.SourceKeyDown).Margin(0, 8, 0, 0).Grid(row: 1),
            HStack(8,
                Button(HStack(8, Icon("Favorite"), TextBlock(Localization.Get("Saa32dcd1b2c738fe"))), store.ShowFavorites)
                    .AutomationName(Localization.Get("Saa32dcd1b2c738fe")),
                Button(HStack(8, Icon("Clock"), TextBlock(Localization.Get("S25a4303fc7712d0a"))), store.ShowHistory)
                    .AutomationName(Localization.Get("S25a4303fc7712d0a")))
                .HAlign(HorizontalAlignment.Left).Margin(0, 8, 0, 0).Grid(row: 2)));
        var output = Surface(Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star()],
            Grid([GridSize.Auto, GridSize.Star(), GridSize.Auto, GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                BodyStrong(Localization.Get("S80461c2decc549dc")).VAlign(VerticalAlignment.Center),
                TextBlock(Localization.Text("{0} 字", state.Output.Length)).Foreground(Theme.SecondaryText)
                    .Margin(12, 0, 0, 0).VAlign(VerticalAlignment.Center).Grid(column: 1),
                Button(Icon(FontIcon(state.IsFavorite ? "\uE735" : "\uE734", fontSize: 20)), store.Favorite)
                    .AutomationName(favoriteLabel).ToolTip(favoriteLabel).IsEnabled(state.CanFavorite)
                    .Width(38).MinHeight(34).Padding(8, 6).Grid(column: 2),
                ToolButton("Copy", Localization.Get("S032eb48c7a431ec0"), store.Copy).Grid(column: 3),
                ToolButton(state.IsSpeaking ? "Stop" : "Audio", Localization.Get("S6d5747e03cc75137"), store.Speak)
                    .IsEnabled(!state.IsSynthesizing && (state.IsSpeaking || !string.IsNullOrWhiteSpace(state.Output))).Grid(column: 4))
                .Set(grid => grid.ColumnSpacing = 8),
            Multiline(state.Output, null, Localization.Get("Sd83fdf18df41c98d")).IsReadOnly().AcceptsReturn()
                .AutomationName(Localization.Get("S80461c2decc549dc")).Ref(outputRef)
                .OnMount(element => store.MountTextBox((TextBox)element, true))
                .OnUnmount(element => store.UnmountTextBox((TextBox)element))
                .Margin(0, 8, 0, 0).Grid(row: 1))).Grid(column: 1);
        return Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], source, output)
            .Set(grid => grid.ColumnSpacing = 16).Margin(16, 24, 16, 16);
    }
    private static ButtonElement ToolButton(string icon, string label, Action action) =>
        Button(Icon(icon), action).AutomationName(label).ToolTip(label).Padding(8, 6).MinHeight(34);
    private static TextBoxElement Multiline(string value, Action<string>? changed, string placeholder) =>
        TextBox(value, changed, placeholderText: placeholder).TextWrapping().FontFamily("Segoe UI").FontSize(16)
            .AutomationName(placeholder)
            .Set(box => {
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
            }).MinHeight(260);
    private static Element Surface(Element content) => Border(content).Padding(16)
        .Background(Theme.Ref("WindCardBackgroundBrush")).WithBorder(Theme.Ref("WindCardStrokeBrush"), 1).CornerRadius(8);
}

public sealed class HomeClipboardTipView(HomePageStore store) : Component
{
    public override Element Render()
    {
        var state = UseExternalStore(store.Subscribe, () => store.State);
        return TeachingTip(Localization.Get("S897b4ae359a2e50b"), state.ClipboardPreview,
            state.IsMiniMode ? store.MiniClipboardTarget() : store.SourceRef) with
        {
            IsOpen = state.IsClipboardTipOpen,
            ActionButtonContent = Localization.Get("S33517926747180e6"),
            CloseButtonContent = Localization.Get("S6e90ac940752ebf1"),
            PreferredPlacement = TeachingTipPlacementMode.Bottom,
            PlacementMargin = new Thickness(8),
            OnActionButtonClick = store.PasteClipboard,
            OnClosed = store.CloseClipboard,
        };
    }
}

