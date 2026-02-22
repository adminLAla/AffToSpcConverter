using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AffToSpcConverter.Convert.Preview;
using AffToSpcConverter.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AffToSpcConverter.Views
{
    public enum PreviewViewMode { Split, Merged }

    public enum AddNoteType { Tap, Hold, Flick, SkyArea }

    public sealed class AddNoteRequest
    {
        public AddNoteType Type { get; init; }
        public int GroundWidth { get; init; } = 1;
        public int Den { get; init; } = 24;
        public int WidthNum { get; init; } = 1;
        public int WidthNum2 { get; init; } = 1;
        public int Dir { get; init; } = 4;
        public int LeftEase { get; init; }
        public int RightEase { get; init; }
        public int GroupId { get; init; } = 1;
    }

    public sealed record AddNotePlacement(
        AddNoteRequest Request,
        int StartTimeMs,
        int EndTimeMs,
        int Lane,
        int PosNum,
        int PosNum2
    );

    /// <summary>
    /// High-performance Skia preview control supporting split and merged overlay views.
    /// </summary>
    public sealed class SpcPreviewGLControl : SKElement, IDisposable
    {
        // ===== 渲染循环 =====
        private Thread? _renderThread;
        private volatile bool _running;
        private const double DefaultTargetFps = 120.0;
        private double _frameTimeMs = 1000.0 / DefaultTargetFps;

        // ===== 帧率 / 帧时间 =====
        private int _frameCount;
        private long _lastFpsTick;
        private int _fps;
        private double _frameTimeSmoothed;
        private long _prevFrameTick;
        private const int FpsSampleMs = 100;

        // ===== 快照 =====
        private long _snapJudgeTimeMsBits;
        private long _snapPxPerMsBits;
        private volatile int _snapSelectedIndex = -1;
        private RenderModel? _snapModel;
        private readonly object _modelLock = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteDouble(ref long storage, double value)
            => Interlocked.Exchange(ref storage, BitConverter.DoubleToInt64Bits(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ReadDouble(ref long storage)
            => BitConverter.Int64BitsToDouble(Interlocked.Read(ref storage));

        // ===== View mode =====
        private volatile PreviewViewMode _viewMode = PreviewViewMode.Split;
        public PreviewViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                if (_viewMode == value) return;
                _viewMode = value;
                RecalcLayout();
            }
        }

        // ===== 布局 =====
        // 分离模式：天空与地面并排
        // 合并模式：地面全宽，天空居中叠加
        private SKRect _groundRect, _skyRect;
        private float _judgeY, _contentLeft, _contentW, _panelH, _ctrlW, _ctrlH;
        private readonly object _layoutLock = new();

        // ===== 常量 =====
        private const double MinPxPerMs = 0.02;
        private const double MaxPxPerMs = 1.20;
        private const double RulerW = 60.0;
        private const double JudgeFromBottom = 100.0;
        private const double TopPad = 8.0;
        private const int GroundLanes = 6;
        private const int SkyDivisions = 8;
        private const float TapHalfH = 22f;   // triH(45) / 2，上下各 22px
        private const float FlickTriH = 45f;
        private const float SkyToGroundWidthRatio = 4f / 6f;

        // ===== 画笔缓存 =====
        private static readonly SKPaint BgPaint = MkFill(15, 15, 18);
        private static readonly SKPaint PanelBgPaint = MkFill(18, 18, 24);
        private static readonly SKPaint PanelBorder = MkStroke(55, 55, 70, 1);
        private static readonly SKPaint GroundGridPaint = MkStroke(35, 35, 46, 1);
        private static readonly SKPaint GroundLaneBasePaint = MkFill(0, 0, 0);
        private static readonly SKPaint GroundLane0OverlayPaint = MkFill(190, 130, 255, 80);
        private static readonly SKPaint GroundLane5OverlayPaint = MkFill(255, 90, 90, 80);
        private static readonly SKPaint SkyOverlayBg = MkFill(25, 20, 40, 50);
        private static readonly SKPaint SkyBorderPaint = MkStroke(80, 60, 130, 1, alpha: 120);
        private static readonly SKPaint SkyGridPaint = MkStroke(50, 40, 80, 1, alpha: 80);
        private static readonly SKPaint SkyGridPaintSplit = MkStroke(35, 35, 46, 1);
        private static readonly SKPaint GroundLabelPaint = MkText(120, 120, 140, 11);
        private static readonly SKPaint SkyLabelPaint = MkText(140, 110, 200, 11);
        private static readonly SKPaint MeasurePaint = MkStroke(80, 86, 112, 1.8f);
        private static readonly SKPaint BeatPaint = MkStroke(55, 58, 78, 1.4f, new float[] { 1, 6 });
        private static readonly SKPaint RulerLabelPaint = MkText(130, 130, 155, 10);
        private static readonly SKPaint JudgePaint = MkStroke(220, 60, 30, 2f);
        private static readonly SKPaint JudgeTextPaint = MkText(220, 60, 30, 12);
        private static readonly SKPaint TapFillDeepBlue = MkFill(40, 70, 150);
        private static readonly SKPaint TapStrokeDeepBlue = MkStroke(80, 120, 210, 1);
        private static readonly SKPaint TapFillWhite = MkFill(170, 210, 255);
        private static readonly SKPaint TapStrokeWhite = MkStroke(210, 235, 255, 1);
        private static readonly SKPaint TapFillPurple = MkFill(210, 140, 255);
        private static readonly SKPaint TapStrokePurple = MkStroke(235, 190, 255, 1);
        private static readonly SKPaint TapFillRed = MkFill(255, 90, 90);
        private static readonly SKPaint TapStrokeRed = MkStroke(255, 140, 140, 1);
        private static readonly SKPaint HoldFillDeepBlue = MkFill(40, 70, 150, 120);
        private static readonly SKPaint HoldStrokeDeepBlue = MkStroke(80, 120, 210, 1, alpha: 200);
        private static readonly SKPaint HoldFillWhite = MkFill(170, 210, 255, 120);
        private static readonly SKPaint HoldStrokeWhite = MkStroke(210, 235, 255, 1, alpha: 220);
        private static readonly SKPaint HoldFillPurple = MkFill(210, 140, 255, 120);
        private static readonly SKPaint HoldStrokePurple = MkStroke(235, 190, 255, 1, alpha: 220);
        private static readonly SKPaint HoldFillRed = MkFill(255, 90, 90, 120);
        private static readonly SKPaint HoldStrokeRed = MkStroke(255, 140, 140, 1, alpha: 220);
        private static readonly SKPaint FlickFillLeftPaint = MkFill(220, 200, 80);
        private static readonly SKPaint FlickStrokeLeftPaint = MkStroke(255, 240, 120, 1.5f);
        private static readonly SKPaint FlickFillRightPaint = MkFill(80, 200, 120);
        private static readonly SKPaint FlickStrokeRightPaint = MkStroke(120, 240, 160, 1.5f);
        private static readonly SKPaint SkyAreaFillPaint = MkFill(140, 100, 230, 70);
        private static readonly SKPaint SkyAreaStrokePaint = MkStroke(180, 150, 255, 1, alpha: 160);
        private static readonly SKPaint SkyAreaStripePaint = MkStripePaint(255, 255, 255, 25, 80f);
        // 选中高亮：白色（填充半透明，描边不透明）
        private static readonly SKPaint SelectedFillPaint = MkFill(255, 255, 255, 70);
        private static readonly SKPaint SelectedStrokePaint = MkStroke(255, 255, 255, 2);
        private static readonly SKPaint FpsPaint = MkText(0, 255, 0, 12);

        private static readonly double[] RulerMultiples = { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0 };

        // ===== 缓存 =====
        private readonly Dictionary<int, string> _rulerTextCache = new();
        private double _rulerCachePxPerMs;
        private readonly Dictionary<int, (float skyLeft, float skyWidth, SKPath path)> _flickPathCache = new();

        // ===== 音符选中事件 =====
        public event Action<RenderItem?>? NoteSelected;
        public event Action<AddNotePlacement>? NotePlacementCommitted;

        private AddNoteRequest? _pendingAddRequest;
        private bool _placingNoteDrag;
        private int _placeStartTimeMs;
        private int _placeStartLane;
        private int _placeStartPosNum;

        public SpcPreviewGLControl()
        {
            Focusable = true;
            IgnorePixelScaling = true;
            WriteDouble(ref _snapPxPerMsBits, 0.12);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RecalcLayout();
            StartRenderLoop();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => StopRenderLoop();

        private void StartRenderLoop()
        {
            if (_running) return;
            _running = true;
            _lastFpsTick = Stopwatch.GetTimestamp();
            _prevFrameTick = _lastFpsTick;
            _renderThread = new Thread(RenderLoop)
            {
                Name = "SpcPreviewRenderLoop",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _renderThread.Start();
        }

        private void StopRenderLoop()
        {
            _running = false;
            _renderThread?.Join(200);
            _renderThread = null;
        }

        private Action? _invalidateAction;

        private void RenderLoop()
        {
            _invalidateAction = InvalidateVisual;
            var sw = Stopwatch.StartNew();
            double nextFrameMs = sw.Elapsed.TotalMilliseconds;

            while (_running)
            {
                double now = sw.Elapsed.TotalMilliseconds;
                double frameTimeMs = Volatile.Read(ref _frameTimeMs);
                double sleepMs = nextFrameMs - now;

                if (sleepMs > 2.0)
                    Thread.Sleep(Math.Max(1, (int)(sleepMs - 1.5)));
                else if (sleepMs < -frameTimeMs * 2)
                    nextFrameMs = sw.Elapsed.TotalMilliseconds;

                while (sw.Elapsed.TotalMilliseconds < nextFrameMs)
                    Thread.SpinWait(8);

                nextFrameMs += frameTimeMs;

                try
                {
                    Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Render,
                        _invalidateAction!);
                }
                catch { break; }
            }
        }

        // ===== 大小变化 =====

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            RecalcLayout();
        }

        private void RecalcLayout()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 4 || h <= 4) return;

            float judgeY = (float)Math.Max(TopPad + 20, h - JudgeFromBottom);
            float contentLeft = (float)RulerW;
            float contentW = (float)(w - RulerW);
            if (contentW < 10) return;

            float panelH = judgeY - (float)TopPad;
            if (panelH < 4) return;

            lock (_layoutLock)
            {
                _ctrlW = (float)w;
                _ctrlH = (float)h;
                _judgeY = judgeY;
                _contentLeft = contentLeft;
                _contentW = contentW;
                _panelH = panelH;

                if (_viewMode == PreviewViewMode.Split)
                {
                    // Side by side: ground right, sky left
                    float skyW = contentW * 0.50f;
                    float gndW = contentW - skyW;
                    _skyRect = new SKRect(contentLeft, (float)TopPad, contentLeft + skyW, (float)TopPad + panelH);
                    _groundRect = new SKRect(contentLeft + skyW, (float)TopPad, contentLeft + contentW, (float)TopPad + panelH);
                }
                else
                {
                    // Merged: ground = full width, sky = centered at 4/6 width of ground
                    _groundRect = new SKRect(contentLeft, (float)TopPad, contentLeft + contentW, (float)TopPad + panelH);
                    float skyW = _groundRect.Width * SkyToGroundWidthRatio;
                    float skyLeft = _groundRect.MidX - skyW * 0.5f;
                    _skyRect = new SKRect(skyLeft, (float)TopPad, skyLeft + skyW, (float)TopPad + panelH);
                }
            }

            SpcSkiaGeometryBuilder.ClearCache();
            ClearFlickPathCache();
            ClearRulerTextCache();
        }

        // ===== Dependency Properties =====

        public IReadOnlyList<ISpcEvent>? Events
        {
            get => (IReadOnlyList<ISpcEvent>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }
        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(IReadOnlyList<ISpcEvent>), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(null, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    var model = c.Events != null ? SpcRenderModelBuilder.Build(c.Events) : null;
                    lock (c._modelLock) { c._snapModel = model; }
                    SpcSkiaGeometryBuilder.ClearCache();
                    c.ClearFlickPathCache();
                    c.ClearRulerTextCache();
                }));

        public double PixelsPerSecond
        {
            get => (double)GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }
        public static readonly DependencyProperty PixelsPerSecondProperty =
            DependencyProperty.Register(nameof(PixelsPerSecond), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(240.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    c.UpdatePxPerMs();
                }));

        public double Speed
        {
            get => (double)GetValue(SpeedProperty);
            set => SetValue(SpeedProperty, value);
        }
        public static readonly DependencyProperty SpeedProperty =
            DependencyProperty.Register(nameof(Speed), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(1.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    c.UpdatePxPerMs();
                }));

        private void UpdatePxPerMs()
        {
            var speed = Math.Max(0.01, Speed);
            PxPerMs = PixelsPerSecond * speed / 1000.0;
        }

        public RenderModel? Model
        {
            get { lock (_modelLock) return _snapModel; }
        }

        public int JudgeTimeMs
        {
            get => (int)GetValue(JudgeTimeMsProperty);
            set => SetValue(JudgeTimeMsProperty, value);
        }
        public static readonly DependencyProperty JudgeTimeMsProperty =
            DependencyProperty.Register(nameof(JudgeTimeMs), typeof(int), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    WriteDouble(ref c._snapJudgeTimeMsBits, c.JudgeTimeMs);
                }));

        public double JudgeTimeMsPrecise
        {
            get => (double)GetValue(JudgeTimeMsPreciseProperty);
            set => SetValue(JudgeTimeMsPreciseProperty, value);
        }
        public static readonly DependencyProperty JudgeTimeMsPreciseProperty =
            DependencyProperty.Register(nameof(JudgeTimeMsPrecise), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(0.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    WriteDouble(ref c._snapJudgeTimeMsBits, c.JudgeTimeMsPrecise);
                }));

        public double PxPerMs
        {
            get => (double)GetValue(PxPerMsProperty);
            set => SetValue(PxPerMsProperty, value);
        }
        public static readonly DependencyProperty PxPerMsProperty =
            DependencyProperty.Register(nameof(PxPerMs), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(0.12, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    WriteDouble(ref c._snapPxPerMsBits, c.PxPerMs);
                    SpcSkiaGeometryBuilder.ClearCache();
                    c.ClearRulerTextCache();
                }));

        public int TargetFps
        {
            get => (int)GetValue(TargetFpsProperty);
            set => SetValue(TargetFpsProperty, value);
        }
        public static readonly DependencyProperty TargetFpsProperty =
            DependencyProperty.Register(nameof(TargetFps), typeof(int), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata((int)DefaultTargetFps, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    int fps = Math.Clamp(c.TargetFps, 30, 240);
                    Volatile.Write(ref c._frameTimeMs, 1000.0 / fps);
                }));

        public bool ShowFpsStats
        {
            get => (bool)GetValue(ShowFpsStatsProperty);
            set => SetValue(ShowFpsStatsProperty, value);
        }
        public static readonly DependencyProperty ShowFpsStatsProperty =
            DependencyProperty.Register(nameof(ShowFpsStats), typeof(bool), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(true));

        // ===== 交互 =====
        private bool _dragTime, _dragZoom;
        private Point _dragStart;
        private int _judgeTimeStart;
        private double _pxPerMsStart;
        private int _selectedItemIndex = -1;
        private bool _isDragClick; // distinguish click from drag

        public int SelectedItemIndex
        {
            get => _selectedItemIndex;
            set { _selectedItemIndex = value; _snapSelectedIndex = value; }
        }

        public void BeginAddNotePlacement(AddNoteRequest request)
        {
            _pendingAddRequest = request;
            _placingNoteDrag = false;
        }

        public double GetJudgeY() => Math.Max(TopPad + 20, ActualHeight - JudgeFromBottom);

        private double TimeAtY(double judgeY, double y)
            => ReadDouble(ref _snapJudgeTimeMsBits) + (judgeY - y) / Math.Max(1e-6, PxPerMs);

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Focus();
            double judgeY = GetJudgeY();
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var pos = e.GetPosition(this);

            if (ctrl)
            {
                double tAnchor = TimeAtY(judgeY, pos.Y);
                double newPx = Math.Clamp(PxPerMs * (e.Delta > 0 ? 1.12 : 1.0 / 1.12), MinPxPerMs, MaxPxPerMs);
                var speed = Math.Max(0.01, Speed);
                PixelsPerSecond = newPx * 1000.0 / speed;
                JudgeTimeMs = Math.Max(0, (int)Math.Round(tAnchor - (judgeY - pos.Y) / PxPerMs));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
            else
            {
                int step = Math.Clamp((int)Math.Round(240 / Math.Max(0.05, PxPerMs)), 20, 2000);
                JudgeTimeMs = Math.Max(0, JudgeTimeMs + (e.Delta > 0 ? -step : step));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.ChangedButton == MouseButton.Left && _pendingAddRequest != null)
            {
                HandleAddNoteMouseDown(e.GetPosition(this));
                e.Handled = true;
                return;
            }

            _dragStart = e.GetPosition(this);
            _judgeTimeStart = JudgeTimeMs;
            _pxPerMsStart = PxPerMs;
            _isDragClick = true;

            if (e.ChangedButton == MouseButton.Left) { _dragTime = true; CaptureMouse(); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.Right) { _dragZoom = true; CaptureMouse(); e.Handled = true; }
        }

        private int _lastMouseMoveTick;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_placingNoteDrag) return;
            if (!_dragTime && !_dragZoom) return;

            var pos = e.GetPosition(this);
            double dist = Math.Abs(pos.Y - _dragStart.Y) + Math.Abs(pos.X - _dragStart.X);
            if (dist > 4) _isDragClick = false;

            int now = Environment.TickCount;
            if (now - _lastMouseMoveTick < 8) return;
            _lastMouseMoveTick = now;

            double dy = pos.Y - _dragStart.Y;
            double judgeY = GetJudgeY();
            if (_dragTime)
            {
                JudgeTimeMs = Math.Max(0, _judgeTimeStart + (int)Math.Round(dy / Math.Max(1e-6, PxPerMs)));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
            else
            {
                double tAnchor = TimeAtY(judgeY, _dragStart.Y);
                double newPx = Math.Clamp(_pxPerMsStart * Math.Exp(-dy * 0.005), MinPxPerMs, MaxPxPerMs);
                var speed = Math.Max(0.01, Speed);
                PixelsPerSecond = newPx * 1000.0 / speed;
                JudgeTimeMs = Math.Max(0, (int)Math.Round(tAnchor - (judgeY - _dragStart.Y) / PxPerMs));
                JudgeTimeMsPrecise = JudgeTimeMs;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == MouseButton.Left && _placingNoteDrag)
            {
                HandleAddNoteMouseUp(e.GetPosition(this));
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left && _dragTime)
            {
                if (_isDragClick)
                    HandleClick(e.GetPosition(this));
                _dragTime = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right && _dragZoom)
            {
                _dragZoom = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_placingNoteDrag)
            {
                _placingNoteDrag = false;
                ReleaseMouseCapture();
                return;
            }
            if (_dragTime || _dragZoom) { _dragTime = false; _dragZoom = false; ReleaseMouseCapture(); }
        }

        private void HandleAddNoteMouseDown(Point pos)
        {
            var request = _pendingAddRequest;
            if (request == null) return;

            if (!TryGetPlacementPoint(pos, request, out int timeMs, out int lane, out int posNum))
                return;

            if (request.Type == AddNoteType.Tap || request.Type == AddNoteType.Flick)
            {
                _pendingAddRequest = null;
                NotePlacementCommitted?.Invoke(new AddNotePlacement(request, timeMs, timeMs, lane, posNum, posNum));
                return;
            }

            _placeStartTimeMs = timeMs;
            _placeStartLane = lane;
            _placeStartPosNum = posNum;
            _placingNoteDrag = true;
            CaptureMouse();
        }

        private void HandleAddNoteMouseUp(Point pos)
        {
            var request = _pendingAddRequest;
            if (request == null) return;

            _placingNoteDrag = false;
            ReleaseMouseCapture();

            if (!TryGetPlacementPoint(pos, request, out int endTimeMs, out int endLane, out int endPosNum))
            {
                endTimeMs = _placeStartTimeMs;
                endLane = _placeStartLane;
                endPosNum = _placeStartPosNum;
            }

            int startTime = _placeStartTimeMs;
            int startLane = _placeStartLane;
            int startPos = _placeStartPosNum;

            if (endTimeMs < startTime)
            {
                (startTime, endTimeMs) = (endTimeMs, startTime);
                (startLane, endLane) = (endLane, startLane);
                (startPos, endPosNum) = (endPosNum, startPos);
            }

            _pendingAddRequest = null;
            NotePlacementCommitted?.Invoke(new AddNotePlacement(request, startTime, endTimeMs, startLane, startPos, endPosNum));
        }

        private bool TryGetPlacementPoint(Point pos, AddNoteRequest request, out int timeMs, out int lane, out int posNum)
        {
            timeMs = 0;
            lane = 0;
            posNum = 0;

            SKRect ground, sky;
            float judgeY;
            lock (_layoutLock)
            {
                ground = _groundRect;
                sky = _skyRect;
                judgeY = _judgeY;
            }

            float x = (float)pos.X;
            float y = (float)pos.Y;

            bool isSky = request.Type == AddNoteType.Flick || request.Type == AddNoteType.SkyArea;
            if (isSky)
            {
                if (x < sky.Left || x > sky.Right || y < sky.Top || y > judgeY) return false;
            }
            else
            {
                if (x < ground.Left || x > ground.Right || y < ground.Top || y > judgeY) return false;
            }

            double pxPerMs = ReadDouble(ref _snapPxPerMsBits);
            double judgeTimeMs = ReadDouble(ref _snapJudgeTimeMsBits);
            double rawTimeMs = judgeTimeMs + (judgeY - y) / Math.Max(1e-6, pxPerMs);
            timeMs = SnapTimeMs(rawTimeMs, pxPerMs);

            if (isSky)
            {
                int den = Math.Max(1, request.Den);
                float u = (x - sky.Left) / Math.Max(1f, sky.Width);
                posNum = Math.Clamp((int)Math.Round(u * den), 0, den);
                return true;
            }

            float laneW = Math.Max(1f, ground.Width / GroundLanes);
            lane = Math.Clamp((int)Math.Floor((x - ground.Left) / laneW), 0, 5);

            int width = request.Type == AddNoteType.Hold
                ? Math.Clamp(request.GroundWidth, 1, 6)
                : Math.Clamp(request.GroundWidth, 1, 4);

            int leftLane = lane;
            if (leftLane + width > GroundLanes) leftLane = GroundLanes - width;
            lane = Math.Clamp(leftLane, 0, 5);
            return true;
        }

        private int SnapTimeMs(double rawTimeMs, double pxPerMs)
        {
            RenderModel? model;
            lock (_modelLock) { model = _snapModel; }
            if (model == null || model.Bpm <= 0 || model.Beats <= 0)
                return Math.Max(0, (int)Math.Round(rawTimeMs));

            double msPerBeat = 60000.0 / model.Bpm;
            double msPerMeasure = msPerBeat * model.Beats;

            double chosenMs = msPerMeasure;
            for (int mi = 0; mi < RulerMultiples.Length; mi++)
            {
                double c = msPerMeasure * RulerMultiples[mi];
                if (c * pxPerMs >= 80) { chosenMs = c; break; }
            }

            double pxPerBeat = msPerBeat * pxPerMs;
            double subMs = 0;
            if (pxPerBeat >= 28) subMs = msPerBeat / 4.0;
            else if (pxPerBeat >= 16) subMs = msPerBeat / 2.0;
            else if (pxPerBeat >= 10) subMs = msPerBeat;

            double snapMeasure = Math.Round(rawTimeMs / chosenMs) * chosenMs;
            double snapSub = subMs > 0 ? Math.Round(rawTimeMs / subMs) * subMs : rawTimeMs;
            double snapped = Math.Abs(snapSub - rawTimeMs) <= Math.Abs(snapMeasure - rawTimeMs)
                ? snapSub
                : snapMeasure;

            return Math.Max(0, (int)Math.Round(snapped));
        }

        private void HandleClick(Point pos)
        {
            double judgeY = GetJudgeY();
            double pxPerMs = PxPerMs;
            double judgeTimeMs = ReadDouble(ref _snapJudgeTimeMsBits);

            RenderModel? model;
            lock (_modelLock) { model = _snapModel; }
            if (model == null) return;

            SKRect ground, sky;
            lock (_layoutLock) { ground = _groundRect; sky = _skyRect; }

            float clickX = (float)pos.X;
            float clickY = (float)pos.Y;
            double clickTimeMs = judgeTimeMs + (judgeY - clickY) / pxPerMs;

            RenderItem? best = null;
            int bestIdx = -1;
            double bestDist = double.MaxValue;

            var items = model.Items;
            for (int idx = 0; idx < items.Count; idx++)
            {
                var item = items[idx];
                float yStart = (float)(judgeY - (item.TimeMs    - judgeTimeMs) * pxPerMs);
                float yEnd   = (float)(judgeY - (item.EndTimeMs - judgeTimeMs) * pxPerMs);

                // Flick 的三角形向上延伸 FlickTriH 像素，需要扩展顶部命中范围
                float extraTop = item.Type == RenderItemType.SkyFlick ? FlickTriH : 0;
                float top    = Math.Min(yStart, yEnd) - extraTop - 8;
                float bottom = Math.Max(yStart, yEnd) + 8;

                if (clickY < top || clickY > bottom) continue;

                // 计算 X 轴命中范围
                float itemLeft, itemRight;
                switch (item.Type)
                {
                    case RenderItemType.GroundTap:
                    case RenderItemType.GroundHold:
                    {
                        int lane = Math.Clamp(item.Lane, 0, 5);
                        int kind = Math.Clamp(item.Kind, 1, 4);
                        int leftLane = lane;
                        if (leftLane + kind > 6) leftLane = 6 - kind;
                        itemLeft  = ground.Left + ground.Width * leftLane / 6f;
                        itemRight = ground.Left + ground.Width * (leftLane + kind) / 6f;
                        break;
                    }
                    case RenderItemType.SkyFlick:
                    {
                        // Flick 只有一个端点位置
                        int den    = Math.Max(1, item.Den);
                        float cx   = sky.Left + sky.Width * Math.Clamp(item.X0 / (float)den, 0, 1);
                        float wPx  = sky.Width * Math.Clamp(item.W0 / (float)den, 0, 1);
                        float half = Math.Max(20, wPx * 0.5f);
                        itemLeft  = cx - half;
                        itemRight = cx + half;
                        break;
                    }
                    case RenderItemType.SkyArea:
                    {
                        // SkyArea 使用起点(X0/W0)与终点(X1/W1)的包围盒联合，
                        // 确保点击音符上半部分（终点侧）也能命中。
                        int den = Math.Max(1, item.Den);

                        float cx0   = sky.Left + sky.Width * Math.Clamp(item.X0 / (float)den, 0, 1);
                        float wPx0  = sky.Width * Math.Clamp(item.W0 / (float)den, 0, 1);
                        float half0 = Math.Max(20, wPx0 * 0.5f);

                        float cx1   = sky.Left + sky.Width * Math.Clamp(item.X1 / (float)den, 0, 1);
                        float wPx1  = sky.Width * Math.Clamp(item.W1 / (float)den, 0, 1);
                        float half1 = Math.Max(20, wPx1 * 0.5f);

                        itemLeft  = Math.Min(cx0 - half0, cx1 - half1);
                        itemRight = Math.Max(cx0 + half0, cx1 + half1);
                        break;
                    }
                    default:
                        continue;
                }

                if (clickX < itemLeft - 8 || clickX > itemRight + 8) continue;

                double dist = Math.Abs(clickTimeMs - item.TimeMs);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = item;
                    bestIdx = idx;
                }
            }

            SelectedItemIndex = bestIdx;
            NoteSelected?.Invoke(best);
        }

        /// <summary>
        /// 强制根据当前事件刷新渲染模型。
        /// 适用于在原列表内就地修改音符的情况。
        /// </summary>
        public void RefreshModel()
        {
            var model = Events != null ? SpcRenderModelBuilder.Build(Events) : null;
            lock (_modelLock) { _snapModel = model; }
            SpcSkiaGeometryBuilder.ClearCache();
            ClearFlickPathCache();
            ClearRulerTextCache();
        }

        // ===== 渲染 =====

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);
            var canvas = e.Surface.Canvas;
            canvas.Clear();

            long nowTick = Stopwatch.GetTimestamp();
            double thisFrameMs = (nowTick - _prevFrameTick) * 1000.0 / Stopwatch.Frequency;
            _prevFrameTick = nowTick;
            if (_frameTimeSmoothed <= 0) _frameTimeSmoothed = thisFrameMs;
            else _frameTimeSmoothed = _frameTimeSmoothed * 0.85 + thisFrameMs * 0.15;

            _frameCount++;
            double elapsedSample = (nowTick - _lastFpsTick) * 1000.0 / Stopwatch.Frequency;
            if (elapsedSample >= FpsSampleMs)
            {
                _fps = (int)Math.Round(_frameCount * 1000.0 / Math.Max(1, elapsedSample));
                _frameCount = 0;
                _lastFpsTick = nowTick;
            }

            SKRect ground, sky;
            float judgeY, contentLeft, contentW, panelH, w, h;
            lock (_layoutLock)
            {
                if (_ctrlW <= 0 || _ctrlH <= 0) return;
                ground = _groundRect;
                sky = _skyRect;
                judgeY = _judgeY;
                contentLeft = _contentLeft;
                contentW = _contentW;
                panelH = _panelH;
                w = _ctrlW;
                h = _ctrlH;
            }

            double judgeTimeMs = ReadDouble(ref _snapJudgeTimeMsBits);
            double pxPerMs = ReadDouble(ref _snapPxPerMsBits);
            int selectedIdx = _snapSelectedIndex;
            var mode = _viewMode;

            RenderModel? model;
            lock (_modelLock) { model = _snapModel; }

            if (mode == PreviewViewMode.Merged)
            {
                PaintMergedBackground(canvas, ground, sky, contentLeft, w, h, judgeY);
                PaintRuler(canvas, ground, judgeY, pxPerMs, judgeTimeMs, model);
                PaintGroundNotes(canvas, ground, judgeY, pxPerMs, judgeTimeMs, selectedIdx, model);
                PaintSkyNotes(canvas, sky, judgeY, pxPerMs, judgeTimeMs, selectedIdx, model);
            }
            else
            {
                PaintSplitBackground(canvas, sky, ground, contentLeft, w, h, judgeY);
                PaintRuler(canvas, ground, judgeY, pxPerMs, judgeTimeMs, model);
                PaintSplitRuler(canvas, sky, judgeY, pxPerMs, judgeTimeMs, model);
                PaintGroundNotes(canvas, ground, judgeY, pxPerMs, judgeTimeMs, selectedIdx, model);
                PaintSkyNotes(canvas, sky, judgeY, pxPerMs, judgeTimeMs, selectedIdx, model);
            }

            PaintJudge(canvas, judgeY, contentLeft, w);
            if (ShowFpsStats)
                PaintFps(canvas, w);
        }

        // ===== 合并背景 =====
        private static void PaintMergedBackground(SKCanvas canvas, SKRect ground, SKRect sky,
            float contentLeft, float w, float h, float judgeY)
        {
            canvas.DrawRect(0, 0, w, h, BgPaint);

            canvas.Save();
            canvas.ClipRect(new SKRect(contentLeft, ground.Top, ground.Right, judgeY));

            canvas.DrawRect(ground.Left, ground.Top, ground.Width, judgeY - ground.Top, PanelBgPaint);
            canvas.DrawRect(ground.Left, ground.Top, ground.Width, judgeY - ground.Top, PanelBorder);

            PaintGroundLaneBackgrounds(canvas, ground, judgeY);

            for (int i = 0; i <= GroundLanes; i++)
            {
                float x = ground.Left + ground.Width * i / (float)GroundLanes;
                canvas.DrawLine(x, ground.Top, x, judgeY, GroundGridPaint);
            }

            // Sky overlay
            canvas.DrawRect(sky.Left, sky.Top, sky.Width, judgeY - sky.Top, SkyOverlayBg);
            canvas.DrawLine(sky.Left, sky.Top, sky.Left, judgeY, SkyBorderPaint);
            canvas.DrawLine(sky.Right, sky.Top, sky.Right, judgeY, SkyBorderPaint);

            for (int i = 1; i < SkyDivisions; i++)
            {
                float x = sky.Left + sky.Width * i / (float)SkyDivisions;
                canvas.DrawLine(x, sky.Top, x, judgeY, SkyGridPaint);
            }

            canvas.Restore();

            DrawText(canvas, "GROUND", ground.Left + 6, ground.Top + 4, GroundLabelPaint);
            DrawText(canvas, "SKY", sky.Left + 6, ground.Top + 18, SkyLabelPaint);
        }

        // ===== 分离背景 =====
        private static void PaintSplitBackground(SKCanvas canvas, SKRect sky, SKRect ground,
            float contentLeft, float w, float h, float judgeY)
        {
            canvas.DrawRect(0, 0, w, h, BgPaint);

            canvas.Save();
            canvas.ClipRect(new SKRect(contentLeft, sky.Top, ground.Right, judgeY));

            // Sky panel (left)
            canvas.DrawRect(sky.Left, sky.Top, sky.Width, judgeY - sky.Top, PanelBgPaint);
            canvas.DrawRect(sky.Left, sky.Top, sky.Width, judgeY - sky.Top, PanelBorder);
            for (int i = 1; i < SkyDivisions; i++)
            {
                float x = sky.Left + sky.Width * i / (float)SkyDivisions;
                canvas.DrawLine(x, sky.Top, x, judgeY, SkyGridPaintSplit);
            }

            // Ground panel (right)
            canvas.DrawRect(ground.Left, ground.Top, ground.Width, judgeY - ground.Top, PanelBgPaint);
            canvas.DrawRect(ground.Left, ground.Top, ground.Width, judgeY - ground.Top, PanelBorder);
            PaintGroundLaneBackgrounds(canvas, ground, judgeY);
            for (int i = 0; i <= GroundLanes; i++)
            {
                float x = ground.Left + ground.Width * i / (float)GroundLanes;
                canvas.DrawLine(x, ground.Top, x, judgeY, GroundGridPaint);
            }

            canvas.Restore();

            DrawText(canvas, "SKY", sky.Left + 6, sky.Top + 4, SkyLabelPaint);
            DrawText(canvas, "GROUND", ground.Left + 6, ground.Top + 4, GroundLabelPaint);
        }

        private static void PaintJudge(SKCanvas canvas, float judgeY, float contentLeft, float w)
        {
            canvas.DrawLine(contentLeft, judgeY, w, judgeY, JudgePaint);
            DrawText(canvas, "JUDGE", contentLeft + 4, judgeY + 4, JudgeTextPaint);
        }

        private void PaintRuler(SKCanvas canvas, SKRect panel,
            float judgeY, double pxPerMs, double judgeTimeMs, RenderModel? model)
        {
            if (model == null || model.Bpm <= 0 || model.Beats <= 0) return;

            double msPerBeat = 60000.0 / model.Bpm;
            double msPerMeasure = msPerBeat * model.Beats;

            double chosenMs = msPerMeasure;
            for (int mi = 0; mi < RulerMultiples.Length; mi++)
            {
                double c = msPerMeasure * RulerMultiples[mi];
                if (c * pxPerMs >= 80) { chosenMs = c; break; }
            }

            float topY = panel.Top;
            float bottomY = judgeY;

            double tMin = judgeTimeMs + (judgeY - bottomY) / pxPerMs;
            double tMax = judgeTimeMs + (judgeY - topY) / pxPerMs;

            int startM = (int)Math.Floor(tMin / chosenMs);
            int endM = (int)Math.Ceiling(tMax / chosenMs);

            if (Math.Abs(_rulerCachePxPerMs - pxPerMs) > 1e-6)
            {
                ClearRulerTextCache();
                _rulerCachePxPerMs = pxPerMs;
            }

            // Determine the rightmost edge to draw ruler lines across
            SKRect ground, sky;
            lock (_layoutLock) { ground = _groundRect; sky = _skyRect; }
            float lineRight = Math.Max(ground.Right, sky.Right);

            for (int m = startM; m <= endM; m++)
            {
                double t = m * chosenMs;
                float y = (float)(judgeY - (t - judgeTimeMs) * pxPerMs);
                if (y < topY - 2 || y > bottomY + 2) continue;

                canvas.DrawLine(panel.Left, y, lineRight, y, MeasurePaint);

                if (!_rulerTextCache.TryGetValue(m, out var label))
                {
                    double val = t / msPerMeasure;
                    label = $"#{val:0.##}";
                    _rulerTextCache[m] = label;
                }
                DrawText(canvas, label, 2, y - 10, RulerLabelPaint);
            }

            double pxPerBeat = msPerBeat * pxPerMs;
            double subMs = 0;
            if (pxPerBeat >= 28) subMs = msPerBeat / 4.0;
            else if (pxPerBeat >= 16) subMs = msPerBeat / 2.0;
            else if (pxPerBeat >= 10) subMs = msPerBeat;

            if (subMs <= 0) return;

            int startS = (int)Math.Floor(tMin / subMs);
            int endS = (int)Math.Ceiling(tMax / subMs);
            for (int s = startS; s <= endS; s++)
            {
                double t = s * subMs;
                if (Math.Abs(t % chosenMs) < 1.0) continue;
                float y = (float)(judgeY - (t - judgeTimeMs) * pxPerMs);
                if (y < topY - 1 || y > bottomY + 1) continue;
                canvas.DrawLine(panel.Left, y, lineRight, y, BeatPaint);
            }
        }

        /// <summary>Paint ruler lines specifically on the sky panel in split mode.</summary>
        private void PaintSplitRuler(SKCanvas canvas, SKRect sky,
            float judgeY, double pxPerMs, double judgeTimeMs, RenderModel? model)
        {
            // In split mode the main PaintRuler already draws across both panels,
            // so this is intentionally empty. The lines span from ground.Left to sky.Right.
        }

        private static int FindFirstIndexByTime(List<RenderItem> items, int timeMs)
        {
            int lo = 0, hi = items.Count - 1, result = items.Count;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (items[mid].TimeMs >= timeMs) { result = mid; hi = mid - 1; }
                else lo = mid + 1;
            }
            return result;
        }

        private static void PaintGroundNotes(SKCanvas canvas, SKRect ground,
             float judgeY, double pxPerMs, double judgeTimeMs, int selectedIdx, RenderModel? model)
         {
             if (model == null) return;
             var items = model.Items;
             float clipTop = ground.Top - 50;
             float clipBottom = judgeY + 50;

             double tMin = judgeTimeMs + (judgeY - clipBottom) / pxPerMs;
             double tMax = judgeTimeMs + (judgeY - clipTop) / pxPerMs;

             for (int idx = 0; idx < items.Count; idx++)
             {
                 var item = items[idx];
                 if (item.Type != RenderItemType.GroundTap && item.Type != RenderItemType.GroundHold)
                     continue;

                 if (item.EndTimeMs < tMin) continue;
                 if (item.TimeMs > tMax && item.EndTimeMs > tMax) break;

                 float yStart = (float)(judgeY - (item.TimeMs - judgeTimeMs) * pxPerMs);
                 float yEnd = (float)(judgeY - (item.EndTimeMs - judgeTimeMs) * pxPerMs);
                 float top = Math.Min(yStart, yEnd);
                 float bottom = Math.Max(yStart, yEnd);
                 if (bottom < clipTop || top > clipBottom) continue;

                 bool isSelected = idx == selectedIdx;

                 if (item.Type == RenderItemType.GroundTap)
                     PaintGroundTap(canvas, ground, yStart, item.Lane, item.Kind, isSelected);
                 else
                     PaintGroundHold(canvas, ground, item, yStart, yEnd, isSelected);
             }
         }

        private void PaintSkyNotes(SKCanvas canvas, SKRect sky,
            float judgeY, double pxPerMs, double judgeTimeMs, int selectedIdx, RenderModel? model)
         {
             if (model == null) return;
             var items = model.Items;
             float clipTop = sky.Top - 50;
             float clipBottom = judgeY + 50;

             double tMin = judgeTimeMs + (judgeY - clipBottom) / pxPerMs;
             double tMax = judgeTimeMs + (judgeY - clipTop) / pxPerMs;

             for (int idx = 0; idx < items.Count; idx++)
             {
                 var item = items[idx];
                 if (item.Type != RenderItemType.SkyFlick && item.Type != RenderItemType.SkyArea)
                     continue;

                 if (item.EndTimeMs < tMin) continue;
                 if (item.TimeMs > tMax && item.EndTimeMs > tMax) break;

                 float yStart = (float)(judgeY - (item.TimeMs - judgeTimeMs) * pxPerMs);
                 float yEnd = (float)(judgeY - (item.EndTimeMs - judgeTimeMs) * pxPerMs);
                 float top = Math.Min(yStart, yEnd);
                 float bottom = Math.Max(yStart, yEnd);
                 if (bottom < clipTop || top > clipBottom) continue;

                 bool isSelected = idx == selectedIdx;

                 if (item.Type == RenderItemType.SkyFlick)
                     PaintSkyFlick(canvas, sky, yStart, item, isSelected);
                 else
                     PaintSkyArea(canvas, sky, item, pxPerMs, yStart, isSelected);
             }
         }

        private static void PaintGroundTap(SKCanvas canvas, SKRect ground, float y, int lane, int kind, bool selected)
        {
            lane = Math.Clamp(lane, 0, 5);
            kind = Math.Clamp(kind, 1, 4);
            int leftLane = lane;
            if (leftLane + kind > 6) leftLane = 6 - kind;

            float x0 = ground.Left + ground.Width * leftLane / 6f;
            float x1 = ground.Left + ground.Width * (leftLane + kind) / 6f;
            float width = (x1 - x0) - 4;
            if (width <= 0) return;

            // Tap 的可点击范围比实际绘制的矩形更宽，左右各扩展 2 像素，方便点击。
            var rect = new SKRect(x0 + 2, y - TapHalfH, x0 + 2 + width, y + TapHalfH);
            var (fill, stroke, _, _) = GetGroundNotePaints(lane, kind);
            canvas.DrawRoundRect(rect, 3, 3, fill);
            canvas.DrawRoundRect(rect, 3, 3, stroke);
            if (selected)
            {
                canvas.DrawRoundRect(rect, 3, 3, SelectedFillPaint);
                canvas.DrawRoundRect(rect, 3, 3, SelectedStrokePaint);
            }
        }

        private static void PaintGroundHold(SKCanvas canvas, SKRect ground, RenderItem item, float y0, float y1, bool selected)
        {
            int lane = Math.Clamp(item.Lane, 0, 5);
            int kind = Math.Clamp(item.Kind, 1, 4);
            int leftLane = lane;
            if (leftLane + kind > 6) leftLane = 6 - kind;

            float x0 = ground.Left + ground.Width * leftLane / 6f;
            float x1 = ground.Left + ground.Width * (leftLane + kind) / 6f;

            float top = Math.Min(y0, y1);
            float bottom = Math.Max(y0, y1);
            if (bottom - top < 1) return;

            float width = (x1 - x0) - 4;
            if (width <= 0) return;

            var body = new SKRect(x0 + 2, top, x0 + 2 + width, bottom);
            var (_, _, fill, stroke) = GetGroundNotePaints(lane, kind);
            canvas.DrawRect(body, fill);
            PaintHoldCenterLine(canvas, body, fill);
            canvas.DrawRect(body, stroke);
            if (selected)
            {
                canvas.DrawRect(body, SelectedFillPaint);
                canvas.DrawRect(body, SelectedStrokePaint);
            }
        }

        private static void PaintHoldCenterLine(SKCanvas canvas, SKRect body, SKPaint fill)
        {
            float centerX = (body.Left + body.Right) * 0.5f;
            var linePaint = MkHoldCenterLinePaint(fill.Color);
            canvas.DrawLine(centerX, body.Top, centerX, body.Bottom, linePaint);
        }

        private static SKPaint MkHoldCenterLinePaint(SKColor baseColor)
        {
            byte Darken(byte v) => (byte)Math.Clamp((int)(v * 0.7f), 0, 255);
            return new SKPaint
            {
                Color = new SKColor(Darken(baseColor.Red), Darken(baseColor.Green), Darken(baseColor.Blue), 200),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8f
            };
        }

        private static void PaintGroundLaneBackgrounds(SKCanvas canvas, SKRect ground, float judgeY)
        {
            float laneW = ground.Width / GroundLanes;
            var lane0 = new SKRect(ground.Left, ground.Top, ground.Left + laneW, judgeY);
            canvas.DrawRect(lane0, GroundLaneBasePaint);
            canvas.DrawRect(lane0, GroundLane0OverlayPaint);

            var lane5 = new SKRect(ground.Left + laneW * 5, ground.Top, ground.Left + laneW * 6, judgeY);
            canvas.DrawRect(lane5, GroundLaneBasePaint);
            canvas.DrawRect(lane5, GroundLane5OverlayPaint);
        }

        private static (SKPaint tapFill, SKPaint tapStroke, SKPaint holdFill, SKPaint holdStroke) GetGroundNotePaints(int lane, int kind)
        {
            if (lane == 0)
                return (TapFillPurple, TapStrokePurple, HoldFillPurple, HoldStrokePurple);
            if (lane == 5)
                return (TapFillRed, TapStrokeRed, HoldFillRed, HoldStrokeRed);

            if (kind > 1)
                return (TapFillWhite, TapStrokeWhite, HoldFillWhite, HoldStrokeWhite);

            return (TapFillDeepBlue, TapStrokeDeepBlue, HoldFillDeepBlue, HoldStrokeDeepBlue);
        }

        private void PaintSkyFlick(SKCanvas canvas, SKRect sky, float y, RenderItem item, bool selected)
        {
            int key = RuntimeHelpers.GetHashCode(item);
            if (!_flickPathCache.TryGetValue(key, out var cached)
                || Math.Abs(cached.skyLeft - sky.Left) > 0.5f
                || Math.Abs(cached.skyWidth - sky.Width) > 0.5f)
            {
                int den = Math.Max(1, item.Den);
                float cx = sky.Left + sky.Width * Math.Clamp(item.X0 / (float)den, 0, 1);
                float wPx = sky.Width * Math.Clamp(item.W0 / (float)den, 0, 1);
                float half = Math.Max(20, wPx * 0.5f);
                bool isLeft = item.Dir == 16;

                var path = new SKPath();
                if (isLeft)
                {
                    path.MoveTo(cx + half, 0);
                    path.LineTo(cx - half, 0);
                    path.LineTo(cx - half, -FlickTriH);
                    path.QuadTo(cx - half * 0.6f, 0, cx + half, 0);
                }
                else
                {
                    path.MoveTo(cx - half, 0);
                    path.LineTo(cx + half, 0);
                    path.LineTo(cx + half, -FlickTriH);
                    path.QuadTo(cx + half * 0.6f, 0, cx - half, 0);
                }
                path.Close();

                if (cached.path != null) cached.path.Dispose();
                cached = (sky.Left, sky.Width, path);
                _flickPathCache[key] = cached;
            }

            bool left = item.Dir == 16;
            var fill = left ? FlickFillLeftPaint : FlickFillRightPaint;
            var stroke = left ? FlickStrokeLeftPaint : FlickStrokeRightPaint;

            canvas.Save();
            canvas.Translate(0, y);
            canvas.DrawPath(cached.path, fill);
            canvas.DrawPath(cached.path, stroke);
            if (selected)
            {
                canvas.DrawPath(cached.path, SelectedFillPaint);
                canvas.DrawPath(cached.path, SelectedStrokePaint);
            }
            canvas.Restore();
        }

        private static void PaintSkyArea(SKCanvas canvas, SKRect sky, RenderItem item, double pxPerMs, float yStart, bool selected)
        {
            var path = SpcSkiaGeometryBuilder.BuildSkyAreaPath(sky, item, pxPerMs);
            canvas.Save();
            canvas.Translate(0, yStart);
            canvas.DrawPath(path, SkyAreaFillPaint);
            canvas.DrawPath(path, SkyAreaStripePaint);
            canvas.DrawPath(path, SkyAreaStrokePaint);
            if (selected)
            {
                canvas.DrawPath(path, SelectedFillPaint);
                canvas.DrawPath(path, SelectedStrokePaint);
            }
            canvas.Restore();
        }

        // ===== 帧率显示 =====
        private string _lastStatsStr = "";
        private int _lastStatsKey = -1;

        private void PaintFps(SKCanvas canvas, float w)
        {
            int fps = _fps;
            int ftUs = (int)(_frameTimeSmoothed * 100);
            int key = fps * 100000 + ftUs;
            if (key != _lastStatsKey)
            {
                _lastStatsKey = key;
                _lastStatsStr = $"FPS: {fps}  ft: {_frameTimeSmoothed:0.0}ms";
            }
            DrawText(canvas, _lastStatsStr, w - 160, 10, FpsPaint);
        }

        private static void DrawText(SKCanvas canvas, string text, float x, float y, SKPaint paint)
        {
            float baseline = y - paint.FontMetrics.Ascent;
            canvas.DrawText(text, x, baseline, paint);
        }

        private void ClearRulerTextCache() => _rulerTextCache.Clear();

        private void ClearFlickPathCache()
        {
            foreach (var kv in _flickPathCache)
                kv.Value.path?.Dispose();
            _flickPathCache.Clear();
        }

        private static SKPaint MkFill(byte r, byte g, byte b, byte a = 255)
            => new() { Color = new SKColor(r, g, b, a), IsAntialias = true, Style = SKPaintStyle.Fill };

        private static SKPaint MkStroke(byte r, byte g, byte b, float w, float[]? dash = null, byte alpha = 255)
        {
            var p = new SKPaint { Color = new SKColor(r, g, b, alpha), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w };
            if (dash != null) p.PathEffect = SKPathEffect.CreateDash(dash, 0);
            return p;
        }

        private static SKPaint MkText(byte r, byte g, byte b, float size)
            => new()
            {
                Color = new SKColor(r, g, b),
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas"),
                TextSize = size
            };

        private static SKPaint MkStripePaint(byte r, byte g, byte b, byte a, float size)
        {
            const float stripePx = 3f;
            float len = Math.Max(1.2f, size * 0.25f);
            float band = stripePx / len;

            var colors = new[]
            {
                new SKColor(200, 170, 255, 0),
                new SKColor(200, 170, 255, 0),
                new SKColor(200, 170, 255, 70),
                new SKColor(200, 170, 255, 70),
                new SKColor(200, 170, 255, 0),
                new SKColor(200, 170, 255, 0)
            };
            var pos = new[]
            {
                0f,
                1.0f - band,
                1.0f,
                1.0f + band,
                1.0f + band * 2f,
                1.5f
            };
            var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(len * 1.5f, len * 3.5f),
                colors,
                pos,
                SKShaderTileMode.Repeat);

            return new SKPaint
            {
                Shader = shader,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
        }

        public void Dispose()
        {
            StopRenderLoop();
            ClearRulerTextCache();
            ClearFlickPathCache();
        }
    }
}
