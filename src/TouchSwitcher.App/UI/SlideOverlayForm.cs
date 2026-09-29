using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TouchSwitcher.Configuration;
using TouchSwitcher.Gesture;
using TouchSwitcher.Interop;
using TouchSwitcher.WindowManagement;

namespace TouchSwitcher.UI;

internal sealed class SlideOverlayForm : Form
{
    private const int HudWidth = 620;
    private const int HudHeight = 96;

    // Persistent Win32 GDI DIB section and device contexts (zero allocation per frame)
    private readonly IntPtr _memDc;
    private readonly IntPtr _hDib;
    private readonly IntPtr _oldBitmap;
    private readonly IntPtr _pBits;
    private readonly Bitmap _buffer;
    private readonly Graphics _graphics;
    private readonly IntPtr _formHwnd;

    // High performance background render thread (immune to UI message loop starvation)
    private readonly Thread _renderThread;
    private readonly ManualResetEventSlim _renderSignal = new(false);
    private readonly object _stateLock = new();
    private volatile bool _disposed;

    // Reusable fonts and formats
    private readonly Font _fontCurrent = new("Segoe UI", 11.5f, FontStyle.Bold);
    private readonly Font _fontSide = new("Segoe UI", 9.5f, FontStyle.Regular);
    private readonly Font _fontArrow = new("Segoe UI", 12f, FontStyle.Bold);
    private readonly Font _fontFallback = new("Segoe UI", 12f, FontStyle.Bold);
    private readonly Font _fontFallbackSide = new("Segoe UI", 10f, FontStyle.Bold);

    private readonly StringFormat _sfCenter = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };
    private readonly StringFormat _sfLeft = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };

    // State variables (accessed under _stateLock)
    private string _currentName = string.Empty;
    private Image? _currentIcon;
    private string _prevName = string.Empty;
    private Image? _prevIcon;
    private string _nextName = string.Empty;
    private Image? _nextIcon;

    private int _screenX;
    private int _screenY;
    private bool _isVisible;
    private bool _isFingersDown;

    // Slide animation state
    private bool _isSliding;
    private SwipeDirection _slideDirection;
    private DateTime _slideStartTime;
    private int _initialSlideOffset;
    private int _slideOffset;

    // Fade state
    private bool _isFadingIn;
    private DateTime _fadeInStartTime;
    private bool _isFadingOut;
    private DateTime _fadeOutStartTime;
    private double _currentAlpha; // 0.0 to 1.0
    private double _fadeStartAlpha;

    protected override bool ShowWithoutActivation => true;

    public SlideOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(HudWidth, HudHeight);

        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;

        // 1. Allocate persistent 32-bit DIB section and memory DC
        _memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);

        var bmi = new NativeMethods.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = HudWidth;
        bmi.bmiHeader.biHeight = -HudHeight; // Top-down DIB
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        _hDib = NativeMethods.CreateDIBSection(_memDc, ref bmi, 0, out _pBits, IntPtr.Zero, 0);
        _oldBitmap = NativeMethods.SelectObject(_memDc, _hDib);

        _buffer = new Bitmap(HudWidth, HudHeight, HudWidth * 4, PixelFormat.Format32bppPArgb, _pBits);
        _graphics = Graphics.FromImage(_buffer);
        _graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        _graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Force HWND creation on UI thread and cache raw handle
        _formHwnd = Handle;
        Show();
        SetLayeredBitmap(0, 0, 0);

        // 2. Start dedicated background render loop (runs at locked ~125 FPS)
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "TouchSwitcher.OverlayRenderer",
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED |
                          NativeMethods.WS_EX_TRANSPARENT |
                          NativeMethods.WS_EX_NOACTIVATE |
                          NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    public void ShowPreview(
        Rectangle screenBounds,
        WindowDoublyQueue doublyQueue,
        IReadOnlyList<SwitchableWindow> windows,
        SwitchMode mode)
    {
        var (current, prev, next) = doublyQueue.GetCurrentTriad(windows, mode);
        if (current == null)
        {
            return;
        }

        string curName = IconHelper.GetFriendlyName(current.ProcessName, current.Title);
        Image? curIcon = IconHelper.GetAppIcon(current.Handle, current.ProcessName);

        string prevName = prev != null ? IconHelper.GetFriendlyName(prev.ProcessName, prev.Title) : string.Empty;
        Image? prevIcon = prev != null ? IconHelper.GetAppIcon(prev.Handle, prev.ProcessName) : null;

        string nextName = next != null ? IconHelper.GetFriendlyName(next.ProcessName, next.Title) : string.Empty;
        Image? nextIcon = next != null ? IconHelper.GetAppIcon(next.Handle, next.ProcessName) : null;

        int screenX = screenBounds.Left + (screenBounds.Width - HudWidth) / 2;
        int screenY = screenBounds.Top + (int)(screenBounds.Height * 0.72) - (HudHeight / 2);

        lock (_stateLock)
        {
            _currentName = curName;
            _currentIcon = curIcon;
            _prevName = prevName;
            _prevIcon = prevIcon;
            _nextName = nextName;
            _nextIcon = nextIcon;

            _screenX = screenX;
            _screenY = screenY;

            _isFingersDown = true;
            _isSliding = false;
            _slideOffset = 0;

            if (!_isVisible || _currentAlpha < 0.95)
            {
                _isFadingIn = true;
                _fadeInStartTime = DateTime.UtcNow;
                _fadeStartAlpha = _currentAlpha;
                _isFadingOut = false;
            }

            _isVisible = true;
        }

        _renderSignal.Set();
    }

    public void TriggerTransition(
        Rectangle screenBounds,
        SwipeDirection direction,
        WindowDoublyQueue doublyQueue,
        IReadOnlyList<SwitchableWindow> windows,
        SwitchMode mode)
    {
        var (current, prev, next) = doublyQueue.GetCurrentTriad(windows, mode);
        if (current == null)
        {
            return;
        }

        string curName = IconHelper.GetFriendlyName(current.ProcessName, current.Title);
        Image? curIcon = IconHelper.GetAppIcon(current.Handle, current.ProcessName);

        string prevName = prev != null ? IconHelper.GetFriendlyName(prev.ProcessName, prev.Title) : string.Empty;
        Image? prevIcon = prev != null ? IconHelper.GetAppIcon(prev.Handle, prev.ProcessName) : null;

        string nextName = next != null ? IconHelper.GetFriendlyName(next.ProcessName, next.Title) : string.Empty;
        Image? nextIcon = next != null ? IconHelper.GetAppIcon(next.Handle, next.ProcessName) : null;

        int screenX = screenBounds.Left + (screenBounds.Width - HudWidth) / 2;
        int screenY = screenBounds.Top + (int)(screenBounds.Height * 0.72) - (HudHeight / 2);

        lock (_stateLock)
        {
            _currentName = curName;
            _currentIcon = curIcon;
            _prevName = prevName;
            _prevIcon = prevIcon;
            _nextName = nextName;
            _nextIcon = nextIcon;

            _screenX = screenX;
            _screenY = screenY;

            _slideDirection = direction;
            _initialSlideOffset = direction == SwipeDirection.Right ? 85 : -85;
            _slideOffset = _initialSlideOffset;
            _isSliding = true;
            _slideStartTime = DateTime.UtcNow;

            _currentAlpha = 1.0;
            _isFadingIn = false;
            _isFadingOut = false;
            _isVisible = true;
        }

        _renderSignal.Set();
    }

    public void Dismiss()
    {
        lock (_stateLock)
        {
            _isFingersDown = false;

            if (_isSliding)
            {
                // Let slide finish; it will start fade-out as soon as slide completes
                return;
            }

            if (_isVisible && !_isFadingOut)
            {
                _isFadingOut = true;
                _fadeOutStartTime = DateTime.UtcNow;
                _fadeStartAlpha = _currentAlpha;
                _isFadingIn = false;
            }
        }

        _renderSignal.Set();
    }

    private void RenderLoop()
    {
        NativeMethods.timeBeginPeriod(1);
        try
        {
            while (!_disposed)
            {
                bool shouldRender;
                lock (_stateLock)
                {
                    shouldRender = _isSliding || _isFadingIn || _isFadingOut || (_isFingersDown && _currentAlpha < 1.0);
                }

                if (!shouldRender)
                {
                    _renderSignal.Wait(250);
                    _renderSignal.Reset();
                    if (_disposed) break;

                    lock (_stateLock)
                    {
                        shouldRender = _isSliding || _isFadingIn || _isFadingOut || (_isFingersDown && _currentAlpha < 1.0);
                    }
                    if (!shouldRender)
                    {
                        continue;
                    }
                }

                UpdateAnimationState();
                DrawAndPresent();

                Thread.Sleep(8); // ~125 FPS
            }
        }
        catch (Exception ex)
        {
            Program.Log($"SlideOverlay RenderLoop error: {ex}");
        }
        finally
        {
            NativeMethods.timeEndPeriod(1);
        }
    }

    private void UpdateAnimationState()
    {
        lock (_stateLock)
        {
            DateTime now = DateTime.UtcNow;

            // 1. Fade-in calculation (80ms smooth entrance)
            if (_isFadingIn)
            {
                double elapsed = (now - _fadeInStartTime).TotalMilliseconds;
                double t = Math.Clamp(elapsed / 80.0, 0.0, 1.0);
                _currentAlpha = _fadeStartAlpha + (1.0 - _fadeStartAlpha) * t;
                if (t >= 1.0)
                {
                    _currentAlpha = 1.0;
                    _isFadingIn = false;
                }
            }

            // 2. Slide calculation (220ms with cubic ease-out)
            if (_isSliding)
            {
                double elapsed = (now - _slideStartTime).TotalMilliseconds;
                double t = Math.Clamp(elapsed / 220.0, 0.0, 1.0);
                double ease = 1.0 - Math.Pow(1.0 - t, 3.0);
                _slideOffset = (int)(_initialSlideOffset * (1.0 - ease));

                if (t >= 1.0)
                {
                    _slideOffset = 0;
                    _isSliding = false;

                    // If fingers were lifted while sliding, immediately start fade-out
                    if (!_isFingersDown && !_isFadingOut)
                    {
                        _isFadingOut = true;
                        _fadeOutStartTime = now;
                        _fadeStartAlpha = _currentAlpha;
                    }
                }
            }

            // 3. Fade-out calculation (140ms smooth exit)
            if (_isFadingOut && !_isSliding)
            {
                double elapsed = (now - _fadeOutStartTime).TotalMilliseconds;
                double t = Math.Clamp(elapsed / 140.0, 0.0, 1.0);
                _currentAlpha = _fadeStartAlpha * (1.0 - t);

                if (t >= 1.0)
                {
                    _currentAlpha = 0.0;
                    _isFadingOut = false;
                    _isVisible = false;
                }
            }
        }
    }

    private void DrawAndPresent()
    {
        string curName, prevName, nextName;
        Image? curIcon, prevIcon, nextIcon;
        int slideOffset, screenX, screenY;
        byte alpha;
        bool isSliding;
        SwipeDirection slideDir;
        bool isVisible;

        lock (_stateLock)
        {
            curName = _currentName;
            curIcon = _currentIcon;
            prevName = _prevName;
            prevIcon = _prevIcon;
            nextName = _nextName;
            nextIcon = _nextIcon;
            slideOffset = _slideOffset;
            screenX = _screenX;
            screenY = _screenY;
            alpha = (byte)Math.Clamp((int)(_currentAlpha * 255), 0, 255);
            isSliding = _isSliding;
            slideDir = _slideDirection;
            isVisible = _isVisible;
        }

        if (alpha == 0 || !isVisible)
        {
            _graphics.Clear(Color.Transparent);
            SetLayeredBitmap(screenX, screenY, 0);
            return;
        }

        // Render scene onto pre-allocated 32-bit DIB section
        _graphics.Clear(Color.Transparent);
        DrawHud(_graphics, slideOffset, alpha, isSliding, slideDir, curName, curIcon, prevName, prevIcon, nextName, nextIcon);

        // Instant hardware blit to DWM
        SetLayeredBitmap(screenX, screenY, alpha);
    }

    private void SetLayeredBitmap(int x, int y, byte opacity)
    {
        var size = new NativeMethods.SIZE(HudWidth, HudHeight);
        var pointSource = new NativeMethods.POINT(0, 0);
        var topPos = new NativeMethods.POINT(x, y);

        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = opacity,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA
        };

        NativeMethods.UpdateLayeredWindow(
            _formHwnd,
            IntPtr.Zero,
            ref topPos,
            ref size,
            _memDc,
            ref pointSource,
            0,
            ref blend,
            NativeMethods.ULW_ALPHA);
    }

    private void DrawHud(
        Graphics g,
        int slideOffset,
        byte alpha,
        bool isSliding,
        SwipeDirection slideDir,
        string curName,
        Image? curIcon,
        string prevName,
        Image? prevIcon,
        string nextName,
        Image? nextIcon)
    {
        int pad = 8;
        int w = HudWidth - (pad * 2); // 604
        int h = HudHeight - (pad * 2); // 80
        var pillRect = new Rectangle(pad, pad, w, h);

        // 1. Two-pass soft ambient elevation shadow
        using (var shadowPath1 = GetRoundedRectPath(new Rectangle(pad - 4, pad - 2, w + 8, h + 8), 26))
        using (var shadowBrush1 = new SolidBrush(Color.FromArgb((int)(alpha * 0.22), 0, 0, 0)))
        {
            g.FillPath(shadowBrush1, shadowPath1);
        }

        using (var shadowPath2 = GetRoundedRectPath(new Rectangle(pad - 2, pad - 1, w + 4, h + 4), 24))
        using (var shadowBrush2 = new SolidBrush(Color.FromArgb((int)(alpha * 0.40), 0, 0, 0)))
        {
            g.FillPath(shadowBrush2, shadowPath2);
        }

        // 2. Dark frosted obsidian glass capsule
        using (var pillPath = GetRoundedRectPath(pillRect, 22))
        {
            using (var bgBrush = new SolidBrush(Color.FromArgb((int)(alpha * 0.94), 20, 20, 24)))
            {
                g.FillPath(bgBrush, pillPath);
            }

            // Crisp glass outline
            using (var borderPen = new Pen(Color.FromArgb((int)(alpha * 0.25), 255, 255, 255), 1.2f))
            {
                g.DrawPath(borderPen, pillPath);
            }
        }

        using var textWhite = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        using var textDim = new SolidBrush(Color.FromArgb((int)(alpha * 0.58), 210, 210, 215));
        using var activeArrow = new SolidBrush(Color.FromArgb(alpha, 245, 190, 50));
        using var dimArrow = new SolidBrush(Color.FromArgb((int)(alpha * 0.35), 160, 160, 165));

        // Left Arrow ◀
        bool leftGlow = isSliding && slideDir == SwipeDirection.Left;
        g.DrawString("◀", _fontArrow, leftGlow ? activeArrow : dimArrow, new Rectangle(pad + 12, pad, 24, h), _sfCenter);

        // Previous App (Left Slot)
        if (!string.IsNullOrEmpty(prevName))
        {
            int prevX = pad + 40 + slideOffset;
            int iconSize = 26;
            int prevIconY = pad + (h - iconSize) / 2;

            DrawAppIcon(g, prevIcon, prevName, new Rectangle(prevX, prevIconY, iconSize, iconSize), alpha, false);
            prevX += iconSize + 10;

            int prevTextW = Math.Max(40, (pad + (w - 220) / 2 - 8) - prevX);
            g.DrawString(prevName, _fontSide, textDim, new Rectangle(prevX, pad, prevTextW, h), _sfLeft);
        }

        // Center Spotlight Capsule (Active App)
        int centerW = 220;
        int centerH = 64;
        int centerX = pad + (w - centerW) / 2 + slideOffset;
        int centerY = pad + (h - centerH) / 2;
        var centerRect = new Rectangle(centerX, centerY, centerW, centerH);

        using (var highlightPath = GetRoundedRectPath(centerRect, 16))
        {
            // Frosted highlight background
            using (var hlBg = new SolidBrush(Color.FromArgb((int)(alpha * 0.16), 255, 255, 255)))
            {
                g.FillPath(hlBg, highlightPath);
            }
            // Active amber/gold border
            using (var hlBorder = new Pen(Color.FromArgb((int)(alpha * 0.65), 220, 175, 45), 1.5f))
            {
                g.DrawPath(hlBorder, highlightPath);
            }
        }

        // Draw Center Icon and Name
        int contentX = centerX + 12;
        int curIconSize = 34;
        int curIconY = centerY + (centerH - curIconSize) / 2;

        DrawAppIcon(g, curIcon, curName, new Rectangle(contentX, curIconY, curIconSize, curIconSize), alpha, true);
        contentX += curIconSize + 10;

        int curTextW = (centerX + centerW - 10) - contentX;
        g.DrawString(curName, _fontCurrent, textWhite, new Rectangle(contentX, centerY, curTextW, centerH), _sfLeft);

        // Next App (Right Slot)
        if (!string.IsNullOrEmpty(nextName))
        {
            int nextStartX = pad + w - 180 + slideOffset;
            int iconSize = 26;
            int nextIconY = pad + (h - iconSize) / 2;

            DrawAppIcon(g, nextIcon, nextName, new Rectangle(nextStartX, nextIconY, iconSize, iconSize), alpha, false);
            int nextTextX = nextStartX + iconSize + 10;

            int nextTextW = (pad + w - 38) - nextTextX;
            g.DrawString(nextName, _fontSide, textDim, new Rectangle(nextTextX, pad, Math.Max(40, nextTextW), h), _sfLeft);
        }

        // Right Arrow ▶
        bool rightGlow = isSliding && slideDir == SwipeDirection.Right;
        g.DrawString("▶", _fontArrow, rightGlow ? activeArrow : dimArrow, new Rectangle(pad + w - 36, pad, 24, h), _sfCenter);
    }

    private void DrawAppIcon(Graphics g, Image? icon, string name, Rectangle destRect, byte alpha, bool isCenter)
    {
        if (icon != null)
        {
            g.DrawImage(icon, destRect);
        }
        else
        {
            // Crisp fallback placeholder icon
            using var iconBg = new SolidBrush(Color.FromArgb((int)(alpha * 0.35), 70, 70, 85));
            using var iconPath = GetRoundedRectPath(destRect, 6);
            g.FillPath(iconBg, iconPath);

            using var borderPen = new Pen(Color.FromArgb((int)(alpha * 0.30), 255, 255, 255), 1f);
            g.DrawPath(borderPen, iconPath);

            string letter = string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            using var brushLetter = new SolidBrush(Color.FromArgb(alpha, 240, 240, 245));
            g.DrawString(letter, isCenter ? _fontFallback : _fontFallbackSide, brushLetter, destRect, _sfCenter);
        }
    }

    private static GraphicsPath GetRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _renderSignal.Set();
            _renderThread.Join(500);

            _renderSignal.Dispose();

            _fontCurrent.Dispose();
            _fontSide.Dispose();
            _fontArrow.Dispose();
            _fontFallback.Dispose();
            _fontFallbackSide.Dispose();

            _sfCenter.Dispose();
            _sfLeft.Dispose();

            _graphics.Dispose();
            _buffer.Dispose();

            if (_oldBitmap != IntPtr.Zero) NativeMethods.SelectObject(_memDc, _oldBitmap);
            if (_hDib != IntPtr.Zero) NativeMethods.DeleteObject(_hDib);
            if (_memDc != IntPtr.Zero) NativeMethods.DeleteDC(_memDc);
        }
        base.Dispose(disposing);
    }
}
