using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private TranslationHistoryPageStore? _translationHistoryStore;

    private void CreateTranslationHistoryStore()
    {
        var store = new TranslationHistoryPageStore
        {
            Clear = ClearTranslationHistory,
            ItemClicked = TranslationHistoryItemClicked,
        };

        var host = new ReactorHostControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        TranslationHistoryPageHost.Content = host;
        host.Mount(new TranslationHistoryPageView(store));
        _translationHistoryStore = store;
    }

    private void ClearTranslationHistory()
    {
        _settings.TranslationHistory.Clear();
        RefreshTranslationHistory();
        SaveSettings();
        ShowInfo(Localization.Text("已清除翻译历史。"), InfoBarSeverity.Success);
    }

    private void TranslationHistoryItemClicked(string sourceText)
    {
        SourceTextBox.Text = sourceText;
        NavigateTo("Home");
        SourceTextBox.Focus(FocusState.Programmatic);
        TranslateButton_Click(TranslateButton, new RoutedEventArgs());
    }
}
