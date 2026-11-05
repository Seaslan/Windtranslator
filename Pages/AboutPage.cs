using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windtranslator.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Windtranslator;

public sealed record AboutPageState(string VersionText);

public sealed class AboutPageStore
{
    private event Action? Changed;

    public AboutPageState State { get; private set; }

    public AboutPageStore(AboutPageState state) => State = state;

    public Action Subscribe(Action onChanged)
    {
        Changed += onChanged;
        return () => Changed -= onChanged;
    }

    public void Update(Func<AboutPageState, AboutPageState> change)
    {
        var next = change(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke();
    }
}

public sealed class AboutPageView : Component
{
    private readonly AboutPageStore _store;

    public AboutPageView(AboutPageStore store) => _store = store;

    public override Element Render()
    {
        var state = UseExternalStore(_store.Subscribe, () => _store.State);

        var appCard = Border(
                VStack(
                    16,
                    HStack(
                        16,
                        Image("ms-appx:///Assets/Square44x44Logo.altform-unplated_targetsize-256.png")
                            .AccessibilityHidden()
                            .Width(96)
                            .Height(96)
                            .VAlign(VerticalAlignment.Center)
                            .Set(image => image.Stretch = Stretch.Uniform),
                        VStack(
                                4,
                                Subtitle(Localization.Get("Sd608ccd939a78fb2")),
                                TextBlock(state.VersionText)
                                    .FontSize(12)
                                    .Foreground(Theme.SecondaryText))
                            .VAlign(VerticalAlignment.Center)),
                    TextBlock(Localization.Get("Sa8e304702ed3a0bd"))
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping())
                .Padding(24))
            .Background(Theme.Ref("WindCardBackgroundBrush"))
            .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8);

        var linksCard = Border(
                VStack(
                    12,
                    VStack(
                        4,
                        BodyStrong(Localization.Get("S38084d301e3f1a31")),
                        Link("Seaslan", "https://github.com/seaslan")),
                    VStack(
                        4,
                        BodyStrong(Localization.Get("S11bd1ff53ceab6fc")),
                        Link(Localization.Get("S0cadb49096a382e0"), "https://github.com/Seaslan/Windtranslator"),
                        Link(Localization.Get("Se464b41fd5c45ba5"), "https://seaslan.github.io/Windtranslator/")))
                .Padding(16))
            .VAlign(VerticalAlignment.Top)
            .Background(Theme.Ref("WindCardBackgroundBrush"))
            .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8);

        var dependenciesCard = Border(
                Expander(
                        Localization.Get("S2c04cdb9edaabc83"),
                        VStack(
                            4,
                            Link(".NET 8", "https://aka.ms/net", compact: true),
                            Link("Windows App SDK", "https://aka.ms/windowsappsdk", compact: true),
                            Link("WinUI 3", "https://aka.ms/winui3", compact: true),
                            Link("Mozilla Translations", "https://github.com/mozilla/translations", compact: true),
                            Link("Bergamot Translator", "https://github.com/browsermt/bergamot-translator", compact: true))
                            .Padding(12, 0, 12, 12),
                        isExpanded: false)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch))
            .VAlign(VerticalAlignment.Top)
            .Background(Theme.Ref("WindCardBackgroundBrush"))
            .WithBorder(Theme.Ref("WindCardStrokeBrush"), 1)
            .CornerRadius(8)
            .Grid(column: 1);

        return VStack(
                8,
                appCard,
                Grid(
                    [GridSize.Star(), GridSize.Star()],
                    [GridSize.Auto],
                    [linksCard, dependenciesCard])
                    .Set(grid => grid.ColumnSpacing = 8),
                TextBlock(Localization.Get("S3b2edcaffaaf8c03"))
                    .FontSize(12)
                    .HAlign(HorizontalAlignment.Center)
                    .Foreground(Theme.SecondaryText))
            .Width(420)
            .MaxWidth(420)
            .Margin(36, 24, 36, 16)
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center);
    }

    private static HyperlinkButtonElement Link(string content, string uri, bool compact = false)
    {
        var link = HyperlinkButton(content, new Uri(uri))
            .TextLink()
            .Padding(0)
            .HAlign(HorizontalAlignment.Left);

        return compact ? link.FontSize(12) : link;
    }
}
