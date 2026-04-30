using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinSender.Capture;

/// <summary>
/// Screen capture via DXGI Desktop Duplication (primary monitor).
/// Falls back to GDI BitBlt if DXGI is unavailable.
/// Returns raw BGRA frames: Width * Height * 4 bytes (top-down).
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreenCapture : IDisposable
{
    private bool _initialized;
    private bool _disposed;
    private bool _useDxgi;
    private int  _dxgiFrameNum;

    private IntPtr _device;
    private IntPtr _context;
    private IntPtr _duplication;
    private IntPtr _stagingTexture;
    private byte[] _frameBuf0 = Array.Empty<byte>();
    private byte[] _frameBuf1 = Array.Empty<byte>();
    private int    _frameBufIdx;

    private byte[]? _cursorShape;
    private DXGI_OUTDUPL_POINTER_SHAPE_INFO _cursorShapeInfo;
    private int _cursorX, _cursorY;
    private bool _cursorVisible;

    // GDI fallback
    private IntPtr _desktopDC;
    private IntPtr _memDC;
    private IntPtr _hBitmap;
    private IntPtr _hOldBitmap;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool ShowCursor { get; set; } = true;

    public void Initialize()
    {
        Width  = GetSystemMetrics(SM_CXSCREEN);
        Height = GetSystemMetrics(SM_CYSCREEN);

        try
        {
            InitializeDxgi();
            _useDxgi = true;
            Console.WriteLine($"[ScreenCapture] DXGI Desktop Duplication ready: {Width}x{Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenCapture] DXGI unavailable ({ex.Message}); falling back to GDI BitBlt");
            InitializeGdi();
            Console.WriteLine($"[ScreenCapture] GDI BitBlt ready: {Width}x{Height}");
        }

        _initialized = true;
    }

    public byte[]? CaptureFrame(int timeoutMs = 33)
    {
        if (!_initialized) throw new InvalidOperationException("Call Initialize() first");
        var frame = _useDxgi ? CaptureFrameDxgi(timeoutMs) : CaptureFrameGdi();
        if (frame != null && ShowCursor)
            DrawCursorOnFrame(frame);
        return frame;
    }

    // ========== DXGI path ==========

    private void InitializeDxgi()
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, 0,
            IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out _device, out _, out _context);
        ThrowOnError(hr, "D3D11CreateDevice");

        var dxgiDeviceGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        hr = Marshal.QueryInterface(_device, ref dxgiDeviceGuid, out IntPtr dxgiDevice);
        ThrowOnError(hr, "QI IDXGIDevice");

        var adapterGuid = new Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");
        hr = CallGetParent(dxgiDevice, adapterGuid, out IntPtr adapter);
        Marshal.Release(dxgiDevice);
        ThrowOnError(hr, "IDXGIDevice::GetParent->IDXGIAdapter");

        hr = DxgiEnumOutputs(adapter, 0, out IntPtr output);
        Marshal.Release(adapter);
        ThrowOnError(hr, "IDXGIAdapter::EnumOutputs(0)");

        var output1Guid = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
        hr = Marshal.QueryInterface(output, ref output1Guid, out IntPtr output1);
        Marshal.Release(output);
        ThrowOnError(hr, "QI IDXGIOutput1");

        hr = DxgiDuplicateOutput(output1, _device, out _duplication);
        Marshal.Release(output1);
        ThrowOnError(hr, "IDXGIOutput1::DuplicateOutput");

        DxgiGetDuplDesc(_duplication, out var duplDesc);
        Width  = (int)duplDesc.ModeDesc_Width;
        Height = (int)duplDesc.ModeDesc_Height;
        Console.WriteLine($"[ScreenCapture] DXGI desktop size from OUTDUPL_DESC: {Width}x{Height}");

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)Width, Height = (uint)Height,
            MipLevels = 1, ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1, SampleQuality = 0,
            Usage = D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
            MiscFlags = 0
        };
        hr = D3D11CreateTexture2D(_device, ref desc, out _stagingTexture);
        ThrowOnError(hr, "ID3D11Device::CreateTexture2D(staging)");
    }

    private unsafe byte[]? CaptureFrameDxgi(int timeoutMs)
    {
        IntPtr frameResource = IntPtr.Zero;
        IntPtr frameTex = IntPtr.Zero;
        bool frameAcquired = false;
        try
        {
            int hr = DxgiAcquireNextFrame(_duplication, (uint)timeoutMs, out var frameInfo, out frameResource);
            if (hr == DXGI_ERROR_WAIT_TIMEOUT) return null;
            ThrowOnError(hr, "AcquireNextFrame");
            frameAcquired = true;

            if (frameInfo.LastMouseUpdateTime != 0)
            {
                _cursorX = frameInfo.PointerPosition_Position.X;
                _cursorY = frameInfo.PointerPosition_Position.Y;
                _cursorVisible = frameInfo.PointerPosition_Visible != 0;
            }

            if (frameInfo.PointerShapeBufferSize > 0)
            {
                var buf = new byte[frameInfo.PointerShapeBufferSize];
                fixed (byte* p = buf)
                {
                    int shapeHr = DxgiGetFramePointerShape(_duplication,
                        frameInfo.PointerShapeBufferSize, (IntPtr)p,
                        out _, out _cursorShapeInfo);
                    if (shapeHr >= 0) _cursorShape = buf;
                }
            }

            if (frameInfo.LastPresentTime == 0 && frameInfo.LastMouseUpdateTime == 0)
            {
                return null;
            }

            var tex2dGuid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            hr = Marshal.QueryInterface(frameResource, ref tex2dGuid, out frameTex);
            ThrowOnError(hr, "QI ID3D11Texture2D");

            D3D11CopyResource(_context, _stagingTexture, frameTex);

            hr = D3D11Map(_context, _stagingTexture, 0, D3D11_MAP_READ, 0, out var mapped);
            ThrowOnError(hr, "Map");

            int rowBytes = Width * 4;
            int frameSize = Width * Height * 4;
            _frameBufIdx ^= 1;
            ref byte[] slot = ref (_frameBufIdx == 0 ? ref _frameBuf0 : ref _frameBuf1);
            if (slot.Length < frameSize) slot = new byte[frameSize];
            byte[] result = slot;
            fixed (byte* dst = result)
            {
                for (int y = 0; y < Height; y++)
                {
                    Buffer.MemoryCopy(
                        (void*)(mapped.pData + y * mapped.RowPitch),
                        dst + y * rowBytes,
                        rowBytes, rowBytes);
                }
            }
            D3D11Unmap(_context, _stagingTexture, 0);

            _dxgiFrameNum++;
            if (_dxgiFrameNum <= 3 || _dxgiFrameNum == 30 || _dxgiFrameNum == 100)
            {
                int cx = Width / 2, cy = Height / 2;
                int off = (cy * rowBytes) + cx * 4;
                Console.WriteLine($"[ScreenCapture] dxgi#{_dxgiFrameNum} center=B{result[off]} G{result[off+1]} R{result[off+2]} A{result[off+3]}, RowPitch={mapped.RowPitch}, AccumFrames={frameInfo.AccumulatedFrames}, LastPresent={frameInfo.LastPresentTime}, cursor=({_cursorX},{_cursorY},vis={_cursorVisible},type={_cursorShapeInfo.Type})");
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenCapture] DXGI frame error: {ex.Message}");
            return null;
        }
        finally
        {
            if (frameAcquired)
            {
                try { DxgiReleaseFrame(_duplication); } catch { }
            }
        }
    }

    private void BlendCursor(byte[] frameBgra, int rowBytes)
    {
        var info = _cursorShapeInfo;
        var shape = _cursorShape!;
        int cx = _cursorX, cy = _cursorY;
        int frameW = Width, frameH = Height;

        switch (info.Type)
        {
            case 1: BlendMonochrome(frameBgra, rowBytes, shape, info, cx, cy, frameW, frameH); break;
            case 2: BlendColor(frameBgra, rowBytes, shape, info, cx, cy, frameW, frameH); break;
            case 4: BlendMaskedColor(frameBgra, rowBytes, shape, info, cx, cy, frameW, frameH); break;
        }
    }

    private static void BlendMonochrome(byte[] frame, int rowBytes, byte[] shape,
        DXGI_OUTDUPL_POINTER_SHAPE_INFO info, int cx, int cy, int frameW, int frameH)
    {
        int actualH = (int)info.Height / 2;
        int pitch = (int)info.Pitch;
        int width = (int)info.Width;
        for (int row = 0; row < actualH; row++)
        {
            int dy = cy + row;
            if (dy < 0 || dy >= frameH) continue;
            for (int col = 0; col < width; col++)
            {
                int dx = cx + col;
                if (dx < 0 || dx >= frameW) continue;
                int byteIdx = col / 8;
                int bitMask = 0x80 >> (col & 7);
                bool andBit = (shape[row * pitch + byteIdx] & bitMask) != 0;
                bool xorBit = (shape[(row + actualH) * pitch + byteIdx] & bitMask) != 0;
                int p = dy * rowBytes + dx * 4;
                byte b = frame[p], g = frame[p + 1], r = frame[p + 2];
                if (andBit)
                {
                    if (xorBit) { b = (byte)~b; g = (byte)~g; r = (byte)~r; }
                }
                else
                {
                    if (xorBit) { b = 255; g = 255; r = 255; } else { b = 0; g = 0; r = 0; }
                }
                frame[p] = b; frame[p + 1] = g; frame[p + 2] = r;
            }
        }
    }

    private static void BlendColor(byte[] frame, int rowBytes, byte[] shape,
        DXGI_OUTDUPL_POINTER_SHAPE_INFO info, int cx, int cy, int frameW, int frameH)
    {
        int pitch = (int)info.Pitch;
        int width = (int)info.Width;
        int height = (int)info.Height;
        for (int row = 0; row < height; row++)
        {
            int dy = cy + row;
            if (dy < 0 || dy >= frameH) continue;
            int srcRow = row * pitch;
            for (int col = 0; col < width; col++)
            {
                int dx = cx + col;
                if (dx < 0 || dx >= frameW) continue;
                int s = srcRow + col * 4;
                byte cb = shape[s], cg = shape[s + 1], cr = shape[s + 2], ca = shape[s + 3];
                if (ca == 0) continue;
                int p = dy * rowBytes + dx * 4;
                if (ca == 255)
                {
                    frame[p] = cb; frame[p + 1] = cg; frame[p + 2] = cr;
                }
                else
                {
                    int inv = 255 - ca;
                    frame[p]     = (byte)((cb * ca + frame[p]     * inv) / 255);
                    frame[p + 1] = (byte)((cg * ca + frame[p + 1] * inv) / 255);
                    frame[p + 2] = (byte)((cr * ca + frame[p + 2] * inv) / 255);
                }
            }
        }
    }

    private static void BlendMaskedColor(byte[] frame, int rowBytes, byte[] shape,
        DXGI_OUTDUPL_POINTER_SHAPE_INFO info, int cx, int cy, int frameW, int frameH)
    {
        int pitch = (int)info.Pitch;
        int width = (int)info.Width;
        int height = (int)info.Height;
        for (int row = 0; row < height; row++)
        {
            int dy = cy + row;
            if (dy < 0 || dy >= frameH) continue;
            int srcRow = row * pitch;
            for (int col = 0; col < width; col++)
            {
                int dx = cx + col;
                if (dx < 0 || dx >= frameW) continue;
                int s = srcRow + col * 4;
                byte cb = shape[s], cg = shape[s + 1], cr = shape[s + 2], ca = shape[s + 3];
                int p = dy * rowBytes + dx * 4;
                if (ca == 0)
                {
                    frame[p] = cb; frame[p + 1] = cg; frame[p + 2] = cr;
                }
                else
                {
                    frame[p]     = (byte)(frame[p]     ^ cb);
                    frame[p + 1] = (byte)(frame[p + 1] ^ cg);
                    frame[p + 2] = (byte)(frame[p + 2] ^ cr);
                }
            }
        }
    }

    // ========== GDI fallback ==========

    private void InitializeGdi()
    {
        // Diagnostics: which window-station/desktop is this process attached to?
        try
        {
            var hSta = GetProcessWindowStation();
            var hDsk = GetThreadDesktop(GetCurrentThreadId());
            string staName = QueryUserObjectName(hSta);
            string dskName = QueryUserObjectName(hDsk);
            Console.WriteLine($"[ScreenCapture] WindowStation='{staName}' Desktop='{dskName}' SessionId={System.Diagnostics.Process.GetCurrentProcess().SessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenCapture] station/desktop probe error: {ex.Message}");
        }

        _desktopDC  = GetDC(IntPtr.Zero);
        if (_desktopDC == IntPtr.Zero)
            Console.WriteLine($"[ScreenCapture] GetDC(NULL) returned 0, lastError={Marshal.GetLastWin32Error()}");
        _memDC      = CreateCompatibleDC(_desktopDC);
        _hBitmap    = CreateCompatibleBitmap(_desktopDC, Width, Height);
        if (_hBitmap == IntPtr.Zero)
            Console.WriteLine($"[ScreenCapture] CreateCompatibleBitmap returned 0, lastError={Marshal.GetLastWin32Error()}");
        _hOldBitmap = SelectObject(_memDC, _hBitmap);
    }

    private byte[]? CaptureFrameGdi()
    {
        bool ok = BitBlt(_memDC, 0, 0, Width, Height, _desktopDC, 0, 0, SRCCOPY);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"[ScreenCapture] BitBlt failed, lastError={err} (0x{err:X})");
            return null;
        }

        var bi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = Width,
            biHeight = -Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB
        };
        byte[] buffer = new byte[Width * Height * 4];
        int lines = GetDIBits(_desktopDC, _hBitmap, 0, (uint)Height, buffer, ref bi, DIB_RGB_COLORS);
        if (lines <= 0)
        {
            int err = Marshal.GetLastWin32Error();
            Console.WriteLine($"[ScreenCapture] GetDIBits returned {lines}, lastError={err}");
            return null;
        }
        return buffer;
    }

    private static string QueryUserObjectName(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(null)";
        var sb = new System.Text.StringBuilder(256);
        if (GetUserObjectInformation(h, 2 /*UOI_NAME*/, sb, (uint)sb.Capacity * 2, out _))
            return sb.ToString();
        return $"(err {Marshal.GetLastWin32Error()})";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCursorResources();

        if (_useDxgi)
        {
            if (_stagingTexture != IntPtr.Zero) Marshal.Release(_stagingTexture);
            if (_duplication    != IntPtr.Zero) Marshal.Release(_duplication);
            if (_context        != IntPtr.Zero) Marshal.Release(_context);
            if (_device         != IntPtr.Zero) Marshal.Release(_device);
        }
        else
        {
            if (_hOldBitmap != IntPtr.Zero) SelectObject(_memDC, _hOldBitmap);
            if (_hBitmap    != IntPtr.Zero) DeleteObject(_hBitmap);
            if (_memDC      != IntPtr.Zero) DeleteDC(_memDC);
            if (_desktopDC  != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _desktopDC);
        }
    }

    // Reusable GDI objects for cursor rendering — allocated once, reused every frame
    // to avoid per-frame CreateCompatibleBitmap/SetDIBits/GetDIBits overhead (~16 MB GDI R/W).
    private IntPtr _cursorDC;
    private IntPtr _cursorBitmap;
    private IntPtr _cursorBits;
    private int    _cursorBitmapSize;
    private int    _cursorBitmapW;

    private unsafe void DrawCursorOnFrame(byte[] frameBgra)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || ci.flags == 0 || ci.hCursor == IntPtr.Zero) return;

        int cx = ci.ptScreenPos.X;
        int cy = ci.ptScreenPos.Y;
        if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return;

        const int CursorSize = 64;
        if (_cursorDC == IntPtr.Zero || _cursorBitmapW != CursorSize)
        {
            if (_cursorBitmap != IntPtr.Zero) DeleteObject(_cursorBitmap);
            if (_cursorDC     != IntPtr.Zero) DeleteDC(_cursorDC);
            _cursorDC         = CreateCompatibleDC(IntPtr.Zero);
            _cursorBitmap     = AllocCursorDIBSection(_cursorDC, CursorSize, CursorSize, out _cursorBits);
            _cursorBitmapW    = CursorSize;
            _cursorBitmapSize = CursorSize * CursorSize * 4;
        }

        new Span<byte>((void*)_cursorBits, _cursorBitmapSize).Clear();
        IntPtr hOld = SelectObject(_cursorDC, _cursorBitmap);
        DrawIconEx(_cursorDC, 0, 0, ci.hCursor, CursorSize, CursorSize, 0, IntPtr.Zero, DI_NORMAL);
        SelectObject(_cursorDC, hOld);

        byte* src = (byte*)_cursorBits;
        int frameRowBytes = Width * 4;
        int srcW = Math.Min(CursorSize, Width - cx);
        int srcH = Math.Min(CursorSize, Height - cy);
        fixed (byte* dst = frameBgra)
        {
            for (int row = 0; row < srcH; row++)
            {
                int dy = cy + row;
                if (dy < 0) continue;
                byte* srcRow = src + row * CursorSize * 4;
                byte* dstRow = dst + dy * frameRowBytes + cx * 4;
                for (int col = 0; col < srcW; col++)
                {
                    byte* s = srcRow + col * 4;
                    byte ca = s[3];
                    if (ca == 0) continue;
                    byte* p = dstRow + col * 4;
                    if (ca == 255)
                    {
                        p[0] = s[0]; p[1] = s[1]; p[2] = s[2];
                    }
                    else
                    {
                        int inv = 255 - ca;
                        p[0] = (byte)((s[0] * ca + p[0] * inv) / 255);
                        p[1] = (byte)((s[1] * ca + p[1] * inv) / 255);
                        p[2] = (byte)((s[2] * ca + p[2] * inv) / 255);
                    }
                }
            }
        }
    }

    private static IntPtr AllocCursorDIBSection(IntPtr hdc, int w, int h, out IntPtr bits)
    {
        var bi = new BITMAPINFOHEADER
        {
            biSize        = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth       = w,
            biHeight      = -h, // top-down
            biPlanes      = 1,
            biBitCount    = 32,
            biCompression = BI_RGB
        };
        return CreateDIBSection(hdc, ref bi, DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
    }

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint iUsage,
        out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    private void DisposeCursorResources()
    {
        if (_cursorBitmap != IntPtr.Zero) { DeleteObject(_cursorBitmap); _cursorBitmap = IntPtr.Zero; }
        if (_cursorDC     != IntPtr.Zero) { DeleteDC(_cursorDC);         _cursorDC     = IntPtr.Zero; }
    }

    // ========== P/Invoke: GDI ==========
    [DllImport("user32.dll")] static extern int    GetSystemMetrics(int n);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll", SetLastError = true)]  static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
    [DllImport("gdi32.dll")]  static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")]  static extern bool   DeleteObject(IntPtr hObj);
    [DllImport("gdi32.dll")]  static extern bool   DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll", SetLastError = true)]  static extern bool   BitBlt(IntPtr hdcDst, int x, int y, int w, int h, IntPtr hdcSrc, int xs, int ys, uint rop);
    [DllImport("gdi32.dll", SetLastError = true)]  static extern int    GetDIBits(IntPtr hDC, IntPtr hBmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll", SetLastError = true)]  static extern int    SetDIBits(IntPtr hDC, IntPtr hBmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] static extern bool   GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] static extern bool   DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, uint istep, IntPtr hbr, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool GetUserObjectInformation(IntPtr h, int idx, System.Text.StringBuilder info, uint len, out uint needed);

    const int  SM_CXSCREEN = 0;
    const int  SM_CYSCREEN = 1;
    const uint SRCCOPY     = 0x00CC0020;
    const int  BI_RGB      = 0;
    const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    const uint DI_NORMAL = 0x3;

    // ========== P/Invoke: D3D11 ==========
    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint numFeatureLevels, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr context);

    const int  D3D_DRIVER_TYPE_HARDWARE   = 1;
    const uint D3D11_SDK_VERSION          = 7;
    const int  DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    const int  D3D11_USAGE_STAGING        = 3;
    const int  D3D11_CPU_ACCESS_READ      = 0x20000;
    const int  D3D11_MAP_READ             = 1;
    const int  DXGI_ERROR_WAIT_TIMEOUT    = unchecked((int)0x887A0027);

    // ========== vtable invocations ==========

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetParentDelegate(IntPtr self, ref Guid riid, out IntPtr parent);
    static int CallGetParent(IntPtr obj, Guid guid, out IntPtr parent)
    {
        // IDXGIObject::GetParent is vtable slot 6
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), 6 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<GetParentDelegate>(fn)(obj, ref guid, out parent);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int EnumOutputsDelegate(IntPtr self, uint idx, out IntPtr output);
    static int DxgiEnumOutputs(IntPtr adapter, uint idx, out IntPtr output)
    {
        // IDXGIAdapter::EnumOutputs is vtable slot 7
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 7 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<EnumOutputsDelegate>(fn)(adapter, idx, out output);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int DuplicateOutputDelegate(IntPtr self, IntPtr device, out IntPtr dup);
    static int DxgiDuplicateOutput(IntPtr output1, IntPtr device, out IntPtr dup)
    {
        // IDXGIOutput1::DuplicateOutput is vtable slot 22
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(output1), 22 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<DuplicateOutputDelegate>(fn)(output1, device, out dup);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public POINT PointerPosition_Position;
        public int PointerPosition_Visible;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int AcquireNextFrameDelegate(IntPtr self, uint timeout, out DXGI_OUTDUPL_FRAME_INFO info, out IntPtr res);
    static int DxgiAcquireNextFrame(IntPtr dup, uint timeout, out DXGI_OUTDUPL_FRAME_INFO info, out IntPtr res)
    {
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(dup), 8 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<AcquireNextFrameDelegate>(fn)(dup, timeout, out info, out res);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_OUTDUPL_DESC
    {
        public uint ModeDesc_Width;
        public uint ModeDesc_Height;
        public uint ModeDesc_RefreshRate_Numerator;
        public uint ModeDesc_RefreshRate_Denominator;
        public uint ModeDesc_Format;
        public uint ModeDesc_ScanlineOrdering;
        public uint ModeDesc_Scaling;
        public uint Rotation;
        public int  DesktopImageInSystemMemory;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void GetDuplDescDelegate(IntPtr self, out DXGI_OUTDUPL_DESC desc);
    static void DxgiGetDuplDesc(IntPtr dup, out DXGI_OUTDUPL_DESC desc)
    {
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(dup), 7 * IntPtr.Size);
        Marshal.GetDelegateForFunctionPointer<GetDuplDescDelegate>(fn)(dup, out desc);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ReleaseFrameDelegate(IntPtr self);
    static void DxgiReleaseFrame(IntPtr dup)
    {
        // IDXGIOutputDuplication::ReleaseFrame is vtable slot 14
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(dup), 14 * IntPtr.Size);
        Marshal.GetDelegateForFunctionPointer<ReleaseFrameDelegate>(fn)(dup);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DXGI_OUTDUPL_POINTER_SHAPE_INFO
    {
        public uint Type;
        public uint Width;
        public uint Height;
        public uint Pitch;
        public POINT HotSpot;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetFramePointerShapeDelegate(IntPtr self, uint bufSize, IntPtr buf,
        out uint requiredSize, out DXGI_OUTDUPL_POINTER_SHAPE_INFO info);
    static int DxgiGetFramePointerShape(IntPtr dup, uint bufSize, IntPtr buf,
        out uint requiredSize, out DXGI_OUTDUPL_POINTER_SHAPE_INFO info)
    {
        // IDXGIOutputDuplication::GetFramePointerShape is vtable slot 11
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(dup), 11 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<GetFramePointerShapeDelegate>(fn)(
            dup, bufSize, buf, out requiredSize, out info);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateTexture2DDelegate(IntPtr self, ref D3D11_TEXTURE2D_DESC desc, IntPtr init, out IntPtr tex);
    static int D3D11CreateTexture2D(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, out IntPtr tex)
    {
        // ID3D11Device::CreateTexture2D is vtable slot 5
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(device), 5 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(fn)(device, ref desc, IntPtr.Zero, out tex);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void CopyResourceDelegate(IntPtr self, IntPtr dst, IntPtr src);
    static void D3D11CopyResource(IntPtr ctx, IntPtr dst, IntPtr src)
    {
        // ID3D11DeviceContext::CopyResource is vtable slot 47
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(ctx), 47 * IntPtr.Size);
        Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(fn)(ctx, dst, src);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int MapDelegate(IntPtr self, IntPtr res, uint sub, int mapType, uint flags, out D3D11_MAPPED_SUBRESOURCE mapped);
    static int D3D11Map(IntPtr ctx, IntPtr res, uint sub, int mapType, uint flags, out D3D11_MAPPED_SUBRESOURCE mapped)
    {
        // ID3D11DeviceContext::Map is vtable slot 14
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(ctx), 14 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<MapDelegate>(fn)(ctx, res, sub, mapType, flags, out mapped);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void UnmapDelegate(IntPtr self, IntPtr res, uint sub);
    static void D3D11Unmap(IntPtr ctx, IntPtr res, uint sub)
    {
        // ID3D11DeviceContext::Unmap is vtable slot 15
        var fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(ctx), 15 * IntPtr.Size);
        Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(fn)(ctx, res, sub);
    }

    static void ThrowOnError(int hr, string op)
    {
        if (hr < 0) throw new Exception($"{op} failed HRESULT=0x{hr:X8}");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize;
        public int Format;
        public uint SampleCount, SampleQuality;
        public int Usage;
        public uint BindFlags;
        public int CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public int RowPitch, DepthPitch;
    }
}
