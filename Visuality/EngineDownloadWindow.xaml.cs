using System.Windows;
using System.Windows.Input;

namespace Visuality
{
    public partial class EngineDownloadWindow : Window
    {
        public EngineDownloadWindow(string engineName)
        {
            InitializeComponent();
            EngineLabel.Text = $"Motor: {engineName}";
        }

        public void UpdateProgress(double progress, string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (progress >= 0)
                {
                    ProgressBarControl.IsIndeterminate = false;
                    ProgressBarControl.Value = progress;
                }
                else
                {
                    ProgressBarControl.IsIndeterminate = true;
                }
                StatusTextControl.Text = status;
            });
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }
    }
}
