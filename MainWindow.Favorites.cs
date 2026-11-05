using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windtranslator.Models;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private FavoritesPageStore? _favoritesStore;

    private void CreateFavoritesStore()
    {
        var store = new FavoritesPageStore
        {
            ItemClicked = FavoriteItemClicked,
            Remove = RemoveFavorite,
            Speak = favorite => _ = SpeakFavoriteAsync(favorite),
            Copy = text =>
            {
                if (!string.IsNullOrEmpty(text))
                {
                    SetClipboardText(text);
                    ShowInfo(Localization.Text("已复制到剪贴板。"), InfoBarSeverity.Success);
                }
            },
        };

        var host = new ReactorHostControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        FavoritesPageHost.Content = host;
        host.Mount(new FavoritesPageView(store));
        _favoritesStore = store;
    }

    private void RefreshFavorites()
    {
        _settings.NormalizeFavorites();
        var favorites = _settings.Favorites.AsEnumerable().Reverse().ToArray();
        _favoritesStore?.Update(state => state with { Items = favorites });
        UpdateFavoritesState();
    }

    private void UpdateFavoritesState()
    {
        if (_settings is null) return;
        var sourceText = HomeState.Source;
        var translatedText = HomeState.Output;
        var canFavorite = !_isTranslating && !_isSelectionTranslating && !_isRealtimeTranslating
            && !string.IsNullOrWhiteSpace(sourceText);
        var isFavorite = FindFavorite(sourceText, translatedText) is not null;
        _homeStore?.Update(state => state with { CanFavorite = canFavorite, IsFavorite = isFavorite });
        _favoritesStore?.Update(state => state with
        {
            IsInteractive = !_isTranslating && !_isSelectionTranslating,
        });
    }

    private void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceText = HomeState.Source;
        if (_isTranslating || _isSelectionTranslating || _isRealtimeTranslating || string.IsNullOrWhiteSpace(sourceText)) return;
        var translatedText = HomeState.Output;
        if (FindFavorite(sourceText, translatedText) is { } existing)
        {
            _settings.Favorites.Remove(existing);
            RefreshFavorites();
            SaveSettings();
            ShowInfo(Localization.Text("已取消收藏。"), InfoBarSeverity.Informational);
            return;
        }
        _settings.Favorites.Add(new FavoriteTranslation
        {
            SourceText = sourceText,
            TranslatedText = translatedText,
            SourceSpeechTag = (HomeSourceLanguage)?.SpeechTag,
        });
        RefreshFavorites();
        SaveSettings();
        ShowInfo(Localization.Text("已加入收藏。"), InfoBarSeverity.Success);
    }

    private FavoriteTranslation? FindFavorite(string sourceText, string translatedText) =>
        _settings.Favorites.FirstOrDefault(item =>
            string.Equals(item.SourceText, sourceText, StringComparison.Ordinal)
            && string.Equals(item.TranslatedText, translatedText, StringComparison.Ordinal));

    private void FavoritesButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Favorites");

    private void FavoriteItemClicked(FavoriteTranslation favorite)
    {
        if (_isTranslating || _isSelectionTranslating) return;
        SetHomeSource(favorite.SourceText);
        SetHomeOutput(favorite.TranslatedText);
        NavigateTo("Home");
        SuspendRealtimeTranslationForCurrentInput();
        RequestHomeFocus();

    }

    private void RemoveFavorite(FavoriteTranslation favorite)
    {
        _settings.Favorites.Remove(favorite);
        RefreshFavorites();
        SaveSettings();
    }

    private async Task SpeakFavoriteAsync(FavoriteTranslation favorite)
    {
        try
        {
            ShowInfo(Localization.Text("正在合成语音..."), InfoBarSeverity.Informational);
            await _speechOutput.SpeakAsync(favorite.SourceText, favorite.SourceSpeechTag);
            SetSpeakingState(true);
            ShowInfo(Localization.Text("正在朗读源文本。"), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            SetSpeakingState(false);
            ShowInfo(Localization.Text("无法播放语音：") + ex.Message, InfoBarSeverity.Error);
        }
    }
}
