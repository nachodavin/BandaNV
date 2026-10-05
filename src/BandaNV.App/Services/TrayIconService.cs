using System.Runtime.InteropServices;

namespace BandaNV.App.Services;

public sealed class TrayIconService : IDisposable
{
    private const uint WmApp =
        0x8000;

    private const uint TrayCallbackMessage =
        WmApp + 42;

    private const uint WmLeftButtonUp =
        0x0202;

    private const uint WmLeftButtonDoubleClick =
        0x0203;

    private const uint WmRightButtonUp =
        0x0205;

    private const int GwlpWndProc =
        -4;

    private const uint NimAdd =
        0x00000000;

    private const uint NimDelete =
        0x00000002;

    private const uint NifMessage =
        0x00000001;

    private const uint NifIcon =
        0x00000002;

    private const uint NifTip =
        0x00000004;

    private const uint ImageIcon =
        1;

    private const uint LrLoadFromFile =
        0x00000010;

    private const uint LrDefaultSize =
        0x00000040;

    private readonly nint _windowHandle;
    private readonly WindowProc _windowProc;
    private readonly nint _previousWindowProc;
    private readonly nint _iconHandle;

    private bool _isVisible;
    private bool _disposed;

    public TrayIconService(
        nint windowHandle,
        string iconPath)
    {
        if (windowHandle == 0)
        {
            throw new ArgumentException(
                "El handle de ventana no es válido.",
                nameof(windowHandle));
        }

        _windowHandle =
            windowHandle;

        _iconHandle =
            LoadImage(
                0,
                iconPath,
                ImageIcon,
                0,
                0,
                LrLoadFromFile |
                LrDefaultSize);

        _windowProc =
            WindowProcedure;

        _previousWindowProc =
            SetWindowLongPtr(
                _windowHandle,
                GwlpWndProc,
                Marshal.GetFunctionPointerForDelegate(
                    _windowProc));

        if (_previousWindowProc == 0)
        {
            throw new InvalidOperationException(
                "No se pudo conectar el icono de bandeja con la ventana de BandaNV.");
        }
    }

    public event EventHandler? RestoreRequested;

    public bool Show()
    {
        ThrowIfDisposed();

        if (_isVisible)
        {
            return true;
        }

        if (_iconHandle == 0)
        {
            return false;
        }

        var data =
            CreateNotifyData();

        if (!ShellNotifyIcon(
                NimAdd,
                ref data))
        {
            return false;
        }

        _isVisible =
            true;

        return true;
    }

    public void Hide()
    {
        if (_disposed ||
            !_isVisible)
        {
            return;
        }

        var data =
            CreateNotifyData();

        ShellNotifyIcon(
            NimDelete,
            ref data);

        _isVisible =
            false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Hide();

        if (_previousWindowProc != 0)
        {
            SetWindowLongPtr(
                _windowHandle,
                GwlpWndProc,
                _previousWindowProc);
        }

        if (_iconHandle != 0)
        {
            DestroyIcon(
                _iconHandle);
        }

        _disposed =
            true;

        GC.SuppressFinalize(
            this);
    }

    private nint WindowProcedure(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam)
    {
        if (message ==
            TrayCallbackMessage)
        {
            var mouseMessage =
                unchecked(
                    (uint)lParam.ToInt64());

            if (mouseMessage is
                WmLeftButtonUp or
                WmLeftButtonDoubleClick or
                WmRightButtonUp)
            {
                RestoreRequested?.Invoke(
                    this,
                    EventArgs.Empty);

                return 0;
            }
        }

        return CallWindowProc(
            _previousWindowProc,
            hWnd,
            message,
            wParam,
            lParam);
    }

    private NotifyIconData CreateNotifyData() =>
        new()
        {
            cbSize =
                (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd =
                _windowHandle,
            uID =
                1,
            uFlags =
                NifMessage |
                NifIcon |
                NifTip,
            uCallbackMessage =
                TrayCallbackMessage,
            hIcon =
                _iconHandle,
            szTip =
                "BandaNV",
            szInfo =
                string.Empty,
            szInfoTitle =
                string.Empty
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [UnmanagedFunctionPointer(
        CallingConvention.Winapi)]
    private delegate nint WindowProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(
        uint message,
        ref NotifyIconData data);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        EntryPoint = "LoadImageW",
        SetLastError = true)]
    private static extern nint LoadImage(
        nint instance,
        string name,
        uint type,
        int width,
        int height,
        uint load);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(
        nint icon);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern nint SetWindowLongPtr(
        nint windowHandle,
        int index,
        nint newValue);

    [DllImport(
        "user32.dll",
        EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(
        nint previousWindowProc,
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);
}
