using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using Windtranslator.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

public sealed record ImageTranslationPageState
{
    public IReadOnlyList<StorageFile> Files { get; init; } = Array.Empty<StorageFile>();
    public ImageSource? Preview { get; init; }
    public string Summary { get; init; } = Localization.Get("S5cc85c2fce504428");
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
    public int ModelIndex { get; init; } = -1;
    public IReadOnlyList<string> SourceNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetNames { get; init; } = Array.Empty<string>();
    public int SourceIndex { get; init; }
    public int TargetIndex { get; init; } = -1;
    public bool IsTranslating { get; init; }
    public bool IsClearConfirmationOpen { get; init; }
    public string Progress { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public bool CanClear => !IsTranslating && Files.Count > 0;
}

public sealed class ImageTranslationPageStore
{
    private event Action? Changed;
    public ImageTranslationPageState State { get; private set; } = new();
    public Action Pick { get; init; } = () => { };
    public Action Translate { get; init; } = () => { };
    public Action Clear { get; init; } = () => { };
    public Action<bool> ConfirmClear { get; init; } = _ => { };
    public Action Copy { get; init; } = () => { };
    public Action<int> SourceChanged { get; init; } = _ => { };
    public Action<int> TargetChanged { get; init; } = _ => { };
    public Action<int> ModelChanged { get; init; } = _ => { };
    public Action Subscribe(Action callback) { Changed += callback; return () => Changed -= callback; }
    public void Update(Func<ImageTranslationPageState, ImageTranslationPageState> update)
    {
        var next = update(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
}

public sealed class ImageTranslationPageView(ImageTranslationPageStore store) : Component
{
    public override Element Render()
    {
        var state = UseExternalStore(store.Subscribe, () => store.State);
        var models = UseMemo(() => state.Models.ToArray(), state.Models);
        var sources = UseMemo(() => state.SourceNames.ToArray(), state.SourceNames);
        var targets = UseMemo(() => state.TargetNames.ToArray(), state.TargetNames);
        var files = UseMemo(() => state.Files.Select(file =>
            Caption(file.Name).TextTrimming(TextTrimming.CharacterEllipsis)
                .Foreground(Theme.SecondaryText).ToolTip(file.Name).WithKey(file.Path)).ToArray(), state.Files);
        var input = Surface(
            Grid([GridSize.Star()], [GridSize.Star(), GridSize.Auto],
                VStack(24,
                    ComboBox(models, state.ModelIndex, store.ModelChanged)
                        .Header(Localization.Get("Sc98e118e0a43f078"))
                        .AutomationName(Localization.Get("Sc98e118e0a43f078")),
                    Grid([GridSize.Star(), GridSize.Star()], [GridSize.Auto],
                        ComboBox(sources, state.SourceIndex, store.SourceChanged)
                            .Header(Localization.Get("S4968bbcf1aff9e7a"))
                            .AutomationName(Localization.Get("S4968bbcf1aff9e7a")),
                        ComboBox(targets, state.TargetIndex, store.TargetChanged)
                            .Header(Localization.Get("S966a23e84aedeea5"))
                            .AutomationName(Localization.Get("S966a23e84aedeea5")).Grid(column: 1))
                        .Set(grid => grid.ColumnSpacing = 8),
                    Border(Grid([GridSize.Star()], [GridSize.Star()],
                        state.Preview is null
                            ? TextBlock(Localization.Get("S2b9075cc8b636dae"))
                                .Foreground(Theme.SecondaryText).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                            // preview.16 Image only accepts string sources; the window owns the decoded bitmap.
                            : Image(string.Empty).Set(image => { image.Source = state.Preview; image.Stretch = Stretch.Uniform; })
                                .AutomationName(state.Summary).Margin(8)))
                        .Background(Theme.Ref("WindLayerBackgroundBrush"))
                        .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1).CornerRadius(4).Height(184),
                    VStack(4,
                        Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
                            BodyStrong(state.Summary).TextWrapping().VAlign(VerticalAlignment.Center),
                            Button(HStack(8, Icon("Add"), TextBlock(Localization.Get("S9f0d4e4656ee5dbc"))), store.Pick)
                                .AutomationName(Localization.Get("S9f0d4e4656ee5dbc"))
                                .IsEnabled(!state.IsTranslating).Grid(column: 1))
                            .Set(grid => grid.ColumnSpacing = 8),
                        ListView(files).Set(list => { list.SelectionMode = ListViewSelectionMode.None; list.IsHitTestVisible = false; })
                            .MaxHeight(112))),
                HStack(8,
                    Button(HStack(8, Icon("Delete"), TextBlock(Localization.Get("Sbce2377283c2455a"))), store.Clear)
                        .AutomationName(Localization.Get("Sbce2377283c2455a")).IsEnabled(state.CanClear),
                    Button(HStack(8, Icon("Forward"), TextBlock(Localization.Get("Sb7986efeed2532ee"))), store.Translate)
                        .AccentButton().AutomationName(Localization.Get("Sb7986efeed2532ee"))
                        .IsEnabled(state.CanClear))
                    .HAlign(HorizontalAlignment.Right).Grid(row: 1))
                .Set(grid => grid.RowSpacing = 24));
        var output = Surface(
            Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
                    BodyStrong(Localization.Get("S80461c2decc549dc")),
                    TextBlock(state.Progress).Foreground(Theme.SecondaryText)
                        .VAlign(VerticalAlignment.Center).Grid(column: 1)),
                TextBox(state.Output, placeholderText: Localization.Get("S3c354cc046473458"))
                    .IsReadOnly().AcceptsReturn().TextWrapping().FontFamily("Segoe UI").FontSize(16)
                    .AutomationName(Localization.Get("S80461c2decc549dc"))
                    .Set(box => {
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
                    }).MinHeight(260).Margin(0, 8, 0, 8).Grid(row: 1),
                Button(Localization.Get("Sdc4a4263a5fbd289"), store.Copy)
                    .AutomationName(Localization.Get("Sdc4a4263a5fbd289"))
                    .IsEnabled(!string.IsNullOrWhiteSpace(state.Output))
                    .HAlign(HorizontalAlignment.Right).Grid(row: 2))).Grid(column: 1);
        var confirmation = ContentDialog(Localization.Text("清除已选图片"),
            TextBlock(Localization.Text("将清除已选图片、预览和当前译文。")).TextWrapping(), Localization.Text("清除")) with
        {
            IsOpen = state.IsClearConfirmationOpen,
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Close,
            OnClosed = result => store.ConfirmClear(result == ContentDialogResult.Primary),
        };
        return Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], input, output, confirmation)
            .Set(grid => grid.ColumnSpacing = 16).Margin(16, 24, 16, 16);
    }
    private static Element Surface(Element content) => Border(content).Padding(16)
        .Background(Theme.Ref("WindCardBackgroundBrush")).WithBorder(Theme.Ref("WindCardStrokeBrush"), 1).CornerRadius(8);
}

