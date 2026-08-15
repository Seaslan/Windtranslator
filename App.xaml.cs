using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using System.Threading.Tasks;

namespace Windtranslator
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private const string MainInstanceKey = "Windtranslator.Main";
        private Window? _window;
        private Microsoft.Windows.AppLifecycle.AppInstance? _mainInstance;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var currentInstance = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey(MainInstanceKey);

            if (!mainInstance.IsCurrent)
            {
                var activationArgs = currentInstance.GetActivatedEventArgs();
                if (activationArgs is not null)
                {
                    await WaitForCompletionAsync(mainInstance.RedirectActivationToAsync(activationArgs));
                }

                return;
            }

            _mainInstance = mainInstance;
            _mainInstance.Activated += OnMainInstanceActivated;
            _window = new MainWindow();
            _window.Activate();
        }

        private void OnMainInstanceActivated(
            object? sender,
            Microsoft.Windows.AppLifecycle.AppActivationArguments args)
        {
            _window?.DispatcherQueue.TryEnqueue(() => _window.Activate());
        }

        private static Task WaitForCompletionAsync(Windows.Foundation.IAsyncAction operation)
        {
            var completionSource = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            operation.Completed = (info, _) =>
            {
                try
                {
                    info.GetResults();
                    completionSource.TrySetResult(null);
                }
                catch (System.Exception exception)
                {
                    completionSource.TrySetException(exception);
                }
            };

            return completionSource.Task;
        }
    }
}
