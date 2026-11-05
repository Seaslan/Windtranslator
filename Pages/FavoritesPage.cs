using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using Windtranslator.Models;
using Windtranslator.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

public sealed record FavoritesPageState(IReadOnlyList<FavoriteTranslation> Items, bool IsInteractive)
{
    public static FavoritesPageState Initial { get; } = new(Array.Empty<FavoriteTranslation>(), true);

    public bool HasItems => Items.Count > 0;
}

public sealed class FavoritesPageStore
{
    private event Action? Changed;

    public FavoritesPageState State { get; private set; } = FavoritesPageState.Initial;

    public Action<FavoriteTranslation> ItemClicked { get; init; } = _ => { };

    public Action<FavoriteTranslation> Remove { get; init; } = _ => { };

    public Action<FavoriteTranslation> Speak { get; init; } = _ => { };

    public Action<string> Copy { get; init; } = _ => { };

    public Action Subscribe(Action onChanged)
    {
        Changed += onChanged;
        return () => Changed -= onChanged;
    }

    public void Update(Func<FavoritesPageState, FavoritesPageState> change)
    {
        var next = change(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
}

public sealed class FavoritesPageView : Component
{
    private readonly FavoritesPageStore _store;

    public FavoritesPageView(FavoritesPageStore store) => _store = store;

    public override Element Render()
    {
        var state = UseExternalStore(_store.Subscribe, () => _store.State);
        var containerStyle = UseMemo(SavedTextItems.CreateContainerStyle);

        var list = ListView(
                state.Items,
                FavoriteKey,
                (favorite, _) => FavoriteItem(favorite, state.IsInteractive).WithKey(FavoriteKey(favorite)))
            .ItemClick(favorite => _store.ItemClicked(favorite))
            .Set(listView => listView.ItemContainerStyle = containerStyle)
            with { SelectionMode = ListViewSelectionMode.None };

        list = list
            .IsEnabled(state.IsInteractive)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Grid(row: 0);

        var empty = TextBlock(Localization.Get("Sf4320f16d26729e4"))
            .IsVisible(!state.HasItems)
            .IsHitTestVisible(false)
            .Foreground(Theme.SecondaryText)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
            .Grid(row: 0);

        return Grid(
                [GridSize.Star()],
                [GridSize.Auto, GridSize.Star()],
                [
                    Subtitle(Localization.Get("Saa32dcd1b2c738fe"))
                        .VAlign(VerticalAlignment.Center),
                    Grid(
                            [GridSize.Star()],
                            [GridSize.Star()],
                            [list, empty])
                        .Margin(0, 24, 0, 0)
                        .Grid(row: 1),
                ])
            .Margin(36, 24, 36, 16);
    }

    private Element FavoriteItem(FavoriteTranslation favorite, bool isInteractive)
    {
        var removeButton = Button(
                Icon(FontIcon("\uE711", fontSize: 14)),
                () => _store.Remove(favorite))
            .MinWidth(28)
            .Padding(4)
            .ToolTip(Localization.Get("S9a27790c41c14ecc"))
            .AutomationName(Localization.Get("S9a27790c41c14ecc"))
            .IsEnabled(isInteractive);

        var moreButton = Button(
                Icon(FontIcon("\uE712", fontSize: 14)))
            .MinWidth(28)
            .Padding(4)
            .ToolTip(Localization.Get("S38844b135cf70dfc"))
            .AutomationName(Localization.Get("S38844b135cf70dfc"))
            .IsEnabled(isInteractive);

        var allText = string.IsNullOrEmpty(favorite.TranslatedText)
            ? favorite.SourceText
            : favorite.SourceText + Environment.NewLine + Environment.NewLine + favorite.TranslatedText;
        moreButton = moreButton.WithFlyout(
            MenuItems(
                new Microsoft.UI.Reactor.Core.MenuFlyoutItemBase[]
                {
                    MenuItem(Localization.Text("朗读源文本"), () => _store.Speak(favorite), "Audio") with
                    {
                        IsEnabled = !string.IsNullOrEmpty(favorite.SourceText),
                    },
                    MenuItem(Localization.Text("复制源文本"), () => _store.Copy(favorite.SourceText), "Copy") with
                    {
                        IsEnabled = !string.IsNullOrEmpty(favorite.SourceText),
                    },
                    MenuItem(Localization.Text("复制译文"), () => _store.Copy(favorite.TranslatedText), "Copy") with
                    {
                        IsEnabled = !string.IsNullOrEmpty(favorite.TranslatedText),
                    },
                    MenuItem(Localization.Text("全部复制"), () => _store.Copy(allText), "Copy") with
                    {
                        IsEnabled = !string.IsNullOrEmpty(allText),
                    },
                }));

        return SavedTextItems.Card(
                Grid(
                    [GridSize.Star(), GridSize.Auto],
                    [GridSize.Auto],
                    [
                        VStack(
                            12,
                            TextBlock(favorite.SourceText).TextWrapping(),
                            Border(Empty())
                                .Height(1)
                                .Background(Theme.Ref("WindCardStrokeBrush")),
                            TextBlock(favorite.TranslatedText)
                                .TextWrapping()
                                .Foreground(Theme.SecondaryText)),
                        VStack(4, removeButton, moreButton)
                            .VAlign(VerticalAlignment.Top)
                            .Grid(column: 1),
                    ])
                    .Set(grid => grid.ColumnSpacing = 12));
    }

    private static string FavoriteKey(FavoriteTranslation favorite) =>
        $"{favorite.SourceText.Length}:{favorite.SourceText}{favorite.TranslatedText}";
}
