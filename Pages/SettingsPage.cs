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

public sealed record OfflineModelRow(string Id, string Title, string Details, bool IsInstalled, bool CanDownload);

public sealed record SettingsPageState
{
    public bool ApiEnabled { get; init; }
    public bool RememberAliyun { get; init; }
    public int CredentialRevision { get; init; }
    public bool AiEnabled { get; init; }
    public bool RememberKeys { get; init; }
    public bool Streaming { get; init; }
    public bool LanguageDetails { get; init; }
    public int PromptStyle { get; init; }
    public string CustomPrompt { get; init; } = string.Empty;
    public IReadOnlyList<string> Providers { get; init; } = Array.Empty<string>();
    public int ManagedProviderIndex { get; init; } = -1;
    public int AiProviderIndex { get; init; } = -1;
    public IReadOnlyList<string> ManagedModels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AiModels { get; init; } = Array.Empty<string>();
    public int AiModelIndex { get; init; } = -1;
    public string ProviderEndpoint { get; init; } = string.Empty;
    public string ProviderEndpointPlaceholder { get; init; } = string.Empty;
    public bool IsFetchingModels { get; init; }
    public bool CanEditProviderKey { get; init; } = true;
    public string ProviderDialog { get; init; } = string.Empty;
    public string DialogProvider { get; init; } = string.Empty;
    public string DialogName { get; init; } = string.Empty;
    public string DialogEndpoint { get; init; } = string.Empty;
    public string DialogModel { get; init; } = string.Empty;
    public IReadOnlyList<string> FetchedModels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SelectedFetchedModels { get; init; } = Array.Empty<string>();
    public int ImageProviderIndex { get; init; } = -1;
    public IReadOnlyList<string> ImageModels { get; init; } = Array.Empty<string>();
    public int ImageModelIndex { get; init; } = -1;
    public int LocalSourceIndex { get; init; }
    public string LocalEndpoint { get; init; } = string.Empty;
    public string LocalModel { get; init; } = string.Empty;
    public bool IsRefreshingLocalModels { get; init; }
    public IReadOnlyList<OfflineModelRow> OfflineModels { get; init; } = Array.Empty<OfflineModelRow>();
    public string OfflineHeader { get; init; } = Localization.Get("Sb385619812be9a22");
    public string OfflineStatus { get; init; } = string.Empty;
    public bool CanRefreshOfflineModels { get; init; }
    public int UiLanguageIndex { get; init; }
    public int ThemeIndex { get; init; }
    public bool Mica { get; init; }
    public bool AlwaysOnTop { get; init; }
    public bool EnterToTranslate { get; init; }
    public bool Realtime { get; init; }
    public int HistoryLimitIndex { get; init; } = 2;
    public double PanelWidth { get; init; } = 760;
    public IReadOnlySet<string> Expanded { get; init; } = new HashSet<string>();
}

public sealed class SettingsPageStore
{
    private event Action? Changed;
    public SettingsPageState State { get; private set; } = new();
    public Action<SettingsPageState, SettingsPageState> Edited { get; init; } = (_, _) => { };
    public Func<bool, string> ReadAliyunKey { get; init; } = _ => string.Empty;
    public Action<string, bool> AliyunKeyChanged { get; init; } = (_, _) => { };
    public Func<string> ReadProviderKey { get; init; } = () => string.Empty;
    public Action<string> ProviderKeyChanged { get; init; } = _ => { };
    public Action<string> DraftKeyChanged { get; init; } = _ => { };
    public Action<string> ProviderAction { get; init; } = _ => { };
    public Action<string> DeleteModel { get; init; } = _ => { };
    public Action<ContentDialogResult> DialogClosed { get; init; } = _ => { };
    public Action RefreshLocalModels { get; init; } = () => { };
    public Action RefreshOfflineModels { get; init; } = () => { };
    public Action<string> UseOrDownloadOfflineModel { get; init; } = _ => { };
    public Action<string> DeleteOfflineModel { get; init; } = _ => { };
    public Action Subscribe(Action callback) { Changed += callback; return () => Changed -= callback; }
    public void Update(Func<SettingsPageState, SettingsPageState> update)
    {
        var next = update(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
    public void Edit(Func<SettingsPageState, SettingsPageState> update)
    {
        var before = State;
        Update(update);
        if (before != State) Edited(before, State);
    }
    public void Expand(string key, bool expanded) => Update(state =>
    {
        var next = new HashSet<string>(state.Expanded);
        if (expanded) next.Add(key); else next.Remove(key);
        return state with { Expanded = next };
    });
}

public sealed class SettingsPageView(SettingsPageStore store) : Component
{
    public override Element Render()
    {
        var state = UseExternalStore(store.Subscribe, () => store.State);
        return ScrollView(
            VStack(12,
                SettingsUi.Section("S83095a6428e8d956"),
                MachineTranslation(state),
                ProviderSettingsUi.Catalog(store, state),
                ProviderSettingsUi.Ai(store, state),
                ImageSettings(state), LocalSettings(state), OfflineSettings(state),
                SettingsUi.Section("Sa1a42cd9b16e2162").Margin(4, 16, 4, 0),
                General(state), Input(state), History(state), Realtime(state),
                ProviderSettingsUi.Dialog(store, state))
                .Width(state.PanelWidth).MaxWidth(760).HAlign(HorizontalAlignment.Center)
                .Padding(36, 36, 36, 16))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Set(scroll => {
                scroll.HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
                scroll.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Auto;
            })
            .OnSizeChanged((_, args) => store.Update(s => s with { PanelWidth = Math.Min(760, Math.Max(0, args.NewSize.Width)) }));
    }


    private Element MachineTranslation(SettingsPageState state) =>
        SettingsUi.Card(store, state, "machine", "Sb1862e99d55f529f", "Sfc13d6134b676aa0", Icon("Globe"),
            VStack(8,
                TextBlock("AccessKey ID"),
                PasswordBox(onPasswordChanged: value => store.AliyunKeyChanged(value, false),
                    placeholderText: Localization.Get("Saddb4567a33a4bc9"))
                    .AutomationName("AccessKey ID")
                    .Set(box => { var key = store.ReadAliyunKey(false); if (box.Password != key) box.Password = key; }),
                TextBlock("AccessKey Secret"),
                PasswordBox(onPasswordChanged: value => store.AliyunKeyChanged(value, true),
                    placeholderText: Localization.Get("S289fe8982ee998ef"))
                    .AutomationName("AccessKey Secret")
                    .Set(box => { var key = store.ReadAliyunKey(true); if (box.Password != key) box.Password = key; }),
                CheckBox(state.RememberAliyun, value => store.Edit(s => s with { RememberAliyun = value }), Localization.Get("Sb955203e9090eef9"))),
            SettingsUi.ModeToggle(state.ApiEnabled, value => store.Edit(s => s with { ApiEnabled = value }), "Sb55d73902020d21c"));

    private Element ImageSettings(SettingsPageState state) =>
        SettingsUi.Card(store, state, "image-settings", "S0c84c73f4b24b3b8", "S38c87877f29cf060", Icon("Pictures"),
            VStack(8,
                ComboBox(state.Providers.Select(Localization.ProviderName).ToArray(), state.ImageProviderIndex,
                    index => { if (index >= 0 && index < state.Providers.Count) store.Edit(s => s with { ImageProviderIndex = index }); })
                    .Header(Localization.Get("S033ed59452549c51")).HAlign(HorizontalAlignment.Stretch),
                TextBlock(Localization.Get("Sc98e118e0a43f078")),
                ListView((int?)state.ImageModelIndex,
                    index => { if (index >= 0 && index < state.ImageModels.Count) store.Edit(s => s with { ImageModelIndex = index }); },
                    state.ImageModels.Select(model => TextBlock(model).WithKey(model)).ToArray())
                    .AutomationName(Localization.Get("Sc98e118e0a43f078"))
                    .Set(list => {
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
                    }),
                SettingsUi.Description("Se0d99ccba87fe53e")));

    private Element LocalSettings(SettingsPageState state) =>
        SettingsUi.Card(store, state, "local", "S8f4e49aeafbade43", "S7244c05cb62fc39c", Icon("Remote"),
            VStack(8,
                ComboBox(new[] { Localization.Text("本地接口"), Localization.Text("Mozilla Translations 模型") }, state.LocalSourceIndex,
                    index => { if (index >= 0 && index <= 1) store.Edit(s => s with { LocalSourceIndex = index }); })
                    .AutomationName(Localization.Get("S8f4e49aeafbade43")),
                VStack(8,
                    TextBlock(Localization.Get("S03b11112dc970014")),
                    TextBox(state.LocalEndpoint, value => store.Edit(s => s with { LocalEndpoint = value }), placeholderText: "http://localhost:11434/v1")
                        .AutomationName(Localization.Get("S03b11112dc970014")),
                    TextBlock(Localization.Get("Sc98e118e0a43f078")),
                    TextBox(state.LocalModel, value => store.Edit(s => s with { LocalModel = value }), placeholderText: Localization.Get("S177c17dc8aa34a2b"))
                        .AutomationName(Localization.Get("Sc98e118e0a43f078")),
                    Button(Localization.Get("S10bf64e5aab41b1b"), store.RefreshLocalModels)
                        .AutomationName(Localization.Get("S10bf64e5aab41b1b")).IsEnabled(!state.IsRefreshingLocalModels))
                    .IsVisible(state.LocalSourceIndex == 0)));

    private Element OfflineSettings(SettingsPageState state)
    {
        var header = Grid([GridSize.Px(24), GridSize.Star()], [GridSize.Auto],
            Icon("Download").Size(20, 20).VAlign(VerticalAlignment.Center),
            VStack(2, BodyStrong(state.OfflineHeader).TextWrapping(), SettingsUi.Description("Sce3334d85570c680"))
                .VAlign(VerticalAlignment.Center).Grid(column: 1))
            .MinHeight(64).Padding(4, 6, 8, 6).Set(grid => grid.ColumnSpacing = 12);
        var rows = state.OfflineModels.Select(model =>
            Grid([GridSize.Star(), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                VStack(4, BodyStrong(model.Title).TextWrapping(),
                    Caption(model.Details).TextWrapping().Foreground(Theme.SecondaryText)),
                Button(Localization.Text(model.IsInstalled ? "使用" : "下载"), () => store.UseOrDownloadOfflineModel(model.Id))
                    .AutomationName(Localization.Text(model.IsInstalled ? "使用" : "下载")).IsEnabled(model.CanDownload).MinWidth(64).Grid(column: 1),
                Button(Localization.Get("S2f9daa828907b93f"), () => store.DeleteOfflineModel(model.Id))
                    .AutomationName(Localization.Get("S2f9daa828907b93f")).IsEnabled(model.IsInstalled).MinWidth(64).Grid(column: 2))
                .Padding(4, 6, 4, 6).Set(grid => grid.ColumnSpacing = 12).WithKey(model.Id)).ToArray();
        return Border(
            (Expander(state.OfflineHeader,
                VStack(8,
                    Grid([GridSize.Auto, GridSize.Star()], [GridSize.Auto],
                        Button(Localization.Get("S6338f8924dee7084"), store.RefreshOfflineModels)
                            .AutomationName(Localization.Get("S6338f8924dee7084")).IsEnabled(state.CanRefreshOfflineModels),
                        TextBlock(state.OfflineStatus).TextWrapping().Foreground(Theme.SecondaryText)
                            .VAlign(VerticalAlignment.Center).Grid(column: 1)).Set(grid => grid.ColumnSpacing = 8),
                    VStack(4, rows)).Margin(16, 0, 16, 16),
                state.Expanded.Contains("offline"), value => store.Expand("offline", value)) with { HeaderTemplate = header })
                .HorizontalContentAlignment(HorizontalAlignment.Stretch).HAlign(HorizontalAlignment.Stretch))
            .Background(Theme.Ref("WindCardBackgroundBrush")).WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8).HAlign(HorizontalAlignment.Stretch).WithKey("offline");
    }

    private Element General(SettingsPageState state) =>
        SettingsUi.Card(store, state, "appearance", "S86a63f23a076b11e", "Sf57e6726727fd3b5", Icon("Highlight"),
            VStack(8,
                ComboBox(new[] { Localization.Text("跟随系统"), "简体中文", "English" }, state.UiLanguageIndex,
                    index => { if (index >= 0 && index <= 2) store.Edit(s => s with { UiLanguageIndex = index }); })
                    .Header(Localization.Get("S3d13868593ae4eeb")).Width(180).HAlign(HorizontalAlignment.Left),
                SettingsUi.Description("S68ab31f0f5faf650"),
                ComboBox(new[] { Localization.Text("跟随系统"), Localization.Text("浅色"), Localization.Text("深色") }, state.ThemeIndex,
                    index => { if (index >= 0 && index <= 2) store.Edit(s => s with { ThemeIndex = index }); })
                    .Header(Localization.Get("S0e61f173997cf413")).Width(180).HAlign(HorizontalAlignment.Left),
                CheckBox(state.Mica, value => store.Edit(s => s with { Mica = value }), Localization.Get("Sa750b72486c0d855")),
                Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
                    VStack(2, BodyStrong(Localization.Get("Sa63886dd9f495cd7")).TextWrapping(), SettingsUi.Description("Sc6755fea10868a8d"))
                        .VAlign(VerticalAlignment.Center),
                    ToggleSwitch(state.AlwaysOnTop, value => store.Edit(s => s with { AlwaysOnTop = value }), onContent: "", offContent: "")
                        .AutomationName(Localization.Get("Sa63886dd9f495cd7")).MinWidth(0).VAlign(VerticalAlignment.Center).Grid(column: 1))
                    .Set(grid => grid.ColumnSpacing = 16)));

    private Element Input(SettingsPageState state) =>
        SettingsUi.Card(store, state, "input", "See2638183d3ea860", "S3e0d676d2d85073d", Icon(FontIcon("\uE765", fontSize: 20)),
            VStack(12,
                ToggleSwitch(state.EnterToTranslate, value => store.Edit(s => s with { EnterToTranslate = value }),
                    onContent: "", offContent: "", header: Localization.Get("Sde8c617ef05a23d7")).MinWidth(0),
                SettingsUi.Shortcut(Localization.Text(state.EnterToTranslate ? "翻译文本" : "插入换行"), "Enter"),
                SettingsUi.Shortcut(Localization.Get("S656f978003f64dfe"), "Shift+Enter").IsVisible(state.EnterToTranslate),
                SettingsUi.Shortcut(Localization.Get("S3bdb827c9c041bd1"), "Ctrl+T")));

    private Element History(SettingsPageState state) =>
        SettingsUi.Card(store, state, "history", "S25a4303fc7712d0a", "Sfd3e65ef362dee63", Icon("Clock"),
            ComboBox(new[] { Localization.Text("不保存翻译历史"), Localization.Text("5 条"), Localization.Text("20 条"), Localization.Text("无上限") },
                state.HistoryLimitIndex, index => { if (index >= 0 && index <= 3) store.Edit(s => s with { HistoryLimitIndex = index }); })
                .AutomationName(Localization.Get("S25a4303fc7712d0a")).MinWidth(180));

    private Element Realtime(SettingsPageState state) =>
        SettingsUi.Card(store, state, "realtime", "S8251b844088b0ecd", null, Icon(FontIcon("\uE713", fontSize: 20)),
            VStack(8,
                ToggleSwitch(state.Realtime, value => store.Edit(s => s with { Realtime = value }),
                    onContent: "", offContent: "", header: Localization.Get("S5a31c60d655639af")).MinWidth(0),
                SettingsUi.Description("Sf1527fb8af9b9760")));
}

public static class SettingsUi
{
    public static Element ModeToggle(bool enabled, Action<bool> changed, string label) => HStack(6,
        Caption(Localization.Text(enabled ? "开" : "关")).Foreground(Theme.SecondaryText).VAlign(VerticalAlignment.Center),
        ToggleSwitch(enabled, changed, onContent: "", offContent: "").MinWidth(0)
            .AutomationName(Localization.Get(label)).VAlign(VerticalAlignment.Center));
    public static Element Section(string key) => BodyStrong(Localization.Get(key)).TextWrapping().Margin(4, 4, 4, 0);
    public static Element Description(string key) => Caption(Localization.Get(key)).TextWrapping().Foreground(Theme.SecondaryText);
    public static Element Shortcut(string action, string shortcut) => Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
        TextBlock(action).VAlign(VerticalAlignment.Center),
        TextBlock(shortcut).FontFamily("Consolas").VAlign(VerticalAlignment.Center).Grid(column: 1))
        .Set(grid => grid.ColumnSpacing = 16);
    public static Element Card(SettingsPageStore store, SettingsPageState state, string key, string title, string? description,
        Element icon, Element content, Element? action = null)
    {
        var header = Grid([GridSize.Px(24), GridSize.Star(), GridSize.Auto], [GridSize.Auto],
            icon.Width(20).Height(20).VAlign(VerticalAlignment.Center),
            VStack(2, BodyStrong(Localization.Get(title)).TextWrapping(),
                description is null ? null : Description(description)).VAlign(VerticalAlignment.Center).Grid(column: 1),
            action?.VAlign(VerticalAlignment.Center).Grid(column: 2))
            .MinHeight(64).Padding(4, 6, 8, 6).Set(grid => grid.ColumnSpacing = 12);
        return Border(
            (Expander(Localization.Get(title), content.Margin(16, 0, 16, 16), state.Expanded.Contains(key),
                value => store.Expand(key, value)) with { HeaderTemplate = header })
                .HorizontalContentAlignment(HorizontalAlignment.Stretch).HAlign(HorizontalAlignment.Stretch))
            .Background(Theme.Ref("WindCardBackgroundBrush")).WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8).HAlign(HorizontalAlignment.Stretch).WithKey(key);
    }
}

