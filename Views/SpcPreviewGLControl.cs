using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
        // 待放置音符的类型。
        public AddNoteType Type { get; init; }
        // 地面音符占用的轨道宽度（用于 Tap/Hold）。
        public int GroundWidth { get; init; } = 1;
        // 天空坐标分母（将位置/宽度分子换算为比例）。
        public int Den { get; init; } = 24;
        // 天空音符起始宽度或宽度参数分子。
        public int WidthNum { get; init; } = 1;
        // 天空区域终止宽度参数分子（用于 SkyArea 等需要双端宽度的类型）。
        public int WidthNum2 { get; init; } = 1;
        // Flick 方向编码（16 表示左，4 表示右）。
        public int Dir { get; init; } = 4;
        // 天空区域左边缘缓动类型编号。
        public int LeftEase { get; init; }
        // 天空区域右边缘缓动类型编号。
        public int RightEase { get; init; }
        // 新增音符所属分组编号（用于需要分组的事件类型）。
        public int GroupId { get; init; } = 1;
    }

    public sealed record AddNotePlacement(
        // 本次放置时使用的参数模板。
        AddNoteRequest Request,
        // 放置起始时间（毫秒）。
        int StartTimeMs,
        // 放置结束时间（毫秒）；对单点音符通常与起始时间相同。
        int EndTimeMs,
        // 放置到的地面轨道编号（若类型使用地面轨道）。
        int Lane,
        // 放置起点的天空位置分子（相对 Den）。
        int PosNum,
        // 放置终点的天空位置分子（相对 Den）。
        int PosNum2
    );

    // 高性能 Skia 预览控件，支持分离视图与合并叠加视图。


    public sealed class SpcPreviewGLControl : SKElement, IDisposable
    {
        // ===== 渲染循环 =====
        // 后台渲染循环线程。
        private Thread? _renderThread;
        // 渲染线程运行标记。
        private volatile bool _running;
        // 控件默认目标帧率。
        private const double DefaultTargetFps = 120.0;
        // 当前目标帧间隔（毫秒），由 TargetFps 换算得到。
        private double _frameTimeMs = 1000.0 / DefaultTargetFps;
        // 是否已挂接 CompositionTarget.Rendering 作为 VSync 渲染驱动。
        private bool _vsyncHooked;

        // ===== 帧率统计 / 帧时长 =====
        // 当前采样窗口内累计渲染帧数。
        private int _frameCount;
        // 上次 FPS 采样窗口起点时间戳。
        private long _lastFpsTick;
        // 最近一次计算得到的 FPS。
        private int _fps;
        // 平滑后的单帧耗时（毫秒）。
        private double _frameTimeSmoothed;
        // 上一帧的时间戳，用于计算帧时长。
        private long _prevFrameTick;
        // 最近统计窗口内的 P95 帧时长（毫秒）。
        private double _frameTimeP95;
        // 最近统计窗口内的 P99 帧时长（毫秒）。
        private double _frameTimeP99;
        // FPS 统计采样窗口时长（毫秒）。
        private const int FpsSampleMs = 100;
        // 帧时长分位统计的环形缓冲区容量。
        private const int FrameStatsCapacity = 512;
        // 最近帧时长样本环形缓冲区（毫秒）。
        private readonly double[] _frameTimeSamples = new double[FrameStatsCapacity];
        // 下一个写入帧时长样本的位置。
        private int _frameTimeSampleWriteIndex;
        // 当前有效帧时长样本数量。
        private int _frameTimeSampleCount;
        // 计算分位数时复用的排序缓冲区，避免重复分配。
        private readonly double[] _frameTimePercentileScratch = new double[FrameStatsCapacity];

        // ===== 渲染快照 =====
        // 供渲染线程读取的判定时间快照（double 按 long 位存储）。
        private long _snapJudgeTimeMsBits;
        // 供渲染线程读取的缩放值快照（double 按 long 位存储）。
        private long _snapPxPerMsBits;
        // 供渲染线程读取的选中项索引快照。
        private volatile int _snapSelectedIndex = -1;
        // 当前用于绘制的渲染模型快照。
        private RenderModel? _snapModel;
        // 保护渲染模型快照读写的锁。
        private readonly object _modelLock = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // 以线程安全方式写入双精度快照值，供渲染线程读取。
        private static void WriteDouble(ref long storage, double value)
            => Interlocked.Exchange(ref storage, BitConverter.DoubleToInt64Bits(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // 以线程安全方式读取双精度快照值。
        private static double ReadDouble(ref long storage)
            => BitConverter.Int64BitsToDouble(Interlocked.Read(ref storage));

        // ===== 视图模式 =====
        // 当前预览显示模式（分离/合并）。
        private volatile PreviewViewMode _viewMode = PreviewViewMode.Split;
        // 控制天空区与地面区的布局方式。
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

        // ===== 布局区域 =====
        // 分离模式下天空与地面面板并排显示。
        // 合并模式下天空面板覆盖在地面面板上方并居中。
        // 地面面板在控件中的绘制区域。
        private SKRect _groundRect;
        // 天空面板在控件中的绘制区域。
        private SKRect _skyRect;
        // 判定线在控件坐标中的 Y 值。
        private float _judgeY;
        // 内容区域（不含左侧标尺）起始 X。
        private float _contentLeft;
        // 内容区域宽度（不含左侧标尺）。
        private float _contentW;
        // 预览面板高度（从 TopPad 到判定线）。
        private float _panelH;
        // 控件当前宽度缓存。
        private float _ctrlW;
        // 控件当前高度缓存。
        private float _ctrlH;
        // 保护布局缓存读写的锁。
        private readonly object _layoutLock = new();

        // ===== 显示参数 =====
        // 时间轴最小缩放（每毫秒像素数）。
        private const double MinPxPerMs = 0.50;
        // 时间轴最大缩放（每毫秒像素数）。
        private const double MaxPxPerMs = 1.50;
        // 左侧标尺区域固定宽度。
        private const double RulerW = 60.0;
        // 判定线距离控件底部的默认偏移。
        private const double JudgeFromBottom = 100.0;
        // 顶部预留内边距。
        private const double TopPad = 8.0;
        // 地面区域轨道总数。
        private const int GroundLanes = 6;
        // 天空区域横向划分数量（用于网格与位置换算）。
        private const int SkyDivisions = 8;
        // 地面 Tap 矩形半高（由视觉尺寸调参得到）。
        private const float TapHalfH = 22f;
        // Flick 三角形主体高度。
        private const float FlickTriH = 45f;
        // 合并视图中天空面板相对地面面板的宽度比例。
        private const float SkyToGroundWidthRatio = 4f / 6f;

        // ===== 画笔资源 =====
        private static readonly SKPaint BgPaint = MkFill(15, 15, 18); // 整个控件背景底色。
        private static readonly SKPaint PanelBgPaint = MkFill(18, 18, 24); // 面板区域背景色。
        private static readonly SKPaint PanelBorder = MkStroke(55, 55, 70, 1); // 面板边框描边。
        private static readonly SKPaint GroundGridPaint = MkStroke(35, 35, 46, 1); // 地面区域网格线。
        private static readonly SKPaint GroundLaneBasePaint = MkFill(0, 0, 0); // 特殊轨道底色基底。
        private static readonly SKPaint GroundLane0OverlayPaint = MkFill(190, 130, 255, 80); // 左侧特殊轨道覆盖色。
        private static readonly SKPaint GroundLane5OverlayPaint = MkFill(255, 90, 90, 80); // 右侧特殊轨道覆盖色。
        private static readonly SKPaint SkyOverlayBg = MkFill(25, 20, 40, 50); // 合并模式天空层半透明底色。
        private static readonly SKPaint SkyBorderPaint = MkStroke(80, 60, 130, 1, alpha: 120); // 天空面板边框。
        private static readonly SKPaint SkyGridPaint = MkStroke(50, 40, 80, 1, alpha: 80); // 天空面板网格线（合并模式）。
        private static readonly SKPaint SkyGridPaintSplit = MkStroke(35, 35, 46, 1); // 天空面板网格线（分离模式）。
        private static readonly SKPaint GroundLabelPaint = MkText(120, 120, 140, 11); // GROUND 标签文字。
        private static readonly SKPaint SkyLabelPaint = MkText(140, 110, 200, 11); // SKY 标签文字。
        private static readonly SKPaint MeasurePaint = MkStroke(80, 86, 112, 1.8f); // 小节线画笔。
        private static readonly SKPaint BeatPaint = MkStroke(55, 58, 78, 1.4f, new float[] { 1, 6 }); // 拍线虚线画笔。
        private static readonly SKPaint RulerLabelPaint = MkText(130, 130, 155, 10); // 标尺刻度文字。
        private static readonly SKPaint JudgePaint = MkStroke(220, 60, 30, 2f); // 判定线画笔。
        private static readonly SKPaint JudgeTextPaint = MkText(220, 60, 30, 12); // 判定线标签文字。
        private static readonly SKPaint TapFillDeepBlue = MkFill(40, 70, 150); // 普通窄地面音符填充色。
        private static readonly SKPaint TapStrokeDeepBlue = MkStroke(80, 120, 210, 1); // 普通窄地面音符描边色。
        private static readonly SKPaint TapFillWhite = MkFill(170, 210, 255); // 宽地面 Tap 填充色。
        private static readonly SKPaint TapStrokeWhite = MkStroke(210, 235, 255, 1); // 宽地面 Tap 描边色。
        private static readonly SKPaint TapFillPurple = MkFill(210, 140, 255); // 0 轨地面 Tap 填充色。
        private static readonly SKPaint TapStrokePurple = MkStroke(235, 190, 255, 1); // 0 轨地面 Tap 描边色。
        private static readonly SKPaint TapFillRed = MkFill(255, 90, 90); // 5 轨地面 Tap 填充色。
        private static readonly SKPaint TapStrokeRed = MkStroke(255, 140, 140, 1); // 5 轨地面 Tap 描边色。
        private static readonly SKPaint HoldFillDeepBlue = MkFill(40, 70, 150, 120); // 普通窄地面 Hold 填充色。
        private static readonly SKPaint HoldStrokeDeepBlue = MkStroke(80, 120, 210, 1, alpha: 200); // 普通窄地面 Hold 描边色。
        private static readonly SKPaint HoldFillWhite = MkFill(170, 210, 255, 120); // 宽地面 Hold 填充色。
        private static readonly SKPaint HoldStrokeWhite = MkStroke(210, 235, 255, 1, alpha: 220); // 宽地面 Hold 描边色。
        private static readonly SKPaint HoldFillPurple = MkFill(210, 140, 255, 120); // 0 轨地面 Hold 填充色。
        private static readonly SKPaint HoldStrokePurple = MkStroke(235, 190, 255, 1, alpha: 220); // 0 轨地面 Hold 描边色。
        private static readonly SKPaint HoldFillRed = MkFill(255, 90, 90, 120); // 5 轨地面 Hold 填充色。
        private static readonly SKPaint HoldStrokeRed = MkStroke(255, 140, 140, 1, alpha: 220); // 5 轨地面 Hold 描边色。
        private static readonly SKPaint HoldCenterLineDeepBlue = MkHoldCenterLinePaint(HoldFillDeepBlue.Color); // 普通窄地面 Hold 中线。
        private static readonly SKPaint HoldCenterLineWhite = MkHoldCenterLinePaint(HoldFillWhite.Color); // 宽地面 Hold 中线。
        private static readonly SKPaint HoldCenterLinePurple = MkHoldCenterLinePaint(HoldFillPurple.Color); // 0 轨地面 Hold 中线。
        private static readonly SKPaint HoldCenterLineRed = MkHoldCenterLinePaint(HoldFillRed.Color); // 5 轨地面 Hold 中线。
        private static readonly SKPaint FlickFillLeftPaint = MkFill(220, 200, 80); // 左向 Flick 填充色。
        private static readonly SKPaint FlickStrokeLeftPaint = MkStroke(255, 240, 120, 1.5f); // 左向 Flick 描边色。
        private static readonly SKPaint FlickFillRightPaint = MkFill(80, 200, 120); // 右向 Flick 填充色。
        private static readonly SKPaint FlickStrokeRightPaint = MkStroke(120, 240, 160, 1.5f); // 右向 Flick 描边色。
        private static readonly SKPaint SkyAreaFillPaint = MkFill(140, 100, 230, 70); // SkyArea 半透明填充。
        private static readonly SKPaint SkyAreaStrokePaint = MkStroke(180, 150, 255, 1, alpha: 160); // SkyArea 外轮廓描边。
        private static readonly SKPaint SkyAreaStripePaint = MkStripePaint(255, 255, 255, 25, 80f); // SkyArea 条纹叠加效果。
        // 选中态使用半透明填充与描边叠加，增强可见性。
        private static readonly SKPaint SelectedFillPaint = MkFill(255, 255, 255, 70); // 选中项高亮填充。
        private static readonly SKPaint SelectedStrokePaint = MkStroke(255, 255, 255, 2); // 选中项高亮描边。
        private static readonly SKPaint FpsPaint = MkText(0, 255, 0, 12); // FPS 统计文字。

        // 标尺步长相对一个小节的倍数候选，用于根据缩放自适应选择刻度密度。
        private static readonly double[] RulerMultiples = { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0, 32.0 };

        // ===== 缓存 =====
        // 标尺标签文本缓存（key 为刻度序号）。
        private readonly Dictionary<int, string> _rulerTextCache = new();
        // 上次生成标尺文本时使用的缩放值，用于判断缓存是否失效。
        private double _rulerCachePxPerMs;
        // Flick 形状路径缓存（按 RenderItem 实例和天空面板尺寸缓存）。
        private readonly Dictionary<int, (float skyLeft, float skyWidth, SKPath path)> _flickPathCache = new();

        // ===== 交互与选中事件 =====
        // 点击或切换选中项时抛出的事件。
        public event Action<RenderItem?>? NoteSelected;
        // 点击命中音符后的事件（携带点击次数，供单击/双击编辑策略使用）。
        public event Action<RenderItem?, int>? NoteClicked;
        // 完成新增音符放置后抛出的提交事件。
        public event Action<AddNotePlacement>? NotePlacementCommitted;

        // 当前待放置的音符请求；为空表示未处于新增模式。
        private AddNoteRequest? _pendingAddRequest;
        // 是否正在拖拽确定新增音符的终点。
        private bool _placingNoteDrag;
        // 新增音符拖拽起点时间（毫秒）。
        private int _placeStartTimeMs;
        // 新增音符拖拽起点轨道。
        private int _placeStartLane;
        // 新增音符拖拽起点天空位置分子。
        private int _placeStartPosNum;

        // 初始化预览控件状态，并注册加载/卸载事件。
        public SpcPreviewGLControl()
        {
            Focusable = true;
            IgnorePixelScaling = true;
            WriteDouble(ref _snapPxPerMsBits, 1.0);
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // 控件加载后重算布局并按当前渲染模式启动预览刷新驱动。
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RecalcLayout();
            StartRenderDriver();
        }

        // 控件卸载时停止所有预览刷新驱动。
        private void OnUnloaded(object sender, RoutedEventArgs e) => StopRenderDriver();

        // 根据当前 VSync 设置启动对应的刷新驱动（后台线程或 CompositionTarget.Rendering）。
        private void StartRenderDriver()
        {
            if (UseVsync)
                StartVsyncRendering();
            else
                StartRenderLoop();
        }

        // 停止所有刷新驱动，供卸载与切换渲染模式时调用。
        private void StopRenderDriver()
        {
            StopVsyncRendering();
            StopRenderLoop();
        }

        // 启用基于 WPF 合成帧回调的 VSync 渲染驱动。
        private void StartVsyncRendering()
        {
            if (_vsyncHooked) return;
            _lastFpsTick = Stopwatch.GetTimestamp();
            _prevFrameTick = _lastFpsTick;
            CompositionTarget.Rendering += OnVsyncRendering;
            _vsyncHooked = true;
        }

        // 停用 VSync 渲染驱动。
        private void StopVsyncRendering()
        {
            if (!_vsyncHooked) return;
            CompositionTarget.Rendering -= OnVsyncRendering;
            _vsyncHooked = false;
        }

        // 在每次屏幕合成帧回调时触发一次重绘，使渲染节奏与显示刷新同步。
        private void OnVsyncRendering(object? sender, EventArgs e)
        {
            if (!UseVsync || !IsVisible) return;
            InvalidateVisual();
        }

        // 启动后台渲染线程并初始化帧率统计计时。
        private void StartRenderLoop()
        {
            if (UseVsync) return;
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

        // 停止后台渲染线程并等待线程退出。
        private void StopRenderLoop()
        {
            _running = false;
            _renderThread?.Join(200);
            _renderThread = null;
            Volatile.Write(ref _invalidateRequestPending, 0);
        }

        // 缓存 UI 线程重绘委托，避免渲染线程循环中重复捕获。
        private Action? _invalidateAction;
        // UI 线程执行的重绘包装委托，用于重置“已排队”标记。
        private Action? _invalidateDispatchAction;
        // 是否已有待执行的重绘请求在 UI 队列中。
        private int _invalidateRequestPending;

        // 在 UI 线程执行重绘，并在结束后允许下一次重绘请求入队。
        private void DispatchInvalidateOnUiThread()
        {
            try
            {
                _invalidateAction?.Invoke();
            }
            finally
            {
                Volatile.Write(ref _invalidateRequestPending, 0);
            }
        }

        // 按目标帧率循环触发界面重绘请求。
        private void RenderLoop()
        {
            _invalidateAction = InvalidateVisual;
            _invalidateDispatchAction = DispatchInvalidateOnUiThread;
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

                if (Interlocked.Exchange(ref _invalidateRequestPending, 1) != 0)
                    continue;

                try
                {
                    Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Render,
                        _invalidateDispatchAction!);
                }
                catch
                {
                    Volatile.Write(ref _invalidateRequestPending, 0);
                    break;
                }
            }
        }

        // ===== 尺寸变化 =====

        // 控件尺寸变化时重新计算预览布局。
        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            RecalcLayout();
        }

        // 根据控件大小计算天空区、地面区、判定线与内容区域矩形。
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
                    // 分离模式：地面在右侧，天空在左侧。
                    float skyW = contentW * 0.50f;
                    float gndW = contentW - skyW;
                    _skyRect = new SKRect(contentLeft, (float)TopPad, contentLeft + skyW, (float)TopPad + panelH);
                    _groundRect = new SKRect(contentLeft + skyW, (float)TopPad, contentLeft + contentW, (float)TopPad + panelH);
                }
                else
                {
                    // 合并模式：地面占满宽度，天空按 4/6 比例居中叠加。
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

        // ===== 依赖属性 =====

        // 供预览控件展示的 SPC 事件列表。
        public IReadOnlyList<ISpcEvent>? Events
        {
            get => (IReadOnlyList<ISpcEvent>?)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }
        // Events 的依赖属性定义；变化时重建渲染模型并清理几何缓存。
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

        // 基础滚动速度（每秒像素数），不含 Speed 倍率。
        public double PixelsPerSecond
        {
            get => (double)GetValue(PixelsPerSecondProperty);
            set => SetValue(PixelsPerSecondProperty, value);
        }
        // PixelsPerSecond 的依赖属性定义；变化时同步更新 PxPerMs。
        public static readonly DependencyProperty PixelsPerSecondProperty =
            DependencyProperty.Register(nameof(PixelsPerSecond), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(1000.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    c.UpdatePxPerMs();
                }));

        // 预览速度倍率，用于缩放时间轴滚动速度。
        public double Speed
        {
            get => (double)GetValue(SpeedProperty);
            set => SetValue(SpeedProperty, value);
        }
        // Speed 的依赖属性定义；变化时同步更新 PxPerMs。
        public static readonly DependencyProperty SpeedProperty =
            DependencyProperty.Register(nameof(Speed), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(1.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    c.UpdatePxPerMs();
                }));

        // 根据速度倍率和像素密度更新时间轴缩放（毫秒到像素）。
        private void UpdatePxPerMs()
        {
            var speed = Math.Max(0.01, Speed);
            PxPerMs = Math.Clamp(PixelsPerSecond * speed / 1000.0, MinPxPerMs, MaxPxPerMs);
        }

        // 当前用于绘制的只读渲染模型快照。
        public RenderModel? Model
        {
            get { lock (_modelLock) return _snapModel; }
        }

        // 判定线对应时间（整数毫秒），用于常规交互更新。
        public int JudgeTimeMs
        {
            get => (int)GetValue(JudgeTimeMsProperty);
            set => SetValue(JudgeTimeMsProperty, value);
        }
        // JudgeTimeMs 的依赖属性定义；变化时写入渲染线程快照。
        public static readonly DependencyProperty JudgeTimeMsProperty =
            DependencyProperty.Register(nameof(JudgeTimeMs), typeof(int), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    WriteDouble(ref c._snapJudgeTimeMsBits, c.JudgeTimeMs);
                }));

        // 判定线对应时间（双精度毫秒），用于更平滑的滚动/缩放。
        public double JudgeTimeMsPrecise
        {
            get => (double)GetValue(JudgeTimeMsPreciseProperty);
            set => SetValue(JudgeTimeMsPreciseProperty, value);
        }
        // JudgeTimeMsPrecise 的依赖属性定义；变化时写入渲染线程快照。
        public static readonly DependencyProperty JudgeTimeMsPreciseProperty =
            DependencyProperty.Register(nameof(JudgeTimeMsPrecise), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(0.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    WriteDouble(ref c._snapJudgeTimeMsBits, c.JudgeTimeMsPrecise);
                }));

        // 当前时间轴缩放值（每毫秒像素数）。
        public double PxPerMs
        {
            get => (double)GetValue(PxPerMsProperty);
            set => SetValue(PxPerMsProperty, value);
        }
        // PxPerMs 的依赖属性定义；变化时更新渲染快照并清理标尺缓存。
        public static readonly DependencyProperty PxPerMsProperty =
            DependencyProperty.Register(nameof(PxPerMs), typeof(double), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(1.0, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    WriteDouble(ref c._snapPxPerMsBits, c.PxPerMs);
                    SpcSkiaGeometryBuilder.ClearCache();
                    c.ClearRulerTextCache();
                }));

        // 渲染线程目标帧率。
        public int TargetFps
        {
            get => (int)GetValue(TargetFpsProperty);
            set => SetValue(TargetFpsProperty, value);
        }
        // TargetFps 的依赖属性定义；变化时更新渲染循环帧间隔。
        public static readonly DependencyProperty TargetFpsProperty =
            DependencyProperty.Register(nameof(TargetFps), typeof(int), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata((int)DefaultTargetFps, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    int fps = Math.Clamp(c.TargetFps, 30, 240);
                    Volatile.Write(ref c._frameTimeMs, 1000.0 / fps);
                }));

        // 是否使用 VSync（WPF 合成帧）作为预览刷新驱动。
        public bool UseVsync
        {
            get => (bool)GetValue(UseVsyncProperty);
            set => SetValue(UseVsyncProperty, value);
        }
        // UseVsync 的依赖属性定义；变化时切换渲染驱动。
        public static readonly DependencyProperty UseVsyncProperty =
            DependencyProperty.Register(nameof(UseVsync), typeof(bool), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(true, (d, _) =>
                {
                    var c = (SpcPreviewGLControl)d;
                    if (!c.IsLoaded) return;
                    c.StopRenderDriver();
                    c.StartRenderDriver();
                }));

        // 是否在右上角显示 FPS 与帧时长统计。
        public bool ShowFpsStats
        {
            get => (bool)GetValue(ShowFpsStatsProperty);
            set => SetValue(ShowFpsStatsProperty, value);
        }
        // ShowFpsStats 的依赖属性定义。
        public static readonly DependencyProperty ShowFpsStatsProperty =
            DependencyProperty.Register(nameof(ShowFpsStats), typeof(bool), typeof(SpcPreviewGLControl),
                new FrameworkPropertyMetadata(true));

        // ===== 交互输入 =====
        // 左键拖拽时间轴滚动是否进行中。
        private bool _dragTime;
        // 右键拖拽缩放是否进行中。
        private bool _dragZoom;
        // 本次拖拽开始时的鼠标位置。
        private Point _dragStart;
        // 本次拖拽开始时的判定时间（整数毫秒）。
        private int _judgeTimeStart;
        // 本次拖拽开始时的缩放值。
        private double _pxPerMsStart;
        // 当前选中渲染项索引。
        private int _selectedItemIndex = -1;
        // 当前这次鼠标按下是否仍可视为点击（未移动成拖拽）。
        private bool _isDragClick;

        // 外部绑定用的选中项索引，同时同步写入渲染线程快照。
        public int SelectedItemIndex
        {
            get => _selectedItemIndex;
            set { _selectedItemIndex = value; _snapSelectedIndex = value; }
        }

        // 进入添加音符放置模式并保存待放置参数。
        public void BeginAddNotePlacement(AddNoteRequest request)
        {
            _pendingAddRequest = request;
            _placingNoteDrag = false;
        }

        // 返回当前控件内判定线的 Y 坐标。
        public double GetJudgeY() => Math.Max(TopPad + 20, ActualHeight - JudgeFromBottom);

        // 将给定纵坐标换算为预览时间（毫秒）。
        private double TimeAtY(double judgeY, double y)
            => ReadDouble(ref _snapJudgeTimeMsBits) + (judgeY - y) / Math.Max(1e-6, PxPerMs);

        // 处理滚轮缩放/滚动：Ctrl 缩放时间轴，否则上下移动时间位置。
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

        // 处理鼠标按下：开始拖拽/缩放，或进入新增音符放置起点。
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

        // 上次处理鼠标移动的 TickCount，用于节流高频移动事件。
        private int _lastMouseMoveTick;
        // 双击判定时间窗口（毫秒）。
        private const long ManualDoubleClickThresholdMs = 280;
        // 双击判定允许的最大位移（像素）。
        private const double ManualDoubleClickMoveThresholdPx = 6.0;
        // 上一次左键点击释放的时间戳（Environment.TickCount64）。
        private long _lastLeftClickUpTickMs = long.MinValue;
        // 上一次左键点击释放的位置（用于双击位移判定）。
        private Point _lastLeftClickUpPos;
        // 是否存在等待配对为“双击”的左键单击。
        private bool _pendingLeftSingleClick;

        // 手动检测左键单击/双击，避免在捕获鼠标的拖拽交互下丢失双击计数。
        private int DetectLeftClickCount(Point pos)
        {
            long nowMs = Environment.TickCount64;
            if (_pendingLeftSingleClick)
            {
                long dt = nowMs - _lastLeftClickUpTickMs;
                double dx = pos.X - _lastLeftClickUpPos.X;
                double dy = pos.Y - _lastLeftClickUpPos.Y;
                double dist2 = dx * dx + dy * dy;
                double move2 = ManualDoubleClickMoveThresholdPx * ManualDoubleClickMoveThresholdPx;
                if (dt >= 0 && dt <= ManualDoubleClickThresholdMs && dist2 <= move2)
                {
                    // 成功配对一次双击后立即复位，避免三击产生重复双击触发。
                    _pendingLeftSingleClick = false;
                    _lastLeftClickUpTickMs = long.MinValue;
                    return 2;
                }
            }

            _pendingLeftSingleClick = true;
            _lastLeftClickUpTickMs = nowMs;
            _lastLeftClickUpPos = pos;
            return 1;
        }

        // 处理鼠标移动，更新拖拽滚动或缩放状态。
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

        // 处理鼠标释放，结束拖拽并在点击场景下执行选中命中。
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
                {
                    var clickPos = e.GetPosition(this);
                    HandleClick(clickPos);
                }
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

        // 鼠标离开控件时取消拖拽/放置状态并释放鼠标捕获。
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

        // 在新增音符模式下记录起点，或直接提交单点音符。
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

        // 在新增音符模式下计算终点并提交拖拽型音符放置结果。
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

        // 将鼠标位置转换为放置参数（时间、轨道或天空坐标），超出有效区域时返回 false。
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

        // 按当前谱面 BPM 标尺将时间吸附到小节线或拍线附近。
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

        // 根据点击位置在预览中命中并选中最近的音符。
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

                // 不同音符的可见形状高度不同，命中框按类型扩展可点击区域。
                float extraTop = item.Type switch
                {
                    RenderItemType.SkyFlick => FlickTriH,
                    // Tap 已改为“底边对齐时间线”，形状主体整体在时间线之上。
                    RenderItemType.GroundTap => TapHalfH * 2f,
                    _ => 0f
                };
                float top = Math.Min(yStart, yEnd) - extraTop - 8;
                float bottom = Math.Max(yStart, yEnd) + 8;

                if (clickY < top || clickY > bottom) continue;

                // 先根据音符类型计算横向命中范围。
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
                        // Flick 只有一个位置点，按显示宽度扩展命中范围。
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
                        // SkyArea 使用起点/终点包围盒并集作为命中范围。
                        // 保证较窄一端也有最小命中宽度，避免难以点中。
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

            if (best == null)
            {
                // 点击空白区域时清除待配对的单击，避免下一次点中音符被误判为双击。
                _pendingLeftSingleClick = false;
                _lastLeftClickUpTickMs = long.MinValue;
            }

            int clickCount = best != null ? DetectLeftClickCount(pos) : 1;

            SelectedItemIndex = bestIdx;
            NoteSelected?.Invoke(best);
            NoteClicked?.Invoke(best, Math.Max(1, clickCount));
        }

        // 根据当前事件列表强制刷新渲染模型。
        // 适用于对原事件列表就地修改后的刷新。


        // 根据当前事件列表重建渲染模型并清理相关缓存。
        public void RefreshModel()
        {
            var model = Events != null ? SpcRenderModelBuilder.Build(Events) : null;
            lock (_modelLock) { _snapModel = model; }
            SpcSkiaGeometryBuilder.ClearCache();
            ClearFlickPathCache();
            ClearRulerTextCache();
        }

        // ===== 渲染 =====

        // 渲染回调：绘制背景、标尺、音符、判定线和帧率信息。
        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            base.OnPaintSurface(e);
            var canvas = e.Surface.Canvas;
            canvas.Clear();

            long nowTick = Stopwatch.GetTimestamp();
            double thisFrameMs = (nowTick - _prevFrameTick) * 1000.0 / Stopwatch.Frequency;
            _prevFrameTick = nowTick;
            RecordFrameTimeSample(thisFrameMs);
            if (_frameTimeSmoothed <= 0) _frameTimeSmoothed = thisFrameMs;
            else _frameTimeSmoothed = _frameTimeSmoothed * 0.85 + thisFrameMs * 0.15;

            _frameCount++;
            double elapsedSample = (nowTick - _lastFpsTick) * 1000.0 / Stopwatch.Frequency;
            if (elapsedSample >= FpsSampleMs)
            {
                _fps = (int)Math.Round(_frameCount * 1000.0 / Math.Max(1, elapsedSample));
                ComputeFrameTimePercentiles();
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

        // ===== 合并视图背景 =====
        // 绘制合并视图模式下的地面背景、天空叠加层和网格。
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

            // 绘制天空叠加层。
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

        // ===== 分离视图背景 =====
        // 绘制分离视图模式下的天空面板与地面面板背景。
        private static void PaintSplitBackground(SKCanvas canvas, SKRect sky, SKRect ground,
            float contentLeft, float w, float h, float judgeY)
        {
            canvas.DrawRect(0, 0, w, h, BgPaint);

            canvas.Save();
            canvas.ClipRect(new SKRect(contentLeft, sky.Top, ground.Right, judgeY));

            // 绘制左侧天空面板。
            canvas.DrawRect(sky.Left, sky.Top, sky.Width, judgeY - sky.Top, PanelBgPaint);
            canvas.DrawRect(sky.Left, sky.Top, sky.Width, judgeY - sky.Top, PanelBorder);
            for (int i = 1; i < SkyDivisions; i++)
            {
                float x = sky.Left + sky.Width * i / (float)SkyDivisions;
                canvas.DrawLine(x, sky.Top, x, judgeY, SkyGridPaintSplit);
            }

            // 绘制右侧地面面板。
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

        // 绘制判定线及其文本标签。
        private static void PaintJudge(SKCanvas canvas, float judgeY, float contentLeft, float w)
        {
            canvas.DrawLine(contentLeft, judgeY, w, judgeY, JudgePaint);
            DrawText(canvas, "JUDGE", contentLeft + 4, judgeY + 4, JudgeTextPaint);
        }

        // 绘制小节线、拍线和标尺标签，并横跨可视面板区域。
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

            // 计算标尺线需要横跨绘制的最右边界。
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

        // 分离模式下的天空标尺扩展入口；主标尺已统一绘制，因此此处留空。
        private void PaintSplitRuler(SKCanvas canvas, SKRect sky,
            float judgeY, double pxPerMs, double judgeTimeMs, RenderModel? model)
        {
            // 分离模式下主 PaintRuler 已覆盖天空与地面两个面板，
            // 因此此处保留空实现。
        }

        // 使用二分查找首个时间不小于指定值的音符索引。
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

        // 绘制当前可视范围内的地面 Tap/Hold 音符。
        private static void PaintGroundNotes(SKCanvas canvas, SKRect ground,
             float judgeY, double pxPerMs, double judgeTimeMs, int selectedIdx, RenderModel? model)
         {
             if (model == null) return;
             var items = model.Items;
             float clipTop = ground.Top - 50;
             float clipBottom = judgeY + 50;

             double tMin = judgeTimeMs + (judgeY - clipBottom) / pxPerMs;
             double tMax = judgeTimeMs + (judgeY - clipTop) / pxPerMs;
             int scanStart = FindFirstIndexByTime(items, (int)Math.Floor(tMin) - model.MaxItemDurationMs - 1);

             for (int idx = scanStart; idx < items.Count; idx++)
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

        // 绘制当前可视范围内的天空 Flick/SkyArea 音符。
        private void PaintSkyNotes(SKCanvas canvas, SKRect sky,
            float judgeY, double pxPerMs, double judgeTimeMs, int selectedIdx, RenderModel? model)
         {
             if (model == null) return;
             var items = model.Items;
             float clipTop = sky.Top - 50;
             float clipBottom = judgeY + 50;

             double tMin = judgeTimeMs + (judgeY - clipBottom) / pxPerMs;
             double tMax = judgeTimeMs + (judgeY - clipTop) / pxPerMs;
             int scanStart = FindFirstIndexByTime(items, (int)Math.Floor(tMin) - model.MaxItemDurationMs - 1);

             for (int idx = scanStart; idx < items.Count; idx++)
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

        // 绘制单个地面 Tap 音符及其选中高亮效果。
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

            // Tap 底边与时间线对齐，视觉上更像从判定线/拍线落下后“落座”到轨道。
            var rect = new SKRect(x0 + 2, y - TapHalfH * 2f, x0 + 2 + width, y);
            var (fill, stroke, _, _, _) = GetGroundNotePaints(lane, kind);
            canvas.DrawRoundRect(rect, 3, 3, fill);
            canvas.DrawRoundRect(rect, 3, 3, stroke);
            if (selected)
            {
                canvas.DrawRoundRect(rect, 3, 3, SelectedFillPaint);
                canvas.DrawRoundRect(rect, 3, 3, SelectedStrokePaint);
            }
        }

        // 绘制单个地面 Hold 音符主体、中心线和选中高亮效果。
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
            var (_, _, fill, stroke, centerLinePaint) = GetGroundNotePaints(lane, kind);
            canvas.DrawRect(body, fill);
            PaintHoldCenterLine(canvas, body, centerLinePaint);
            canvas.DrawRect(body, stroke);
            if (selected)
            {
                canvas.DrawRect(body, SelectedFillPaint);
                canvas.DrawRect(body, SelectedStrokePaint);
            }
        }

        // 在 Hold 音符中线位置绘制加深竖线以增强辨识度。
        private static void PaintHoldCenterLine(SKCanvas canvas, SKRect body, SKPaint linePaint)
        {
            float centerX = (body.Left + body.Right) * 0.5f;
            canvas.DrawLine(centerX, body.Top, centerX, body.Bottom, linePaint);
        }

        // 根据 Hold 底色生成中心线画笔。
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

        // 绘制地面两侧特殊轨道（0/5）的底色覆盖层。
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

        // 根据轨道位置与音符宽度类型选择地面音符的填充/描边配色。
        private static (SKPaint tapFill, SKPaint tapStroke, SKPaint holdFill, SKPaint holdStroke, SKPaint holdCenterLine) GetGroundNotePaints(int lane, int kind)
        {
            if (lane == 0)
                return (TapFillPurple, TapStrokePurple, HoldFillPurple, HoldStrokePurple, HoldCenterLinePurple);
            if (lane == 5)
                return (TapFillRed, TapStrokeRed, HoldFillRed, HoldStrokeRed, HoldCenterLineRed);

            if (kind > 1)
                return (TapFillWhite, TapStrokeWhite, HoldFillWhite, HoldStrokeWhite, HoldCenterLineWhite);

            return (TapFillDeepBlue, TapStrokeDeepBlue, HoldFillDeepBlue, HoldStrokeDeepBlue, HoldCenterLineDeepBlue);
        }

        // 绘制单个天空 Flick 音符，并按天空面板尺寸缓存路径。
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

        // 绘制单个天空 SkyArea 音符（填充、条纹、描边与选中态）。
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
        // 上次生成的 FPS 文本，键值未变化时直接复用字符串。
        private string _lastStatsStr = "";
        // FPS 文本缓存键（由 FPS/ft/P95/P99 与 VSync 状态拼接生成）。
        private long _lastStatsKey = -1;

        // 将一帧的耗时写入环形缓冲区，供分位统计使用。
        private void RecordFrameTimeSample(double frameMs)
        {
            if (frameMs <= 0 || double.IsNaN(frameMs) || double.IsInfinity(frameMs))
                return;

            _frameTimeSamples[_frameTimeSampleWriteIndex] = frameMs;
            _frameTimeSampleWriteIndex = (_frameTimeSampleWriteIndex + 1) % FrameStatsCapacity;
            if (_frameTimeSampleCount < FrameStatsCapacity)
                _frameTimeSampleCount++;
        }

        // 根据最近帧时长样本计算 P95 / P99。
        private void ComputeFrameTimePercentiles()
        {
            int count = _frameTimeSampleCount;
            if (count <= 0)
            {
                _frameTimeP95 = 0;
                _frameTimeP99 = 0;
                return;
            }

            Array.Copy(_frameTimeSamples, _frameTimePercentileScratch, count);
            Array.Sort(_frameTimePercentileScratch, 0, count);
            _frameTimeP95 = GetPercentileFromSorted(_frameTimePercentileScratch, count, 0.95);
            _frameTimeP99 = GetPercentileFromSorted(_frameTimePercentileScratch, count, 0.99);
        }

        // 从已排序样本中读取指定分位值（线性插值）。
        private static double GetPercentileFromSorted(double[] sorted, int count, double percentile)
        {
            if (count <= 0) return 0;
            if (count == 1) return sorted[0];

            double pos = Math.Clamp(percentile, 0.0, 1.0) * (count - 1);
            int lo = (int)Math.Floor(pos);
            int hi = Math.Min(count - 1, lo + 1);
            double t = pos - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }

        // 在右上角绘制 FPS 与帧时长统计信息。
        private void PaintFps(SKCanvas canvas, float w)
        {
            int fps = _fps;
            int ftX10 = (int)Math.Round(_frameTimeSmoothed * 10.0);
            int p95X10 = (int)Math.Round(_frameTimeP95 * 10.0);
            int p99X10 = (int)Math.Round(_frameTimeP99 * 10.0);
            long key =
                (((long)(UseVsync ? 1 : 0) & 0x1) << 60) |
                (((long)fps & 0xFFFF) << 44) |
                (((long)ftX10 & 0x3FFF) << 30) |
                (((long)p95X10 & 0x3FFF) << 16) |
                ((long)p99X10 & 0xFFFF);
            if (key != _lastStatsKey)
            {
                _lastStatsKey = key;
                _lastStatsStr = $"{(UseVsync ? "VSync " : "")}FPS:{fps}  ft:{_frameTimeSmoothed:0.0}ms  P95:{_frameTimeP95:0.0}  P99:{_frameTimeP99:0.0}";
            }
            DrawText(canvas, _lastStatsStr, Math.Max(4, w - 460), 10, FpsPaint);
        }

        // 按左上对齐方式在画布指定位置绘制文本。
        private static void DrawText(SKCanvas canvas, string text, float x, float y, SKPaint paint)
        {
            float baseline = y - paint.FontMetrics.Ascent;
            canvas.DrawText(text, x, baseline, paint);
        }

        // 清空标尺标签文本缓存。
        private void ClearRulerTextCache() => _rulerTextCache.Clear();

        // 释放并清空 Flick 路径缓存。
        private void ClearFlickPathCache()
        {
            foreach (var kv in _flickPathCache)
                kv.Value.path?.Dispose();
            _flickPathCache.Clear();
        }

        // 创建纯色填充画笔。
        private static SKPaint MkFill(byte r, byte g, byte b, byte a = 255)
            => new() { Color = new SKColor(r, g, b, a), IsAntialias = true, Style = SKPaintStyle.Fill };

        // 创建描边画笔（支持透明度与虚线）。
        private static SKPaint MkStroke(byte r, byte g, byte b, float w, float[]? dash = null, byte alpha = 255)
        {
            var p = new SKPaint { Color = new SKColor(r, g, b, alpha), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w };
            if (dash != null) p.PathEffect = SKPathEffect.CreateDash(dash, 0);
            return p;
        }

        // 创建文本绘制画笔。
        private static SKPaint MkText(byte r, byte g, byte b, float size)
            => new()
            {
                Color = new SKColor(r, g, b),
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas"),
                TextSize = size
            };

        // 创建天空区域使用的斜条纹着色画笔。
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

        // 停止渲染循环并释放预览控件持有的缓存资源。
        public void Dispose()
        {
            StopRenderDriver();
            ClearRulerTextCache();
            ClearFlickPathCache();
        }
    }
}
