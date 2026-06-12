using VSAI.AILogic;
using VSAI.Class;
using VSAI.MouseMovementLibraries.GHubSupport;
using VSAI.UILibrary;
using Class;
using InputLogic;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using Other;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UILibrary;

namespace VSAI.Controls
{
    public partial class AimMenuControl : UserControl
    {
        //--
        UISections.ColorPicker colorPickerInstance = null;
        UISections.ColorPicker fovColorPickerInstance = null;
        //--
        private MainWindow? _mainWindow;
        private bool _isInitialized;

        // Local minimize state management
        private readonly Dictionary<string, bool> _localMinimizeState = new()
        {
            { "Aim Assist", false },
            { "Aim Config", false },
            { "Predictions", false },
            { "Auto Trigger", false },
            { "FOV Config", false },
            { "ESP Config", false }
        };

        // Public properties for MainWindow access
        public StackPanel AimAssistPanel => AimAssist;
        public StackPanel TriggerBotPanel => TriggerBot;
        public StackPanel ESPConfigPanel => ESPConfig;
        public StackPanel AimConfigPanel => AimConfig;
        public StackPanel PredictionsPanel => Predictions;
        public StackPanel FOVConfigPanel => FOVConfig;
        public ScrollViewer AimMenuScrollViewer => AimMenu;

        public AimMenuControl()
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

            AIManager.ImageSizeUpdated += OnImageSizeChanged;

            // Load all sections (each wrapped to prevent one failure from stopping others)
            TryLoad("AimAssist", LoadAimAssist);
            TryLoad("AimConfig", LoadAimConfig);
            TryLoad("Predictions", LoadPredictions);
            TryLoad("TriggerBot", LoadTriggerBot);
            TryLoad("FOVConfig", LoadFOVConfig);
            TryLoad("ESPConfig", LoadESPConfig);

            // Apply minimize states after loading
            ApplyMinimizeStates();
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
            ApplyPanelState("Aim Assist", AimAssistPanel);
            ApplyPanelState("Aim Config", AimConfigPanel);
            ApplyPanelState("Predictions", PredictionsPanel);
            ApplyPanelState("Auto Trigger", TriggerBotPanel);
            ApplyPanelState("FOV Config", FOVConfigPanel);
            ApplyPanelState("ESP Config", ESPConfigPanel);
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

        private void LoadAimAssist()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, AimAssist);

            builder
                .AddTitle("Assistência de Mira", true, t =>
                {
                    uiManager.AT_Aim = t;
                    t.Minimize.Click += (s, e) =>
                    {
                        TogglePanel("Aim Assist", AimAssistPanel);
                        _mainWindow?.UpdateAimAssistSliderVisibility();
                    };
                }, stateKey: "Aim Assist")
                .AddToggle("Aim Assist", "Assistência de Mira", t =>
                {
                    uiManager.T_AimAligner = t;
                    t.Reader.Click += (s, e) =>
                    {
                        if (Dictionary.toggleState["Aim Assist"] && Dictionary.lastLoadedModel == "N/A")
                        {
                            Dictionary.toggleState["Aim Assist"] = false;
                            _mainWindow.UpdateToggleUI(t, false);
                            LogManager.Log(LogManager.LogLevel.Warning, "Por favor, carregue um modelo primeiro", true);
                        }
                    };
                }, tooltip: "Liga ou desliga a assistência de mira. Você deve carregar um modelo primeiro.")
                .AddToggle("Constant AI Tracking", "Rastreamento Contínuo por IA", t =>
                {
                    uiManager.T_ConstantAITracking = t;
                    t.Reader.Click += (s, e) =>
                    {
                        if (Dictionary.toggleState["Constant AI Tracking"])
                        {
                            if (Dictionary.lastLoadedModel == "N/A")
                            {
                                Dictionary.toggleState["Constant AI Tracking"] = false;
                                _mainWindow.UpdateToggleUI(t, false);
                            }
                            else
                            {
                                Dictionary.toggleState["Aim Assist"] = true;
                                _mainWindow.UpdateToggleUI(uiManager.T_AimAligner, true);
                            }
                        }
                    };
                }, tooltip: "Sempre rastreia os alvos sem precisar segurar uma tecla. Quando desligado, você deve segurar a tecla de mira.")
                .AddToggle("Sticky Aim", "Mira Pegajosa", t => uiManager.T_StickyAim = t,
                    tooltip: "Trava em um alvo até que ele saia do alcance, em vez de alternar entre alvos.")
                .AddSlider("Sticky Aim Threshold", "Limite da Mira Pegajosa", "Pixels", 1, 1, 0, 100, s =>
                {
                    uiManager.S_StickyAimThreshold = s;
                    // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["Sticky Aim"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "O quão longe um alvo deve se mover antes de alternar para um novo alvo. Maior = permanece travado por mais tempo.")
                .AddKeyChanger("Aim Keybind", "Tecla de Mira", k => uiManager.C_Keybind = k,
                    tooltip: "A tecla que você segura para ativar a assistência de mira.")
                .AddKeyChanger("Second Aim Keybind", "Segunda Tecla de Mira", tooltip: "Uma tecla alternativa para ativar a assistência de mira.")
                .AddSeparator();
        }

        private void LoadAimConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, AimConfig);

            builder
                .AddTitle("Configurações de Mira", true, t =>
                {
                    uiManager.AT_AimConfig = t;
                    t.Minimize.Click += (s, e) =>
                    {
                        TogglePanel("Aim Config", AimConfigPanel);
                        _mainWindow?.UpdateAimConfigSliderVisibility();
                    };
                }, stateKey: "Aim Config")
                .AddDropdown("Mouse Movement Method", "Método de Movimento do Mouse", d =>
                {
                    uiManager.D_MouseMovementMethod = d;
                    d.DropdownBox.SelectedIndex = -1;  // Prevent auto-selection

                    // Add options
                    _mainWindow.AddDropdownItem(d, "Mouse Event");
                    _mainWindow.AddDropdownItem(d, "SendInput");
                    uiManager.DDI_LGHUB = _mainWindow.AddDropdownItem(d, "LG HUB");
                    uiManager.DDI_RazerSynapse = _mainWindow.AddDropdownItem(d, "Razer Synapse (Requer Periférico Razer)");
                    uiManager.DDI_ddxoft = _mainWindow.AddDropdownItem(d, "Driver de Entrada Virtual ddxoft");

                    // Setup handlers
                    uiManager.DDI_LGHUB.Selected += async (s, e) =>
                    {
                        if (!new LGHubMain().Load())
                            await ResetToMouseEvent();
                    };

                    uiManager.DDI_RazerSynapse.Selected += async (s, e) =>
                    {
                        if (!await RZMouse.Load())
                            await ResetToMouseEvent();
                    };

                    uiManager.DDI_ddxoft.Selected += async (s, e) =>
                    {
                        if (!await DdxoftMain.Load())
                            await ResetToMouseEvent();
                    };
                }, tooltip: "Como os movimentos do mouse são enviados. Tente opções diferentes se a assistência não estiver funcionando.")
                .AddDropdown("Movement Path", "Caminho de Movimento", d =>
                {
                    d.DropdownBox.SelectedIndex = 0;
                    uiManager.D_MovementPath = d;
                    _mainWindow.AddDropdownItem(d, "Bézier Cúbica");
                    _mainWindow.AddDropdownItem(d, "Exponencial");
                    _mainWindow.AddDropdownItem(d, "Linear");
                    _mainWindow.AddDropdownItem(d, "Adaptativo");
                    _mainWindow.AddDropdownItem(d, "Ruído Perlin");
                }, tooltip: "O estilo de curva usado ao se mover até o alvo. Afeta o quão natural o movimento parece.")
                .AddDropdown("Detection Area Type", "Tipo de Área de Detecção", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;
                    uiManager.D_DetectionAreaType = d;
                    uiManager.DDI_ClosestToCenterScreen = _mainWindow.AddDropdownItem(d, "Mais Próximo ao Centro da Tela");
                    _mainWindow.AddDropdownItem(d, "Mais Próximo ao Mouse");

                    uiManager.DDI_ClosestToCenterScreen.Selected += async (s, e) =>
                    {
                        await Task.Delay(100);
                        MainWindow.FOVWindow.FOVStrictEnclosure.Margin = new Thickness(
                            Convert.ToInt16((WinAPICaller.ScreenWidth / 2) / WinAPICaller.scalingFactorX) - 320,
                            Convert.ToInt16((WinAPICaller.ScreenHeight / 2) / WinAPICaller.scalingFactorY) - 320,
                            0, 0);
                    };
                }, tooltip: "Como os alvos são priorizados. Centro da tela é o melhor para a maioria dos jogos.")
                .AddDropdown("Aiming Boundaries Alignment", "Alinhamento do Ponto de Mira", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;
                    uiManager.D_AimingBoundariesAlignment = d;
                    _mainWindow.AddDropdownItem(d, "Centro");
                    _mainWindow.AddDropdownItem(d, "Topo / Cabeça");
                    _mainWindow.AddDropdownItem(d, "Base / Corpo");
                }, tooltip: "Onde mirar na caixa de detecção do alvo. Centro é geralmente o melhor.");

            // Add sliders with validation
            AddConfigSliders(builder, uiManager);
            builder.AddSeparator();
        }

        private void AddConfigSliders(SectionBuilder builder, UI uiManager)
        {
            builder
                .AddSlider("Mouse Sensitivity (+/-)", "Sensibilidade do Mouse (+/-)", "Sensibilidade", 0.01, 0.01, 0.01, 1, s =>
                {
                    uiManager.S_MouseSensitivity = s;
                    s.Slider.PreviewMouseLeftButtonUp += (sender, e) =>
                    {
                        var value = s.Slider.Value;
                        if (value >= 0.98)
                            LogManager.Log(LogManager.LogLevel.Warning,
                                "A Sensibilidade do Mouse configurada pode fazer o assistente não conseguir mirar, por favor diminua se tiver esse problema", true);
                        else if (value <= 0.1)
                            LogManager.Log(LogManager.LogLevel.Warning,
                                "A Sensibilidade do Mouse configurada pode deixar a mira instável, por favor aumente se tiver esse problema", true);
                    };
                }, tooltip: "Velocidade do movimento da mira. Menor = mais rápida e responsiva, maior = mais lenta e suave.")
                .AddSlider("Mouse Jitter", "Tremor de Mira (Jitter)", "Jitter", 1, 1, 0, 15, s => uiManager.S_MouseJitter = s,
                    tooltip: "Adiciona pequenos movimentos aleatórios para fazer a mira parecer mais humana.")
                .AddToggle("Y Axis Percentage Adjustment", "Ajuste Percentual no Eixo Y", t => uiManager.T_YAxisPercentageAdjustment = t,
                    tooltip: "Ativa o ajuste de mira vertical por porcentagem da altura do alvo.")
                .AddToggle("X Axis Percentage Adjustment", "Ajuste Percentual no Eixo X", t => uiManager.T_XAxisPercentageAdjustment = t,
                    tooltip: "Ativa o ajuste de mira horizontal por porcentagem da largura do alvo.")
                .AddSlider("Y Offset (Up/Down)", "Deslocamento Y (Cima/Baixo)", "Deslocamento", 1, 1, -150, 150, s =>
                {
                    uiManager.S_YOffset = s;
                    // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["Y Axis Percentage Adjustment"]
                        ? Visibility.Collapsed : Visibility.Visible;
                }, tooltip: "Move o ponto de mira para cima (negativo) ou para baixo (positivo) em pixels.")
                .AddSlider("Y Offset (%)", "Deslocamento Y (%)", "Porcentagem", 1, 1, 0, 100, s =>
                {
                    uiManager.S_YOffsetPercent = s;
                    // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["Y Axis Percentage Adjustment"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Move o ponto de mira para cima ou para baixo como uma porcentagem da altura do alvo.")
                .AddSlider("X Offset (Left/Right)", "Deslocamento X (Esquerda/Direita)", "Deslocamento", 1, 1, -150, 150, s =>
                {
                    uiManager.S_XOffset = s;
                    // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["X Axis Percentage Adjustment"]
                        ? Visibility.Collapsed : Visibility.Visible;
                }, tooltip: "Move o ponto de mira para a esquerda (negativo) ou direita (positivo) em pixels.")
                .AddSlider("X Offset (%)", "Deslocamento X (%)", "Porcentagem", 1, 1, 0, 100, s =>
                {
                    uiManager.S_XOffsetPercent = s;
                    // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["X Axis Percentage Adjustment"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Move o ponto de mira para a esquerda ou direita como uma porcentagem da largura do alvo.");
        }

        private void LoadPredictions()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, Predictions);

            builder
                .AddTitle("Predições (Antecipação)", true, t =>
                {
                    uiManager.AT_Predictions = t;
                    t.Minimize.Click += (s, e) =>
                    {
                        TogglePanel("Predictions", PredictionsPanel);
                        _mainWindow?.UpdatePredictionSliderVisibility();
                    };
                }, stateKey: "Predictions")
                .AddToggle("Predictions", "Predições", t => uiManager.T_Predictions = t,
                    tooltip: "Preveja onde um alvo em movimento estará. Ajuda a rastrear alvos rápidos.")
                .AddDropdown("Prediction Method", "Método de Predição", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;
                    uiManager.D_PredictionMethod = d;
                    _mainWindow.AddDropdownItem(d, "Filtro de Kalman");
                    _mainWindow.AddDropdownItem(d, "Predição do Shall0e");
                    _mainWindow.AddDropdownItem(d, "Predição EMA do wisethef0x");

                    // Update slider visibility when prediction method changes
                    d.DropdownBox.SelectionChanged += (s, e) => _mainWindow?.UpdatePredictionSliderVisibility();
                }, tooltip: "O algoritmo usado para prever o movimento do alvo. Tente diferentes opções para ver qual funciona melhor.")
                .AddSlider("Kalman Lead Time", "Tempo de Avanço (Kalman)", "Segundos", 0.01, 0.01, 0.02, 0.30, s =>
                {
                    uiManager.S_KalmanLeadTime = s;
                    // Start collapsed - visibility will be set by LoadDropdownStates
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "O quão à frente prever a posição do alvo. Maior = mais predição, pode passar do alvo.")
                .AddSlider("WiseTheFox Lead Time", "Tempo de Avanço (WiseTheFox)", "Segundos", 0.01, 0.01, 0.02, 0.30, s =>
                {
                    uiManager.S_WiseTheFoxLeadTime = s;
                    // Start collapsed - visibility will be set by LoadDropdownStates
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "O quão à frente prever a posição do alvo. Maior = mais predição, pode passar do alvo.")
                .AddSlider("Shalloe Lead Multiplier", "Multiplicador de Avanço (Shalloe)", "Quadros", 0.5, 0.5, 1, 10, s =>
                {
                    uiManager.S_ShalloeLeadMultiplier = s;
                    // Start collapsed - visibility will be set by LoadDropdownStates
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "Quantos quadros à frente prever. Maior = mais predição, pode passar do alvo.")
                .AddToggle("EMA Smoothening", "Suavização EMA", t => uiManager.T_EMASmoothing = t,
                    tooltip: "Suaviza os movimentos de mira para reduzir tremores e estabilizar o rastreamento.")
                .AddSlider("EMA Smoothening", "Intensidade de Suavização EMA", "Suavização", 0.01, 0.01, 0.01, 1, s =>
                {
                    uiManager.S_EMASmoothing = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        if (Dictionary.toggleState["EMA Smoothening"])
                        {
                            MouseManager.smoothingFactor = s.Slider.Value;
                        }
                    };
                }, tooltip: "Quantidade de suavização a aplicar. Menor = mais suave porém mais lento, maior = mais rápido porém mais tremido.")
                .AddSeparator();
        }

        private void LoadTriggerBot()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, TriggerBot);

            builder
                .AddTitle("Disparo Automático (Triggerbot)", true, t =>
                {
                    uiManager.AT_TriggerBot = t;
                    t.Minimize.Click += (s, e) => TogglePanel("Auto Trigger", TriggerBotPanel);
                }, stateKey: "Auto Trigger")
                .AddToggle("Auto Trigger", "Disparo Automático", t => uiManager.T_AutoTrigger = t,
                    tooltip: "Clica automaticamente quando um alvo é detectado na área da sua mira.")
                .AddToggle("Cursor Check", "Verificação de Mira", t => uiManager.T_CursorCheck = t,
                    tooltip: "Apenas dispara quando o cursor estiver diretamente sobre o alvo. Mais preciso, mas pode perder alguns tiros.")
                .AddToggle("Spray Mode", "Modo Spray (Disparo Contínuo)", t => uiManager.T_SprayMode = t,
                    tooltip: "Segura o disparo em vez de dar cliques únicos. Perfeito para armas automáticas.")
                //.AddToggle("Only When Held", t => uiManager.T_OnlyWhenHeld = t)
                .AddSlider("Auto Trigger Delay", "Atraso do Disparo Automático", "Segundos", 0.01, 0.1, 0.01, 1, s => uiManager.S_AutoTriggerDelay = s,
                    tooltip: "Tempo de espera antes de disparar após detectar um alvo. Evita disparos acidentais.")
                .AddSeparator();
        }

        private void LoadFOVConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, FOVConfig);

            builder
                .AddTitle("Configurações de FOV", true, t =>
                {
                    uiManager.AT_FOV = t;
                    t.Minimize.Click += (s, e) => TogglePanel("FOV Config", FOVConfigPanel);
                }, stateKey: "FOV Config")
                .AddToggle("FOV", "Exibir Círculo de FOV", t => uiManager.T_FOV = t,
                    tooltip: "Exibe um círculo na tela indicando a área de detecção de alvos.")
                .AddToggle("Dynamic FOV", "FOV Dinâmico", t => uiManager.T_DynamicFOV = t,
                    tooltip: "Altera o tamanho do FOV ao segurar uma tecla. Útil ao usar miras de precisão/zoom.")
                .AddToggle("Third Person Support", "Suporte para Terceira Pessoa", t => uiManager.T_ThirdPersonSupport = t,
                    tooltip: "Ajusta a posição do FOV para jogos com câmera em terceira pessoa.")
                .AddKeyChanger("Dynamic FOV Keybind", "Tecla do FOV Dinâmico", k => uiManager.C_DynamicFOV = k,
                    tooltip: "A tecla a ser segurada para alternar para o tamanho do FOV dinâmico.")
                .AddDropdown("FOV Style", "Estilo do FOV", d =>
                {
                    uiManager.D_FOVSTYLE = d;

                    var circleItem = _mainWindow.AddDropdownItem(d, "Círculo");
                    var rectangleItem = _mainWindow.AddDropdownItem(d, "Retângulo");

                    circleItem.Selected += (s, e) =>
                    {
                        MainWindow.FOVWindow.Circle.Visibility = Visibility.Visible;
                        MainWindow.FOVWindow.RectangleShape.Visibility = Visibility.Collapsed;
                    };

                    rectangleItem.Selected += (s, e) =>
                    {
                        MainWindow.FOVWindow.Circle.Visibility = Visibility.Collapsed;
                        MainWindow.FOVWindow.RectangleShape.Visibility = Visibility.Visible;
                    };
                }, tooltip: "Formato da sobreposição do FOV. Círculo é o mais comum.")
                .AddColorChanger("FOV Color", "Cor do FOV", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (fovColorPickerInstance != null && fovColorPickerInstance.IsVisible)
                        {
                            fovColorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        fovColorPickerInstance = new UISections.ColorPicker(initialColor, "Cor do FOV");

                        fovColorPickerInstance.ColorChanged += (color) =>
                        {
                            // Update the color square
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            // Save to dictionary for persistence
                            Dictionary.colorState["FOV Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                            PropertyChanger.PostColor(color);
                        };

                        fovColorPickerInstance.Closed += (sender, args) =>
                        {
                            fovColorPickerInstance = null;
                        };

                        fovColorPickerInstance.Show();
                    };
                })
                .AddSlider("FOV Size", "Tamanho do FOV", "Tamanho", 1, 1, 10, 640, s =>
                {
                    uiManager.S_FOVSize = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        _mainWindow.ActualFOV = s.Slider.Value;
                        PropertyChanger.PostNewFOVSize(_mainWindow.ActualFOV);
                    };
                }, tooltip: "Tamanho da área de detecção. Menor = mais preciso, maior = cobertura mais ampla.")
                .AddSlider("Dynamic FOV Size", "Tamanho do FOV Dinâmico", "Tamanho", 1, 1, 10, 640, s =>
                {
                    uiManager.S_DynamicFOVSize = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        if (Dictionary.toggleState["Dynamic FOV"])
                            PropertyChanger.PostNewFOVSize(s.Slider.Value);
                    };
                }, tooltip: "Tamanho do FOV ao segurar a tecla do FOV dinâmico. Geralmente menor para miras scoped.")
                .AddSeparator();
        }

        private void LoadESPConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ESPConfig);

            builder
                .AddTitle("Configurações de ESP (Visualização)", true, t =>
                {
                    uiManager.AT_DetectedPlayer = t;
                    t.Minimize.Click += (s, e) => TogglePanel("ESP Config", ESPConfigPanel);
                }, stateKey: "ESP Config")
                .AddToggle("Show Detected Player", "Mostrar Caixa do Jogador", t => uiManager.T_ShowDetectedPlayer = t,
                    tooltip: "Desenha uma caixa ao redor dos alvos detectados na tela.")
                .AddToggle("Show AI Confidence", "Mostrar Confiança da IA", t => uiManager.T_ShowAIConfidence = t,
                    tooltip: "Exibe o quanto a IA está confiante sobre cada detecção (0-100%).")
                .AddToggle("Show Tracers", "Mostrar Linhas de Rastreamento (Tracers)", t => uiManager.T_ShowTracers = t,
                    tooltip: "Desenha linhas da borda da tela até os alvos detectados.");

            builder.AddDropdown("Tracer Position", "Posição dos Tracers", d =>
            {
                d.DropdownBox.SelectedIndex = 0;
                uiManager.D_TracerPosition = d;
                _mainWindow.AddDropdownItem(d, "Topo");
                _mainWindow.AddDropdownItem(d, "Meio");
                _mainWindow.AddDropdownItem(d, "Base");
                d.DropdownBox.SelectionChanged += (s, e) =>
                {
                    if (Dictionary.toggleState["Show Detected Player"])
                    {
                        // simulate a click to turn it off
                        uiManager.T_ShowDetectedPlayer.Reader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        // simulate a click to turn it back on
                        uiManager.T_ShowDetectedPlayer.Reader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    else
                    {
                        if (Dictionary.DetectedPlayerOverlay != null)
                        {
                            Dictionary.DetectedPlayerOverlay.ForceReposition();
                        }
                    }
                };
            }, tooltip: "De onde as linhas de rastreamento começam na tela.");

            builder
                .AddColorChanger("Detected Player Color", "Cor da Caixa do Jogador", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "Cor da Caixa do Jogador");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            // Update the color square
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            // Save to dictionary for persistence
                            Dictionary.colorState["Detected Player Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                            PropertyChanger.PostDPColor(color);
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddSlider("AI Confidence Font Size", "Tamanho da Fonte da Confiança", "Tamanho", 1, 1, 1, 30, s =>
                {
                    uiManager.S_DPFontSize = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPFontSize((int)s.Slider.Value);
                }, tooltip: "Tamanho do texto para a exibição da porcentagem de confiança.")
                .AddSlider("Corner Radius", "Arredondamento dos Cantos", "Raio", 1, 1, 0, 100, s =>
                {
                    uiManager.S_DPCornerRadius = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPWCornerRadius((int)s.Slider.Value);
                }, tooltip: "Quão arredondados são os cantos da caixa de detecção. 0 = cantos vivos.")
                .AddSlider("Border Thickness", "Espessura da Borda", "Espessura", 0.1, 1, 0.1, 10, s =>
                {
                    uiManager.S_DPBorderThickness = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPWBorderThickness(s.Slider.Value);
                }, tooltip: "Espessura da linha da borda da caixa de detecção.")
                .AddSlider("Opacity", "Opacidade da Caixa", "Opacidade", 0.1, 0.1, 0, 1, s =>
                {
                    uiManager.S_DPOpacity = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPWOpacity(s.Slider.Value);
                }, tooltip: "O quão transparente a caixa de detecção é. 0 = invisível, 1 = sólida.");
        }

        #endregion

        #region Helper Methods

        private void OnImageSizeChanged(int imageSize)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_mainWindow?.uiManager.S_FOVSize != null && _mainWindow?.uiManager.S_DynamicFOVSize != null)
                {
                    UpdateFovSizeSlider(_mainWindow.uiManager.S_FOVSize, imageSize);
                    UpdateFovSizeSlider(_mainWindow.uiManager.S_DynamicFOVSize, imageSize);
                }
            });
        }
        private void UpdateFovSizeSlider(ASlider slider, int imageSize = 640)
        {
            if (slider.Slider == null) return;
            if (imageSize < slider.Slider.Value)
            {
                slider.Slider.Value = imageSize;
            }
            slider.Slider.Maximum = imageSize;
        }

        private async Task ResetToMouseEvent()
        {
            await Task.Delay(500);
            _mainWindow!.uiManager.D_MouseMovementMethod!.DropdownBox.SelectedIndex = 0;
        }

        private void HandleColorChange(AColorChanger colorChanger, string settingKey, Action<Color> updateAction)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                colorChanger.ColorChangingBorder.Background = new SolidColorBrush(color);
                Dictionary.colorState[settingKey] = color.ToString();
                updateAction(color);
            }
        }

        public void Dispose()
        {
            // Save minimize states before disposing
            SaveMinimizeStatesToGlobal();
        }

        #endregion

        #region Section Builder

        private class SectionBuilder
        {
            private readonly AimMenuControl _parent;
            private readonly StackPanel _panel;

            public SectionBuilder(AimMenuControl parent, StackPanel panel)
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

            public SectionBuilder AddColorChanger(string title, string displayTitle, Action<AColorChanger>? configure = null)
            {
                var colorChanger = _parent.CreateColorChanger(title, displayTitle);
                configure?.Invoke(colorChanger);
                _panel.Children.Add(colorChanger);
                return this;
            }

            public SectionBuilder AddButton(string title, Action<APButton>? configure = null, string? tooltip = null)
            {
                var button = new APButton(title, tooltip);
                configure?.Invoke(button);
                _panel.Children.Add(button);
                return this;
            }

            public SectionBuilder AddFileLocator(string title, Action<AFileLocator>? configure = null,
                string filter = "All files (*.*)|*.*", string dlExtension = "")
            {
                var fileLocator = new AFileLocator(title, title, filter, dlExtension);
                configure?.Invoke(fileLocator);
                _panel.Children.Add(fileLocator);
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

        private AColorChanger CreateColorChanger(string title, string displayTitle)
        {
            var colorChanger = new AColorChanger(displayTitle);
            colorChanger.ColorChangingBorder.Background =
                (Brush)new BrushConverter().ConvertFromString(Dictionary.colorState[title]);
            return colorChanger;
        }

        #endregion
    }
}