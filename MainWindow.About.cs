using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windtranslator.Services;

namespace Windtranslator;

public sealed partial class MainWindow
{
    private AboutPageStore? _aboutStore;

    private AboutPageStore CreateAboutStore()
    {
        var store = new AboutPageStore(
            new AboutPageState(Localization.Text("版本 ") + GetApplicationVersion()));

        var host = new ReactorHostControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        AboutPageHost.Content = host;
        host.Mount(new AboutPageView(store));
        _aboutStore = store;
        return store;
    }
}
