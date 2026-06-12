using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VSAI.AILogic;

namespace VSAI
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            TxtHwid.Text = LicenseValidator.GetHWID();
            TxtLicenseKey.Text = LicenseValidator.LoadSavedLicenseKey();
            Loaded += (s, e) =>
            {
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            };
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            string licenseKey = TxtLicenseKey.Text.Trim();
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                ShowError("Por favor, insira a chave de licença.");
                return;
            }

            SetUiState(isBusy: true);

            var (success, message) = await LicenseValidator.ValidateLicenseAsync(licenseKey);

            if (success)
            {
                LicenseValidator.SaveLicenseKey(licenseKey);
                ShowSuccess(message);

                await System.Threading.Tasks.Task.Delay(1000);

                // Open main menu
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var mainWindow = new MainWindow();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                this.Close();
            }
            else
            {
                ShowError(message);
                SetUiState(isBusy: false);
            }
        }

        private void SetUiState(bool isBusy)
        {
            TxtLicenseKey.IsEnabled = !isBusy;
            BtnValidate.IsEnabled = !isBusy;
            LoadingSpinner.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (isBusy)
            {
                LblStatus.Text = "";
            }
        }

        private void ShowError(string msg)
        {
            LblStatus.Foreground = Brushes.Red;
            LblStatus.Text = msg;
        }

        private void ShowSuccess(string msg)
        {
            LblStatus.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // #FF2ECC71
            LblStatus.Text = msg;
        }

        private void BtnCopyHwid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtHwid.Text);
                MessageBox.Show("HWID copiado para a área de transferência!", "VS AI", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao copiar HWID: {ex.Message}", "VS AI", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtLicenseKey_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnValidate_Click(sender, e);
            }
        }
    }
}
