using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NeoRF.EarthModels;

public enum SceneView
{
    Rotation,
    Revolution
}

/// <summary>
/// 天文场景画布：地球自转 / 地球公转。
/// 纯 CPU 像素着色 + 矢量标注，无外部贴图依赖，三平台表现一致。
/// 交互约定：单指/鼠标拖动只改变地球自身的角度，视角平面保持不变；双指捏合缩放。
/// </summary>
public class CelestialView : Control, IDisposable
{
    // ---------------------------------------------------------------- 属性
    public static readonly StyledProperty<SceneRuntime?> SceneProperty =
        AvaloniaProperty.Register<CelestialView, SceneRuntime?>(nameof(Scene));

    public static readonly StyledProperty<SceneView> ViewProperty =
        AvaloniaProperty.Register<CelestialView, SceneView>(nameof(View));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(IsPlaying), true);

    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<CelestialView, double>(nameof(Speed), 1.0);

    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ShowLabels), true);

    public static readonly StyledProperty<bool> ShowOrbitProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ShowOrbit), true);

    public static readonly StyledProperty<bool> ShowGraticuleProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ShowGraticule), true);

    public static readonly StyledProperty<bool> ExaggerateTiltProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ExaggerateTilt));

    public static readonly StyledProperty<int> FrameRateProperty =
        AvaloniaProperty.Register<CelestialView, int>(nameof(FrameRate), 120);

    public static readonly StyledProperty<bool> ShowHudProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ShowHud), true);

    public static readonly StyledProperty<bool> ShowCloudsProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ShowClouds), true);

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<CelestialView, double>(nameof(Zoom), 1.0);

    /// <summary>自转视图里太阳直射点的纬度（公转视图由轨道角自动计算）。</summary>
    public static readonly StyledProperty<double> DeclinationDegProperty =
        AvaloniaProperty.Register<CelestialView, double>(nameof(DeclinationDeg), 0.0);

    /// <summary>显式视角俯仰角；NaN 表示使用视图默认值。它只由独立控件修改，绝不随拖动变化。</summary>
    public static readonly StyledProperty<double> CameraElevationProperty =
        AvaloniaProperty.Register<CelestialView, double>(nameof(CameraElevation), double.NaN);

    /// <summary>是否绘制赤道 / 南北回归线 / 南北极圈等教材纬线。</summary>
    public static readonly StyledProperty<bool> ShowKeyLatitudesProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(ShowKeyLatitudes), true);

    /// <summary>特写模式：隐藏 HUD、阳光箭头等，只保留地球与教材纬线。</summary>
    public static readonly StyledProperty<bool> InsetModeProperty =
        AvaloniaProperty.Register<CelestialView, bool>(nameof(InsetMode));

    public SceneRuntime? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public SceneView View { get => GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public double Speed { get => GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
    public bool ShowLabels { get => GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    public bool ShowOrbit { get => GetValue(ShowOrbitProperty); set => SetValue(ShowOrbitProperty, value); }
    public bool ShowGraticule { get => GetValue(ShowGraticuleProperty); set => SetValue(ShowGraticuleProperty, value); }
    public bool ExaggerateTilt { get => GetValue(ExaggerateTiltProperty); set => SetValue(ExaggerateTiltProperty, value); }
    public int FrameRate { get => GetValue(FrameRateProperty); set => SetValue(FrameRateProperty, value); }
    public bool ShowHud { get => GetValue(ShowHudProperty); set => SetValue(ShowHudProperty, value); }
    public bool ShowClouds { get => GetValue(ShowCloudsProperty); set => SetValue(ShowCloudsProperty, value); }
    public double DeclinationDeg { get => GetValue(DeclinationDegProperty); set => SetValue(DeclinationDegProperty, value); }
    public double CameraElevation { get => GetValue(CameraElevationProperty); set => SetValue(CameraElevationProperty, value); }
    public bool ShowKeyLatitudes { get => GetValue(ShowKeyLatitudesProperty); set => SetValue(ShowKeyLatitudesProperty, value); }
    public bool InsetMode { get => GetValue(InsetModeProperty); set => SetValue(InsetModeProperty, value); }

    /// <summary>缩放（0.6–6）：滚轮、按钮或双指捏合，带平滑过渡。</summary>
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>本模型的文案（由模型界面注入，启动器不再提供）。</summary>
    public ModelStrings Texts { get; set; } = ModelStrings.Default;

    // ---------------------------------------------------------------- 状态
    private double _orbitDeg;
    private double _spinDeg = 20;
    private DispatcherTimer? _timer;
    private WriteableBitmap? _sphere;
    private int _sphereSize;
    private long _lastTick;
    private double _accum;
    private bool _animating;

    /// <summary>运动时的球面渲染边长：按实测帧耗时自适应，慢机器自动降一档。</summary>
    private int MotionResolution { get; set; } = 460;

    private double _zoom = 1;
    private double _zoomTarget = 1;
    private double _camElev = 8;
    private double _camElevTarget = 8;
    private double _cloudShift;
    private double _spinVelocity;
    private double _frameAverage;

    // 多点触控：每个手指一个 Pointer.Id，两指时切换为捏合缩放
    private readonly Dictionary<int, Point> _pointers = new();
    private bool _pinching;
    private double _pinchStartDistance;
    private double _pinchStartZoom;
    private bool _dragging;
    private Point _dragLast;

    public double OrbitAngle => _orbitDeg;
    public double SpinAngle => _spinDeg;

    /// <summary>
    /// 当前太阳直射纬度。公转视图按真实黄赤交角计算：
    /// 春分/秋分 0°、夏至 +23.5°、冬至 −23.5°；夸张倾角只影响示意图形，不影响教学读数。
    /// </summary>
    public double SubsolarLatitude
    {
        get
        {
            if (View == SceneView.Revolution)
            {
                var tilt = Math.Clamp(Scene?.Scene.Planet.AxialTiltDeg ?? 23.5, 0, 60);
                return Math.Asin(Math.Sin(Deg2Rad(tilt)) * Math.Sin(Deg2Rad(_orbitDeg))) * 57.2957795;
            }
            return Math.Clamp(DeclinationDeg, -66.5, 66.5);
        }
    }

    public string DeclinationLabel => DeclinationText(SubsolarLatitude);

    /// <summary>定格在二分二至时，日期直接取模型数据里的教材日期，避免近似公式差一两天。</summary>
    public string CurrentDate
    {
        get
        {
            if (View == SceneView.Revolution)
            {
                var season = NearestSeason(2.5);
                if (season?.Date != null) return FormatSeasonDate(season.Date);
            }
            return DateForAngle(_orbitDeg);
        }
    }

    private string FormatSeasonDate(string monthDay)
    {
        var parts = monthDay.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var d)
            && m is >= 1 and <= 12 && d is >= 1 and <= 31)
        {
            var date = new DateTime(2025, m, d);
            var chinese = Texts.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            return date.ToString(chinese ? "M月d日" : "MMM d", CultureInfo.InvariantCulture);
        }
        return monthDay;
    }

    public event Action? AnglesChanged;
    /// <summary>缩放目标变化（滚轮、按钮、捏合）。</summary>
    public event Action<double>? ZoomChanged;

    static CelestialView()
    {
        AffectsRender<CelestialView>(SceneProperty, ViewProperty, ShowLabelsProperty, ShowOrbitProperty,
            ShowGraticuleProperty, ExaggerateTiltProperty, IsPlayingProperty, SpeedProperty,
            DeclinationDegProperty, CameraElevationProperty, ShowKeyLatitudesProperty, InsetModeProperty,
            ZoomProperty, ShowCloudsProperty, ShowHudProperty);
    }

    private double DefaultElevation => double.IsNaN(CameraElevation)
        ? (View == SceneView.Revolution ? 26 : 8)
        : Math.Clamp(CameraElevation, 0, 82);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewProperty || change.Property == CameraElevationProperty)
        {
            // 视角只由视图/独立控件决定，拖动永远不会改它
            _camElevTarget = DefaultElevation;
            InvalidateVisual();
        }
        else if (change.Property == DeclinationDegProperty ||
                 change.Property == ShowKeyLatitudesProperty ||
                 change.Property == InsetModeProperty)
        {
            InvalidateVisual();
        }
    }

    public CelestialView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    // ---------------------------------------------------------------- 交互
    /// <summary>设置绝对缩放。捏合时 immediate=true 保证跟手。</summary>
    public void SetZoom(double value, bool immediate = false)
    {
        _zoomTarget = Math.Clamp(value, 0.6, 6.0);
        if (immediate) _zoom = _zoomTarget;
        SetCurrentValue(ZoomProperty, _zoomTarget);
        ZoomChanged?.Invoke(_zoomTarget);
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => SetZoom(_zoomTarget * factor);

    public void ResetView()
    {
        SetZoom(1.0, immediate: true);
        _camElevTarget = DefaultElevation;
        _spinVelocity = 0;
        _lastTick = Stopwatch.GetTimestamp();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Scene == null) return;

        var p = e.GetPosition(this);
        _pointers[e.Pointer.Id] = p;
        e.Pointer.Capture(this);

        if (_pointers.Count >= 2)
        {
            BeginPinch();
            _dragging = false;
        }
        else
        {
            _dragging = true;
            _pinching = false;
            _spinVelocity = 0;
            _dragLast = p;
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointers.ContainsKey(e.Pointer.Id)) return;

        var p = e.GetPosition(this);
        _pointers[e.Pointer.Id] = p;

        if (_pointers.Count >= 2)
        {
            UpdatePinch();
            e.Handled = true;
            return;
        }
        if (!_dragging) return;

        var dx = p.X - _dragLast.X;
        var dy = p.Y - _dragLast.Y;
        _dragLast = p;
        DragBy(dx, dy);
        e.Handled = true;
    }

    /// <summary>
    /// 手动转动地球。只接受水平分量：竖直分量在任何情况下都不会改变相机俯仰角，
    /// 因此轨道面、经纬网、地轴所在的平面始终固定。
    /// </summary>
    public void DragBy(double dx, double dy)
    {
        _ = dy;
        if (View == SceneView.Revolution)
        {
            _orbitDeg = Wrap360(_orbitDeg + dx * 0.32);
            _spinVelocity = 0;
        }
        else
        {
            _spinDeg = Wrap360(_spinDeg + dx * 0.42);
            _spinVelocity = dx * 0.42 * 55;
        }
        AnglesChanged?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pointers.Remove(e.Pointer.Id)) return;
        e.Pointer.Capture(null);

        if (_pointers.Count >= 2)
        {
            BeginPinch();
            _dragging = false;
        }
        else if (_pointers.Count == 1)
        {
            // 从捏合回到单指：重新建立拖动基准，避免地球突然跳一下
            _pinching = false;
            _dragging = true;
            _dragLast = _pointers.Values.First();
            _spinVelocity = 0;
        }
        else
        {
            _dragging = false;
            _pinching = false;
        }
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomBy(Math.Pow(1.18, e.Delta.Y));
        e.Handled = true;
    }

    private void BeginPinch()
    {
        var pts = _pointers.Values.Take(2).ToArray();
        _pinchStartDistance = Math.Max(8.0, Distance(pts[0], pts[1]));
        _pinchStartZoom = _zoomTarget;
        _pinching = true;
        _spinVelocity = 0;
    }

    private void UpdatePinch()
    {
        if (!_pinching) { BeginPinch(); return; }
        var pts = _pointers.Values.Take(2).ToArray();
        var d = Math.Max(8.0, Distance(pts[0], pts[1]));
        SetZoom(PinchZoom(_pinchStartZoom, _pinchStartDistance, d), immediate: true);
    }

    /// <summary>捏合比例换算：两指间距放大多少倍，缩放就放大多少倍（限幅 0.6–6）。</summary>
    public static double PinchZoom(double startZoom, double startDistance, double currentDistance)
        => Math.Clamp(startZoom * Math.Max(8.0, currentDistance) / Math.Max(8.0, startDistance), 0.6, 6.0);

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Wrap360(double deg) => ((deg % 360) + 360) % 360;

    // ---------------------------------------------------------------- 动画
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _camElevTarget = DefaultElevation;
        // 8ms 轮询以支撑最高 120 FPS（模型内部仍按 FrameRate 限流）；不依赖系统 VSync 节拍
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, OnTick);
        _lastTick = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>
    /// 窗口关闭时释放计时器与 WriteableBitmap：DispatcherTimer 的 Tick 事件会持有本控件实例，
    /// 不置空会让 collectible 的 ModelLoadContext 无法回收（模型卸载不干净）。
    /// </summary>
    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        _sphere?.Dispose();
        _sphere = null;
        _sphereSize = 0;
        GC.SuppressFinalize(this);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;
        if (dt > 0.25) dt = 0.25;

        if (Scene == null)
        {
            _animating = false;
            return;
        }

        _cloudShift += dt * (IsPlaying ? 1.6 : 0.18);
        if (_cloudShift > 360) _cloudShift -= 360;

        if (_animating && dt > 0.001 && dt < 0.5)
        {
            _frameAverage = _frameAverage <= 0 ? dt : _frameAverage * 0.92 + dt * 0.08;
            if (_frameAverage > 0.026 && MotionResolution > 320) MotionResolution -= 12;
            else if (_frameAverage < 0.015 && MotionResolution < 460) MotionResolution += 6;
        }

        var smoothing = Math.Min(1.0, dt * 10.0);
        var viewChanged = false;
        if (Math.Abs(_zoomTarget - _zoom) > 0.0005)
        {
            _zoom += (_zoomTarget - _zoom) * smoothing;
            viewChanged = true;
        }
        if (Math.Abs(_camElevTarget - _camElev) > 0.01)
        {
            _camElev += (_camElevTarget - _camElev) * smoothing;
            viewChanged = true;
        }

        var frameInterval = 1.0 / Math.Clamp(FrameRate, 12, 120);
        _accum += dt;
        var due = _accum >= frameInterval;
        if (!due && !viewChanged) return;
        var step = _accum;
        if (due) _accum = 0;

        // 手动转动/捏合时画面定格，松手后继续播放（不必先点暂停）
        var holding = _dragging || _pinching;
        if (IsPlaying && !holding)
        {
            var speed = Math.Clamp(Speed, 0.05, 8);
            _spinDeg = (_spinDeg + step * speed * 60.0) % 360.0;
            _orbitDeg = (_orbitDeg + step * speed * 12.0) % 360.0;
            _spinVelocity = 0;
        }
        else if (!IsPlaying && !_dragging && Math.Abs(_spinVelocity) > 0.4 && View == SceneView.Rotation)
        {
            _spinDeg = Wrap360(_spinDeg + _spinVelocity * step);
            _spinVelocity *= Math.Pow(0.02, step);
        }
        else
        {
            _spinVelocity = 0;
        }

        var wasAnimating = _animating;
        _animating = (IsPlaying && !holding) || _dragging || Math.Abs(_spinVelocity) > 0.4 || viewChanged;
        InvalidateVisual();
        if (wasAnimating && !_animating) InvalidateVisual();
    }

    public void JumpToOrbit(double deg)
    {
        _orbitDeg = Wrap360(deg);
        AnglesChanged?.Invoke();
        InvalidateVisual();
    }

    public void JumpToSpin(double deg)
    {
        _spinDeg = Wrap360(deg);
        AnglesChanged?.Invoke();
        InvalidateVisual();
    }

    public void StepOrbit(double deltaDeg)
    {
        _orbitDeg = Wrap360(_orbitDeg + deltaDeg);
        AnglesChanged?.Invoke();
        InvalidateVisual();
    }

    public void StepSpin(double deltaDeg)
    {
        _spinDeg = Wrap360(_spinDeg + deltaDeg);
        AnglesChanged?.Invoke();
        InvalidateVisual();
    }

    public void ResetAnimation()
    {
        _orbitDeg = 0;
        _spinDeg = 20;
        AnglesChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>取离当前轨道角最近的节气（超过阈值返回 null）。</summary>
    public SeasonPoint? NearestSeason(double maxDistanceDeg = 8)
    {
        var seasons = Scene?.Scene.Orbit.Seasons;
        if (seasons == null || seasons.Count == 0) return null;
        SeasonPoint? best = null;
        var bestDistance = double.MaxValue;
        foreach (var s in seasons)
        {
            var d = Math.Abs(WrapSigned(s.AngleDeg - _orbitDeg));
            if (d < bestDistance) { bestDistance = d; best = s; }
        }
        return bestDistance <= maxDistanceDeg ? best : null;
    }

    private static double WrapSigned(double deg) => ((deg + 180) % 360 + 360) % 360 - 180;

    // ---------------------------------------------------------------- 绘制
    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width < 80 || size.Height < 80) return;

        DrawSpace(ctx, size);

        var runtime = Scene;
        if (runtime == null)
        {
            DrawLoading(ctx, size);
            return;
        }

        if (View == SceneView.Revolution) RenderRevolution(ctx, size, runtime);
        else RenderRotation(ctx, size, runtime);
    }

    // 每帧 Render 都会调用：渐变画刷改成静态只读，避免在热路径反复分配。
    // 全部使用相对坐标，可安全共享；Radius -> RadiusX/RadiusY（旧 API 已过时）。
    private static readonly IBrush SpaceBg = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.15, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.85, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#080D1C"), 0),
            new GradientStop(Color.Parse("#0B1226"), 0.55),
            new GradientStop(Color.Parse("#04060E"), 1)
        }
    };

    private static readonly IBrush SpaceBand = new RadialGradientBrush
    {
        Center = new RelativePoint(0.72, 0.18, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.72, 0.18, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(1.1, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(26, 120, 150, 255), 0),
            new GradientStop(Color.FromArgb(0, 120, 150, 255), 1)
        }
    };

    private static readonly IBrush AtmosphereGlow = new RadialGradientBrush
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(90, 70, 140, 235), 0.60),
            new GradientStop(Color.FromArgb(34, 80, 150, 240), 0.80),
            new GradientStop(Color.FromArgb(0, 80, 150, 240), 1.0)
        }
    };

    private static readonly IBrush SunGlow = new RadialGradientBrush
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(120, 255, 196, 92), 0.10),
            new GradientStop(Color.FromArgb(48, 255, 168, 60), 0.34),
            new GradientStop(Color.FromArgb(0, 255, 150, 40), 1.0)
        }
    };

    private static readonly IBrush EarthHalo = new SolidColorBrush(Color.FromArgb(56, 70, 140, 235));

    private void DrawSpace(DrawingContext ctx, Size size)
    {
        ctx.FillRectangle(SpaceBg, new Rect(size));

        foreach (var star in Stars)
        {
            var brush = star.Level switch
            {
                0 => StarDim,
                1 => StarMid,
                _ => StarBright
            };
            ctx.DrawEllipse(brush, null, new Point(star.X * size.Width, star.Y * size.Height), star.R, star.R);
        }

        ctx.FillRectangle(SpaceBand, new Rect(size));
    }

    private void DrawLoading(DrawingContext ctx, Size size)
    {
        DrawText(ctx, Texts["loading"], size.Width / 2, size.Height / 2 - 8, 14, MutedBrush,
            FontWeight.Normal, TextAlignment.Center);
    }

    // ---------------------------------------------------------------- 自转视图
    private void RenderRotation(DrawingContext ctx, Size size, SceneRuntime runtime)
    {
        var w = size.Width;
        var h = size.Height;
        var center = new Point(w * 0.5, h * 0.52);
        var radius = Math.Min(w * 0.46, h * 0.44) * _zoom;
        if (radius < 24) return;

        var cam = CameraMatrix(-12, _camElev);
        var tilt = TiltDegrees(runtime);
        var planet = Matrix4x4.Multiply(Matrix4x4.CreateRotationY(Deg2Rad(_spinDeg)),
                                        Matrix4x4.CreateRotationZ(Deg2Rad(-tilt)));

        // 太阳光由「直射纬度」反推：直射点严格落在对应纬线上，且始终朝向右前方可见半球
        var light = RotationLight(cam, tilt);

        ctx.DrawEllipse(AtmosphereGlow, null, center, radius * 1.16, radius * 1.16);

        var px = PixelSizeFor(radius);
        if (px > 8 && runtime.Texture != null)
        {
            var bmp = EnsureSphereBitmap(px);
            SphereRaster.Draw(bmp, px, cam, planet, light, runtime.Texture, _cloudShift, ShowClouds);
            ctx.DrawImage(bmp, new Rect(0, 0, px, px),
                new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        }

        var showGrid = ShowGraticule && runtime.Scene.Planet.ShowGraticule && !InsetMode;
        if (showGrid) DrawGraticule(ctx, cam, planet, center, radius);
        if (ShowKeyLatitudes)
            DrawKeyLatitudes(ctx, cam, planet, center, radius, runtime, ShowLabels && radius > 88);

        DrawTerminator(ctx, light, center, radius);
        DrawSubsolarMarker(ctx, center, radius, light, ShowLabels && !InsetMode);
        DrawAxis(ctx, cam, planet, center, radius, runtime, ShowLabels && !InsetMode);

        if (InsetMode) return;
        DrawSunlightArrows(ctx, center, radius);

        var period = runtime.Scene.Planet.RotationHours;
        if (ShowHud) DrawHud(ctx, new Point(20, 78), new[]
        {
            Texts.F("spinInfo", _spinDeg.ToString("0"), period.ToString("0.##")),
            Texts.F("declinationInfo", DeclinationText(SubsolarLatitude))
        });
    }

    /// <summary>构造相机空间的阳光方向，使其与地轴的夹角对应指定直射纬度。</summary>
    private Vector3 RotationLight(Matrix4x4 cam, double tiltDeg)
    {
        var axisCam = Vector3.TransformNormal(AxisWorld(tiltDeg), cam);
        var forwardRight = Vector3.Normalize(new Vector3(0.86f, 0f, 0.50f));
        var u = forwardRight - axisCam * Vector3.Dot(forwardRight, axisCam);
        if (u.Length() < 0.001f) u = new Vector3(1, 0, 0);
        u = Vector3.Normalize(u);
        var d = Deg2Rad(SubsolarLatitude);
        return Vector3.Normalize(axisCam * (float)Math.Sin(d) + u * (float)Math.Cos(d));
    }

    private static Vector3 AxisWorld(double tiltDeg)
        => Vector3.Transform(new Vector3(0, 1, 0), Matrix4x4.CreateRotationZ(Deg2Rad(-tiltDeg)));

    private static double TropicDegrees(SceneRuntime runtime)
    {
        var t = runtime.Scene.Planet.TropicDeg;
        if (t <= 0 || t > 45) t = Math.Abs(runtime.Scene.Planet.AxialTiltDeg);
        if (t <= 0 || t > 45) t = 23.5;
        return t;
    }

    /// <summary>教材五带分界线：赤道、南北回归线（23.5°）、南北极圈（66.5°）。</summary>
    private void DrawKeyLatitudes(DrawingContext ctx, Matrix4x4 cam, Matrix4x4 planet,
        Point center, double radius, SceneRuntime runtime, bool labels)
    {
        var tropic = TropicDegrees(runtime);
        var polar = 90.0 - tropic;

        // 五条纬线一起画：先画线，再统一布置标签，避免自转时多个标签
        // 被投影到相近位置而互相覆盖 / 抖动（原来的逐条独立绘制会重叠）。
        var lines = new (double Lat, IPen Pen, string? Label, IBrush Brush)[]
        {
            (0, EquatorPen, labels ? Texts["lat.equator"] : null, LabelBrush),
            (tropic, TropicPen, labels ? Texts["lat.tropicN"] : null, TropicBrush),
            (-tropic, TropicPen, labels ? Texts["lat.tropicS"] : null, TropicBrush),
            (polar, PolarPen, labels ? Texts["lat.polarN"] : null, PolarBrush),
            (-polar, PolarPen, labels ? Texts["lat.polarS"] : null, PolarBrush)
        };

        foreach (var l in lines)
            DrawSpherePolyline(ctx, cam, planet, center, radius, l.Pen,
                t => LatLonToUnit(l.Lat, t * 360.0), 144);

        if (!labels) return;

        // 收集每个标签的锚点，按屏幕 Y 排序后做垂直去重叠
        var placed = new List<(double Y, double X, string Text, IBrush Brush)>();
        foreach (var l in lines)
        {
            if (l.Label == null) continue;
            Vector3 best = default;
            var bestScore = double.NegativeInfinity;
            for (var lon = -180; lon < 180; lon += 6)
            {
                var world = Vector3.Transform(LatLonToUnit(l.Lat, lon), planet);
                var c = Vector3.Transform(world, cam);
                if (c.Z < 0.10f) continue;
                var score = c.X * 2.0 + c.Z;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (double.IsNegativeInfinity(bestScore)) continue;
            placed.Add((center.Y - best.Y * radius, center.X + best.X * radius, l.Label, l.Brush));
        }

        placed.Sort((a, b) => a.Y.CompareTo(b.Y));
        const double minGap = 16.0;
        var lastY = double.NegativeInfinity;
        foreach (var p in placed)
        {
            var y = p.Y;
            if (y - lastY < minGap) y = lastY + minGap;
            lastY = y;
            DrawLabelChip(ctx, p.Text, new Point(p.X, y), InsetMode ? 9.0 : 10.5, p.Brush, toRight: false);
        }
    }

    /// <summary>带深色底衬的标签：压在明亮的地球上也读得清，且不会被画布边缘裁掉。</summary>
    private static void DrawLabelChip(DrawingContext ctx, string text, Point anchor,
        double fontSize, IBrush brush, bool toRight)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), fontSize, brush)
        {
            TextAlignment = TextAlignment.Left,
            Trimming = TextTrimming.None
        };
        var x = toRight ? anchor.X + 9 : Math.Max(3, anchor.X - formatted.Width - 9);
        var y = anchor.Y - formatted.Height - 4;
        ctx.DrawRectangle(ChipBg, null,
            new Rect(x - 5, y - 2, formatted.Width + 10, formatted.Height + 5), 4, 4);
        ctx.DrawText(formatted, new Point(x, y));
    }

    private void DrawGraticule(DrawingContext ctx, Matrix4x4 cam, Matrix4x4 planet, Point center, double radius)
    {
        for (var lat = -75; lat <= 75; lat += 15)
        {
            var pen = Math.Abs(lat) < 1 ? EquatorPen : GridPen;
            DrawSpherePolyline(ctx, cam, planet, center, radius, pen, (t) =>
            {
                var lon = t * 360.0;
                return LatLonToUnit(lat, lon);
            }, 120);
        }

        for (var lon = 0; lon < 360; lon += 30)
        {
            var l = lon;
            DrawSpherePolyline(ctx, cam, planet, center, radius, GridPen, (t) =>
            {
                var lat = -90 + t * 180.0;
                return LatLonToUnit(lat, l);
            }, 90);
        }
    }

    private void DrawSpherePolyline(DrawingContext ctx, Matrix4x4 cam, Matrix4x4 planet, Point center,
        double radius, IPen pen, Func<double, Vector3> pointAt, int steps)
    {
        Point? prev = null;
        for (var i = 0; i <= steps; i++)
        {
            var local = pointAt(i / (double)steps);
            var world = Vector3.Transform(local, planet);
            var c = Vector3.Transform(world, cam);
            if (c.Z <= 0.02)
            {
                prev = null;
                continue;
            }
            var p = new Point(center.X + c.X * radius, center.Y - c.Y * radius);
            if (prev.HasValue) ctx.DrawLine(pen, prev.Value, p);
            prev = p;
        }
    }

    private void DrawTerminator(DrawingContext ctx, Vector3 light, Point center, double radius)
    {
        var up = Math.Abs(light.Y) > 0.92f ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
        var u = Vector3.Normalize(Vector3.Cross(light, up));
        var v = Vector3.Normalize(Vector3.Cross(light, u));

        Point? prev = null;
        for (var i = 0; i <= 180; i++)
        {
            var a = i / 180.0 * Math.PI * 2;
            var n = u * (float)Math.Cos(a) + v * (float)Math.Sin(a);
            if (n.Z <= 0.03f)
            {
                prev = null;
                continue;
            }
            var p = new Point(center.X + n.X * radius, center.Y - n.Y * radius);
            if (prev.HasValue) ctx.DrawLine(TerminatorPen, prev.Value, p);
            prev = p;
        }
    }

    /// <summary>球面直射点：黄圈 + 纬度读数，位置由光照方向（即日下点）精确投影。</summary>
    private void DrawSubsolarMarker(DrawingContext ctx, Point center, double radius,
        Vector3 light, bool withLabel)
    {
        if (light.Z <= 0.02f) return;
        var p = new Point(center.X + light.X * radius, center.Y - light.Y * radius);
        var marker = InsetMode ? 6.5 : 5.0;
        ctx.DrawEllipse(SubsolarGlowBrush, null, p, marker * 2.5, marker * 2.5);
        ctx.DrawEllipse(null, SubsolarPen, p, marker, marker);
        if (withLabel)
            DrawLabelChip(ctx, Texts["directPoint"] + " " + DeclinationText(SubsolarLatitude),
                p, 11.5, SubsolarBrush, toRight: p.X < center.X);
    }

    private void DrawAxis(DrawingContext ctx, Matrix4x4 cam, Matrix4x4 planet, Point center, double radius,
        SceneRuntime runtime, bool showLabels)
    {
        var top = Vector3.Transform(Vector3.Transform(new Vector3(0, 1.32f, 0), planet), cam);
        var bottom = Vector3.Transform(Vector3.Transform(new Vector3(0, -1.32f, 0), planet), cam);
        var pTop = new Point(center.X + top.X * radius, center.Y - top.Y * radius);
        var pBottom = new Point(center.X + bottom.X * radius, center.Y - bottom.Y * radius);
        ctx.DrawLine(AxisPen, pTop, pBottom);

        if (!showLabels) return;
        DrawText(ctx, Texts["north"], pTop.X + 8, pTop.Y - 16, 11.5, AxisBrush);
        DrawText(ctx, Texts["south"], pBottom.X + 8, pBottom.Y + 2, 11.5, AxisBrush);
    }

    private void DrawSunlightArrows(DrawingContext ctx, Point center, double radius)
    {
        for (var i = -1; i <= 1; i++)
        {
            var y = center.Y + i * radius * 0.42;
            var x0 = center.X + radius * 1.42;
            var x1 = center.X + radius * 1.06;
            ctx.DrawLine(SunRayPen, new Point(x0, y), new Point(x1, y));
            ctx.DrawLine(SunRayPen, new Point(x1, y), new Point(x1 + 8, y - 5));
            ctx.DrawLine(SunRayPen, new Point(x1, y), new Point(x1 + 8, y + 5));
        }
    }

    // ---------------------------------------------------------------- 公转视图
    private void RenderRevolution(DrawingContext ctx, Size size, SceneRuntime runtime)
    {
        var w = size.Width;
        var h = size.Height;
        var center = new Point(w * 0.5, h * 0.51);
        var a = Math.Min(w * 0.36, h * 0.38) * _zoom;
        if (a < 40) return;

        var earthR = Math.Clamp(Math.Min(w, h) * 0.052, 16, 34) * (0.65 + 0.35 * _zoom);
        var sunR = earthR * 1.05;
        var cam = CameraMatrix(0, _camElev);

        var theta = Deg2Rad(_orbitDeg);
        var earthPos = new Vector3((float)(-a * Math.Sin(theta)), 0, (float)(-a * Math.Cos(theta)));
        var earthScreen = ToScreen(earthPos, cam, center);
        var lightWorld = Vector3.Normalize(-earthPos);
        var lightCam = Vector3.TransformNormal(lightWorld, cam);

        ctx.DrawEllipse(SunGlow, null, center, sunR * 6.2, sunR * 6.2);

        // sunBody 依赖场景里的 runtime.Star，不能静态化；仅把过时 API 换成 RadiusX/Y
        var sunBody = new RadialGradientBrush
        {
            Center = new RelativePoint(0.42, 0.38, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.42, 0.38, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.62, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.62, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#FFF3D0"), 0),
                new GradientStop(runtime.Star, 0.62),
                new GradientStop(Color.Parse("#F08A1E"), 1)
            }
        };
        ctx.DrawEllipse(sunBody, null, center, sunR, sunR);

        if (ShowOrbit) DrawOrbit(ctx, cam, center, a);

        var tilt = TiltDegrees(runtime);
        var planet = Matrix4x4.Multiply(Matrix4x4.CreateRotationY(Deg2Rad(_spinDeg)),
                                        Matrix4x4.CreateRotationZ(Deg2Rad(-tilt)));
        var px = PixelSizeFor(earthR);
        if (px > 8)
        {
            ctx.DrawEllipse(EarthHalo, null,
                earthScreen, earthR * 1.35, earthR * 1.35);
            var bmp = EnsureSphereBitmap(px);
            if (runtime.Texture != null)
                SphereRaster.Draw(bmp, px, cam, planet, lightCam, runtime.Texture, _cloudShift, ShowClouds);
            ctx.DrawImage(bmp, new Rect(0, 0, px, px),
                new Rect(earthScreen.X - earthR, earthScreen.Y - earthR, earthR * 2, earthR * 2));
        }

        var axis = AxisWorld(tilt);
        var axisTop = ToScreen(earthPos + axis * (float)(earthR * 1.9), cam, center);
        var axisBottom = ToScreen(earthPos - axis * (float)(earthR * 1.9), cam, center);
        ctx.DrawLine(AxisPen, axisTop, axisBottom);
        if (ShowLabels) DrawText(ctx, Texts["north"], axisTop.X + 6, axisTop.Y - 14, 10.5, AxisBrush);

        ctx.DrawLine(SunRayPen, center, ToScreen(earthPos * 0.92f, cam, center));
        if (ShowLabels) DrawText(ctx, Texts["sun"], center.X, center.Y + sunR + 8, 12, SunLabelBrush,
            FontWeight.SemiBold, TextAlignment.Center);

        if (ShowLabels) DrawSeasonMarkers(ctx, cam, center, a, runtime);
        if (ShowLabels) DrawSubsolarMarker(ctx, earthScreen, earthR, lightCam, false);

        if (ShowLabels)
        {
            DrawText(ctx, Texts["earth"], earthScreen.X, earthScreen.Y - earthR - 20, 12,
                PlanetLabelBrush, FontWeight.SemiBold, TextAlignment.Center);
        }

        if (ShowHud) DrawHud(ctx, new Point(20, 78), new[]
        {
            Texts.F("orbitInfo", _orbitDeg.ToString("0")),
            Texts.F("declinationInfo", DeclinationLabel),
            Texts.F("dateInfo", CurrentDate)
        });
    }

    private void DrawOrbit(DrawingContext ctx, Matrix4x4 cam, Point center, double a)
    {
        Point? prevBack = null;
        Point? prevFront = null;
        for (var i = 0; i <= 240; i++)
        {
            var t = i / 240.0 * Math.PI * 2;
            var p = new Vector3((float)(-a * Math.Sin(t)), 0, (float)(-a * Math.Cos(t)));
            var c = Vector3.Transform(p, cam);
            var sp = new Point(center.X + c.X, center.Y - c.Y);
            if (c.Z < 0)
            {
                if (prevBack.HasValue) ctx.DrawLine(OrbitBackPen, prevBack.Value, sp);
                prevBack = sp;
                prevFront = null;
            }
            else
            {
                if (prevFront.HasValue) ctx.DrawLine(OrbitFrontPen, prevFront.Value, sp);
                prevFront = sp;
                prevBack = null;
            }
        }
    }

    private void DrawSeasonMarkers(DrawingContext ctx, Matrix4x4 cam, Point center, double a, SceneRuntime runtime)
    {
        var seasons = runtime.Scene.Orbit.Seasons;
        if (seasons.Count == 0)
        {
            seasons = new List<SeasonPoint>
            {
                new() { AngleDeg = 0, Key = "season.springEquinox", Date = "3/21" },
                new() { AngleDeg = 90, Key = "season.summerSolstice", Date = "6/22" },
                new() { AngleDeg = 180, Key = "season.autumnEquinox", Date = "9/23" },
                new() { AngleDeg = 270, Key = "season.winterSolstice", Date = "12/22" }
            };
        }

        foreach (var s in seasons)
        {
            var t = Deg2Rad(s.AngleDeg);
            var pos = new Vector3((float)(-a * Math.Sin(t)), 0, (float)(-a * Math.Cos(t)));
            var outer = pos * 1.19f;
            var p = ToScreen(outer, cam, center);
            ctx.DrawEllipse(SeasonDotBrush, null, ToScreen(pos, cam, center), 2.4, 2.4);

            DrawText(ctx, Texts[s.Key], p.X, p.Y - 16, 12, SeasonBrush, FontWeight.SemiBold, TextAlignment.Center);
            if (!string.IsNullOrEmpty(s.Date))
                DrawText(ctx, s.Date, p.X, p.Y - 2, 10, MutedBrush, FontWeight.Normal, TextAlignment.Center);
        }
    }

    // ---------------------------------------------------------------- 工具
    private static Matrix4x4 CameraMatrix(double azimDeg, double elevDeg)
        => Matrix4x4.Multiply(Matrix4x4.CreateRotationY(Deg2Rad(azimDeg)), Matrix4x4.CreateRotationX(Deg2Rad(elevDeg)));

    private static Point ToScreen(Vector3 world, Matrix4x4 cam, Point center)
    {
        var c = Vector3.Transform(world, cam);
        return new Point(center.X + c.X, center.Y - c.Y);
    }

    private static Vector3 LatLonToUnit(double latDeg, double lonDeg)
    {
        var lat = Deg2Rad(latDeg);
        var lon = Deg2Rad(lonDeg);
        var cosLat = Math.Cos(lat);
        return new Vector3((float)(cosLat * Math.Sin(lon)), (float)Math.Sin(lat), (float)(cosLat * Math.Cos(lon)));
    }

    private double TiltDegrees(SceneRuntime runtime)
    {
        var tilt = runtime.Scene.Planet.AxialTiltDeg;
        if (ExaggerateTilt) tilt *= 1.9;
        return Math.Clamp(tilt, 0, 60);
    }

    private int PixelSizeFor(double logicalRadius)
    {
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var target = logicalRadius * 2 * scale;
        var cap = _animating ? MotionResolution : 820;
        return Math.Max(8, (int)Math.Round(Math.Min(target, cap)));
    }

    private WriteableBitmap EnsureSphereBitmap(int px)
    {
        if (_sphere != null && _sphereSize == px) return _sphere;
        // 自适应分辨率会频繁改 px，旧位图必须先释放，否则每帧都可能泄漏一张 ~0.8MB 位图
        _sphere?.Dispose();
        _sphere = new WriteableBitmap(new PixelSize(px, px), new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        _sphereSize = px;
        return _sphere;
    }

    private static float Deg2Rad(double deg) => (float)(deg * Math.PI / 180.0);

    private static readonly (double X, double Y, double R, int Level)[] Stars = BuildStars();

    private static (double, double, double, int)[] BuildStars()
    {
        var rnd = new Random(20240501);
        var list = new (double, double, double, int)[340];
        for (var i = 0; i < list.Length; i++)
        {
            var level = rnd.NextDouble() switch
            {
                < 0.62 => 0,
                < 0.90 => 1,
                _ => 2
            };
            list[i] = (rnd.NextDouble(), rnd.NextDouble(), level == 2 ? 1.5 : 0.7 + rnd.NextDouble() * 0.5, level);
        }
        return list;
    }

    private static readonly IBrush StarDim = new SolidColorBrush(Color.FromArgb(80, 200, 218, 255));
    private static readonly IBrush StarMid = new SolidColorBrush(Color.FromArgb(145, 226, 238, 255));
    private static readonly IBrush StarBright = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromArgb(165, 186, 204, 232));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(235, 238, 246, 255));
    private static readonly IBrush SeasonBrush = new SolidColorBrush(Color.FromArgb(235, 255, 226, 168));
    private static readonly IBrush SeasonDotBrush = new SolidColorBrush(Color.FromArgb(210, 255, 214, 130));
    private static readonly IBrush SunLabelBrush = new SolidColorBrush(Color.FromArgb(240, 255, 214, 140));
    private static readonly IBrush PlanetLabelBrush = new SolidColorBrush(Color.FromArgb(240, 170, 210, 255));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromArgb(220, 255, 214, 130));
    private static readonly IBrush SubsolarBrush = new SolidColorBrush(Color.FromArgb(240, 255, 236, 170));
    private static readonly IBrush SubsolarGlowBrush = new SolidColorBrush(Color.FromArgb(64, 255, 214, 110));
    private static readonly IBrush ChipBg = new SolidColorBrush(Color.FromArgb(185, 8, 16, 34));
    private static readonly IBrush TropicBrush = new SolidColorBrush(Color.FromArgb(240, 255, 196, 120));
    private static readonly IBrush PolarBrush = new SolidColorBrush(Color.FromArgb(210, 164, 210, 255));
    private static readonly Pen SubsolarPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 226, 150)), 1.4);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(44, 190, 220, 255)), 0.9);
    private static readonly IPen EquatorPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 130, 235, 255)), 1.6);
    private static readonly IPen TropicPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 186, 92)), 1.5,
        new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly IPen PolarPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 140, 190, 255)), 1.2,
        new DashStyle(new double[] { 3, 5 }, 0));
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 210, 120)), 1.3,
        new DashStyle(new double[] { 7, 5 }, 0));
    private static readonly IPen TerminatorPen = new Pen(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), 1.1,
        new DashStyle(new double[] { 4, 4 }, 0));
    private static readonly IPen SunRayPen = new Pen(new SolidColorBrush(Color.FromArgb(95, 255, 226, 160)), 1.0,
        new DashStyle(new double[] { 5, 5 }, 0));
    private static readonly IPen OrbitFrontPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 150, 190, 255)), 1.2);
    private static readonly IPen OrbitBackPen = new Pen(new SolidColorBrush(Color.FromArgb(48, 150, 190, 255)), 1.0);

    private static void DrawText(DrawingContext ctx, string text, double x, double y, double fontSize,
        IBrush brush, FontWeight weight = FontWeight.Normal, TextAlignment align = TextAlignment.Left)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), fontSize, brush);
        var px = align switch
        {
            TextAlignment.Center => x - formatted.Width / 2,
            TextAlignment.Right => x - formatted.Width,
            _ => x
        };
        ctx.DrawText(formatted, new Point(px, y));
    }

    private static void DrawHud(DrawingContext ctx, Point at, string[] lines)
    {
        if (lines.Length == 0) return;
        const double fontSize = 11.5;
        var texts = new List<FormattedText>();
        double width = 0;
        double height = 0;
        foreach (var line in lines)
        {
            var ft = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Normal), fontSize, LabelBrush);
            texts.Add(ft);
            width = Math.Max(width, ft.Width);
            height += ft.Height + 3;
        }

        var rect = new Rect(at.X, at.Y, width + 22, height + 12);
        ctx.DrawRectangle(HudBrush, HudPen, rect, 8, 8);
        var y = at.Y + 6;
        foreach (var ft in texts)
        {
            ctx.DrawText(ft, new Point(at.X + 11, y));
            y += ft.Height + 3;
        }
    }

    private static readonly IBrush HudBrush = new SolidColorBrush(Color.FromArgb(96, 10, 20, 40));
    private static readonly IPen HudPen = new Pen(new SolidColorBrush(Color.FromArgb(46, 120, 170, 255)), 1);

    private string DeclinationText(double decl)
    {
        if (Math.Abs(decl) < 0.05) return "0° " + Texts["lat.equator"];
        var suffix = decl > 0 ? Texts["northShort"] : Texts["southShort"];
        return $"{Math.Abs(decl):0.0}°{suffix}";
    }

    private string DateForAngle(double deg)
    {
        var dayOfYear = 79 + deg / 360.0 * 365.2422;
        var date = new DateTime(2025, 1, 1).AddDays(dayOfYear);
        var chinese = Texts.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        return date.ToString(chinese ? "M月d日" : "MMM d", CultureInfo.InvariantCulture);
    }
}
