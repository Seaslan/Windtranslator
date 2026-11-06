using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using Windows.UI;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private Grid WindowRoot = null!;
    private Border AcrylicFallbackLayer = null!;
    private NavigationView SidebarNavigationView = null!;
    private NavigationViewItem HomeNavigationItem = null!;
    private NavigationViewItem ImageNavigationItem = null!;
    private NavigationViewItem FileNavigationItem = null!;
    private NavigationViewItem SettingsNavigationItem = null!;
    private NavigationViewItem AboutNavigationItem = null!;
    private Border TitleBarDragRegion = null!;
    private Border MiniTitleBarDragRegion = null!;
    private TextBlock PageTitleTextBlock = null!;
    private ContentControl HomeToolbarHost = null!;
    private ContentControl HomePageHost = null!;
    private ContentControl TranslationHistoryPageHost = null!;
    private ContentControl FavoritesPageHost = null!;
    private ContentControl SettingsPageHost = null!;
    private ContentControl ImagePageHost = null!;
    private ContentControl FilePageHost = null!;
    private ContentControl AboutPageHost = null!;
    private Grid MiniWindowGrid = null!;
    private ContentControl MiniPageHost = null!;
    private InfoBar StatusInfoBar = null!;
    private ContentControl ClipboardTipHost = null!;

    private void BuildNativeShell()
    {
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        WindowRoot = new Grid { Background = transparent };
        WindowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WindowRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        WindowRoot.PointerPressed += WindowRoot_PointerPressed;

        AcrylicFallbackLayer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRowSpan(AcrylicFallbackLayer, 2);

        HomeNavigationItem = new NavigationViewItem { Icon = new SymbolIcon(Symbol.Home), Content = Localization.Get("S3e0d676d2d85073d"), Tag = "Home" };
        ImageNavigationItem = new NavigationViewItem { Icon = new SymbolIcon(Symbol.Pictures), Content = Localization.Get("S0c84c73f4b24b3b8"), Tag = "Image" };
        FileNavigationItem = new NavigationViewItem { Icon = new SymbolIcon(Symbol.Document), Content = Localization.Get("S822ba2796e8de87e"), Tag = "File" };
        SettingsNavigationItem = new NavigationViewItem { Content = Localization.Get("Sdf3d58c7d84b85f2"), Tag = "Settings" };
        SettingsNavigationItem.Icon = new AnimatedIcon
        {
            Source = new AnimatedSettingsVisualSource(),
            FallbackIconSource = new FontIconSource { Glyph = "\uE713" },
        };
        AboutNavigationItem = new NavigationViewItem { Icon = new SymbolIcon(Symbol.Help), Content = Localization.Get("S52d25a9e30ba94f1"), Tag = "About" };

        SidebarNavigationView = new NavigationView
        {
            Background = transparent,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            CompactModeThresholdWidth = 0,
            ExpandedModeThresholdWidth = 900,
            IsPaneOpen = true,
            CompactPaneLength = 48,
            OpenPaneLength = 200,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Visible,
            IsBackEnabled = false,
            IsSettingsVisible = false,
        };
        SidebarNavigationView.MenuItems.Add(HomeNavigationItem);
        SidebarNavigationView.MenuItems.Add(ImageNavigationItem);
        SidebarNavigationView.MenuItems.Add(FileNavigationItem);
        SidebarNavigationView.MenuItems.Add(SettingsNavigationItem);
        SidebarNavigationView.FooterMenuItems.Add(AboutNavigationItem);
        SidebarNavigationView.SelectionChanged += SidebarNavigationView_SelectionChanged;
        SidebarNavigationView.BackRequested += SidebarNavigationView_BackRequested;

        var content = new Grid { Background = transparent };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        TitleBarDragRegion = new Border { Background = transparent };
        PageTitleTextBlock = new TextBlock { Text = Localization.AppName, Margin = new Thickness(16, 0, 0, 0), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var title = new Grid { Height = 48, Background = transparent };
        title.Children.Add(TitleBarDragRegion);
        title.Children.Add(PageTitleTextBlock);
        Grid.SetRow(title, 0);
        content.Children.Add(title);

        HomeToolbarHost = Host();
        HomeToolbarHost.Visibility = Visibility.Visible;
        Grid.SetRow(HomeToolbarHost, 1);
        content.Children.Add(HomeToolbarHost);
        HomePageHost = Host();
        HomePageHost.Visibility = Visibility.Visible;
        Grid.SetRow(HomePageHost, 2);
        content.Children.Add(HomePageHost);
        TranslationHistoryPageHost = Host(); Grid.SetRow(TranslationHistoryPageHost, 2); content.Children.Add(TranslationHistoryPageHost);
        FavoritesPageHost = Host(); Grid.SetRow(FavoritesPageHost, 2); content.Children.Add(FavoritesPageHost);
        SettingsPageHost = Host(); Grid.SetRow(SettingsPageHost, 2); content.Children.Add(SettingsPageHost);
        ImagePageHost = Host(); Grid.SetRow(ImagePageHost, 2); content.Children.Add(ImagePageHost);
        FilePageHost = Host(); Grid.SetRow(FilePageHost, 2); content.Children.Add(FilePageHost);
        AboutPageHost = Host(); Grid.SetRow(AboutPageHost, 2); content.Children.Add(AboutPageHost);
        SidebarNavigationView.Content = content;

        MiniWindowGrid = new Grid { Background = transparent, Visibility = Visibility.Collapsed };
        MiniWindowGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        MiniWindowGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        MiniTitleBarDragRegion = new Border { Background = transparent };
        var miniTitle = new Grid { Background = transparent };
        miniTitle.Children.Add(MiniTitleBarDragRegion);
        miniTitle.Children.Add(new TextBlock { Text = Localization.AppName, Margin = new Thickness(16, 0, 0, 0), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        MiniWindowGrid.Children.Add(miniTitle);
        MiniPageHost = Host();
        MiniPageHost.Visibility = Visibility.Visible;
        MiniPageHost.Padding = new Thickness(12);
        Grid.SetRow(MiniPageHost, 1);
        MiniWindowGrid.Children.Add(MiniPageHost);

        StatusInfoBar = new InfoBar { IsOpen = false, Severity = InfoBarSeverity.Informational, Margin = new Thickness(16, 0, 16, 16), CornerRadius = new CornerRadius(8) };
        Grid.SetRow(StatusInfoBar, 1);
        ClipboardTipHost = Host();
        WindowRoot.Children.Add(AcrylicFallbackLayer);
        WindowRoot.Children.Add(SidebarNavigationView);
        WindowRoot.Children.Add(MiniWindowGrid);
        WindowRoot.Children.Add(StatusInfoBar);
        WindowRoot.Children.Add(ClipboardTipHost);
        Content = WindowRoot;
    }

    private static ContentControl Host() => new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Collapsed,
    };
}
