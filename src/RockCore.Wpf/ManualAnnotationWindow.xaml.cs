using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RockCore.Core.Models;
using RockCore.Wpf.ViewModels;

namespace RockCore.Wpf;

/// <summary>
/// 人工标注窗口
///
/// 架构（简洁可靠）：
///   Border (ImageHostBorder, 浅灰背景)
///     └── Viewbox (Stretch=Uniform, 自动随窗口等比例缩放)
///           └── Canvas (ImageCanvas, 尺寸=图像原始像素, LayoutTransform=RotateTransform)
///                ├── Image (原图, Stretch=Fill, 尺寸=Canvas尺寸)
///                └── 标注图形 (Rectangle/Line/Ellipse/Border, 坐标=图像像素坐标)
///
/// 关键特性：
///   • 打开窗口时图像自动铺满可用区域
///   • 放大/缩小窗口时，Viewbox 自动同步等比例缩放
///   • 旋转按钮：Canvas.LayoutTransform=RotateTransform(angle)，Viewbox 重新适配
///   • e.GetPosition(ImageCanvas) 直接返回图像原始像素坐标——无需手动坐标转换
///   • 已保存的标注始终用原始像素坐标存储，交给 RuleEngineImageAnalyzer.ComputeFromAnnotation
///   • 所有标注图形（线/矩形/文字）随 Canvas 一起旋转——视觉效果与图像保持一致
///
/// 颜色区分：
///   橙色 - 比例尺    绿色 - 岩芯盒    蓝色 - 岩芯段    红色 - 节理
/// </summary>
public partial class ManualAnnotationWindow : Window
{
    // ===== 标注模式 =====
    private enum AnnotationMode { Scale, Piece }
    private AnnotationMode _mode = AnnotationMode.Scale;

    // ===== 标注数据（全部使用图像原始像素坐标，可直接交给计算引擎）=====
    private Point? _scaleP1 = null;
    private Point? _scaleP2 = null;
    private double _pixelPerCm = 0;
    private List<List<Point>> _pieces = new(); // 岩芯段：折线点列表
    private int _pieceCounter = 0;

    // 折线绘制状态
    private bool _pieceDrawing = false;
    private List<Point> _currentPiecePoints = new();
    private Polyline? _previewPieceLine = null;

    // ===== 原始图像尺寸（像素，加载时从位图读取）=====
    private int _imgW = 0;
    private int _imgH = 0;

    // ===== 旋转状态（0/1/2/3 = 0°/90°/180°/270°，顺时针）=====
    // 关键设计：标注坐标始终对应"当前显示的图像"，不需要运行时坐标变换
    // 每次旋转：真正旋转位图 + 交换 Canvas 尺寸 + 旋转所有已保存的标注点
    private int _rotationSteps = 0;

    // ===== 绘制参数（按图像大小自动缩放，Viewbox 再做一次缩放，最终显示大小一致）=====
    private double _strokeBase;     // 基础线宽
    private double _fontBase;       // 基础字号
    private double _markerDiameter; // 比例尺点标记直径

    // ===== 输入/输出 =====
    private readonly string _imagePath;
    private readonly double _defaultDepthStart;
    private readonly double _defaultDepthEnd;
    private readonly CorePhotoViewModel? _photoVm;
    public ImageAnalysisResult? Result { get; private set; }

    public ManualAnnotationWindow(string imagePath, double depthStart, double depthEnd, CorePhotoViewModel? photoVm = null)
    {
        InitializeComponent();
        _imagePath = imagePath;
        _defaultDepthStart = depthStart;
        _defaultDepthEnd = depthEnd;
        _photoVm = photoVm;
        Title = $"人工标注 - {System.IO.Path.GetFileName(imagePath)}";
        DepthStartInput.Text = depthStart.ToString("F2");
        DepthEndInput.Text = depthEnd.ToString("F2");
    }

    // ====================================================================
    // 初始化：加载图像 → 设置 Canvas 尺寸 → 自动铺满窗口
    // ====================================================================
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_imagePath))
        {
            MessageBox.Show(this, $"找不到图像文件: {_imagePath}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        // 读取位图（支持旋转）
        var bitmap = new BitmapImage();
        using (var fs = File.OpenRead(_imagePath))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = fs;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        // 用 TransformedBitmap 来旋转（真正改变像素尺寸，Canvas 宽高自动对应）
        BitmapSource finalBitmap = bitmap;
        if (_rotationSteps == 1)
            finalBitmap = new TransformedBitmap(bitmap, new RotateTransform(90));
        else if (_rotationSteps == 2)
            finalBitmap = new TransformedBitmap(bitmap, new RotateTransform(180));
        else if (_rotationSteps == 3)
            finalBitmap = new TransformedBitmap(bitmap, new RotateTransform(270));

        _imgW = finalBitmap.PixelWidth;
        _imgH = finalBitmap.PixelHeight;

        // Canvas 尺寸 = 当前显示的图像像素（旋转后已自动交换宽高）
        ImageCanvas.Width = _imgW;
        ImageCanvas.Height = _imgH;

        // Image 填满整个 Canvas（由 Viewbox 做最终缩放）
        SourceImage.Source = finalBitmap;
        SourceImage.Width = _imgW;
        SourceImage.Height = _imgH;
        Canvas.SetLeft(SourceImage, 0);
        Canvas.SetTop(SourceImage, 0);

        // 按图像大小计算线宽/字号/标记（压缩系数，避免遮挡）
        _strokeBase = Math.Max(2, _imgW / 800.0);
        _fontBase = Math.Max(14, _imgW / 80.0);
        _markerDiameter = Math.Max(10, _imgW / 150.0);

        if (ZoomInfo != null) ZoomInfo.Text = $"原图: {_imgW} × {_imgH} px";

        // ---- 恢复已有标注数据（从训练数据中加载用户之前的绘制）----
        if (_photoVm != null)
        {
            try
            {
                var photo = _photoVm.ToModel();
                if (!string.IsNullOrEmpty(photo.AnalysisResultJson))
                {
                    var prevResult = System.Text.Json.JsonSerializer.Deserialize<ImageAnalysisResult>(photo.AnalysisResultJson);
                    if (prevResult?.TrainingData != null)
                    {
                        var td = prevResult.TrainingData;

                        // 恢复比例尺
                        if (td.ScalePoint1.HasValue && td.ScalePoint2.HasValue)
                        {
                            _scaleP1 = new Point(td.ScalePoint1.Value.X, td.ScalePoint1.Value.Y);
                            _scaleP2 = new Point(td.ScalePoint2.Value.X, td.ScalePoint2.Value.Y);
                            _pixelPerCm = td.PixelPerCm;
                        }

                        // 恢复岩芯段折线
                        _pieces.Clear();
                        _pieceCounter = 0;
                        foreach (var tp in td.CorePieces)
                        {
                            var points = new List<Point>();
                            foreach (var pt in tp.PolylinePoints)
                                points.Add(new Point(pt.X, pt.Y));
                            if (points.Count >= 2)
                            {
                                _pieces.Add(points);
                                _pieceCounter++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"恢复历史标注失败: {ex.Message}");
            }
        }

        UpdateStatusAndMode();
        RedrawAllAnnotations();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Viewbox 自动处理缩放，无需手动调整；这里只做一次 Redraw 以防万一
        if (_imgW > 0) RedrawAllAnnotations();
    }

    // ====================================================================
    // 模式切换
    // ====================================================================
    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (ModeScale == null) return;

        if (sender == ModeScale) _mode = AnnotationMode.Scale;
        else if (sender == ModePiece) _mode = AnnotationMode.Piece;

        // 切换模式时结束当前折线绘制
        if (_pieceDrawing && sender != ModePiece)
        {
            FinishCurrentPiece();
        }

        UpdateStatusAndMode();
    }

    private void UpdateStatusAndMode()
    {
        if (StatusText == null) return;
        StatusText.Text = _mode switch
        {
            AnnotationMode.Scale => "在图像上点击两点以标注比例尺",
            AnnotationMode.Piece => _pieceDrawing
                ? $"正在绘制岩芯段（{_currentPiecePoints.Count} 个点），右键或回车结束"
                : "在图像上依次点击绘制岩芯段折线（右键/回车结束）",
            _ => ""
        };
    }

    // ====================================================================
    // 旋转按钮：真正旋转位图 + 交换 Canvas 尺寸 + 旋转所有已标注的点
    // 标注坐标始终对应"当前显示的图像"，所以旋转操作必须同时旋转所有已保存点
    // ====================================================================
    private void RotateButton_Click(object sender, RoutedEventArgs e)
    {
        // 记录旋转前的 Canvas 尺寸（用于坐标变换）
        double oldW = _imgW;
        double oldH = _imgH;

        // 增加旋转步数（0→1→2→3→0）
        _rotationSteps = (_rotationSteps + 1) % 4;

        // 重新加载并旋转图像（也会重算 _imgW/_imgH）
        var bitmap = new BitmapImage();
        using (var fs = File.OpenRead(_imagePath))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = fs;
            bitmap.EndInit();
            bitmap.Freeze();
        }

        BitmapSource finalBitmap = bitmap;
        if (_rotationSteps == 1)
            finalBitmap = new TransformedBitmap(bitmap, new RotateTransform(90));
        else if (_rotationSteps == 2)
            finalBitmap = new TransformedBitmap(bitmap, new RotateTransform(180));
        else if (_rotationSteps == 3)
            finalBitmap = new TransformedBitmap(bitmap, new RotateTransform(270));

        _imgW = finalBitmap.PixelWidth;
        _imgH = finalBitmap.PixelHeight;

        ImageCanvas.Width = _imgW;
        ImageCanvas.Height = _imgH;
        SourceImage.Source = finalBitmap;
        SourceImage.Width = _imgW;
        SourceImage.Height = _imgH;

        // 重新计算绘制参数（压缩系数，避免遮挡）
        _strokeBase = Math.Max(2, _imgW / 800.0);
        _fontBase = Math.Max(14, _imgW / 80.0);
        _markerDiameter = Math.Max(10, _imgW / 150.0);

        // 旋转所有已保存的标注坐标 —— 顺时针 90°: (x, y) → (oldH - y, x)
        RotateAllAnnotations(oldW, oldH);

        // 重绘
        RedrawAllAnnotations();
        UpdateSidePanel();
        if (ZoomInfo != null) ZoomInfo.Text = $"原图: {_imgW} × {_imgH} px";
    }

    // 顺时针旋转所有标注点 90°
    private Point RotateCW(Point p, double oldH)
    {
        // 顺时针 90°: (x, y) → (oldH - y, x)
        return new Point(oldH - p.Y, p.X);
    }

    private void RotateAllAnnotations(double oldW, double oldH)
    {
        // 比例尺点
        if (_scaleP1.HasValue) _scaleP1 = RotateCW(_scaleP1.Value, oldH);
        if (_scaleP2.HasValue) _scaleP2 = RotateCW(_scaleP2.Value, oldH);

        // 岩芯段折线
        for (int i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];
            for (int j = 0; j < piece.Count; j++)
            {
                piece[j] = RotateCW(piece[j], oldH);
            }
        }
    }

    // ====================================================================
    // 鼠标事件：左键点击/拖拽
    // e.GetPosition(ImageCanvas) 直接返回图像原始像素坐标——无需任何坐标转换
    // ====================================================================
    private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(ImageCanvas); // 👉 原始像素坐标

        // 比例尺：两次点击
        if (_mode == AnnotationMode.Scale)
        {
            if (_scaleP1 == null)
            {
                _scaleP1 = p;
                _scaleP2 = null;
                if (StatusText != null)
                    StatusText.Text = $"P1: ({p.X:F0}, {p.Y:F0}) px — 请点击 P2";
            }
            else
            {
                _scaleP2 = p;
                RecalcScaleAndRedraw();
            }
            RedrawAllAnnotations();
            return;
        }

        // 岩芯段：折线模式，每次点击添加一个点
        if (_mode == AnnotationMode.Piece)
        {
            AddPiecePoint(p);
            return;
        }
    }

    private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        // 岩芯段折线预览
        if (_pieceDrawing && _currentPiecePoints.Count > 0 && _previewPieceLine != null)
        {
            Point previewPoint = e.GetPosition(ImageCanvas);
            var points = new List<Point>(_currentPiecePoints);
            points.Add(previewPoint);
            _previewPieceLine.Points = new PointCollection(points);
        }
    }

    private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 岩芯段和比例尺都通过单次点击完成，不需要左键抬起处理
    }

    // 岩芯段折线点处理
    private void AddPiecePoint(Point p)
    {
        if (!_pieceDrawing)
        {
            // 开始新的折线
            _pieceDrawing = true;
            _currentPiecePoints.Clear();
            _currentPiecePoints.Add(p);

            // 创建预览折线
            _previewPieceLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                StrokeThickness = _strokeBase,
                StrokeDashArray = new DoubleCollection(new double[] { 6, 4 }),
                Points = new PointCollection(new[] { p })
            };
            ImageCanvas.Children.Add(_previewPieceLine);
        }
        else
        {
            // 添加新点
            double dist = Math.Sqrt((p.X - _currentPiecePoints[^1].X) * (p.X - _currentPiecePoints[^1].X) +
                                    (p.Y - _currentPiecePoints[^1].Y) * (p.Y - _currentPiecePoints[^1].Y));
            if (dist > 5) // 过滤太近的点
            {
                _currentPiecePoints.Add(p);
            }
        }
        UpdateStatusAndMode();
    }

    private void FinishCurrentPiece()
    {
        if (_pieceDrawing && _currentPiecePoints.Count >= 2)
        {
            _pieceCounter++;
            _pieces.Add(new List<Point>(_currentPiecePoints));
        }

        // 清理状态
        _pieceDrawing = false;
        _currentPiecePoints.Clear();
        if (_previewPieceLine != null)
        {
            ImageCanvas.Children.Remove(_previewPieceLine);
            _previewPieceLine = null;
        }

        RedrawAllAnnotations();
        UpdateSidePanel();
        UpdateStatusAndMode();
    }

    // 右键结束折线绘制
    private void ImageCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == AnnotationMode.Piece && _pieceDrawing)
        {
            e.Handled = true;
            FinishCurrentPiece();
        }
    }

    // 键盘事件：回车结束折线绘制
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Return) && _mode == AnnotationMode.Piece && _pieceDrawing)
        {
            FinishCurrentPiece();
        }
        else if (e.Key == Key.Escape && _pieceDrawing)
        {
            // 取消当前折线
            _pieceDrawing = false;
            _currentPiecePoints.Clear();
            if (_previewPieceLine != null)
            {
                ImageCanvas.Children.Remove(_previewPieceLine);
                _previewPieceLine = null;
            }
            UpdateStatusAndMode();
        }
    }

    // ====================================================================
    // 比例尺输入框（实际厘米数）
    // ====================================================================
    private void ScaleCmInput_LostFocus(object sender, RoutedEventArgs e)
    {
        RecalcScaleAndRedraw();
    }

    private void RecalcScaleAndRedraw()
    {
        if (_scaleP1 == null || _scaleP2 == null) return;
        double distPx = Math.Sqrt(
            (_scaleP2.Value.X - _scaleP1.Value.X) * (_scaleP2.Value.X - _scaleP1.Value.X) +
            (_scaleP2.Value.Y - _scaleP1.Value.Y) * (_scaleP2.Value.Y - _scaleP1.Value.Y));
        if (distPx <= 1) { _pixelPerCm = 0; return; }
        if (!double.TryParse(ScaleCmInput.Text, out double cm) || cm <= 0) cm = 10;
        _pixelPerCm = distPx / cm;
        if (ScaleInfo != null)
            ScaleInfo.Text = $"{distPx:F0} px / {cm:F1} cm = {_pixelPerCm:F1} px/cm";
        if (StatusText != null)
            StatusText.Text = $"比例尺已设置: {_pixelPerCm:F1} px/cm = {distPx:F0} px / {cm:F1} cm";
        RedrawAllAnnotations();
        UpdateSidePanel();
    }

    // ====================================================================
    // 撤销 / 清空
    // ====================================================================
    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == AnnotationMode.Scale)
        {
            if (_scaleP2 != null)
            {
                _scaleP2 = null;
                _pixelPerCm = 0;
                if (ScaleInfo != null) ScaleInfo.Text = "P1 已设，等待 P2…";
                if (StatusText != null) StatusText.Text = "已撤销 P2，请重新点击第 2 点";
            }
            else if (_scaleP1 != null)
            {
                _scaleP1 = null;
                _pixelPerCm = 0;
                if (ScaleInfo != null) ScaleInfo.Text = "未设置（点击图像两点标注）";
                if (StatusText != null) StatusText.Text = "已清空比例尺";
            }
        }
        else if (_mode == AnnotationMode.Piece && _pieces.Count > 0)
        {
            _pieces.RemoveAt(_pieces.Count - 1);
            if (_pieces.Count == 0) _pieceCounter = 0;
        }
        RedrawAllAnnotations();
        UpdateSidePanel();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, "确定要清空所有标注吗？", "确认",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        _scaleP1 = null; _scaleP2 = null; _pixelPerCm = 0;
        _pieces.Clear();
        _pieceCounter = 0;
        if (ScaleInfo != null) ScaleInfo.Text = "未设置（点击图像两点标注）";

        // 清理绘制状态
        _pieceDrawing = false;
        _currentPiecePoints.Clear();
        if (_previewPieceLine != null)
        {
            ImageCanvas.Children.Remove(_previewPieceLine);
            _previewPieceLine = null;
        }

        RedrawAllAnnotations();
        UpdateSidePanel();
    }

    // ====================================================================
    // 完成 / 取消
    // ====================================================================
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        // 结束当前正在绘制的折线
        FinishCurrentPiece();

        if (_pieces.Count == 0)
        {
            MessageBox.Show(this, "请至少标注一个岩芯段后再完成。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!double.TryParse(DepthStartInput.Text, out double ds)) ds = _defaultDepthStart;
        if (!double.TryParse(DepthEndInput.Text, out double de)) de = _defaultDepthEnd;

        try
        {
            // 将折线转换为计算引擎需要的格式
            // (起点X, 起点Y, 终点X, 终点Y, 折线总长度, 标签, 折线点)
            var pieceRects = new List<(double sx, double sy, double ex, double ey, double len, string label, List<(double x, double y)> points)>();
            int idx = 1;
            foreach (var piecePoints in _pieces)
            {
                // 计算折线总长度（实际岩芯段长度）
                double totalLen = 0;
                for (int i = 0; i < piecePoints.Count - 1; i++)
                {
                    double dx = piecePoints[i + 1].X - piecePoints[i].X;
                    double dy = piecePoints[i + 1].Y - piecePoints[i].Y;
                    totalLen += Math.Sqrt(dx * dx + dy * dy);
                }

                // 折线起点和终点
                Point start = piecePoints.First();
                Point end = piecePoints.Last();

                // 保留真正的折线点，供标注图绘制使用
                var contourPoints = new List<(double x, double y)>();
                foreach (var pt in piecePoints)
                    contourPoints.Add((pt.X, pt.Y));

                pieceRects.Add((start.X, start.Y, end.X, end.Y, totalLen, $"P{idx}", contourPoints));
                idx++;
            }

            // 读取比例尺厘米数（同时作为每行标准深度长度）
            double scaleCmVal = 100.0;
            if (!string.IsNullOrWhiteSpace(ScaleCmInput.Text))
                double.TryParse(ScaleCmInput.Text, out scaleCmVal);
            if (scaleCmVal <= 0) scaleCmVal = 100.0;

            var analyzer = new RockCore.Infrastructure.ImageAnalysis.RuleEngineImageAnalyzer();
            
            // 传递比例尺端点（用于标注图绘制）
            (double x, double y)? scaleP1 = _scaleP1.HasValue ? (_scaleP1.Value.X, _scaleP1.Value.Y) : null;
            (double x, double y)? scaleP2 = _scaleP2.HasValue ? (_scaleP2.Value.X, _scaleP2.Value.Y) : null;
            
            Result = analyzer.ComputeFromAnnotation(
                _imagePath, ds, de, _pixelPerCm, scaleCmVal, null,
                pieceRects,
                new List<(double x1, double y1, double x2, double y2)>(),
                scaleP1, scaleP2,
                _imgW, _imgH);

            if (Result != null)
            {
                try { _ = new RockCore.Infrastructure.ImageAnalysis.ImageAnnotationService().DrawAnnotations(_imagePath, Result, ds, de); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"生成标注图失败: {ex.Message}"); }
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"计算失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ====================================================================
    // 重绘所有已保存的标注（核心显示逻辑）
    // 每一条标注都绘制在 Canvas 的原始像素坐标中，
    // LayoutTransform 自动做旋转，Viewbox 自动做缩放——结果就是图像与标注完全对齐
    // ====================================================================
    private void RedrawAllAnnotations()
    {
        if (ImageCanvas == null || SourceImage == null || _imgW == 0) return;

        // 清除所有标注元素，保留 SourceImage
        List<UIElement> toRemove = new();
        foreach (UIElement child in ImageCanvas.Children)
        {
            if (child != SourceImage && child != _previewPieceLine) toRemove.Add(child);
        }
        foreach (var el in toRemove) ImageCanvas.Children.Remove(el);

        double stroke = _strokeBase;
        double font = _fontBase * 0.6;     // 标签缩小 40%，避免遮挡岩芯
        double marker = _markerDiameter * 0.7;  // 标记点也适当缩小

        // ---- 1. 比例尺（橙色圆点 + 连线 + 距离文本）----
        if (_scaleP1 != null)
        {
            Point p1 = _scaleP1.Value;
            AddDot(p1, new SolidColorBrush(Color.FromRgb(230, 126, 34)), marker);
            AddLabel($"P1 ({p1.X:F0},{p1.Y:F0})", p1.X + marker, p1.Y - marker,
                new SolidColorBrush(Color.FromRgb(230, 126, 34)), font);

            if (_scaleP2 != null)
            {
                Point p2 = _scaleP2.Value;
                ImageCanvas.Children.Add(new Line
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(230, 126, 34)),
                    StrokeThickness = stroke,
                    X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y
                });
                AddDot(p2, new SolidColorBrush(Color.FromRgb(230, 126, 34)), marker);

                double distPx = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
                double distCm = _pixelPerCm > 0 ? distPx / _pixelPerCm : 0;
                double midX = (p1.X + p2.X) / 2;
                double midY = (p1.Y + p2.Y) / 2;
                string label = _pixelPerCm > 0
                    ? $"{distPx:F0}px = {distCm:F1}cm"
                    : $"{distPx:F0}px (请设置厘米数)";
                AddLabel(label, midX + marker, midY - font,
                    new SolidColorBrush(Color.FromRgb(230, 126, 34)), font);
            }
        }

        // ---- 2. 岩芯段（蓝色折线，只显示折线本身，不绘制端点圆点和长度标签，避免遮挡岩芯）----
        int pieceIdx = 1;
        foreach (var piecePoints in _pieces)
        {
            if (piecePoints.Count >= 2)
            {
                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                    StrokeThickness = stroke,
                    Points = new PointCollection(piecePoints)
                };
                ImageCanvas.Children.Add(polyline);

                // 折线端点处只显示极小编号 P1 P2...，放在折线起点上方一点点
                double tinyFont = _fontBase * 0.4;
                Point start = piecePoints[0];
                AddLabel($"P{pieceIdx}", start.X + marker, start.Y - tinyFont - 2,
                    new SolidColorBrush(Color.FromRgb(52, 152, 219)), tinyFont);
            }
            pieceIdx++;
        }
    }

    // ====================================================================
    // 辅助绘制方法
    // ====================================================================
    private void AddDot(Point center, Brush fill, double diameter)
    {
        var dot = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = fill,
            Stroke = Brushes.Black,
            StrokeThickness = Math.Max(1, _strokeBase * 0.2)
        };
        Canvas.SetLeft(dot, center.X - diameter / 2);
        Canvas.SetTop(dot, center.Y - diameter / 2);
        ImageCanvas.Children.Add(dot);
    }

    private void AddLabel(string text, double x, double y, Brush fg, double fontSize)
    {
        // 半透明白色背景 → 在任何图像颜色上都清晰可见
        var tb = new TextBlock
        {
            Text = text,
            Foreground = fg,
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(6, 2, 6, 2)
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = tb
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        ImageCanvas.Children.Add(border);
    }

    // ====================================================================
    // 右侧数据面板
    // ====================================================================
    private void UpdateSidePanel()
    {
        if (PiecesHeader != null) PiecesHeader.Text = $"岩芯段 ({_pieces.Count})";

        var pieceList = new List<UIElement>();
        int pieceNum = 1;
        for (int i = 0; i < _pieces.Count; i++)
        {
            int idx = i;
            var piecePoints = _pieces[i];
            
            // 计算折线总长度
            double totalLen = 0;
            for (int j = 0; j < piecePoints.Count - 1; j++)
            {
                double dx = piecePoints[j + 1].X - piecePoints[j].X;
                double dy = piecePoints[j + 1].Y - piecePoints[j].Y;
                totalLen += Math.Sqrt(dx * dx + dy * dy);
            }
            double lenCm = _pixelPerCm > 0 ? totalLen / _pixelPerCm : 0;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2),
                Background = i % 2 == 0
                    ? new SolidColorBrush(Color.FromArgb(0x18, 52, 152, 219))
                    : Brushes.Transparent
            };

            var txt = new TextBlock
            {
                Text = $"P{pieceNum}: {piecePoints.Count}点 " + 
                       $"({totalLen:F0}px)" + 
                       (_pixelPerCm > 0 ? $" = {lenCm:F1}cm" : ""),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40))
            };
            row.Children.Add(txt);

            var del = new Button
            {
                Content = "×",
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = idx,
                FontSize = 10,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            del.Click += (s, _) =>
            {
                _pieces.RemoveAt((int)((Button)s!).Tag!);
                if (_pieces.Count == 0) _pieceCounter = 0;
                RedrawAllAnnotations();
                UpdateSidePanel();
            };
            row.Children.Add(del);
            pieceList.Add(row);
            pieceNum++;
        }
        if (PiecesList != null) PiecesList.ItemsSource = pieceList;
    }
}
