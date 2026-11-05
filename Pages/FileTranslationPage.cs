using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Windtranslator.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

public sealed record FileTranslationModeOption(string Name, bool IsVisible, bool IsEnabled);

public sealed record FileTranslationPageState
{
    public string FilePath { get; init; } = Localization.Get("Sa96f374e86bc73e4");
    public string FileSize { get; init; } = string.Empty;
    public bool HasFile { get; init; }
    public IReadOnlyList<string> SourceLanguageNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetLanguageNames { get; init; } = Array.Empty<string>();
    public int SourceLanguageIndex { get; init; } = -1;
    public int TargetLanguageIndex { get; init; } = -1;
    public int ModeIndex { get; init; }
    public IReadOnlyList<FileTranslationModeOption> Modes { get; init; } = Array.Empty<FileTranslationModeOption>();
    public string ModeHint { get; init; } = string.Empty;
    public bool IsTranslating { get; init; }
    public string ProgressText { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public bool IsClearConfirmationOpen { get; init; }

    public bool CanTranslate => !IsTranslating && HasFile && ModeIndex >= 0
        && ModeIndex < Modes.Count && Modes[ModeIndex].IsEnabled;
    public bool CanClear => !IsTranslating && HasFile;
    public bool CanCopy => !IsTranslating && Output.Length > 0;
    public bool CanExport => CanCopy && HasFile;
}

public sealed class FileTranslationPageStore
{
    private event Action? Changed;
    public FileTranslationPageState State { get; private set; } = new();
    public Action PickFile { get; init; } = () => { };
    public Action Translate { get; init; } = () => { };
    public Action Clear { get; init; } = () => { };
    public Action<bool> ConfirmClear { get; init; } = _ => { };
    public Action Copy { get; init; } = () => { };
    public Action Export { get; init; } = () => { };
    public Action<int> SourceLanguageChanged { get; init; } = _ => { };
    public Action<int> TargetLanguageChanged { get; init; } = _ => { };
    public Action<int> ModeChanged { get; init; } = _ => { };

    public Action Subscribe(Action onChanged)
    {
        Changed += onChanged;
        return () => Changed -= onChanged;
    }

    public void Update(Func<FileTranslationPageState, FileTranslationPageState> change)
    {
        var next = change(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
}

public sealed class FileTranslationPageView : Component
{
    private readonly FileTranslationPageStore _store;
    public FileTranslationPageView(FileTranslationPageStore store) => _store = store;

    public override Element Render()
    {
        var state = UseExternalStore(_store.Subscribe, () => _store.State);
        var sourceNames = UseMemo(() => state.SourceLanguageNames.ToArray(), state.SourceLanguageNames);
        var targetNames = UseMemo(() => state.TargetLanguageNames.ToArray(), state.TargetLanguageNames);
        var modeNames = UseMemo(() => state.Modes.Select(mode => mode.Name).ToArray(), state.Modes);
        var modeStyle = UseMemo(() => new FileModeStyleSelector(state.Modes), state.Modes);

        var input = CardSurface(
            Grid(
                [GridSize.Star()], [GridSize.Star(), GridSize.Auto],
                [
                    VStack(16,
                        Button(HStack(8, Icon("OpenFile"), TextBlock(Localization.Get("S98b3c10d1fa31ee7"))), _store.PickFile)
                            .AutomationName(Localization.Get("S98b3c10d1fa31ee7"))
                            .IsEnabled(!state.IsTranslating)
                            .HAlign(HorizontalAlignment.Left),
                        TextBlock(state.FilePath).TextWrapping().Foreground(Theme.SecondaryText),
                        TextBlock(state.FileSize).Foreground(Theme.SecondaryText),
                        Grid(
                            [GridSize.Star(), GridSize.Star()], [GridSize.Auto],
                            [
                                ComboBox(sourceNames, state.SourceLanguageIndex, _store.SourceLanguageChanged)
                                    .Header(Localization.Get("S4968bbcf1aff9e7a"))
                                    .AutomationName(Localization.Get("S4968bbcf1aff9e7a"))
                                    .IsEnabled(!state.IsTranslating),
                                ComboBox(targetNames, state.TargetLanguageIndex, _store.TargetLanguageChanged)
                                    .Header(Localization.Get("S966a23e84aedeea5"))
                                    .AutomationName(Localization.Get("S966a23e84aedeea5"))
                                    .IsEnabled(!state.IsTranslating)
                                    .Grid(column: 1),
                            ])
                            .Set(grid => grid.ColumnSpacing = 8),
                        ComboBox(modeNames, state.ModeIndex, _store.ModeChanged)
                            .Header(Localization.Get("Sd37804040d141ab0"))
                            .Set(combo => combo.ItemContainerStyleSelector = modeStyle)
                            .AutomationName(Localization.Get("Sd37804040d141ab0"))
                            .IsEnabled(!state.IsTranslating),
                        TextBlock(state.ModeHint).TextWrapping().Foreground(Theme.SecondaryText)),
                    Grid(
                        [GridSize.Star(), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                        [
                            HStack(8,
                                ProgressRing().IsActive(state.IsTranslating).Width(22).Height(22),
                                TextBlock(state.ProgressText)
                                    .Foreground(Theme.SecondaryText)
                                    .VAlign(VerticalAlignment.Center))
                                .VAlign(VerticalAlignment.Center),
                            Button(HStack(8, Icon("Delete"), TextBlock(Localization.Get("Sbce2377283c2455a"))), _store.Clear)
                                .AutomationName(Localization.Get("Sbce2377283c2455a"))
                                .IsEnabled(state.CanClear)
                                .Grid(column: 1),
                            Button(HStack(8, Icon("Forward"), TextBlock(Localization.Get("Sb7986efeed2532ee"))), _store.Translate)
                                .AccentButton()
                                .AutomationName(Localization.Get("Sb7986efeed2532ee"))
                                .IsEnabled(state.CanTranslate)
                                .HAlign(HorizontalAlignment.Right)
                                .Grid(column: 2),
                        ])
                        .Set(grid => grid.ColumnSpacing = 8)
                        .Grid(row: 1),
                ])
                .Set(grid => grid.RowSpacing = 24));

        var output = CardSurface(
                Grid(
                    [GridSize.Star()], [GridSize.Auto, GridSize.Star(), GridSize.Auto],
                    [
                        BodyStrong(Localization.Get("S80461c2decc549dc")),
                        TextBox(state.Output, placeholderText: Localization.Get("Sd83fdf18df41c98d"))
                            .IsReadOnly()
                            .AcceptsReturn()
                            .TextWrapping()
                            .FontFamily("Segoe UI")
                            .FontSize(16)
                            .AutomationName(Localization.Get("S80461c2decc549dc"))
                            .Set(box =>
                            {
                                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
                                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
                            })
                            .MinHeight(340)
                            .Margin(0, 8, 0, 8)
                            .Grid(row: 1),
                        HStack(8,
                            Button(Localization.Get("S63d90d977348ab1f"), _store.Copy)
                                .AutomationName(Localization.Get("S63d90d977348ab1f"))
                                .IsEnabled(state.CanCopy),
                            Button(Localization.Get("S76420433f22d1c80"), _store.Export)
                                .AutomationName(Localization.Get("S76420433f22d1c80"))
                                .IsEnabled(state.CanExport))
                            .HAlign(HorizontalAlignment.Right)
                            .Grid(row: 2),
                    ]))
            .Grid(column: 1);

        var confirmation = ContentDialog(
            Localization.Text("清除已选文件"),
            TextBlock(Localization.Text("将清除已选文件和当前译文。")).TextWrapping(),
            Localization.Text("清除")) with
        {
            IsOpen = state.IsClearConfirmationOpen,
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = ContentDialogButton.Close,
            OnClosed = result => _store.ConfirmClear(result == ContentDialogResult.Primary),
        };

        return Grid([GridSize.Star(), GridSize.Star()], [GridSize.Star()], [input, output, confirmation])
            .Set(grid => grid.ColumnSpacing = 16)
            .Margin(16, 24, 16, 16);
    }

    private static Element CardSurface(Element content) => Border(content)
        .Padding(16)
        .Background(Theme.Ref("WindCardBackgroundBrush"))
        .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
        .CornerRadius(8);

    // Reactor preview.16 exposes no per-item enabled/visibility properties for
    // ComboBox. Keep its native containers and keyboard selection behavior.
    internal sealed class FileModeStyleSelector : StyleSelector
    {
        private readonly IReadOnlyList<FileTranslationModeOption> _modes;
        private readonly Style[] _styles;

        public FileModeStyleSelector(IReadOnlyList<FileTranslationModeOption> modes)
        {
            _modes = modes;
            _styles = modes.Select(mode => new Style(typeof(ComboBoxItem))
            {
                Setters =
                {
                    new Setter(Control.IsEnabledProperty, mode.IsEnabled),
                    new Setter(UIElement.VisibilityProperty, mode.IsVisible ? Visibility.Visible : Visibility.Collapsed),
                },
            }).ToArray();
        }

        protected override Style SelectStyleCore(object item, DependencyObject container)
        {
            for (var index = 0; index < _modes.Count; index++)
                if (Equals(item, _modes[index].Name)) return _styles[index];
            return base.SelectStyleCore(item, container);
        }
    }
}
