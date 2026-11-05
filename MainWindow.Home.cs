using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using Windtranslator.Models;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private HomePageStore? _homeStore;
    private IReadOnlyList<LanguageOption> _homeTargetLanguages = Array.Empty<LanguageOption>();
    private HomePageState HomeState => _homeStore!.State;
    private LanguageOption? HomeSourceLanguage => Languages.ElementAtOrDefault(HomeState.SourceIndex);
    private LanguageOption? HomeTargetLanguage => _homeTargetLanguages.ElementAtOrDefault(HomeState.TargetIndex);

    private void CreateHomeStore()
    {
        _homeStore = new HomePageStore
        {
            Translate = () => _ = TranslateHomeAsync(),
            Clear = () => ClearSourceButton_Click(this, new RoutedEventArgs()),
            Copy = () => CopyButton_Click(this, new RoutedEventArgs()),
            Mic = () => MicButton_Click(this, new RoutedEventArgs()),
            Speak = () => SpeakButton_Click(this, new RoutedEventArgs()),
            Favorite = () => AddFavoriteButton_Click(this, new RoutedEventArgs()),
            ShowFavorites = () => NavigateTo("Favorites"),
            ShowHistory = () => NavigateTo("History"),
            Swap = () => SwapLanguageButton_Click(this, new RoutedEventArgs()),
            MiniMode = () => MiniModeButton_Click(this, new RoutedEventArgs()),
            EditSource = text => { if (text != HomeState.Source) SetHomeSource(text); },
            SourceChanged = index =>
            {
                if (_settings is null || _suppressEvents || index < 0 || index >= Languages.Count || index == HomeState.SourceIndex) return;
                _homeStore!.Update(state => state with { SourceIndex = index });
                RefreshHomeTargetLanguages();
                SaveSettings();
            },
            TargetChanged = index =>
            {
                if (_settings is null || _suppressEvents || index < 0 || index >= _homeTargetLanguages.Count || index == HomeState.TargetIndex) return;
                _homeStore!.Update(state => state with { TargetIndex = index });
                SaveSettings();
            },
            ModeChanged = index =>
            {
                if (_settings is null || _suppressEvents || index < 0 || index >= HomeState.Modes.Count
                    || index == HomeState.ModeIndex || !HomeState.Modes[index].IsEnabled) return;
                SetHomeMode(index);
                SaveSettings();
            },
            MountTextBox = (box, output) => InstallSelectionTranslationCommands(box,
                output ? SelectionTranslationOrigin.Output : SelectionTranslationOrigin.Source),
            UnmountTextBox = box => _selectionTranslationTargets.Remove(box),
            SourceKeyDown = SourceTextBox_KeyDown,
            PasteClipboard = PastePendingClipboard,
            CloseClipboard = CloseClipboardTip,
            MiniClipboardTarget = () => MiniStore.ClipboardSourceRef,
        };
        var pageHost = CreateReactorHost();
        HomePageHost.Content = pageHost;
        pageHost.Mount(new HomePageView(_homeStore));
        var toolbarHost = CreateReactorHost();
        HomeToolbarHost.Content = toolbarHost;
        toolbarHost.Mount(new HomeToolbarView(_homeStore));
        var tipHost = CreateReactorHost();
        ClipboardTipHost.Content = tipHost;
        tipHost.Mount(new HomeClipboardTipView(_homeStore));
    }

    private static ReactorHostControl CreateReactorHost() => new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    private void SetHomeSource(string text)
    {
        if (text == HomeState.Source) return;
        _homeStore!.Update(state => state with { Source = text });
        if (_settings is not null) UpdateUiState();
    }

    private void SetHomeOutput(string text)
    {
        if (text == HomeState.Output) return;
        _homeStore!.Update(state => state with { Output = text });
        if (_settings is not null) UpdateUiState();
    }

    private void RequestHomeFocus(bool output = false, int start = -1, int length = 0) =>
        _homeStore!.Update(state => state with
        {
            FocusRequest = state.FocusRequest + 1, FocusOutput = output,
            SelectionStart = start, SelectionLength = length,
        });

    private void SetHomeMode(int index) => _homeStore!.Update(state => state with { ModeIndex = index });

    private void RefreshHomeTargetLanguages(LanguageOption? preferred = null)
    {
        preferred ??= HomeTargetLanguage;
        var source = HomeSourceLanguage ?? Languages[0];
        var targets = Languages.Where(language => language.PromptName != "自动检测"
            && (source.ApiCode == "auto" || language.PromptName != source.PromptName)).ToArray();
        var target = targets.FirstOrDefault(language => language.PromptName == preferred?.PromptName)
            ?? targets.FirstOrDefault(language => language.PromptName == "英语") ?? targets.FirstOrDefault();
        _homeTargetLanguages = targets;
        _homeStore!.Update(state => state with
        {
            Targets = targets.Select(language => language.Display).ToArray(),
            TargetIndex = Array.IndexOf(targets, target),
        });
    }

    private void UpdateTranslationModeVisibility()
    {
        var names = new[] { Localization.Get("Sb1862e99d55f529f"), Localization.Get("S5239fbb2909810d6"), Localization.Get("S8f4e49aeafbade43") };
        var modes = names.Select((name, index) => new FileTranslationModeOption(name, IsTranslationModeEnabled(index), IsTranslationModeEnabled(index))).ToArray();
        _homeStore!.Update(state => state with { Modes = state.Modes.SequenceEqual(modes) ? state.Modes : modes });
        UpdateFileModeAvailability();
    }
}

