using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windtranslator.Models;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private void RefreshFavorites()
    {
        _settings.NormalizeFavorites();
        FavoritesListView.ItemsSource = _settings.Favorites.AsEnumerable().Reverse().ToList();
        FavoritesEmptyText.Visibility = _settings.Favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFavoritesState();
    }

    private void UpdateFavoritesState()
    {
        if (_settings is null) return;
        var sourceText = SourceTextBox.Text;
        var translatedText = OutputTextBox.Text;
        AddFavoriteButton.IsEnabled = !_isTranslating && !_isSelectionTranslating
            && !string.IsNullOrWhiteSpace(sourceText);
        FavoriteIcon.Glyph = _settings.Favorites.Any(item =>
            string.Equals(item.SourceText, sourceText, StringComparison.Ordinal)
            && string.Equals(item.TranslatedText, translatedText, StringComparison.Ordinal))
            ? "\uE735" : "\uE734";
        FavoritesListView.IsEnabled = !_isTranslating && !_isSelectionTranslating;
    }

    private void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceText = SourceTextBox.Text;
        if (_isTranslating || _isSelectionTranslating || string.IsNullOrWhiteSpace(sourceText)) return;
        var translatedText = OutputTextBox.Text;
        if (_settings.Favorites.Any(item =>
            string.Equals(item.SourceText, sourceText, StringComparison.Ordinal)
            && string.Equals(item.TranslatedText, translatedText, StringComparison.Ordinal)))
        {
            ShowInfo(Localization.Text("此翻译已收藏。"), InfoBarSeverity.Informational);
            return;
        }
        _settings.Favorites.Add(new FavoriteTranslation
        {
            SourceText = sourceText,
            TranslatedText = translatedText,
            SourceSpeechTag = (SourceLanguageComboBox.SelectedItem as LanguageOption)?.SpeechTag,
        });
        RefreshFavorites();
        SaveSettings();
        ShowInfo(Localization.Text("已加入收藏。"), InfoBarSeverity.Success);
    }

    private void FavoritesButton_Click(object sender, RoutedEventArgs e) => NavigateTo("Favorites");

    private void FavoritesListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_isTranslating || _isSelectionTranslating || e.ClickedItem is not FavoriteTranslation favorite) return;
        CloseSelectionTranslationTeachingTip();
        SourceTextBox.Text = favorite.SourceText;
        OutputTextBox.Text = favorite.TranslatedText;
        NavigateTo("Home");
        SourceTextBox.Focus(FocusState.Programmatic);
        SourceTextBox.SelectionStart = favorite.SourceText.Length;
        SourceTextBox.SelectionLength = 0;
    }

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FavoriteTranslation favorite }) return;
        _settings.Favorites.Remove(favorite);
        RefreshFavorites();
        SaveSettings();
    }

    private void FavoriteMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FavoriteTranslation favorite } button) return;

        // Capture this card explicitly; flyouts live outside the list's visual tree.
        var menu = new MenuFlyout();
        var speak = new MenuFlyoutItem { Text = Localization.Text("朗读源文本"), Icon = new SymbolIcon(Symbol.Audio) };
        speak.Click += async (_, _) => await SpeakFavoriteAsync(favorite);
        menu.Items.Add(speak);
        AddCopyItem(Localization.Text("复制源文本"), favorite.SourceText);
        AddCopyItem(Localization.Text("复制译文"), favorite.TranslatedText);
        AddCopyItem(Localization.Text("全部复制"), string.IsNullOrEmpty(favorite.TranslatedText)
            ? favorite.SourceText
            : favorite.SourceText + Environment.NewLine + Environment.NewLine + favorite.TranslatedText);
        menu.ShowAt(button);

        void AddCopyItem(string label, string text)
        {
            var item = new MenuFlyoutItem
            {
                Text = label,
                Icon = new SymbolIcon(Symbol.Copy),
                IsEnabled = !string.IsNullOrEmpty(text),
            };
            item.Click += (_, _) =>
            {
                SetClipboardText(text);
                ShowInfo(Localization.Text("已复制到剪贴板。"), InfoBarSeverity.Success);
            };
            menu.Items.Add(item);
        }
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
