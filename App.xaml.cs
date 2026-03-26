using Microsoft.UI.Xaml.Navigation;

namespace BLauncher
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? m_window;

        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += (s, e) => {
                try { System.IO.File.AppendAllText("crash.log", $"[{DateTime.Now}] CRASH: {e.Message}\n{e.Exception}\n\n"); } catch {}
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
