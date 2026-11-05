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

internal static class ProviderSettingsUi
{
    public static Element Catalog(SettingsPageStore store, SettingsPageState state) =>
        SettingsUi.Card(store, state, "providers", "S033ed59452549c51", "S65f5df356d359832", Icon("People"),
            VStack(12,
                Grid([GridSize.Star(), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                    ComboBox(state.Providers.Select(Localization.ProviderName).ToArray(), state.ManagedProviderIndex,
                        index => { if (index >= 0 && index < state.Providers.Count) store.Edit(s => s with { ManagedProviderIndex = index }); })
                        .AutomationName(Localization.Get("S033ed59452549c51")).HAlign(HorizontalAlignment.Stretch),
                    IconButton("Add", "Sfc7a472874bcbfb2", () => store.ProviderAction("add-provider")).Grid(column: 1),
                    IconButton("Delete", "S4256f8b835ecf7c4", () => store.ProviderAction("delete-provider")).Grid(column: 2))
                    .Set(grid => grid.ColumnSpacing = 8),
                SettingsUi.Description("Sd2ddc5721495c249"),
                TextBox(state.ProviderEndpoint, value => store.Edit(s => s with { ProviderEndpoint = value }))
                    .Header(Localization.Get("S03b11112dc970014")).PlaceholderText(state.ProviderEndpointPlaceholder),
                PasswordBox(onPasswordChanged: store.ProviderKeyChanged, placeholderText: Localization.Get("Sca478c91d3839a44"))
                    .Header(Localization.Get("S23189d55f6973f2c")).IsEnabled(state.CanEditProviderKey)
                    .Set(box => { var key = store.ReadProviderKey(); if (box.Password != key) box.Password = key; }),
                Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
                    BodyStrong(Localization.Get("Sc98e118e0a43f078")).VAlign(VerticalAlignment.Center),
                    HStack(8,
                        Button(HStack(6, Icon(FontIcon("\uE72C", fontSize: 14)),
                            TextBlock(Localization.Text(state.IsFetchingModels ? "获取中…" : "获取模型"))), () => store.ProviderAction("fetch-models"))
                            .AutomationName(Localization.Get("S83fcfb4c1f2c1641")).Padding(8, 4),
                        IconButton("Add", "S168e845a86bc3703", () => store.ProviderAction("add-model"))).Grid(column: 1)),
                VStack(state.ManagedModels.Select(model =>
                    Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
                        TextBlock(model).TextTrimming(TextTrimming.CharacterEllipsis).ToolTip(model).VAlign(VerticalAlignment.Center),
                        IconButton("Delete", "S1b5932b8946d2d68", () => store.DeleteModel(model)).Grid(column: 1))
                    .MinHeight(40).Padding(4).Set(grid => grid.ColumnSpacing = 8).WithKey(model)).ToArray()),
                CheckBox(state.RememberKeys, value => store.Edit(s => s with { RememberKeys = value }), Localization.Get("S6bf572941e265a92")),
                Button(Localization.Get("Scb5d682bac3d1a2d"), () => store.ProviderAction("reset"))
                    .AutomationName(Localization.Get("Scb5d682bac3d1a2d")).ToolTip(Localization.Get("Sc5dcfd20f2ed36e0"))
                    .HAlign(HorizontalAlignment.Right))).IsEnabled(!state.IsFetchingModels);

    public static Element Ai(SettingsPageStore store, SettingsPageState state)
    {
        var promptDescription = state.PromptStyle switch
        {
            1 => Localization.Text("适合商务与公文，使用严谨、完整的书面表达。"),
            2 => Localization.Text("适合日常交流，使用地道口语，可灵活调整句式。"),
            3 => Localization.Text("删减客套和重复表达，用更少的文字保留关键信息。"),
            _ => Localization.Text("忠实保留原文语气、句式和信息，不额外润色。"),
        };
        return SettingsUi.Card(store, state, "ai", "S5239fbb2909810d6", "Sf9b2ebb952cdc2f8", Icon("Message"),
            VStack(8,
                ComboBox(state.Providers.Select(Localization.ProviderName).ToArray(), state.AiProviderIndex,
                    index => { if (index >= 0 && index < state.Providers.Count) store.Edit(s => s with { AiProviderIndex = index }); })
                    .Header(Localization.Get("S033ed59452549c51")).HAlign(HorizontalAlignment.Stretch),
                TextBlock(Localization.Get("Sc98e118e0a43f078")),
                ListView((int?)state.AiModelIndex,
                    index => { if (index >= 0 && index < state.AiModels.Count) store.Edit(s => s with { AiModelIndex = index }); },
                    state.AiModels.Select(model => TextBlock(model).WithKey(model)).ToArray())
                    .AutomationName(Localization.Get("Sc98e118e0a43f078"))
                    .Set(list => {
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
                        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
                    }),
                SettingsUi.Description("Sfc845a324d6f21f8"),
                ToggleSwitch(state.Streaming, value => store.Edit(s => s with { Streaming = value }), header: Localization.Get("Sc65e6c6ea8ca51a2")),
                SettingsUi.Description("Sfdacdfb1a22025b3"),
                TextBlock(Localization.Get("S874a8e31c7002510")),
                ComboBox(new[] { Localization.Text("标准"), Localization.Text("正式"), Localization.Text("自然"), Localization.Text("简洁") },
                    state.PromptStyle, index => { if (index >= 0 && index <= 3) store.Edit(s => s with { PromptStyle = index }); })
                    .AutomationName(Localization.Get("S874a8e31c7002510")).MinWidth(180),
                Caption(promptDescription).TextWrapping().Foreground(Theme.SecondaryText),
                CheckBox(state.LanguageDetails, value => store.Edit(s => s with { LanguageDetails = value }), Localization.Get("S635407edd2f3b389")),
                TextBlock(Localization.Get("Sfcc26843914a0a07")),
                TextBox(state.CustomPrompt, value => store.Edit(s => s with { CustomPrompt = value }), placeholderText: Localization.Get("Sa9ad44de015edc6f"))
                    .AcceptsReturn().TextWrapping().AutomationName(Localization.Get("Sfcc26843914a0a07")).MinHeight(96)),
            SettingsUi.ModeToggle(state.AiEnabled, value => store.Edit(s => s with { AiEnabled = value }), "S8d5dd50781ddc74b"));
    }

    public static Element Dialog(SettingsPageStore store, SettingsPageState state)
    {
        var addingProvider = state.ProviderDialog == "add-provider";
        var addingModel = state.ProviderDialog == "add-model";
        var deleting = state.ProviderDialog == "delete-provider";
        var title = addingProvider ? Localization.Text("添加供应商") : addingModel ? Localization.Text("添加自定义模型")
            : deleting ? Localization.Text("删除供应商") : Localization.Text("选择要保存的模型 — {0}", Localization.ProviderName(state.DialogProvider));
        var validName = !string.IsNullOrWhiteSpace(state.DialogName) && !state.DialogName.Trim().Contains(':')
            && !string.Equals(state.DialogName.Trim(), "本地 AI", StringComparison.OrdinalIgnoreCase)
            && !state.Providers.Contains(state.DialogName.Trim(), StringComparer.OrdinalIgnoreCase);
        var validEndpoint = Uri.TryCreate(state.DialogEndpoint.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        Element content;
        if (addingProvider)
            content = VStack(12,
                TextBox(state.DialogName, value => store.Update(s => s with { DialogName = value })).Header(Localization.Text("供应商名称")),
                TextBox(state.DialogEndpoint, value => store.Update(s => s with { DialogEndpoint = value }),
                    placeholderText: "https://example.com/v1").Header(Localization.Text("接口地址")),
                TextBox(state.DialogModel, value => store.Update(s => s with { DialogModel = value })).Header(Localization.Text("模型名称")),
                PasswordBox(onPasswordChanged: store.DraftKeyChanged).Header("API Key")
                    .OnMount(element => ((Microsoft.UI.Xaml.Controls.PasswordBox)element).Password = string.Empty)
                    .WithKey("provider-draft-key"),
                TextBlock(Localization.Text("请填写唯一的供应商名称（不含冒号）、有效的 HTTP/HTTPS 接口地址和模型名称。")).TextWrapping()
                    .IsVisible(!validName || !validEndpoint || string.IsNullOrWhiteSpace(state.DialogModel)));
        else if (addingModel)
            content = TextBox(state.DialogModel, value => store.Update(s => s with { DialogModel = value }),
                placeholderText: Localization.Text("输入自定义模型名称")).Header(Localization.Text("模型名称"));
        else if (deleting)
            content = TextBlock(Localization.Text("删除 {0} 及其模型配置和保存的密钥？", Localization.ProviderName(state.DialogProvider))).TextWrapping();
        else
            content = VStack(12,
                TextBlock(Localization.Text("仅显示尚未保存的模型，可多选。现有模型会保留。")).TextWrapping(),
                ScrollView(VStack(4, state.FetchedModels.Select(model =>
                    CheckBox(state.SelectedFetchedModels.Contains(model), value => store.Update(s =>
                    {
                        var selected = s.SelectedFetchedModels.ToList();
                        if (value) { if (!selected.Contains(model)) selected.Add(model); } else selected.Remove(model);
                        return s with { SelectedFetchedModels = selected.ToArray() };
                    }), model).WithKey(model)).ToArray())).MaxHeight(360));
        return ContentDialog(title, content, deleting ? Localization.Text("删除") :
            addingProvider || addingModel ? Localization.Text("添加") : Localization.Text("保存")) with
        {
            IsOpen = state.ProviderDialog.Length > 0,
            CloseButtonText = Localization.Text("取消"),
            DefaultButton = deleting ? ContentDialogButton.Close : ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = addingProvider ? validName && validEndpoint && !string.IsNullOrWhiteSpace(state.DialogModel)
                : addingModel ? !string.IsNullOrWhiteSpace(state.DialogModel) : deleting || state.SelectedFetchedModels.Count > 0,
            OnClosed = store.DialogClosed,
        };
    }

    private static ButtonElement IconButton(string icon, string key, Action action) => Button(Icon(icon), action)
        .SubtleButton().Width(28).Height(28).MinWidth(0).MinHeight(0).Padding(0)
        .AutomationName(Localization.Get(key)).ToolTip(Localization.Get(key));
}

