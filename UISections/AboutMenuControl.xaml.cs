using VSAI.Theme;
using Other;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VSAI.Controls
{
    public partial class AboutMenuControl : UserControl
    {
        private MainWindow? _mainWindow;
        private bool _isInitialized;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        // Cached resources
        private Brush? _themeColor;
        private FontFamily? _fontFamily;

        // Credits data - easy to add/remove people
        private static readonly (string name, string role, string? github)[] CoreTeam =
        {
            ("Vitor Santos", "Fundador & Desenvolvedor", null)
        };

        private static readonly (string name, string? github, bool highlighted)[] Contributors = Array.Empty<(string, string?, bool)>();

        // Public properties for MainWindow access
        public Label AboutSpecsControl => AboutSpecs;
        public ScrollViewer AboutMenuScrollViewer => AboutMenu;

        public AboutMenuControl()
        {
            InitializeComponent();
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            _mainWindow = mainWindow;
            _isInitialized = true;

            // Use ThemeManager directly for theme color
            _themeColor = new SolidColorBrush(ThemeManager.ThemeColor);
            _fontFamily = Application.Current.TryFindResource("Atkinson Hyperlegible") as FontFamily
                ?? new FontFamily("Segoe UI"); // Fallback font

            // Define a versão dinamicamente
            AboutDesc.Content = $"v{AILogic.AutoUpdater.CurrentVersion}";

            LoadCoreTeam();
        }

        private void LoadCoreTeam()
        {
            CoreTeamPanel.Children.Clear();

            foreach (var (name, role, github) in CoreTeam)
            {
                var panel = CreateCoreTeamMember(name, role, github);
                CoreTeamPanel.Children.Add(panel);
            }
        }

        private StackPanel CreateCoreTeamMember(string name, string role, string? github)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Avatar container
            var avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                Background = _themeColor,
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true
            };

            if (name == "Vitor Santos")
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri("pack://application:,,,/Graphics/logovs.png");
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var image = new Ellipse
                    {
                        Width = 48,
                        Height = 48,
                        Fill = new ImageBrush(bitmap)
                        {
                            Stretch = Stretch.UniformToFill
                        }
                    };

                    avatarBorder.Background = Brushes.Transparent;
                    avatarBorder.Child = image;
                }
                catch
                {
                    var fallbackText = new TextBlock
                    {
                        Text = "VS",
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    avatarBorder.Child = fallbackText;
                }
            }
            else
            {
                // Fallback text (first letter)
                var fallbackText = new TextBlock
                {
                    Text = name[0].ToString().ToUpper(),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatarBorder.Child = fallbackText;

                // Try to load GitHub avatar
                if (!string.IsNullOrEmpty(github))
                {
                    LoadGitHubAvatar(github, avatarBorder, fallbackText);
                }
            }

            panel.Children.Add(avatarBorder);

            // Name
            var nameText = new TextBlock
            {
                Text = name,
                FontFamily = _fontFamily,
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Make clickable if has GitHub
            if (!string.IsNullOrEmpty(github))
            {
                nameText.Cursor = Cursors.Hand;
                nameText.MouseEnter += (s, e) => nameText.TextDecorations = TextDecorations.Underline;
                nameText.MouseLeave += (s, e) => nameText.TextDecorations = null;
                nameText.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
                avatarBorder.Cursor = Cursors.Hand;
                avatarBorder.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
            }

            panel.Children.Add(nameText);

            // Role
            var roleText = new TextBlock
            {
                Text = role,
                FontFamily = _fontFamily,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(roleText);

            return panel;
        }

        private async void LoadGitHubAvatar(string username, Border avatarBorder, TextBlock fallbackText)
        {
            try
            {
                var imageUrl = $"https://github.com/{username}.png?size=96";
                var response = await _httpClient.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageData = await response.Content.ReadAsByteArrayAsync();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = new System.IO.MemoryStream(imageData);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            var image = new Ellipse
                            {
                                Width = 48,
                                Height = 48,
                                Fill = new ImageBrush(bitmap)
                                {
                                    Stretch = Stretch.UniformToFill
                                }
                            };

                            avatarBorder.Background = Brushes.Transparent;
                            avatarBorder.Child = image;
                        }
                        catch
                        {
                            // Keep fallback text on error
                        }
                    });
                }
            }
            catch
            {
                // Keep fallback text on error
            }
        }
        private static void OpenGitHubProfile(string username)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"https://github.com/{username}",
                    UseShellExecute = true
                });
            }
            catch { }
        }



        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.com/invite/WH8FWPdBba",
                    UseShellExecute = true
                });
            }
            catch { }
        }

    }
}
