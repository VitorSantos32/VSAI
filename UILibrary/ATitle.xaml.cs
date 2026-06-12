using VSAI.Class;

namespace VSAI.UILibrary
{
    /// <summary>
    /// Interaction logic for ATitle.xaml
    /// </summary>
    public partial class ATitle : System.Windows.Controls.UserControl
    {
        private readonly string _stateKey;

        // stateKey: chave no Dictionary.minimizeState (em inglês)
        // Text: texto exibido na UI (pode ser em português)
        public ATitle(string Text, bool MinimizableMenu = false, string? stateKey = null)
        {
            InitializeComponent();

            // Se stateKey não for passado, usa Text como chave (compatibilidade)
            _stateKey = stateKey ?? Text;

            LabelTitle.Content = Text;

            if (MinimizableMenu)
            {
                Minimize.Visibility = System.Windows.Visibility.Visible;
                if (Dictionary.minimizeState.TryGetValue(_stateKey, out var state))
                {
                    switch (state)
                    {
                        case false:
                            Minimize.Content = "\xE921";
                            break;
                        case true:
                            Minimize.Content = "\xE710";
                            break;
                    }
                }
                else
                {
                    Minimize.Content = "\xE921";
                }
            }

            Minimize.Click += (s, e) =>
            {
                if (Dictionary.minimizeState.TryGetValue(_stateKey, out var currentState))
                {
                    switch (currentState)
                    {
                        case false:
                            Minimize.Content = "\xE710";
                            break;
                        case true:
                            Minimize.Content = "\xE921";
                            break;
                    }
                    Dictionary.minimizeState[_stateKey] = !currentState;
                }
            };
        }
    }
}