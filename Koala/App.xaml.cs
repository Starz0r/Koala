using System;

using Koala.Services;

using Windows.ApplicationModel.Activation;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Koala
{
    public sealed partial class App : Application
    {
        public static string CmdArgv { get; set; }

        private Lazy<ActivationService> _activationService;

        private ActivationService ActivationService
        {
            get { return _activationService.Value; }
        }

        public App()
        {
            InitializeComponent();

            // Deferred execution until used. Check https://msdn.microsoft.com/library/dd642331(v=vs.110).aspx for further info on Lazy<T> class.
            _activationService = new Lazy<ActivationService>(CreateActivationService);
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            if (!args.PrelaunchActivated)
            {
                await ActivationService.ActivateAsync(args);
            }
        }

        protected override async void OnActivated(IActivatedEventArgs args)
        {
            // Handle CommandLineLaunch
            if (args.Kind == ActivationKind.CommandLineLaunch)
            {
                var commandLine = args as CommandLineActivatedEventArgs;
                if (commandLine != null)
                {
                    CmdArgv = commandLine.Operation.Arguments;
                }
            }

            // Return Control
            await ActivationService.ActivateAsync(args);

            // Resume Normally
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            rootFrame.Navigate(typeof(Views.MainPage), "OnActivated");

            Window.Current.Activate();
            base.OnActivated(args);
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private ActivationService CreateActivationService()
        {
            return new ActivationService(this, typeof(Views.MainPage));
        }
    }
}
