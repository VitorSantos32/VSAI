using VSAI.Class;
using Other;
using SharpGen.Runtime;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using LogLevel = Other.LogManager.LogLevel;
using WinRT;


namespace AILogic
{
    internal class CaptureManager
    {
        #region Variables
        private string _currentCaptureMethod = ""; // Track current method
        private bool _directXFailedPermanently = false; // Track if DirectX failed with unsupported error
        private bool _notificationShown = false; // Prevent spam notifications

        // Capturing
        public Bitmap? screenCaptureBitmap { get; private set; }
        public Bitmap? directXBitmap { get; private set; }
        private ID3D11Device? _dxDevice;
        private IDXGIOutputDuplication? _deskDuplication;
        private ID3D11Texture2D? _stagingTex;

        // Frame caching for DirectX and WGC
        private Bitmap? _cachedFrame;
        private Rectangle _cachedFrameBounds;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private readonly TimeSpan _frameCacheTimeout = TimeSpan.FromMilliseconds(15); // Adjust as needed
        private float[]? _cachedFloatArray;
        private Rectangle _cachedFloatBounds;
        private DateTime _lastFloatTime = DateTime.MinValue;

        // WGC (Windows Graphics Capture) variables
        private GraphicsCaptureItem? _wgcItem;
        private Direct3D11CaptureFramePool? _wgcFramePool;
        private GraphicsCaptureSession? _wgcSession;
        private ID3D11Device? _wgcD3DDevice;
        private IDirect3DDevice? _wgcWinRTDevice;
        private ID3D11Texture2D? _wgcGpuTexture;
        private ID3D11Texture2D? _wgcStagingTex;
        private readonly object _wgcLock = new();
        private bool _wgcInitialized = false;

        // Display change handling
        public readonly object _displayLock = new();
        public bool _displayChangesPending { get; set; } = false;

        // Performance tracking
        private int _consecutiveFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 5;

        // stride matching
        private bool _lastStrideMatch = true;
        private int _lastSrcStride = 0;
        private int _lastDstStride = 0;

        // WGC COM interop
        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = false,
            CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int D3D11CreateDeviceWGC(
            IntPtr pAdapter, int driverType, IntPtr software, uint flags,
            IntPtr pFeatureLevels, uint featureLevels, uint sdkVersion,
            out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        #endregion
        #region Handlers
        public CaptureManager()
        {
            // Subscribe to display changes FIRST
            DisplayManager.DisplayChanged += OnDisplayChanged;
        }

        private void OnDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {
            lock (_displayLock)
            {
                _displayChangesPending = true;
                _consecutiveFailures = 0;
                DisposeDxgiResources();
                DisposeWGCResources();
            }
            LogManager.Log(LogLevel.Info, "Display change detected. Capture resources will be reinitialized.");
        }

        public void HandlePendingDisplayChanges()
        {
            lock (_displayLock)
            {
                if (!_displayChangesPending) return;

                try
                {
                    InitializeDxgiDuplication();
                    _displayChangesPending = false;
                }
                catch (Exception ex)
                {

                }
            }
        }

        #endregion
        #region DirectX
        public void InitializeDxgiDuplication()
        {
            DisposeDxgiResources();
            try
            {
                var currentDisplay = DisplayManager.CurrentDisplay;
                if (currentDisplay == null)
                {
                    LogManager.Log(LogLevel.Error, "No current display available. DisplayManager may not be initialized.");
                    throw new InvalidOperationException("No current display available. DisplayManager may not be initialized.");
                }

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                IDXGIOutput1? targetOutput1 = null;
                IDXGIAdapter1? targetAdapter = null;
                bool foundTarget = false;

                for (uint adapterIndex = 0;
                    factory.EnumAdapters1(adapterIndex, out var adapter).Success;
                    adapterIndex++)
                {
                    LogManager.Log(LogLevel.Info, $"Checking Adapter {adapterIndex}: {adapter.Description.Description.TrimEnd('\0')}");

                    for (uint outputIndex = 0;
                        adapter.EnumOutputs(outputIndex, out var output).Success;
                        outputIndex++)
                    {
                        using (output)
                        {
                            var output1 = output.QueryInterface<IDXGIOutput1>();
                            var outputDesc = output1.Description;
                            var outputBounds = new Vortice.Mathematics.Rect(
                                outputDesc.DesktopCoordinates.Left,
                                outputDesc.DesktopCoordinates.Top,
                                outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left,
                                outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top);
                            LogManager.Log(LogLevel.Info, $"Found Output {outputIndex}: DeviceName = '{outputDesc.DeviceName.TrimEnd('\0')}', Bounds = {outputBounds}");

                            // Try different matching strategies
                            bool nameMatch = currentDisplay?.DeviceName != null && outputDesc.DeviceName.TrimEnd('\0') == currentDisplay.DeviceName.TrimEnd('\0');
                            bool boundsMatch = currentDisplay?.Bounds != null && outputBounds.Equals(currentDisplay.Bounds);

                            if (nameMatch || boundsMatch)
                            {
                                targetOutput1 = output1;
                                targetAdapter = adapter;
                                foundTarget = true;
                                break;
                            }
                            output1.Dispose();
                        }
                    }

                    if (foundTarget) break;
                }

                // Fallback to specific display index if not found
                if (!foundTarget)
                {
                    int targetIndex = currentDisplay?.Index ?? 0;
                    int currentIndex = 0;

                    for (uint adapterIndex = 0;
                        factory.EnumAdapters1(adapterIndex, out var adapter).Success;
                        adapterIndex++)
                    {
                        for (uint outputIndex = 0;
                            adapter.EnumOutputs(outputIndex, out var output).Success;
                            outputIndex++)
                        {
                            if (currentIndex == targetIndex)
                            {
                                LogManager.Log(LogLevel.Warning, $"Could not match display by name or bounds. Found a fallback index, {targetIndex}.");
                                targetOutput1 = output.QueryInterface<IDXGIOutput1>();
                                targetAdapter = adapter;
                                foundTarget = true;
                                break;
                            }
                            currentIndex++;
                            output.Dispose();
                        }

                        if (foundTarget)
                            break;
                        adapter.Dispose();
                    }
                }

                if (targetAdapter == null || targetOutput1 == null)
                {
                    LogManager.Log(LogLevel.Error, "No suitable display output found for DirectX capture.", true, 6000);
                    throw new Exception("No suitable display output found");
                }

                FeatureLevel[] featureLevels = {
                    FeatureLevel.Level_12_2, // 50 series support
                    FeatureLevel.Level_12_1,
                    FeatureLevel.Level_12_0,
                    FeatureLevel.Level_11_1,
                    FeatureLevel.Level_11_0,
                    FeatureLevel.Level_10_1,
                    FeatureLevel.Level_10_0,
                    FeatureLevel.Level_9_3,
                    FeatureLevel.Level_9_2,
                    FeatureLevel.Level_9_1
                };

                // Create D3D11 device
                var result = D3D11.D3D11CreateDevice(
                    targetAdapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.None,
                    featureLevels,
                    out _dxDevice);

                if (result.Failure || _dxDevice == null)
                {
                    result = D3D11.D3D11CreateDevice(
                      targetAdapter,
                      DriverType.Unknown,
                      DeviceCreationFlags.None,
                      null,
                      out _dxDevice);

                    if (result.Failure || _dxDevice == null)
                    {
                        LogManager.Log(LogLevel.Error, $"Failed to create D3D11 device: {result}", true, 6000);
                        throw new Exception($"Failed to create D3D11 device: {result}");
                    }
                }

                // Create desktop duplication
                _deskDuplication = targetOutput1.DuplicateOutput(_dxDevice);
                _consecutiveFailures = 0; //reset on success

                LogManager.Log(LogLevel.Info, "DirectX Desktop Duplication initialized successfully.");
            }
            catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.Unsupported || ex.HResult == unchecked((int)0x887A0004))
            {
                LogManager.Log(LogLevel.Error, $"DirectX Desktop Duplication not supported on this system: {ex.Message}", true, 6000);
                _directXFailedPermanently = true;
                DisposeDxgiResources();

                AimSettings.ScreenCaptureMethod = "GDI+";
                _currentCaptureMethod = "GDI+";

                LogManager.Log(LogLevel.Error, "DirectX Desktop Duplication not supported on this system. Switched to GDI+ capture.", true, 6000);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"Failed to initialize DirectX Desktop Duplication: {ex.Message}", true, 6000);
                DisposeDxgiResources();
                throw;
            }
        }
        #endregion

        #region WGC (Windows Graphics Capture)

        private void InitializeWGC()
        {
            try
            {
                DisposeWGCResources();

                // Create a fresh D3D11 device for WGC (separate from DXGI device)
                var result = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    null,
                    out _wgcD3DDevice);

                if (result.Failure || _wgcD3DDevice == null)
                    throw new Exception($"WGC: Failed to create D3D11 device: {result}");

                // Get DXGI device interface from D3D11 device
                using var dxgiDevice = _wgcD3DDevice.QueryInterface<IDXGIDevice>();

                // Create WinRT IDirect3DDevice from DXGI device via COM interop
                int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
                if (hr != 0 || inspectable == IntPtr.Zero)
                    throw new Exception($"WGC: CreateDirect3D11DeviceFromDXGIDevice failed: 0x{hr:X}");

                _wgcWinRTDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);

                // Get the current monitor as a GraphicsCaptureItem
                var currentDisplay = DisplayManager.CurrentDisplay;
                if (currentDisplay == null)
                    throw new InvalidOperationException("WGC: No current display available.");

                var hMonitor = NativeMethods.MonitorFromPoint(
                    new NativeMethods.POINT { x = (int)currentDisplay.Bounds.X + 1, y = (int)currentDisplay.Bounds.Y + 1 },
                    NativeMethods.MONITOR_DEFAULTTONEAREST);

                // Create capture item from monitor using native VTable invocation to prevent CsWinRT marshal errors
                Guid interopIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                IntPtr hClassName = IntPtr.Zero;
                int hrString = NativeMethods.WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", (uint)"Windows.Graphics.Capture.GraphicsCaptureItem".Length, out hClassName);
                if (hrString != 0)
                    throw new Exception($"WindowsCreateString failed: 0x{hrString:X}");

                IntPtr factoryPtr = IntPtr.Zero;
                int hrFactory = 0;
                try
                {
                    hrFactory = NativeMethods.RoGetActivationFactory(
                        hClassName,
                        ref interopIid,
                        out factoryPtr);
                }
                finally
                {
                    if (hClassName != IntPtr.Zero)
                    {
                        NativeMethods.WindowsDeleteString(hClassName);
                    }
                }

                if (hrFactory != 0 || factoryPtr == IntPtr.Zero)
                    throw new Exception($"RoGetActivationFactory failed: 0x{hrFactory:X}");

                try
                {
                    unsafe
                    {
                        IntPtr* vtable = *(IntPtr**)factoryPtr;
                        IntPtr createForMonitorPtr = vtable[4]; // Slot 4: CreateForMonitor
                        
                        var createForMonitor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ref Guid, out IntPtr, int>)createForMonitorPtr;
                        
                        Guid itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // GUID of GraphicsCaptureItem
                        IntPtr itemPointer;
                        hr = createForMonitor(factoryPtr, hMonitor, ref itemIid, out itemPointer);
                        
                        if (hr != 0 || itemPointer == IntPtr.Zero)
                            throw new Exception($"CreateForMonitor via VTable failed: 0x{hr:X}");
                            
                        _wgcItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
                    }
                }
                finally
                {
                    Marshal.Release(factoryPtr);
                }

                var itemSize = _wgcItem.Size;

                // Create frame pool with small buffer (1 frame for lowest latency)
                _wgcFramePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _wgcWinRTDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    itemSize);

                _wgcFramePool.FrameArrived += WGC_OnFrameArrived;

                _wgcSession = _wgcFramePool.CreateCaptureSession(_wgcItem);
                _wgcSession.IsCursorCaptureEnabled = false; // Don't capture cursor
                _wgcSession.StartCapture();

                _wgcInitialized = true;
                LogManager.Log(LogLevel.Info, "WGC (Windows Graphics Capture) inicializado com sucesso.");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"WGC: Falha na inicialização: {ex.Message}", true, 6000);
                DisposeWGCResources();
                throw;
            }
        }

        private static readonly float[] _byteToFloatLut = CreateByteToFloatLut();
        private static float[] CreateByteToFloatLut()
        {
            var lut = new float[256];
            for (int i = 0; i < 256; i++)
                lut[i] = i / 255f;
            return lut;
        }

        private static unsafe void ConvertBgraToFloatArrayInPlace(byte* src, int srcStride, float[] result, int w, int h, int imageSize)
        {
            int totalPixels = imageSize * imageSize;
            int rOffset = 0;
            int gOffset = totalPixels;
            int bOffset = totalPixels * 2;

            bool isThirdPerson = AimSettings.ThirdPersonSupport;
            int maskW = isThirdPerson ? w / 2 : 0;
            int maskH = isThirdPerson ? h / 2 : 0;
            int startMaskY = h - maskH;

            fixed (float* dest = result)
            {
                float* rPtr = dest + rOffset;
                float* gPtr = dest + gOffset;
                float* bPtr = dest + bOffset;

                for (int y = 0; y < h; y++)
                {
                    byte* p = src + (long)y * srcStride;
                    int rowStart = y * w;

                    if (isThirdPerson && y >= startMaskY)
                    {
                        for (int x = 0; x < maskW; x++)
                        {
                            int idx = rowStart + x;
                            bPtr[idx] = 0f;
                            gPtr[idx] = 0f;
                            rPtr[idx] = 0f;
                        }
                        for (int x = maskW; x < w; x++)
                        {
                            int idx = rowStart + x;
                            byte* px = p + (x * 4);
                            bPtr[idx] = _byteToFloatLut[px[0]];
                            gPtr[idx] = _byteToFloatLut[px[1]];
                            rPtr[idx] = _byteToFloatLut[px[2]];
                        }
                    }
                    else
                    {
                        int x = 0;
                        int widthLimit = w - 3;
                        for (; x < widthLimit; x += 4)
                        {
                            int baseIdx = rowStart + x;
                            byte* px = p + (x * 4);

                            bPtr[baseIdx] = _byteToFloatLut[px[0]];
                            gPtr[baseIdx] = _byteToFloatLut[px[1]];
                            rPtr[baseIdx] = _byteToFloatLut[px[2]];

                            bPtr[baseIdx + 1] = _byteToFloatLut[px[4]];
                            gPtr[baseIdx + 1] = _byteToFloatLut[px[5]];
                            rPtr[baseIdx + 1] = _byteToFloatLut[px[6]];

                            bPtr[baseIdx + 2] = _byteToFloatLut[px[8]];
                            gPtr[baseIdx + 2] = _byteToFloatLut[px[9]];
                            rPtr[baseIdx + 2] = _byteToFloatLut[px[10]];

                            bPtr[baseIdx + 3] = _byteToFloatLut[px[12]];
                            gPtr[baseIdx + 3] = _byteToFloatLut[px[13]];
                            rPtr[baseIdx + 3] = _byteToFloatLut[px[14]];
                        }

                        for (; x < w; x++)
                        {
                            int idx = rowStart + x;
                            byte* px = p + (x * 4);
                            bPtr[idx] = _byteToFloatLut[px[0]];
                            gPtr[idx] = _byteToFloatLut[px[1]];
                            rPtr[idx] = _byteToFloatLut[px[2]];
                        }
                    }
                }
            }
        }

        private static unsafe void ConvertBgraToFloatArrayInPlaceScaled(byte* src, int srcStride, float[] result, int srcW, int srcH, int destW, int destH, int imageSize)
        {
            int totalPixels = imageSize * imageSize;
            int rOffset = 0;
            int gOffset = totalPixels;
            int bOffset = totalPixels * 2;

            bool isThirdPerson = AimSettings.ThirdPersonSupport;
            int maskW = isThirdPerson ? destW / 2 : 0;
            int maskH = isThirdPerson ? destH / 2 : 0;
            int startMaskY = destH - maskH;

            fixed (float* dest = result)
            {
                float* rPtr = dest + rOffset;
                float* gPtr = dest + gOffset;
                float* bPtr = dest + bOffset;

                float scaleX = (float)srcW / destW;
                float scaleY = (float)srcH / destH;

                for (int dy = 0; dy < destH; dy++)
                {
                    int rowStart = dy * destW;
                    int sy = (int)(dy * scaleY);
                    sy = Math.Max(0, Math.Min(sy, srcH - 1));
                    byte* pRow = src + (long)sy * srcStride;

                    if (isThirdPerson && dy >= startMaskY)
                    {
                        for (int dx = 0; dx < maskW; dx++)
                        {
                            int idx = rowStart + dx;
                            bPtr[idx] = 0f;
                            gPtr[idx] = 0f;
                            rPtr[idx] = 0f;
                        }
                        for (int dx = maskW; dx < destW; dx++)
                        {
                            int idx = rowStart + dx;
                            int sx = (int)(dx * scaleX);
                            sx = Math.Max(0, Math.Min(sx, srcW - 1));
                            byte* px = pRow + (sx * 4);
                            bPtr[idx] = _byteToFloatLut[px[0]];
                            gPtr[idx] = _byteToFloatLut[px[1]];
                            rPtr[idx] = _byteToFloatLut[px[2]];
                        }
                    }
                    else
                    {
                        for (int dx = 0; dx < destW; dx++)
                        {
                            int idx = rowStart + dx;
                            int sx = (int)(dx * scaleX);
                            sx = Math.Max(0, Math.Min(sx, srcW - 1));
                            byte* px = pRow + (sx * 4);
                            bPtr[idx] = _byteToFloatLut[px[0]];
                            gPtr[idx] = _byteToFloatLut[px[1]];
                            rPtr[idx] = _byteToFloatLut[px[2]];
                        }
                    }
                }
            }
        }

        private void WGC_OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null) return;

                var surface = frame.Surface;
                if (surface == null) return;

                var access = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>(surface);
                using var texture = access.GetInterface<ID3D11Texture2D>();

                var texDesc = texture.Description;
                uint w = texDesc.Width;
                uint h = texDesc.Height;

                lock (_wgcLock)
                {
                    if (_wgcD3DDevice == null) return;

                    if (_wgcGpuTexture == null || 
                        _wgcGpuTexture.Description.Width != w || 
                        _wgcGpuTexture.Description.Height != h)
                    {
                        _wgcGpuTexture?.Dispose();
                        _wgcGpuTexture = _wgcD3DDevice.CreateTexture2D(new Texture2DDescription
                        {
                            Width = w,
                            Height = h,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Default,
                            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                            CPUAccessFlags = CpuAccessFlags.None
                        });
                    }

                    _wgcD3DDevice.ImmediateContext.CopyResource(_wgcGpuTexture, texture);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Warning, $"WGC frame error: {ex.Message}");
            }
        }

        private bool WGCToFloat(Rectangle detectionBox, float[] result, int imageSize, bool allowStaleCache, bool needBitmap, out Bitmap? bitmapForSaving)
        {
            bitmapForSaving = null;
            int w = detectionBox.Width;
            int h = detectionBox.Height;

            lock (_wgcLock)
            {
                if (!_wgcInitialized || _wgcD3DDevice == null || _wgcGpuTexture == null)
                {
                    return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                }

                float scaleX = (float)_wgcGpuTexture.Description.Width / DisplayManager.ScreenWidth;
                float scaleY = (float)_wgcGpuTexture.Description.Height / DisplayManager.ScreenHeight;

                int relX = (int)Math.Round((detectionBox.X - DisplayManager.ScreenLeft) * scaleX);
                int relY = (int)Math.Round((detectionBox.Y - DisplayManager.ScreenTop) * scaleY);
                int cropW = (int)Math.Round(w * scaleX);
                int cropH = (int)Math.Round(h * scaleY);

                relX = Math.Max(0, Math.Min(relX, (int)_wgcGpuTexture.Description.Width - 1));
                relY = Math.Max(0, Math.Min(relY, (int)_wgcGpuTexture.Description.Height - 1));
                int srcW = Math.Min(cropW, (int)_wgcGpuTexture.Description.Width - relX);
                int srcH = Math.Min(cropH, (int)_wgcGpuTexture.Description.Height - relY);

                if (srcW <= 0 || srcH <= 0)
                {
                    return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                }

                if (_wgcStagingTex == null ||
                    _wgcStagingTex.Description.Width != (uint)srcW ||
                    _wgcStagingTex.Description.Height != (uint)srcH)
                {
                    _wgcStagingTex?.Dispose();
                    _wgcStagingTex = _wgcD3DDevice.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)srcW,
                        Height = (uint)srcH,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None
                    });
                }

                var box = new Box(relX, relY, 0, relX + srcW, relY + srcH, 1);
                _wgcD3DDevice.ImmediateContext.CopySubresourceRegion(
                    _wgcStagingTex, 0,
                    0, 0, 0,
                    _wgcGpuTexture, 0, box);

                var map = _wgcD3DDevice.ImmediateContext.Map(_wgcStagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    unsafe
                    {
                        byte* srcPtr = (byte*)map.DataPointer;
                        int srcStride = (int)map.RowPitch;

                        ConvertBgraToFloatArrayInPlaceScaled(srcPtr, srcStride, result, srcW, srcH, w, h, imageSize);

                        if (needBitmap)
                        {
                            bitmapForSaving = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                            var mapDest = bitmapForSaving.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bitmapForSaving.PixelFormat);
                            try
                            {
                                byte* dstPtr = (byte*)mapDest.Scan0;
                                int dstStride = mapDest.Stride;
                                float scaleXDest = (float)srcW / w;
                                float scaleYDest = (float)srcH / h;

                                for (int dy = 0; dy < h; dy++)
                                {
                                    byte* dstRow = dstPtr + (dy * dstStride);
                                    int sy = (int)(dy * scaleYDest);
                                    sy = Math.Max(0, Math.Min(sy, srcH - 1));
                                    byte* srcRow = srcPtr + (sy * srcStride);

                                    for (int dx = 0; dx < w; dx++)
                                    {
                                        int sx = (int)(dx * scaleXDest);
                                        sx = Math.Max(0, Math.Min(sx, srcW - 1));
                                        
                                        byte* srcPixel = srcRow + (sx * 4);
                                        byte* dstPixel = dstRow + (dx * 4);
                                        
                                        dstPixel[0] = srcPixel[0];
                                        dstPixel[1] = srcPixel[1];
                                        dstPixel[2] = srcPixel[2];
                                        dstPixel[3] = srcPixel[3];
                                    }
                                }

                                if (AimSettings.ThirdPersonSupport)
                                {
                                    int maskW = w / 2;
                                    int maskH = h / 2;
                                    int startY = h - maskH;
                                    for (int y = startY; y < h; y++)
                                    {
                                        byte* rowPtr = dstPtr + (y * dstStride);
                                        for (int x = 0; x < maskW; x++)
                                        {
                                            int pixelOffset = x * 4;
                                            rowPtr[pixelOffset + 0] = 0;
                                            rowPtr[pixelOffset + 1] = 0;
                                            rowPtr[pixelOffset + 2] = 0;
                                            rowPtr[pixelOffset + 3] = 255;
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                bitmapForSaving.UnlockBits(mapDest);
                            }

                            UpdateCache(bitmapForSaving, detectionBox);
                        }
                    }
                }
                finally
                {
                    _wgcD3DDevice.ImmediateContext.Unmap(_wgcStagingTex, 0);
                }

                if (_cachedFloatArray == null || _cachedFloatArray.Length != result.Length)
                {
                    _cachedFloatArray = new float[result.Length];
                }
                Array.Copy(result, _cachedFloatArray, result.Length);
                _cachedFloatBounds = detectionBox;
                _lastFloatTime = DateTime.Now;

                return true;
            }
        }

        private void DisposeWGCResources()
        {
            try
            {
                _wgcSession?.Dispose();
                _wgcSession = null;

                if (_wgcFramePool != null)
                {
                    _wgcFramePool.FrameArrived -= WGC_OnFrameArrived;
                    _wgcFramePool.Dispose();
                    _wgcFramePool = null;
                }

                _wgcItem = null;
                _wgcWinRTDevice?.Dispose();
                _wgcWinRTDevice = null;
                
                lock (_wgcLock)
                {
                    _wgcGpuTexture?.Dispose();
                    _wgcGpuTexture = null;
                    _wgcStagingTex?.Dispose();
                    _wgcStagingTex = null;
                }

                _wgcD3DDevice?.Dispose();
                _wgcD3DDevice = null;
                _wgcInitialized = false;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Warning, $"WGC dispose error: {ex.Message}");
            }
        }

        private static class NativeMethods
        {
            public const uint MONITOR_DEFAULTTONEAREST = 2;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT { public int x, y; }

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

            [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            public static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);

            [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
            public static extern int WindowsDeleteString(IntPtr hstring);

            [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
            public static extern int RoGetActivationFactory(
                IntPtr activatableClassId,
                [In] ref Guid iid,
                out IntPtr factory);
        }

        #endregion

        private bool DirectXToFloat(Rectangle detectionBox, float[] result, int imageSize, bool allowStaleCache, bool needBitmap, out Bitmap? bitmapForSaving)
        {
            bitmapForSaving = null;
            int w = detectionBox.Width;
            int h = detectionBox.Height;
            bool frameAcquired = false;
            IDXGIResource? desktopResource = null;

            try
            {
                lock (_displayLock)
                {
                    if (_displayChangesPending)
                    {
                        InitializeDxgiDuplication();
                        _displayChangesPending = false;
                    }
                }

                if (_dxDevice == null || _dxDevice.ImmediateContext == null || _deskDuplication == null)
                {
                    InitializeDxgiDuplication();
                    if (_dxDevice == null || _dxDevice.ImmediateContext == null || _deskDuplication == null)
                    {
                        lock (_displayLock) { _displayChangesPending = true; }
                        return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                    }
                }

                if (_stagingTex == null ||
                    _stagingTex.Description.Width != w ||
                    _stagingTex.Description.Height != h)
                {
                    _stagingTex?.Dispose();
                    _stagingTex = _dxDevice.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)w,
                        Height = (uint)h,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new(1, 0),
                        Usage = ResourceUsage.Staging,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None
                    });
                }

                int timeout = _consecutiveFailures > 0 ? 5 : 1;
                var resultDxgi = _deskDuplication!.AcquireNextFrame((uint)timeout, out var frameInfo, out desktopResource);

                if (resultDxgi == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    _consecutiveFailures = 0;
                    return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                }
                else if (resultDxgi == Vortice.DXGI.ResultCode.DeviceRemoved || resultDxgi == Vortice.DXGI.ResultCode.AccessLost)
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                        lock (_displayLock) { _displayChangesPending = true; }
                    return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                }
                else if (resultDxgi != Result.Ok)
                {
                    _consecutiveFailures++;
                    return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                }

                frameAcquired = true;
                _consecutiveFailures = 0;

                using (var screenTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
                {
                    int relativeDetectionLeft = detectionBox.Left - DisplayManager.ScreenLeft;
                    int relativeDetectionTop = detectionBox.Top - DisplayManager.ScreenTop;
                    int relativeDetectionRight = relativeDetectionLeft + detectionBox.Width;
                    int relativeDetectionBottom = relativeDetectionTop + detectionBox.Height;

                    int srcLeft = Math.Max(relativeDetectionLeft, 0);
                    int srcTop = Math.Max(relativeDetectionTop, 0);
                    int srcRight = Math.Min(relativeDetectionRight, DisplayManager.ScreenWidth);
                    int srcBottom = Math.Min(relativeDetectionBottom, DisplayManager.ScreenHeight);

                    if (srcRight > srcLeft && srcBottom > srcTop)
                    {
                        var box = new Box(srcLeft, srcTop, 0, srcRight, srcBottom, 1);
                        _dxDevice.ImmediateContext.CopySubresourceRegion(
                               _stagingTex, 0,
                               (uint)(srcLeft - relativeDetectionLeft),
                               (uint)(srcTop - relativeDetectionTop),
                               0,
                               screenTexture, 0, box);
                    }
                    else
                    {
                        return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
                    }

                    var map = _dxDevice.ImmediateContext.Map(_stagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        unsafe
                        {
                            byte* srcPtr = (byte*)map.DataPointer;
                            int srcStride = (int)map.RowPitch;

                            ConvertBgraToFloatArrayInPlace(srcPtr, srcStride, result, w, h, imageSize);

                            if (needBitmap)
                            {
                                bitmapForSaving = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                                var mapDest = bitmapForSaving.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bitmapForSaving.PixelFormat);
                                try
                                {
                                    byte* dstPtr = (byte*)mapDest.Scan0;
                                    int dstStride = mapDest.Stride;
                                    int copyBytesPerRow = Math.Min(srcStride, dstStride);
                                    for (int y = 0; y < h; y++)
                                    {
                                        Buffer.MemoryCopy(srcPtr + y * srcStride, dstPtr + y * dstStride, dstStride, copyBytesPerRow);
                                    }
                                    
                                    if (AimSettings.ThirdPersonSupport)
                                    {
                                        int maskW = w / 2;
                                        int maskH = h / 2;
                                        int startY = h - maskH;
                                        for (int y = startY; y < h; y++)
                                        {
                                            byte* rowPtr = dstPtr + (y * dstStride);
                                            for (int x = 0; x < maskW; x++)
                                            {
                                                int pixelOffset = x * 4;
                                                rowPtr[pixelOffset + 0] = 0;
                                                rowPtr[pixelOffset + 1] = 0;
                                                rowPtr[pixelOffset + 2] = 0;
                                                rowPtr[pixelOffset + 3] = 255;
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    bitmapForSaving.UnlockBits(mapDest);
                                }

                                UpdateCache(bitmapForSaving, detectionBox);
                            }
                        }
                    }
                    finally
                    {
                        _dxDevice.ImmediateContext.Unmap(_stagingTex, 0);
                    }
                }

                if (_cachedFloatArray == null || _cachedFloatArray.Length != result.Length)
                {
                    _cachedFloatArray = new float[result.Length];
                }
                Array.Copy(result, _cachedFloatArray, result.Length);
                _cachedFloatBounds = detectionBox;
                _lastFloatTime = DateTime.Now;

                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"DirectX capture error: {ex.Message}");
                if (++_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    lock (_displayLock) { _displayChangesPending = true; }
                return ConvertCachedFrameToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
            }
            finally
            {
                desktopResource?.Dispose();
                if (frameAcquired && _deskDuplication != null)
                {
                    try { _deskDuplication.ReleaseFrame(); } catch { }
                }
            }
        }

        private bool ConvertCachedFrameToFloat(Rectangle detectionBox, float[] result, int imageSize, bool allowStaleCache, bool needBitmap, out Bitmap? bitmapForSaving)
        {
            bitmapForSaving = null;
            if (_cachedFloatArray != null &&
                _cachedFloatBounds.Equals(detectionBox) &&
                (allowStaleCache || DateTime.Now - _lastFloatTime <= _frameCacheTimeout))
            {
                Array.Copy(_cachedFloatArray, result, result.Length);
                if (needBitmap && _cachedFrame != null)
                {
                    bitmapForSaving = (Bitmap)_cachedFrame.Clone();
                }
                return true;
            }
            return false;
        }

        private void UpdateCache(Bitmap frame, Rectangle bounds)
        {
            if (_cachedFrame == null ||
                !_cachedFrameBounds.Equals(bounds) ||
                DateTime.Now - _lastFrameTime > _frameCacheTimeout)
            {
                _cachedFrame?.Dispose();
                _cachedFrame = (Bitmap)frame.Clone();
                _cachedFrameBounds = bounds;
            }
            _lastFrameTime = DateTime.Now;
        }

        private bool GdiToFloat(Rectangle detectionBox, float[] result, int imageSize, bool needBitmap, out Bitmap? bitmapForSaving)
        {
            bitmapForSaving = null;
            int w = detectionBox.Width;
            int h = detectionBox.Height;

            if (screenCaptureBitmap == null || screenCaptureBitmap.Width != w || screenCaptureBitmap.Height != h)
            {
                screenCaptureBitmap?.Dispose();
                screenCaptureBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            }

            try
            {
                using (var g = Graphics.FromImage(screenCaptureBitmap))
                {
                    g.CopyFromScreen(
                        detectionBox.Left,
                        detectionBox.Top,
                        0, 0,
                        detectionBox.Size,
                        CopyPixelOperation.SourceCopy
                    );
                }

                var bmpData = screenCaptureBitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, screenCaptureBitmap.PixelFormat);
                try
                {
                    unsafe
                    {
                        byte* srcPtr = (byte*)bmpData.Scan0;
                        int srcStride = bmpData.Stride;
                        ConvertBgraToFloatArrayInPlace(srcPtr, srcStride, result, w, h, imageSize);
                    }
                }
                finally
                {
                    screenCaptureBitmap.UnlockBits(bmpData);
                }

                if (needBitmap)
                {
                    bitmapForSaving = (Bitmap)screenCaptureBitmap.Clone();
                }

                if (_cachedFloatArray == null || _cachedFloatArray.Length != result.Length)
                {
                    _cachedFloatArray = new float[result.Length];
                }
                Array.Copy(result, _cachedFloatArray, result.Length);
                _cachedFloatBounds = detectionBox;
                _lastFloatTime = DateTime.Now;

                return true;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"GDI+ screen capture failed: {ex.Message}");
                return false;
            }
        }

        public bool CaptureToFloat(Rectangle detectionBox, float[] result, int imageSize, bool allowStaleCache, bool needBitmap, out Bitmap? bitmapForSaving)
        {
            bitmapForSaving = null;
            string selectedMethod = AimSettings.ScreenCaptureMethod;

            if (_directXFailedPermanently && selectedMethod == "DirectX")
            {
                AimSettings.ScreenCaptureMethod = "GDI+";
                selectedMethod = "GDI+";
                _currentCaptureMethod = "GDI+";
            }

            if (selectedMethod != _currentCaptureMethod)
            {
                screenCaptureBitmap?.Dispose();
                screenCaptureBitmap = null;
                directXBitmap?.Dispose();
                directXBitmap = null;
                _currentCaptureMethod = selectedMethod;
                _notificationShown = false;

                if (selectedMethod == "GDI+")
                {
                    DisposeDxgiResources();
                    DisposeWGCResources();
                }
                else if (selectedMethod == "DirectX")
                {
                    DisposeWGCResources();
                    InitializeDxgiDuplication();
                }
                else if (selectedMethod == "WGC")
                {
                    DisposeDxgiResources();
                    InitializeWGC();
                }
            }

            if (selectedMethod == "WGC")
            {
                return WGCToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
            }
            else if (selectedMethod == "DirectX" && !_directXFailedPermanently)
            {
                return DirectXToFloat(detectionBox, result, imageSize, allowStaleCache, needBitmap, out bitmapForSaving);
            }
            else
            {
                return GdiToFloat(detectionBox, result, imageSize, needBitmap, out bitmapForSaving);
            }
        }

        public Bitmap? ScreenGrab(Rectangle detectionBox, bool allowStaleCache = false)
        {
            // Fallback screen grab
            var size = detectionBox.Width;
            var result = new float[3 * size * size];
            if (CaptureToFloat(detectionBox, result, size, allowStaleCache, true, out var bmp))
            {
                return bmp;
            }
            return null;
        }

        #region dispose
        public void DisposeDxgiResources()
        {
            lock (_displayLock)
            {
                try
                {

                    // Try to release any pending frame
                    if (_deskDuplication != null)
                    {
                        try
                        {
                            _deskDuplication.ReleaseFrame();
                        }
                        catch { }
                    }

                    _deskDuplication?.Dispose();
                    _stagingTex?.Dispose();
                    _dxDevice?.Dispose();
                    _cachedFrame?.Dispose();
                    directXBitmap?.Dispose();

                    _deskDuplication = null;
                    _stagingTex = null;
                    _dxDevice = null;
                    _cachedFrame = null;

                    // Small delay to ensure resources are fully released
                    //System.Threading.Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogLevel.Error, $"Error disposing DXGI resources: {ex.Message}");
                }
            }
        }
        public void Dispose()
        {
            DisplayManager.DisplayChanged -= OnDisplayChanged;
            DisposeDxgiResources();
            screenCaptureBitmap?.Dispose();
        }
        #endregion
    }
}
