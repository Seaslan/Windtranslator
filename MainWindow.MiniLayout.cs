using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private MiniTranslatorStore? _miniStore;

    private MiniTranslatorStore MiniStore => _miniStore ??= CreateMiniStore();

    private bool IsMiniTranslating => _miniStore?.State.IsTranslating == true;

    private MiniTranslatorStore CreateMiniStore()
    {
        var store = new MiniTranslatorStore
        {
            Translate = () => _ = TranslateMiniAsync(),
            ExitMiniMode = () => SetMiniMode(false, resizeWindow: true),
            CopyResult = text => { if (!string.IsNullOrWhiteSpace(text)) SetClipboardText(text); },
        };
        // ReactorHostControl has no parameterless constructor, so XAML only reserves its slot.
        var host = new ReactorHostControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        MiniPageHost.Content = host;
        host.Mount(new MiniTranslatorView(store));
        return store;
    }

    private static bool ShouldUseStackedMiniLayout(double width, double height, bool isStacked)
    {
        // Two text areas need enough height. Different enter/exit thresholds avoid
        // flicker when resizing close to the height, width, or aspect-ratio boundary.
        return height >= (isStacked ? 480 : 520)
            && (width <= (isStacked ? 624 : 600) || height >= width + (isStacked ? -24 : 0));
    }

    private void UpdateMiniLayout(double width, double height) =>
        MiniStore.Update(s => s with { IsStacked = ShouldUseStackedMiniLayout(width, height, s.IsStacked) });

    private void ShowMiniTranslator(double width, double height)
    {
        var store = MiniStore;
        store.TargetLanguageNames = Languages.Skip(1).Select(language => language.Display).ToArray();
        store.Update(s => s with
        {
            IsStacked = ShouldUseStackedMiniLayout(width, height, s.IsStacked),
            TargetIndex = Math.Clamp(_settings.TargetLanguageIndex - 1, 0, Languages.Count - 2),
        });
        store.RequestFocus();
    }

    private async Task TranslateMiniAsync()
    {
        var store = MiniStore;
        var state = store.State;
        var text = state.Input.Trim();
        if (!state.CanTranslate || text.Length == 0) return;
        var targetLanguage = Languages[Math.Clamp(state.TargetIndex + 1, 1, Languages.Count - 1)];

        try
        {
            store.Update(s => s with { IsTranslating = true, HasResult = false, Result = string.Empty });
            UpdateUiState();
            var result = await TranslateTextAsync(
                text,
                Math.Clamp(HomeState.ModeIndex, 0, 2),
                Languages[0],
                targetLanguage,
                CancellationToken.None,
                onTextUpdated: translatedText => store.Update(s => s with { Result = translatedText }));
            store.Update(s => s with { Result = result, HasResult = true });
            AddTranslationHistory(text);
        }
        catch (Exception ex)
        {
            store.Update(s => s with { Result = string.Empty, HasResult = false });
            ShowInfo(Localization.Text("翻译失败：") + ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            store.Update(s => s with { IsTranslating = false });
            if (!_isWindowClosed) UpdateUiState();
        }
    }
}
