using System.Globalization;
using VSAI.AILogic;

namespace VSAI.Class
{
    internal static class AimSettings
    {
        public static AIEngine SelectedEngine
        {
            get
            {
                var value = GetDropdown("AI Engine");
                if (Enum.TryParse<AIEngine>(value, true, out var result))
                {
                    return result;
                }
                return AIEngine.DirectML;
            }
        }

        public static int ImageSize
        {
            get => int.Parse(GetDropdown("Image Size"), CultureInfo.InvariantCulture);
            set => SetDropdown("Image Size", value.ToString(CultureInfo.InvariantCulture));
        }

        public static string ScreenCaptureMethod
        {
            get => GetDropdown("Screen Capture Method");
            set => SetDropdown("Screen Capture Method", value);
        }

        public static string DetectionAreaType => GetDropdown("Detection Area Type");
        public static string TargetClass => GetDropdown("Target Class");
        public static string PredictionMethod => GetDropdown("Prediction Method");
        public static string AimingBoundariesAlignment => GetDropdown("Aiming Boundaries Alignment");
        public static string TracerPosition => GetDropdown("Tracer Position");
        public static string MovementPath => GetDropdown("Movement Path");
        public static string MouseMovementMethod => GetDropdown("Mouse Movement Method");

        public static double FovSize => GetSlider("FOV Size");
        public static float MinimumConfidence => (float)(GetSlider("AI Minimum Confidence") / 100.0);
        public static double StickyAimThreshold => GetSlider("Sticky Aim Threshold");
        public static double OverlayOpacity => GetSlider("Opacity");
        public static double YOffset => GetSlider("Y Offset (Up/Down)");
        public static double XOffset => GetSlider("X Offset (Left/Right)");
        public static double YOffsetPercent => GetSlider("Y Offset (%)");
        public static double XOffsetPercent => GetSlider("X Offset (%)");
        public static int AutoTriggerDelayMilliseconds => (int)(GetSlider("Auto Trigger Delay") * 1000);
        public static int MouseJitter => (int)GetSlider("Mouse Jitter");
        public static double MouseSensitivity => GetSlider("Mouse Sensitivity (+/-)");
        public static double KalmanLeadTime => GetSlider("Kalman Lead Time");
        public static double WiseTheFoxLeadTime => GetSlider("WiseTheFox Lead Time");
        public static double ShalloeLeadMultiplier => GetSlider("Shalloe Lead Multiplier");
        public static int AiFpsLimit => Math.Max(0, (int)Math.Round(GetSlider("AI FPS Limit")));

        public static bool AutoTrigger => GetToggle("Auto Trigger");
        public static bool ConstantAiTracking => GetToggle("Constant AI Tracking");
        public static bool SprayMode => GetToggle("Spray Mode");
        public static bool CursorCheck => GetToggle("Cursor Check");
        public static bool AimAssist => GetToggle("Aim Assist");
        public static bool ShowDetectedPlayer => GetToggle("Show Detected Player");
        public static bool ShowFov => GetToggle("FOV");
        public static bool ShowAiConfidence => GetToggle("Show AI Confidence");
        public static bool ShowTracers => GetToggle("Show Tracers");
        public static bool UseXAxisPercentageAdjustment => GetToggle("X Axis Percentage Adjustment");
        public static bool UseYAxisPercentageAdjustment => GetToggle("Y Axis Percentage Adjustment");
        public static bool Predictions => GetToggle("Predictions");
        public static bool StickyAim => GetToggle("Sticky Aim");
        public static bool CollectDataWhilePlaying => GetToggle("Collect Data While Playing");
        public static bool AutoLabelData => GetToggle("Auto Label Data");
        public static bool ThirdPersonSupport => GetToggle("Third Person Support");

        public static double GetSlider(string key) =>
            Convert.ToDouble(Dictionary.sliderSettings[key], CultureInfo.InvariantCulture);

        public static bool GetToggle(string key) =>
            Convert.ToBoolean(Dictionary.toggleState[key], CultureInfo.InvariantCulture);

        public static string GetDropdown(string key)
        {
            var value = Convert.ToString(Dictionary.dropdownState[key], CultureInfo.InvariantCulture) ?? string.Empty;

            // Map PT-BR translations back to English for internal business logic
            switch (value)
            {
                // Screen Capture Method
                case "DirectX (Rápido)": return "DirectX";
                case "GDI+ (Compatibilidade)": return "GDI+";

                // Detection Area Type
                case "Mais Próximo ao Centro da Tela": return "Closest to Center Screen";
                case "Mais Próximo ao Mouse": return "Closest to Mouse";

                // Aiming Boundaries Alignment
                case "Centro": return "Center";
                case "Topo / Cabeça": return "Top";
                case "Base / Corpo": return "Bottom";

                // Prediction Method
                case "Filtro de Kalman": return "Kalman Filter";
                case "Predição do Shall0e": return "Shall0e's Prediction";
                case "Predição EMA do wisethef0x": return "wisethef0x's EMA Prediction";

                // Mouse Movement Method
                case "Razer Synapse (Requer Periférico Razer)": return "Razer Synapse (Require Razer Peripheral)";
                case "Driver de Entrada Virtual ddxoft": return "ddxoft Virtual Input Driver";

                // Movement Path
                case "Bézier Cúbica": return "Cubic Bezier";
                case "Exponencial": return "Exponential";
                case "Linear": return "Linear";
                case "Adaptativo": return "Adaptive";
                case "Ruído Perlin": return "Perlin Noise";

                // Tracer Position
                case "Topo": return "Top";
                case "Meio": return "Middle";
                case "Base": return "Bottom";
            }
            return value;
        }

        private static void SetDropdown(string key, string value)
        {
            Dictionary.dropdownState[key] = value;
        }
    }
}
