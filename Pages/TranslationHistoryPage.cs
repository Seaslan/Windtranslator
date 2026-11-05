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

public sealed record TranslationHistoryPageState(IReadOnlyList<string> Items)
{
    public static TranslationHistoryPageState Initial { get; } = new(Array.Empty<string>());

    public bool HasItems => Items.Count > 0;
}

public sealed class TranslationHistoryPageStore
{
    private event Action? Changed;

    public TranslationHistoryPageState State { get; private set; } = TranslationHistoryPageState.Initial;

    public Action Clear { get; init; } = () => { };

    public Action<string> ItemClicked { get; init; } = _ => { };

    public Action Subscribe(Action onChanged)
    {
        Changed += onChanged;
        return () => Changed -= onChanged;
    }

    public void Update(Func<TranslationHistoryPageState, TranslationHistoryPageState> change)
    {
        var next = change(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
}

public sealed class TranslationHistoryPageView : Component
{
    private readonly TranslationHistoryPageStore _store;

    public TranslationHistoryPageView(TranslationHistoryPageStore store) => _store = store;

    public override Element Render()
    {
        var state = UseExternalStore(_store.Subscribe, () => _store.State);
        var entries = state.Items
            .Select((text, index) => new HistoryEntry(index, text))
            .ToArray();

        var header = Grid(
            [GridSize.Auto, GridSize.Star(), GridSize.Auto],
            [GridSize.Auto],
            [
                Subtitle(Localization.Get("S25a4303fc7712d0a"))
                    .VAlign(VerticalAlignment.Center),
                Button(
                        HStack(8, Icon("Delete"), TextBlock(Localization.Get("Sae8dec30ceb9d800"))),
                        _store.Clear)
                    .IsEnabled(state.HasItems)
                    .AutomationName(Localization.Get("Sae8dec30ceb9d800"))
                    .Grid(column: 2),
            ])
            .Set(grid => grid.ColumnSpacing = 12);

        var list = ListView(
                entries,
                entry => $"{entry.Index}:{entry.Text}",
                (entry, _) => HistoryItem(entry.Text))
            .Set(listView => listView.IsItemClickEnabled = true)
            .ItemClick(entry => _store.ItemClicked(entry.Text))
            .Set(listView => listView.SelectionMode = ListViewSelectionMode.None)
            .Grid(row: 0)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch);

        var empty = TextBlock(Localization.Get("S6ea15464fb844e0c"))
            .IsVisible(!state.HasItems)
            .IsHitTestVisible(false)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .Grid(row: 0);

        return Grid(
                [GridSize.Star()],
                [GridSize.Star()],
                [list, empty])
            .Margin(0, 24, 0, 0)
            .Grid(row: 1);
    }

    private static Element HistoryItem(string text) =>
        Border(TextBlock(text).TextWrapping())
            .Margin(0, 0, 0, 8)
            .Padding(12)
            .Background(Theme.Ref("WindCardBackgroundBrush"))
            .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8);

    private sealed record HistoryEntry(int Index, string Text);
}
