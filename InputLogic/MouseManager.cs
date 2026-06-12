using VSAI.Class;
using VSAI.MouseMovementLibraries.GHubSupport;
using Class;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using MouseMovementLibraries.SendInputSupport;
using System.Drawing;
using System.Runtime.InteropServices;

namespace InputLogic
{
    internal class MouseManager
    {
        private static double ScreenWidth => DisplayManager.ScreenWidth;
        private static double ScreenHeight => DisplayManager.ScreenHeight;

        private static DateTime LastClickTime = DateTime.MinValue;
        private static bool isSpraying = false;

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private static double previousX = 0;
        private static double previousY = 0;
        private static double _prevErrorX = 0;
        private static double _prevErrorY = 0;
        private static double _integralX = 0;
        private static double _integralY = 0;
        private static bool _isLockedX = false;
        private static bool _isLockedY = false;
        private static double _captureMovedX = 0;
        private static double _captureMovedY = 0;
        public static double smoothingFactor = 0.5;
        public static bool IsEMASmoothingEnabled = false;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private static Random MouseRandom = new();

        private static double _targetX = 0;
        private static double _targetY = 0;
        private static double _movedX = 0;
        private static double _movedY = 0;
        private static DateTime _lastAiUpdate = DateTime.UtcNow;
        private static readonly object _mouseLock = new object();
        private static Thread? _asyncMouseThread;
        private static bool _isMouseThreadRunning = false;

        static MouseManager()
        {
            _isMouseThreadRunning = true;
            _asyncMouseThread = new Thread(AsyncMouseLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            _asyncMouseThread.Start();
        }

        private static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor) => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        public static void RecordCaptureState()
        {
            lock (_mouseLock)
            {
                _captureMovedX = _movedX;
                _captureMovedY = _movedY;
            }
        }

        // Cleanup
        private static (Action down, Action up) GetMouseActions()
        {
            string mouseMovementMethod = AimSettings.MouseMovementMethod;
            Action mouseDownAction;
            Action mouseUpAction;

            switch (mouseMovementMethod)
            {
                case "SendInput":
                    mouseDownAction = () => SendInputMouse.SendMouseCommand(MOUSEEVENTF_LEFTDOWN);
                    mouseUpAction = () => SendInputMouse.SendMouseCommand(MOUSEEVENTF_LEFTUP);
                    break;
                case "LG HUB":
                    mouseDownAction = () => LGMouse.Move(1, 0, 0, 0);
                    mouseUpAction = () => LGMouse.Move(0, 0, 0, 0);
                    break;
                case "Razer Synapse (Require Razer Peripheral)":
                    mouseDownAction = () => RZMouse.mouse_click(1);
                    mouseUpAction = () => RZMouse.mouse_click(0);
                    break;
                case "ddxoft Virtual Input Driver":
                    mouseDownAction = () => DdxoftMain.ddxoftInstance.btn!(1);
                    mouseUpAction = () => DdxoftMain.ddxoftInstance.btn(2);
                    break;
                default:
                    mouseDownAction = () => mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouseUpAction = () => mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
            }

            return (mouseDownAction, mouseUpAction);
        }

        public static async Task DoTriggerClick(RectangleF? detectionBox = null)
        {
            // there was a toggle for this, but i realized if it was off, it would never stop spraying. - T
            if (!(InputBindingManager.IsHoldingBinding("Aim Keybind") || InputBindingManager.IsHoldingBinding("Second Aim Keybind")))
            {
                ResetSprayState();
                return;
            }


            if (AimSettings.SprayMode)
            {
                if (AimSettings.CursorCheck)
                {
                    Point mousePos = WinAPICaller.GetCursorPosition();

                    if (detectionBox.HasValue && !detectionBox.Value.Contains(mousePos.X, mousePos.Y))
                    {
                        if (isSpraying) ReleaseMouseButton();
                        return;
                    }
                }

                if (!isSpraying) HoldMouseButton();
                return;
            }

            // Single click logic if spray mode off
            int timeSinceLastClick = (int)(DateTime.UtcNow - LastClickTime).TotalMilliseconds;
            int triggerDelayMilliseconds = AimSettings.AutoTriggerDelayMilliseconds;
            const int clickDelayMilliseconds = 20;

            if (timeSinceLastClick < triggerDelayMilliseconds && LastClickTime != DateTime.MinValue)
            {
                return;
            }

            var (mouseDown, mouseUp) = GetMouseActions();

            mouseDown.Invoke();
            await Task.Delay(clickDelayMilliseconds);
            mouseUp.Invoke();

            LastClickTime = DateTime.UtcNow;
        }

        #region Spray Mode Methods
        public static void HoldMouseButton()
        {
            if (isSpraying) return;

            var (mouseDown, _) = GetMouseActions();
            mouseDown.Invoke();
            isSpraying = true;
        }

        public static void ReleaseMouseButton()
        {
            if (!isSpraying) return;

            var (_, mouseUp) = GetMouseActions();
            mouseUp.Invoke();
            isSpraying = false;
        }

        public static void ResetSprayState()
        {
            if (isSpraying)
            {
                ReleaseMouseButton();
            }
        }
        #endregion

        private static void AsyncMouseLoop()
        {
            timeBeginPeriod(1);

            while (_isMouseThreadRunning)
            {
                bool aimActive = AimSettings.AimAssist && 
                    (AimSettings.ConstantAiTracking ||
                     InputBindingManager.IsHoldingBinding("Aim Keybind") ||
                     InputBindingManager.IsHoldingBinding("Second Aim Keybind"));

                if (!aimActive)
                {
                    lock (_mouseLock)
                    {
                        _targetX = 0;
                        _targetY = 0;
                        _movedX = 0;
                        _movedY = 0;
                    }
                    _prevErrorX = 0;
                    _prevErrorY = 0;
                    _integralX = 0;
                    _integralY = 0;
                    _isLockedX = false;
                    _isLockedY = false;
                    Thread.Sleep(5);
                    continue;
                }

                double mx = 0, my = 0;
                lock (_mouseLock)
                {
                    mx = _targetX;
                    my = _targetY;
                }

                // Amortecimento se o alvo for muito antigo (evita mira travada/congelada)
                double msSinceLastUpdate = (DateTime.UtcNow - _lastAiUpdate).TotalMilliseconds;
                if (msSinceLastUpdate > 120)
                {
                    double decay = Math.Max(0.0, 1.0 - (msSinceLastUpdate - 120) / 80.0);
                    mx *= decay;
                    my *= decay;
                }

                if (Math.Abs(mx) >= 0.5 || Math.Abs(my) >= 0.5)
                {
                    // Fator de passo em 0.7 distribui a força ao longo de 2-3ms, eliminando tremidas discretas na tela
                    double stepFactor = 0.7;

                    int ix = (int)Math.Round(mx * stepFactor);
                    int iy = (int)Math.Round(my * stepFactor);

                    if (ix == 0 && Math.Abs(mx) >= 0.5)
                    {
                        ix = Math.Sign(mx);
                    }
                    if (iy == 0 && Math.Abs(my) >= 0.5)
                    {
                        iy = Math.Sign(my);
                    }

                    // Limita pulos bruscos do cursor
                    ix = Math.Clamp(ix, -80, 80);
                    iy = Math.Clamp(iy, -80, 80);

                    if (ix != 0 || iy != 0)
                    {
                        PerformRawMove(ix, iy);

                        lock (_mouseLock)
                        {
                            _targetX -= ix;
                            _targetY -= iy;
                            _movedX += ix;
                            _movedY += iy;
                        }
                    }
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            timeEndPeriod(1);
        }

        private static void PerformRawMove(int x, int y)
        {
            switch (AimSettings.MouseMovementMethod)
            {
                case "SendInput":
                    SendInputMouse.SendMouseCommand(MOUSEEVENTF_MOVE, x, y);
                    break;

                case "LG HUB":
                    LGMouse.Move(0, x, y, 0);
                    break;

                case "Razer Synapse (Require Razer Peripheral)":
                    RZMouse.mouse_move(x, y, true);
                    break;

                case "ddxoft Virtual Input Driver":
                    DdxoftMain.ddxoftInstance.movR!(x, y);
                    break;

                default:
                    mouse_event(MOUSEEVENTF_MOVE, (uint)x, (uint)y, 0, 0);
                    break;
            }
        }

        public static void MoveCrosshair(int detectedX, int detectedY, bool disableY = false)
        {
            int halfScreenWidth = (int)ScreenWidth / 2;
            int halfScreenHeight = (int)ScreenHeight / 2;

            int targetX = detectedX - halfScreenWidth;
            int targetY = disableY ? 0 : (detectedY - halfScreenHeight);

            int MouseJitter = AimSettings.MouseJitter;
            int jitterX = MouseRandom.Next(-MouseJitter, MouseJitter);
            int jitterY = MouseRandom.Next(-MouseJitter, MouseJitter);

            double dx = targetX;
            double dy = targetY;

            // Resetar o histórico do PID se houver uma pausa longa (início de nova sessão de mira)
            double msSinceLastUpdate = (DateTime.UtcNow - _lastAiUpdate).TotalMilliseconds;
            if (msSinceLastUpdate > 150)
            {
                _prevErrorX = dx;
                _prevErrorY = dy;
                _integralX = 0;
                _integralY = 0;
                _isLockedX = false;
                _isLockedY = false;
            }

            // --- DETECTOR DE LOCK E HISTERESE DO EIXO X ---
            double errorMagX = Math.Abs(dx);
            if (disableY)
            {
                _isLockedX = true;
            }
            else
            {
                if (errorMagX < 3.0)
                {
                    _isLockedX = true;
                }
                else if (errorMagX > 15.0)
                {
                    _isLockedX = false;
                }
            }

            // --- CONTROLADOR PID PARA O EIXO X ---
            // Zona morta pequena (0.8 px) para ignorar ruído de detecção sob lock.
            // Sem lock, mantemos 1.2 px para aproximação suave.
            double deadzoneX = _isLockedX ? 0.8 : 1.2;
            double dx_error = 0;
            if (errorMagX > deadzoneX)
            {
                dx_error = Math.Sign(dx) * (errorMagX - deadzoneX);
            }

            // Termo derivativo (D)
            double derivativeX = dx_error - _prevErrorX;
            _prevErrorX = dx_error;

            // Termo integral (I) super leve para correção de pequenos drifts residuais
            if (_isLockedX && dx_error != 0)
            {
                if (Math.Sign(dx_error) != Math.Sign(_integralX) && _integralX != 0)
                {
                    _integralX *= 0.50; // Atenua ao mudar de direção
                }
                _integralX += dx_error;
                _integralX = Math.Clamp(_integralX, -10.0, 10.0);
            }
            else
            {
                _integralX = 0;
            }

            double sens = Math.Max(0.01, AimSettings.MouseSensitivity);
            // Ganhos estáveis do eixo X:
            double KpMultiplier = _isLockedX ? 0.90 : 0.75;
            double Kp = sens * KpMultiplier;
            double Kd = Kp * 0.12;
            double Ki = _isLockedX ? (Kp * 0.10) : 0.0;

            // Aplica o PID do eixo X
            dx = Kp * dx_error + Kd * derivativeX + Ki * _integralX;


            // --- CONTROLADOR PID PARA O EIXO Y ---
            if (!disableY)
            {
                double errorMagY = Math.Abs(dy);
                if (errorMagY < 3.0)
                {
                    _isLockedY = true;
                }
                else if (errorMagY > 15.0)
                {
                    _isLockedY = false;
                }

                double deadzoneY = _isLockedY ? 0.8 : 1.2;
                double dy_error = 0;
                if (errorMagY > deadzoneY)
                {
                    dy_error = Math.Sign(dy) * (errorMagY - deadzoneY);
                }

                double derivativeY = dy_error - _prevErrorY;
                _prevErrorY = dy_error;

                if (_isLockedY && dy_error != 0)
                {
                    if (Math.Sign(dy_error) != Math.Sign(_integralY) && _integralY != 0)
                    {
                        _integralY *= 0.50;
                    }
                    _integralY += dy_error;
                    _integralY = Math.Clamp(_integralY, -10.0, 10.0);
                }
                else
                {
                    _integralY = 0;
                }

                double KpMultiplierY = _isLockedY ? 0.90 : 0.75;
                double KpY = sens * KpMultiplierY;
                double KdY = KpY * 0.12;
                double KiY = _isLockedY ? (KpY * 0.10) : 0.0;

                dy = KpY * dy_error + KdY * derivativeY + KiY * _integralY;
            }
            else
            {
                dy = 0;
                _prevErrorY = 0;
                _integralY = 0;
                _isLockedY = false;
            }

            // Aplica a suavização EMA se ativa e se NÃO estivermos sob lock
            if (AimSettings.GetToggle("EMA Smoothening") && !_isLockedX)
            {
                double alpha = AimSettings.GetSlider("EMA Smoothening");
                dx = EmaSmoothing(previousX, dx, alpha);
                if (!disableY)
                {
                    dy = EmaSmoothing(previousY, dy, alpha);
                }
            }

            // Salva coordenadas suavizadas para a próxima iteração
            previousX = dx;
            previousY = dy;

            // Adiciona jitter se houver
            dx += jitterX;
            dy += disableY ? 0 : jitterY;

            lock (_mouseLock)
            {
                _targetX = dx;
                _targetY = dy;
                _movedX = 0;
                _movedY = 0;
                _lastAiUpdate = DateTime.UtcNow;
            }

            if (!AimSettings.AutoTrigger)
            {
                ResetSprayState();
            }
        }
    }
}
