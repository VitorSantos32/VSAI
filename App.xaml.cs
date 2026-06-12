using VSAI.Theme;
using Class;
using System.Windows;

namespace VSAI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Set up global exception handlers to log startup crashes
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.WriteAllText("crash_unhandled.txt", args.ExceptionObject.ToString());
                }
                catch { }
            };
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.WriteAllText("crash_dispatcher.txt", args.Exception.ToString());
                }
                catch { }
            };

            // Initialize custom ONNX DLL resolver
            AILogic.EngineManager.Initialize();

            // Initialize the application theme from saved settings
            InitializeTheme();

            // Set shutdown mode to prevent app from closing when startup window closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;


            // code IS reachable, only in release though
            try
            {
                // Create and show startup window
                var startupWindow = new StartupWindow();
                startupWindow.Show();

                // Reset shutdown mode after startup window is shown
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                // If startup window fails, launch main window directly
                MessageBox.Show($"Startup animation failed: {ex.Message}\nLaunching main application...",
                              "VS AI", MessageBoxButton.OK, MessageBoxImage.Information);

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();

                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
        }

        private void InitializeTheme()
        {
            try
            {
                // Load the color state configuration
                var colorState = new Dictionary<string, dynamic>
                {
                    { "Theme Color", "#FF2ECC71" }
                };

                // Load saved colors
                SaveDictionary.LoadJSON(colorState, "bin\\colors.cfg");

                // Check for migration from old default purple color
                if (colorState.TryGetValue("Theme Color", out var themeColor) && themeColor is string colorString)
                {
                    if (colorString.Equals("#FF722ED1", StringComparison.OrdinalIgnoreCase))
                    {
                        colorString = "#FF2ECC71";
                        colorState["Theme Color"] = colorString;
                        SaveDictionary.WriteJSON(colorState, "bin\\colors.cfg");
                    }
                    ThemeManager.SetThemeColor(colorString);
                }
                else
                {
                    // Use default green if no saved color
                    ThemeManager.SetThemeColor("#FF2ECC71");
                }
            }
            catch (Exception ex)
            {
                // Log error and use default color
                ThemeManager.SetThemeColor("#FF2ECC71");
            }
        }
    }
}