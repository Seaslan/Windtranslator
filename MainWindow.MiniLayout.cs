using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private bool _isStackedMiniLayout;
    private bool _isMiniTranslating;
    private bool _miniHasResult;
    private bool _updatingMiniText;
    private string _miniInputText = string.Empty;
    private string _miniResultText = string.Empty;

    private static bool ShouldUseStackedMiniLayout(double width, double height, bool isStacked)
    {
        // Two text areas need enough height. Different enter/exit thresholds avoid
        // flicker when resizing close to the height, width, or aspect-ratio boundary.
        return height >= (isStacked ? 480 : 520)
            && (width <= (isStacked ? 624 : 600) || height >= width + (isStacked ? -24 : 0));
    }

    private void UpdateMiniLayout(double width, double height)
    {
        var stacked = ShouldUseStackedMiniLayout(width, height, _isStackedMiniLayout);
        if (stacked == _isStackedMiniLayout) return;

        _isStackedMiniLayout = stacked;
        MiniResultRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MiniSourceLabel.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        MiniResultPanel.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        UpdateMiniTextPresentation();
    }

    private void SetMiniInputText(string text)
    {
        _miniInputText = text;
        _miniResultText = string.Empty;
        _miniHasResult = false;
        UpdateMiniTextPresentation();
    }

    private void MiniSourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // TextChanged can be delivered after a programmatic layout refresh. Restoring
        // the same source must not clear a result that was retained during resizing.
        if (_updatingMiniText || _isMiniTranslating || MiniSourceTextBox.IsReadOnly
            || MiniSourceTextBox.Text == _miniInputText) return;
        _miniInputText = MiniSourceTextBox.Text;
        _miniResultText = string.Empty;
        _miniHasResult = false;
        // The lower controls may not exist yet during XAML initialization.
        if (MiniOutputTextBox is not null) MiniOutputTextBox.Text = string.Empty;
        if (MiniCopyResultButton is not null) MiniCopyResultButton.IsEnabled = false;
    }

    private void UpdateMiniTextPresentation()
    {
        _updatingMiniText = true;
        try
        {
            var showResultInSource = !_isStackedMiniLayout
                && (_miniHasResult || (_isMiniTranslating && _miniResultText.Length > 0));
            var visibleText = showResultInSource ? _miniResultText : _miniInputText;
            if (MiniSourceTextBox.Text != visibleText) MiniSourceTextBox.Text = visibleText;
            MiniSourceTextBox.IsReadOnly = _isMiniTranslating || (!_isStackedMiniLayout && _miniHasResult);
            if (MiniOutputTextBox.Text != _miniResultText) MiniOutputTextBox.Text = _miniResultText;
            MiniCopyResultButton.IsEnabled = !string.IsNullOrWhiteSpace(_miniResultText);
            MiniProgressRing.IsActive = _isMiniTranslating;
            MiniClearButton.IsEnabled = !_isMiniTranslating;
            MiniTargetLanguageComboBox.IsEnabled = !_isMiniTranslating;
            MiniTranslateButton.IsEnabled = !_isMiniTranslating && (_isStackedMiniLayout || !_miniHasResult);
        }
        finally
        {
            _updatingMiniText = false;
        }
    }

    private void MiniCopyResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_miniResultText)) SetClipboardText(_miniResultText);
    }
}
