using VSAI.AILogic;
using VSAI.Class;
using VSAI.UILibrary;
using Other;
using System.Windows;
using System.Windows.Controls;
using UILibrary;
using Visuality;
using LogLevel = Other.LogManager.LogLevel;

namespace VSAI.Controls
{
    public partial class SettingsMenuControl : UserControl
    {
        private MainWindow? _mainWindow;
        private bool _isInitialized;
        private bool _suppressImageSizeSelectionChanged;
        private ADropdown? _engineDropdown;
        private bool _suppressEngineSelectionChanged;

        // Local minimize state management
        private readonly Dictionary<string, bool> _localMinimizeState = new()
        {
            { "Model Settings", false },
            { "Settings Menu", false },
            { "Theme Settings", false },
            { "Screen Settings", false }
        };

        // Public properties for MainWindow access
        public StackPanel ModelSettingsPanel => ModelSettings;
        public StackPanel SettingsConfigPanel => SettingsConfig;
        public StackPanel ThemeMenuPanel => ThemeMenu;
        public StackPanel DisplaySelectMenuPanel => DisplaySelectMenu;
        public ScrollViewer SettingsMenuScrollViewer => SettingsMenu;

        public SettingsMenuControl()
        {
            InitializeComponent();
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            _mainWindow = mainWindow;
            _isInitialized = true;

            // Load minimize states from global dictionary if they exist
            LoadMinimizeStatesFromGlobal();

            TryLoad("ModelSettings", LoadModelSettings);
            TryLoad("SettingsConfig", LoadSettingsConfig);
            TryLoad("ThemeMenu", LoadThemeMenu);
            TryLoad("DisplaySelectMenu", LoadDisplaySelectMenu);

            // Apply minimize states after loading
            ApplyMinimizeStates();

            // Subscribe to display changes
            DisplayManager.DisplayChanged += OnDisplayChanged;

            // Subscribe to AI class updates for Target Class dropdown
            AIManager.ClassesUpdated += OnClassesChanged;

            // Subscribe to dynamic model status changes
            AIManager.DynamicModelStatusChanged += OnDynamicModelStatusChanged;

            // Set visibility based on current model status (handles case where model loaded before panel opened)
            UpdateDynamicModelDropdownsVisibility(AIManager.CurrentModelIsDynamic);
            UpdateTargetClassDropdown(_mainWindow!.uiManager.D_TargetClass!);
        }

        private static void TryLoad(string sectionName, Action loader)
        {
            try
            {
                loader();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Erro ao carregar seção '{sectionName}':\n{ex}",
                    "Erro de Seção",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        #region Minimize State Management

        private void LoadMinimizeStatesFromGlobal()
        {
            foreach (var key in _localMinimizeState.Keys.ToList())
            {
                if (Dictionary.minimizeState.ContainsKey(key))
                {
                    _localMinimizeState[key] = Dictionary.minimizeState[key];
                }
            }
        }

        private void SaveMinimizeStatesToGlobal()
        {
            foreach (var kvp in _localMinimizeState)
            {
                Dictionary.minimizeState[kvp.Key] = kvp.Value;
            }
        }

        private void ApplyMinimizeStates()
        {
            ApplyPanelState("Model Settings", ModelSettingsPanel);
            ApplyPanelState("Settings Menu", SettingsConfigPanel);
            ApplyPanelState("Theme Settings", ThemeMenuPanel);
            ApplyPanelState("Screen Settings", DisplaySelectMenuPanel);
        }

        private void ApplyPanelState(string stateName, StackPanel panel)
        {
            if (_localMinimizeState.TryGetValue(stateName, out bool isMinimized))
            {
                SetPanelVisibility(panel, !isMinimized);
            }
        }

        private void SetPanelVisibility(StackPanel panel, bool isVisible)
        {
            foreach (UIElement child in panel.Children)
            {
                // Keep titles, spacers, and bottom rectangles always visible
                bool shouldStayVisible = child is ATitle || child is ASpacer || child is ARectangleBottom;

                child.Visibility = shouldStayVisible
                    ? Visibility.Visible
                    : (isVisible ? Visibility.Visible : Visibility.Collapsed);
            }
        }

        public void ResetEngineSelectionToCurrent()
        {
            if (_engineDropdown == null) return;

            _suppressEngineSelectionChanged = true;
            try
            {
                string currentEngine = Dictionary.dropdownState.ContainsKey("AI Engine") ? Dictionary.dropdownState["AI Engine"] : "DirectML";
                for (int i = 0; i < _engineDropdown.DropdownBox.Items.Count; i++)
                {
                    if ((_engineDropdown.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == currentEngine)
                    {
                        _engineDropdown.DropdownBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            finally
            {
                _suppressEngineSelectionChanged = false;
            }
        }

        private void TogglePanel(string stateName, StackPanel panel)
        {
            if (!_localMinimizeState.ContainsKey(stateName)) return;

            // Toggle the state
            _localMinimizeState[stateName] = !_localMinimizeState[stateName];

            // Apply the new visibility
            SetPanelVisibility(panel, !_localMinimizeState[stateName]);

            // Save to global dictionary
            SaveMinimizeStatesToGlobal();
        }

        #endregion

        #region Menu Section Loaders

        private void LoadModelSettings()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ModelSettings);

            builder
                .AddTitle("Configurações do Modelo", true, t =>
                {
                    uiManager.AT_ModelSettings = t;
                    t.Minimize.Click += (s, e) => TogglePanel("Model Settings", ModelSettingsPanel);
                }, stateKey: "Model Settings")
                .AddDropdown("AI Engine", "Motor de Execução", d =>
                {
                    _engineDropdown = d;
                    _mainWindow.AddDropdownItem(d, "DirectML");
                    _mainWindow.AddDropdownItem(d, "TensorRT");
                    _mainWindow.AddDropdownItem(d, "OpenVINO");

                    string currentEngine = Dictionary.dropdownState.ContainsKey("AI Engine") ? Dictionary.dropdownState["AI Engine"] : "DirectML";
                    if (currentEngine == "CUDA")
                    {
                        currentEngine = "DirectML";
                        Dictionary.dropdownState["AI Engine"] = "DirectML";
                    }

                    for (int i = 0; i < d.DropdownBox.Items.Count; i++)
                    {
                        if ((d.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == currentEngine)
                        {
                            d.DropdownBox.SelectedIndex = i;
                            break;
                        }
                    }

                    d.DropdownBox.SelectionChanged += async (s, e) =>
                    {
                        if (_suppressEngineSelectionChanged)
                            return;

                        if (d.DropdownBox.SelectedItem == null || e.AddedItems.Count == 0)
                            return;

                        string? selectedEngine = (d.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                        if (string.IsNullOrEmpty(selectedEngine))
                            return;

                        string savedEngine = Dictionary.dropdownState.ContainsKey("AI Engine") ? Dictionary.dropdownState["AI Engine"] : "DirectML";
                        
                        bool isDownloaded = true;
                        if (Enum.TryParse<AIEngine>(selectedEngine, true, out var parsedEngine))
                        {
                            isDownloaded = EngineManager.IsEngineDownloaded(parsedEngine);
                        }

                        if (selectedEngine != savedEngine || !isDownloaded)
                        {
                            await EngineManager.HandleEngineChangeAsync(selectedEngine, _mainWindow);
                        }
                    };
                }, tooltip: "Escolha o motor de processamento da IA (DirectML = Genérico, CUDA/TensorRT = NVIDIA, OpenVINO = Intel).")
                .AddDropdown("Image Size", "Tamanho da Imagem (Resolução)", d =>
                {
                    uiManager.D_ImageSize = d;

                    // Add size options
                    _mainWindow.AddDropdownItem(d, "640");
                    _mainWindow.AddDropdownItem(d, "512");
                    _mainWindow.AddDropdownItem(d, "416");
                    _mainWindow.AddDropdownItem(d, "320");
                    _mainWindow.AddDropdownItem(d, "256");
                    _mainWindow.AddDropdownItem(d, "160");

                    // Set default to current value
                    var currentSize = Dictionary.dropdownState["Image Size"];
                    for (int i = 0; i < d.DropdownBox.Items.Count; i++)
                    {
                        if ((d.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == currentSize)
                        {
                            d.DropdownBox.SelectedIndex = i;
                            break;
                        }
                    }

                    // Handle selection change
                    d.DropdownBox.SelectionChanged += async (s, e) =>
                    {
                        if (_suppressImageSizeSelectionChanged)
                            return;

                        if (d.DropdownBox.SelectedItem == null || e.AddedItems.Count == 0)
                            return;

                        var newSize = (d.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                        if (string.IsNullOrEmpty(newSize))
                            return;

                        await _mainWindow!.ChangeImageSizeAsync(newSize);
                    };
                }, tooltip: "Resolução que a IA usa para detecção. Menor = mais rápido, porém menos preciso.")
                .AddSlider("AI FPS Limit", "Limite de FPS da IA", "FPS", 5, 5, 0, 240, s =>
                {
                    uiManager.S_AIFpsLimit = s;
                    s.SetValueFormatter(value => value <= 0 ? "Ilimitado" : $"{value:F0} FPS");
                }, tooltip: "Limita o loop de IA para reduzir uso de CPU/GPU. 0 mantém velocidade máxima.")
                .AddButton("Run Performance Helper", "Abrir Assistente de Desempenho", b =>
                {
                    b.Reader.Click += (s, e) => _mainWindow!.ShowPerformanceHelper();
                }, tooltip: "Abre o assistente de desempenho para o modelo carregado.")
                .AddDropdown("Target Class", "Classe Alvo da IA", d =>
                {
                    d.DropdownBox.SelectedIndex = 0;
                    uiManager.D_TargetClass = d;
                    _mainWindow.AddDropdownItem(d, "Melhor Confiança");
                    UpdateTargetClassDropdown(d);
                }, tooltip: "Qual tipo de alvo mirar. Melhor Confiança escolhe a detecção mais certeira.")
                .AddSlider("AI Minimum Confidence", "Confiança Mínima da IA", "% Confiança", 1, 1, 1, 100, s =>
                {
                    uiManager.S_AIMinimumConfidence = s;
                    s.Slider.PreviewMouseLeftButtonUp += (sender, e) =>
                    {
                        var value = s.Slider.Value;
                        if (value >= 95)
                            LogManager.Log(LogLevel.Warning, "A confiança mínima configurada está muito alta e a IA pode não detectar os jogadores.", true);
                        else if (value <= 35)
                            LogManager.Log(LogLevel.Warning, "A confiança mínima configurada está muito baixa e pode causar falsos positivos.", true);
                    };
                }, tooltip: "Certeza que a IA deve ter antes de mirar. Maior = menos falsos positivos, menor = detecta mais rápido.")
                .AddToggle("Enable Model Switch Keybind", "Ativar Atalho para Trocar Modelo", t => uiManager.T_EnableModelSwitchKeybind = t,
                    tooltip: "Permite alternar entre modelos de IA usando uma tecla de atalho.")
                .AddKeyChanger("Model Switch Keybind", "Atalho para Trocar Modelo", k => uiManager.C_ModelSwitchKeybind = k,
                    tooltip: "Pressione esta tecla para alternar entre os modelos de IA disponíveis.")
                .AddKeyChanger("Emergency Stop Keybind", "Atalho de Parada de Emergência", k => uiManager.C_EmergencyKeybind = k,
                    tooltip: "Pressione esta tecla para parar imediatamente todas as funções do assistente.")
                .AddSeparator();
        }

        private void LoadSettingsConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, SettingsConfig);

            builder
                .AddTitle("Configurações Gerais", true, t =>
                {
                    uiManager.AT_SettingsMenu = t;
                    t.Minimize.Click += (s, e) => TogglePanel("Settings Menu", SettingsConfigPanel);
                }, stateKey: "Settings Menu")
                .AddToggle("Collect Data While Playing", "Salvar Capturas para Treinamento (Coleta)", t => uiManager.T_CollectDataWhilePlaying = t,
                    tooltip: "Salva capturas de tela das detecções para treinar novos modelos.")
                .AddToggle("Auto Label Data", "Rotular Capturas Automaticamente", t => uiManager.T_AutoLabelData = t,
                    tooltip: "Rotula automaticamente as capturas de tela coletadas com dados de detecção.")
                .AddToggle("Mouse Background Effect", "Efeito Visual do Mouse no Fundo", t => uiManager.T_MouseBackgroundEffect = t,
                    tooltip: "Exibe um efeito visual na interface ao mover o mouse.")
                .AddToggle("UI TopMost", "Janela Sempre no Topo", t => uiManager.T_UITopMost = t,
                    tooltip: "Mantém esta janela acima de todas as outras.")
                .AddToggle("Debug Mode", "Modo Depuração (Logs Detalhados)", t => uiManager.T_DebugMode = t,
                    tooltip: "Exibe informações adicionais úteis para solução de problemas.")
                .AddButton("Save Config", "Salvar Configurações", b =>
                {
                    uiManager.B_SaveConfig = b;
                    b.Reader.Click += (s, e) => new ConfigSaver().ShowDialog();
                }, tooltip: "Salva suas configurações atuais em um arquivo para carregar mais tarde.")
                .AddSeparator();
        }

        private void LoadDisplaySelectMenu()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, DisplaySelectMenu);

            builder
                .AddTitle("Configurações de Tela", true, t =>
                {
                    uiManager.AT_DisplaySelector = t;
                    t.Minimize.Click += (s, e) =>
                        TogglePanel("Screen Settings", DisplaySelectMenuPanel);
                }, stateKey: "Screen Settings")
                .AddDropdown("Screen Capture Method", "Método de Captura", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;  // Prevent auto-selection that overwrites saved state
                    uiManager.D_ScreenCaptureMethod = d;
                    _mainWindow.AddDropdownItem(d, "DirectX");
                    _mainWindow.AddDropdownItem(d, "WGC");
                    _mainWindow.AddDropdownItem(d, "GDI+");
                }, tooltip: "Como a tela é capturada. WGC é ideal para Windows 10/11 (rápido e sem tela preta), DirectX é excelente para placas dedicadas e GDI+ é mais lento.")
                .AddToggle("StreamGuard", "Ocultar do Stream / OBS (StreamGuard)", t => uiManager.T_StreamGuard = t,
                    tooltip: "Esconde a sobreposição em gravações de tela e transmissões. Fica na bandeja do sistema.")
                .AddSeparator();

            // Handle DisplaySelector separately as it's a custom control
            uiManager.DisplaySelector = new ADisplaySelector();
            uiManager.DisplaySelector.RefreshDisplays();

            // Insert after title but before separator
            var insertIndex = DisplaySelectMenu.Children.Count - 2;
            DisplaySelectMenu.Children.Insert(insertIndex, uiManager.DisplaySelector);

            // Add refresh button after DisplaySelector
            var refreshButton = new APButton("Atualizar Monitores", "Update the list of available monitors.");
            refreshButton.Reader.Click += (s, e) =>
            {
                try
                {
                    DisplayManager.RefreshDisplays();
                    uiManager.DisplaySelector.RefreshDisplays();
                    LogManager.Log(LogLevel.Info, "Lista de monitores atualizada com sucesso.", true);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogLevel.Error, $"Erro ao atualizar monitores: {ex.Message}", true);
                }
            };
            DisplaySelectMenu.Children.Insert(insertIndex + 1, refreshButton);
        }

        private void LoadThemeMenu()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ThemeMenu);

            builder
                .AddTitle("Configurações de Tema", true, t =>
                {
                    uiManager.AT_ThemeColorWheel = t;
                    t.Minimize.Click += (s, e) =>
                        TogglePanel("Theme Settings", ThemeMenuPanel);
                }, stateKey: "Theme Settings")
                .AddSeparator();

            // Handle ColorWheel separately as it's a custom control
            uiManager.ThemeColorWheel = new AColorWheel();

            //--
            if (uiManager.ThemeColorWheel.FindName("ArrowButton") is Button arrowButton)
                arrowButton.Visibility = Visibility.Visible;
            //--

            // Insert before separator
            var insertIndex = ThemeMenu.Children.Count - 2;
            ThemeMenu.Children.Insert(insertIndex, uiManager.ThemeColorWheel);
        }

        #endregion

        #region Helper Methods

        private void OnDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    LogManager.Log(LogLevel.Info, $"Foco da IA alternado para Tela {e.DisplayIndex + 1} ({e.Bounds.Width}x{e.Bounds.Height})", true);
                    UpdateDisplayRelatedSettings(e);
                }
                catch (Exception ex)
                {
                }
            });
        }

        private void UpdateDisplayRelatedSettings(DisplayChangedEventArgs e)
        {
            Dictionary.sliderSettings["SelectedDisplay"] = e.DisplayIndex;
        }

        private async Task ResetToMouseEvent()
        {
            await Task.Delay(500);
            _mainWindow!.uiManager.D_MouseMovementMethod!.DropdownBox.SelectedIndex = 0;
        }

        public void UpdateImageSizeDropdown(string newSize, bool suppressSelectionChanged = true)
        {
            if (_mainWindow?.uiManager.D_ImageSize != null)
            {
                var dropdown = _mainWindow.uiManager.D_ImageSize;
                _suppressImageSizeSelectionChanged = suppressSelectionChanged;
                try
                {
                    for (int i = 0; i < dropdown.DropdownBox.Items.Count; i++)
                    {
                        if ((dropdown.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == newSize)
                        {
                            dropdown.DropdownBox.SelectedIndex = i;
                            break;
                        }
                    }
                }
                finally
                {
                    _suppressImageSizeSelectionChanged = false;
                }
            }
        }

        private void OnClassesChanged(Dictionary<int, string> classes)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_mainWindow?.uiManager.D_TargetClass != null)
                {
                    UpdateTargetClassDropdown(_mainWindow.uiManager.D_TargetClass, classes);
                }
            });
        }

        private void OnDynamicModelStatusChanged(bool isDynamic)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateDynamicModelDropdownsVisibility(isDynamic);
            });
        }

        private void UpdateDynamicModelDropdownsVisibility(bool isDynamic)
        {
            // Only Image Size depends on dynamic model - it's hidden for static models
            // Target Class is always visible since both static and dynamic models can have multiple classes
            var imageSizeVisibility = isDynamic ? Visibility.Visible : Visibility.Collapsed;

            if (_mainWindow?.uiManager.D_ImageSize != null)
            {
                _mainWindow.uiManager.D_ImageSize.Visibility = imageSizeVisibility;
            }
        }

        private void UpdateTargetClassDropdown(ADropdown dropdown, Dictionary<int, string>? _classes = null)
        {
            if (dropdown?.DropdownBox == null) return;
            var visibility = _classes != null && _classes.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            dropdown.Visibility = visibility;
            _mainWindow!.uiManager.D_TargetClass!.Visibility = visibility;

            string? selection = (dropdown.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            var removedItems = dropdown.DropdownBox.Items.Cast<ComboBoxItem>()
                .Where(item => item.Content?.ToString() != "Melhor Confiança" && item.Content?.ToString() != "Best Confidence")
                .ToList();

            foreach (var item in removedItems)
            {
                dropdown.DropdownBox.Items.Remove(item);
            }

            var classes = _classes ?? FileManager.AIManager?.ModelClasses ?? new Dictionary<int, string>();

            foreach (var kvp in classes.OrderBy(x => x.Key))
            {
                _mainWindow!.AddDropdownItem(dropdown, kvp.Value);
            }

            if (!string.IsNullOrEmpty(selection)) // tries to restore the selection
            {
                for (int i = 0; i < dropdown.DropdownBox.Items.Count; i++)
                {
                    if ((dropdown.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == selection)
                    {
                        dropdown.DropdownBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            dropdown.DropdownBox.SelectedIndex = 0;
        }

        public void Dispose()
        {
            DisplayManager.DisplayChanged -= OnDisplayChanged;
            AIManager.ClassesUpdated -= OnClassesChanged;
            _mainWindow?.uiManager.DisplaySelector?.Dispose();

            // Save minimize states before disposing
            SaveMinimizeStatesToGlobal();
        }

        #endregion

        #region Control Creation Methods

        private AToggle CreateToggle(string title, string displayTitle, string? tooltip = null)
        {
            var toggle = new AToggle(displayTitle, tooltip);
            _mainWindow!.toggleInstances[title] = toggle;

            // Set initial state
            if (Dictionary.toggleState[title])
                toggle.EnableSwitch();
            else
                toggle.DisableSwitch();

            // Handle click
            toggle.Reader.Click += (sender, e) =>
            {
                Dictionary.toggleState[title] = !Dictionary.toggleState[title];
                _mainWindow.UpdateToggleUI(toggle, Dictionary.toggleState[title]);
                _mainWindow.Toggle_Action(title);
            };

            return toggle;
        }

        //copied & Pasted from other class
        private AKeyChanger CreateKeyChanger(string title, string displayTitle, string keybind, string? tooltip = null)
        {
            var keyChanger = new AKeyChanger(displayTitle, keybind, tooltip);

            keyChanger.Reader.Click += (sender, e) =>
            {
                keyChanger.KeyNotifier.Content = "...";
                _mainWindow!.bindingManager.StartListeningForBinding(title);

                Action<string, string>? bindingSetHandler = null;
                bindingSetHandler = (bindingId, key) =>
                {
                    if (bindingId == title)
                    {
                        keyChanger.KeyNotifier.Content = KeybindNameManager.ConvertToRegularKey(key);
                        Dictionary.bindingSettings[bindingId] = key;
                        _mainWindow.bindingManager.OnBindingSet -= bindingSetHandler;
                    }
                };

                _mainWindow.bindingManager.OnBindingSet += bindingSetHandler;
            };

            return keyChanger;
        }

        private ASlider CreateSlider(string title, string displayTitle, string label, double frequency, double buttonSteps,
            double min, double max, string? tooltip = null)
        {
            var slider = new ASlider(displayTitle, label, buttonSteps, tooltip)
            {
                Slider = { Minimum = min, Maximum = max, TickFrequency = frequency }
            };

            slider.Slider.Value = Dictionary.sliderSettings.TryGetValue(title, out var value) ? value : min;
            slider.Slider.ValueChanged += (s, e) => Dictionary.sliderSettings[title] = slider.Slider.Value;

            return slider;
        }

        private ADropdown CreateDropdown(string title, string displayTitle, string? tooltip = null) => new(displayTitle, title, tooltip);

        #endregion

        #region Section Builder

        private class SectionBuilder
        {
            private readonly SettingsMenuControl _parent;
            private readonly StackPanel _panel;

            public SectionBuilder(SettingsMenuControl parent, StackPanel panel)
            {
                _parent = parent;
                _panel = panel;
            }

            public SectionBuilder AddTitle(string title, bool canMinimize, Action<ATitle>? configure = null, string? stateKey = null)
            {
                var titleControl = new ATitle(title, canMinimize, stateKey);
                configure?.Invoke(titleControl);
                _panel.Children.Add(titleControl);
                return this;
            }

            public SectionBuilder AddToggle(string title, string displayTitle, Action<AToggle>? configure = null, string? tooltip = null)
            {
                var toggle = _parent.CreateToggle(title, displayTitle, tooltip);
                configure?.Invoke(toggle);
                _panel.Children.Add(toggle);
                return this;
            }

            public SectionBuilder AddKeyChanger(string title, string displayTitle, Action<AKeyChanger>? configure = null, string? defaultKey = null, string? tooltip = null)
            {
                var key = defaultKey ?? Dictionary.bindingSettings[title];
                var keyChanger = _parent.CreateKeyChanger(title, displayTitle, key, tooltip);
                configure?.Invoke(keyChanger);
                _panel.Children.Add(keyChanger);
                return this;
            }

            public SectionBuilder AddSlider(string title, string displayTitle, string label, double frequency, double buttonSteps,
                double min, double max, Action<ASlider>? configure = null, string? tooltip = null)
            {
                var slider = _parent.CreateSlider(title, displayTitle, label, frequency, buttonSteps, min, max, tooltip);
                configure?.Invoke(slider);
                _panel.Children.Add(slider);
                return this;
            }

            public SectionBuilder AddDropdown(string title, string displayTitle, Action<ADropdown>? configure = null, string? tooltip = null)
            {
                var dropdown = _parent.CreateDropdown(title, displayTitle, tooltip);
                configure?.Invoke(dropdown);
                _panel.Children.Add(dropdown);
                return this;
            }

            public SectionBuilder AddButton(string title, string displayTitle, Action<APButton>? configure = null, string? tooltip = null)
            {
                string icon = title == "Run Performance Helper" ? "\uE768" : "\uE8B0";
                var button = new APButton(displayTitle, tooltip, icon);
                configure?.Invoke(button);
                _panel.Children.Add(button);
                return this;
            }

            public SectionBuilder AddSeparator()
            {
                _panel.Children.Add(new ARectangleBottom());
                _panel.Children.Add(new ASpacer());
                return this;
            }
        }

        #endregion
    }
}
