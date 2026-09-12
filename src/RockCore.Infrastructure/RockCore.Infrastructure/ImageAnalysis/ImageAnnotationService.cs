using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Infrastructure.ImageAnalysis;

/// <summary>
/// 图像标注服务：将分析结果绘制到原图上，生成带标注的可视化图像
/// 使用 System.Drawing 绘制中文标签，解决 OpenCvSharp 不支持中文的问题
/// </summary>
public class ImageAnnotationService
{
    /// <summary>
    /// 根据分析结果在原图上绘制标注，返回带标注的图像路径
    /// </summary>
    public string DrawAnnotations(string originalImagePath, ImageAnalysisResult result, double depthStart, double depthEnd)
    {
        if (!result.Success)
            return originalImagePath;

        // 防御性：如果 AnnotationData 为 null（JSON反序列化后可能丢失），从 TrainingData 重建
        if (result.AnnotationData == null)
            result.AnnotationData = RebuildAnnotationDataFromTraining(result);

        if (result.AnnotationData == null)
            return originalImagePath;

        // 使用 OpenCvSharp 读取原图
        using var cvImage = Cv2.ImRead(originalImagePath, ImreadModes.Color);
        if (cvImage.Empty())
            return originalImagePath;

        int imgW = cvImage.Width;
        int imgH = cvImage.Height;

        // 将 OpenCV Mat 转换为 Bitmap
        using var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(cvImage);

        var annotation = result.AnnotationData;

        // 动态字号：根据图像宽度自适应
        float fontSize = Math.Max(12f, Math.Min(24f, imgW / 100f));
        int lineThickness = Math.Max(2, imgW / 500);

        // ===== 统一用一个 Graphics 对象绘制所有标注 =====
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            // 1. 绘制岩芯盒边界（绿色粗线）
            if (annotation.CoreBoxRect != null)
            {
                var rect = annotation.CoreBoxRect;
                if (rect.X >= 0 && rect.Y >= 0 && rect.Width > 10 && rect.Height > 10
                    && rect.X + rect.Width <= imgW && rect.Y + rect.Height <= imgH)
                {
                    using var pen = new Pen(Color.Lime, lineThickness + 2);
                    g.DrawRectangle(pen, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
                }
            }

            // 2. 绘制岩芯段折线（按完整性等级上色）
            int strokeWidth = Math.Max(3, lineThickness + 2);
            foreach (var piece in annotation.CorePieces)
            {
                if (piece.ContourPoints == null || piece.ContourPoints.Count < 2) continue;

                Color color = GetIntegrityColor(piece.Level);

                var pts = piece.ContourPoints
                    .Select(p => new PointF((float)p.X, (float)p.Y))
                    .ToArray();

                using var outlinePen = new Pen(Color.FromArgb(220, 0, 0, 0), strokeWidth + 3);
                if (pts.Length == 2)
                    g.DrawLine(outlinePen, pts[0], pts[1]);
                else
                    g.DrawLines(outlinePen, pts);

                using var mainPen = new Pen(color, strokeWidth);
                if (pts.Length == 2)
                    g.DrawLine(mainPen, pts[0], pts[1]);
                else
                    g.DrawLines(mainPen, pts);
            }

            // 3. 绘制结构面（红色竖线 + 白色三角形标记，画在岩芯线之上）
            float triangleSize = Math.Max(12f, Math.Min(24f, imgW / 80f));
            using var jointPen = new Pen(Color.FromArgb(255, 255, 40, 40), Math.Max(3, lineThickness + 1));
            using var whiteBrush = new SolidBrush(Color.White);
            using var jointOutlinePen = new Pen(Color.FromArgb(255, 255, 40, 40), 2);

            foreach (var joint in annotation.Joints)
            {
                float x1 = (float)joint.Start.X;
                float y1 = (float)joint.Start.Y;
                float x2 = (float)joint.End.X;
                float y2 = (float)joint.End.Y;
                float centerX = (x1 + x2) / 2f;

                // 节理线（红色竖线贯穿岩芯上下）
                g.DrawLine(jointPen, x1, y1, x2, y2);

                // 节理上方白色三角形标记（朝上）
                PointF[] triangle = new PointF[]
                {
                    new PointF(centerX, y1 - triangleSize),
                    new PointF(centerX - triangleSize * 0.7f, y1 + triangleSize * 0.3f),
                    new PointF(centerX + triangleSize * 0.7f, y1 + triangleSize * 0.3f)
                };
                g.FillPolygon(whiteBrush, triangle);
                g.DrawPolygon(jointOutlinePen, triangle);
            }

            // 4. 绘制比例尺（橙色线段 + 两端圆点 + 文字标签）
            if (annotation.RulerRect != null && annotation.RulerRect.Width > 0)
            {
                var ruler = annotation.RulerRect;
                float p1X = (float)ruler.Left;
                float p1Y = (float)ruler.Top;
                float p2X = (float)ruler.Right;
                float p2Y = (float)ruler.Bottom;

                using var pen = new Pen(Color.FromArgb(255, 230, 126, 34), lineThickness + 1);
                g.DrawLine(pen, p1X, p1Y, p2X, p2Y);

                float dotSize = Math.Max(6f, imgW / 200f);
                using var brush = new SolidBrush(Color.FromArgb(255, 230, 126, 34));
                g.FillEllipse(brush, p1X - dotSize / 2, p1Y - dotSize / 2, dotSize, dotSize);
                g.FillEllipse(brush, p2X - dotSize / 2, p2Y - dotSize / 2, dotSize, dotSize);

                float midX = (p1X + p2X) / 2;
                float midY = (p1Y + p2Y) / 2;
                double distPx = Math.Sqrt((p2X - p1X) * (p2X - p1X) + (p2Y - p1Y) * (p2Y - p1Y));
                double distCm = annotation.PixelPerCm > 0 ? distPx / annotation.PixelPerCm : 0;
                string label = annotation.PixelPerCm > 0
                    ? $"{distPx:F0}px = {distCm:F1}cm"
                    : $"{distPx:F0}px";

                using var font = new Font("微软雅黑", fontSize * 0.8f, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.FromArgb(255, 230, 126, 34));
                SizeF textSize = g.MeasureString(label, font);
                g.DrawString(label, font, textBrush, midX - textSize.Width / 2, midY - textSize.Height - 5);
            }

            // 5. 绘制完整性分段标签（放在分段起始端（浅端）第一个piece的上方）
            if (result.IntegritySegments != null && result.IntegritySegments.Count > 0)
            {
                foreach (var seg in result.IntegritySegments)
                {
                    if (seg.LabelAnchorX <= 0 || seg.LabelAnchorY <= 0) continue;
                    if (seg.LabelAnchorX >= imgW || seg.LabelAnchorY >= imgH) continue;

                    string levelText = GetIntegrityLevelText(seg.Level);
                    string depthText = $"{seg.DepthStart:F2}-{seg.DepthEnd:F2}m";
                    string labelText = $"{depthText}  {levelText}";

                    Color labelColor = GetIntegrityColor(seg.Level);

                    // 标签锚定在分段第一个piece的左上方
                    float labelX = Math.Max(5, (float)seg.LabelAnchorX + 5);
                    float labelY = Math.Max(fontSize + 5, (float)seg.LabelAnchorY - fontSize * 2.2f);

                    // 标签不超出图片范围
                    if (labelY < 10) labelY = (float)seg.LabelAnchorY + 5;
                    if (labelX + 250 > imgW) labelX = imgW - 260;

                    DrawTextWithBackground(g, labelText, labelX, labelY, labelColor, fontSize * 0.8f);
                }
            }
        }

        // 6. 在左上角绘制分析信息汇总
        DrawSummaryOverlaySimple(bitmap, result, depthStart, depthEnd, fontSize);

        // 7. 在底部绘制图例
        DrawLegendSimple(bitmap, fontSize);

        // 8. 在右上角绘制完整性分段文字列表
        DrawIntegritySegmentsList(bitmap, result, fontSize);

        // 保存标注图像
        string annotatedPath = GetAnnotatedImagePath(originalImagePath);
        bitmap.Save(annotatedPath, ImageFormat.Jpeg);

        return annotatedPath;
    }

    /// <summary>
    /// 当 AnnotationData 为空时，从 TrainingData 重建标注数据
    /// 确保用户原始绘制的岩芯线和节理位置 100% 精确复现
    /// </summary>
    private static ImageAnnotationData RebuildAnnotationDataFromTraining(ImageAnalysisResult result)
    {
        var annotation = new ImageAnnotationData
        {
            PixelPerCm = result.PixelPerCm
        };

        if (result.TrainingData == null)
            return annotation;

        // 重建比例尺
        if (result.TrainingData.ScalePoint1.HasValue && result.TrainingData.ScalePoint2.HasValue)
        {
            var sp1 = result.TrainingData.ScalePoint1.Value;
            var sp2 = result.TrainingData.ScalePoint2.Value;
            double minX = Math.Min(sp1.X, sp2.X);
            double minY = Math.Min(sp1.Y, sp2.Y);
            double maxX = Math.Max(sp1.X, sp2.X);
            double maxY = Math.Max(sp1.Y, sp2.Y);
            annotation.RulerRect = AnnotationRect.FromXYWH(minX, minY, maxX - minX, maxY - minY);
            annotation.DepthRuler = new RulerAnnotation
            {
                Rect = annotation.RulerRect,
                Label = $"比例尺: {result.PixelPerCm:F1}px/cm"
            };
        }

        // 重建岩芯段折线（100% 原始用户绘制）
        foreach (var tp in result.TrainingData.CorePieces)
        {
            var piece = new CorePieceAnnotation
            {
                DepthStart = 0,
                DepthEnd = 0,
                LengthCm = tp.LengthCm
            };

            // 直接使用用户原始绘制的折线点，不做任何计算转换
            if (tp.PolylinePoints != null && tp.PolylinePoints.Count > 0)
            {
                foreach (var pt in tp.PolylinePoints)
                    piece.ContourPoints.Add(new ContourPoint((int)pt.X, (int)pt.Y));

                // 计算外接矩形
                double minPx = tp.PolylinePoints.Min(p => p.X);
                double minPy = tp.PolylinePoints.Min(p => p.Y);
                double maxPx = tp.PolylinePoints.Max(p => p.X);
                double maxPy = tp.PolylinePoints.Max(p => p.Y);
                piece.Rect = AnnotationRect.FromXYWH(minPx, minPy, maxPx - minPx, maxPy - minPy);
            }

            annotation.CorePieces.Add(piece);
        }

        // 重建节理：基于 TrainingData 中的岩芯段顺序（绘制顺序），按同行相邻 piece 生成节理
        if (annotation.CorePieces.Count > 0)
        {
            // 按绘制顺序识别行（X回退 + Y下移 双层判断）
            var rows = IdentifyCoreRowsFromAnnotationsByDrawOrder(annotation.CorePieces);

            double imgH = result.TrainingData != null && result.TrainingData.ImageHeight > 0
                ? result.TrainingData.ImageHeight
                : 1000.0;
            double jointLineHalfLen = Math.Max(15.0, imgH * 0.015);

            foreach (var row in rows)
            {
                for (int i = 0; i < row.Count - 1; i++)
                {
                    var cur = row[i];
                    var next = row[i + 1];

                    if (cur.ContourPoints.Count < 2 || next.ContourPoints.Count < 2)
                        continue;

                    double curEndX = cur.ContourPoints.Max(p => p.X);
                    double nextStartX = next.ContourPoints.Min(p => p.X);

                    double gapCenterX = (curEndX + nextStartX) / 2.0;

                    double curY = cur.ContourPoints.Average(p => p.Y);
                    double nextY = next.ContourPoints.Average(p => p.Y);
                    double yCenter = (curY + nextY) / 2.0;
                    double gapTop = yCenter - jointLineHalfLen;
                    double gapBot = yCenter + jointLineHalfLen;

                    var joint = new JointAnnotation
                    {
                        Start = AnnotationPoint.FromXY(gapCenterX, gapTop),
                        End = AnnotationPoint.FromXY(gapCenterX, gapBot),
                        Type = "节理",
                        WidthCm = result.PixelPerCm > 0
                            ? Math.Max(1.0, Math.Abs(nextStartX - curEndX)) / result.PixelPerCm
                            : 0,
                        Angle = 90,
                        IsThrough = true
                    };
                    joint.ContourPoints.Add(new ContourPoint((int)gapCenterX, (int)gapTop));
                    joint.ContourPoints.Add(new ContourPoint((int)gapCenterX, (int)gapBot));
                    annotation.Joints.Add(joint);
                }
            }
        }

        return annotation;
    }

    /// <summary>
    /// 从 CorePieceAnnotation 列表识别岩芯行（基于绘制顺序 + X回退 + Y下移 双层判断）
    /// CorePieces 的顺序就是绘制顺序
    /// </summary>
    private static List<List<CorePieceAnnotation>> IdentifyCoreRowsFromAnnotationsByDrawOrder(List<CorePieceAnnotation> pieces)
    {
        var rows = new List<List<CorePieceAnnotation>>();
        if (pieces.Count == 0) return rows;

        double GetLeftX(CorePieceAnnotation p)
        {
            if (p.ContourPoints.Count > 0) return p.ContourPoints.Min(pt => pt.X);
            return p.Rect.Left;
        }
        double GetRightX(CorePieceAnnotation p)
        {
            if (p.ContourPoints.Count > 0) return p.ContourPoints.Max(pt => pt.X);
            return p.Rect.Right;
        }
        double GetYCenter(CorePieceAnnotation p)
        {
            if (p.ContourPoints.Count > 0) return p.ContourPoints.Average(pt => pt.Y);
            return (p.Rect.Top + p.Rect.Bottom) / 2.0;
        }

        var currentRow = new List<CorePieceAnnotation> { pieces[0] };
        double prevRightX = GetRightX(pieces[0]);
        double prevYCenter = GetYCenter(pieces[0]);

        for (int i = 1; i < pieces.Count; i++)
        {
            var p = pieces[i];
            double curLeftX = GetLeftX(p);
            double curRightX = GetRightX(p);
            double curYCenter = GetYCenter(p);

            // 比较当前左X vs 前一个右X（不是前一个左X）
            bool xFallback = curLeftX < prevRightX * 0.7;
            bool yMoveDown = curYCenter > prevYCenter + 5.0;

            if (xFallback && yMoveDown)
            {
                rows.Add(currentRow);
                currentRow = new List<CorePieceAnnotation> { p };
            }
            else
            {
                currentRow.Add(p);
            }

            prevRightX = curRightX;
            prevYCenter = curYCenter;
        }

        rows.Add(currentRow);
        return rows;
    }

    /// <summary>
    /// 从 CorePieceAnnotation 列表识别岩芯行（按 YCenter 聚类，旧方法保留备用）
    /// </summary>
    private static List<List<CorePieceAnnotation>> IdentifyCoreRowsFromAnnotations(List<CorePieceAnnotation> pieces)
    {
        var rows = new List<List<CorePieceAnnotation>>();
        if (pieces.Count == 0) return rows;

        // 计算每个 piece 的 YCenter
        var piecesWithY = pieces
            .Select(p => new
            {
                Piece = p,
                YCenter = p.ContourPoints.Count > 0
                    ? p.ContourPoints.Average(pt => pt.Y)
                    : (p.Rect.Top + p.Rect.Bottom) / 2.0
            })
            .OrderBy(x => x.YCenter)
            .ToList();

        if (piecesWithY.Count == 0) return rows;

        double estimatedHeight = piecesWithY.Average(x => x.Piece.Rect.Height > 0 ? x.Piece.Rect.Height : 20.0);
        double threshold = Math.Max(40.0, estimatedHeight * 3.0);

        var currentRow = new List<CorePieceAnnotation> { piecesWithY[0].Piece };
        double currentRowY = piecesWithY[0].YCenter;

        for (int i = 1; i < piecesWithY.Count; i++)
        {
            double dy = Math.Abs(piecesWithY[i].YCenter - currentRowY);
            if (dy <= threshold)
            {
                currentRow.Add(piecesWithY[i].Piece);
                currentRowY = currentRow.Average(p =>
                    p.ContourPoints.Count > 0 ? p.ContourPoints.Average(pt => pt.Y) : (p.Rect.Top + p.Rect.Bottom) / 2.0);
            }
            else
            {
                rows.Add(currentRow);
                currentRow = new List<CorePieceAnnotation> { piecesWithY[i].Piece };
                currentRowY = piecesWithY[i].YCenter;
            }
        }

        rows.Add(currentRow);
        return rows;
    }
    private static Color GetIntegrityColor(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Intact => Color.FromArgb(255, 100, 220, 100),
            IntegrityLevel.RelativelyIntact => Color.FromArgb(255, 100, 200, 255),
            IntegrityLevel.Poor => Color.FromArgb(255, 255, 200, 80),
            IntegrityLevel.RelativelyBroken => Color.FromArgb(255, 255, 140, 80),
            IntegrityLevel.Broken => Color.FromArgb(255, 255, 80, 80),
            _ => Color.FromArgb(255, 180, 180, 180)
        };
    }

    private void DrawTextWithBackground(Graphics g, string text, float x, float y, Color textColor, float fontSize)
    {
        using var font = new Font("微软雅黑", fontSize, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        SizeF textSize = g.MeasureString(text, font);

        using var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        g.FillRectangle(bgBrush, x - 4, y - 2, textSize.Width + 8, textSize.Height + 4);

        g.DrawString(text, font, brush, x, y);
    }

    private void DrawTextWithBackground(Bitmap bitmap, string text, float x, float y, Color textColor, float fontSize)
    {
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        // 创建字体（使用系统支持的中文字体）
        using var font = new Font("微软雅黑", fontSize, FontStyle.Regular);
        using var brush = new SolidBrush(textColor);

        // 计算文字大小
        SizeF textSize = g.MeasureString(text, font);

        // 绘制半透明黑色背景（透明度50%）
        using var bgBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));
        g.FillRectangle(bgBrush, x - 2, y - textSize.Height + 2, textSize.Width + 6, textSize.Height + 2);

        // 绘制文字
        g.DrawString(text, font, brush, x + 2, y - textSize.Height + 4);
    }

    private void DrawSummaryOverlaySimple(Bitmap bitmap, ImageAnalysisResult result, double depthStart, double depthEnd, float fontSize)
    {
        var lines = new List<string>
        {
            $"深度: {depthStart:F2}m - {depthEnd:F2}m",
            $"{GetIntegrityLevelText(result.IntegrityLevel)} / {result.StructuralDevelopment}",
            $"节理数: {result.JointCount}",
            $"比例尺: {(result.PixelPerCm > 0 ? $"{result.PixelPerCm:F1}px/cm" : "未设置")}"
        };

        float lineHeight = fontSize + 6;
        float x = 10;
        float y = 10;
        float bgWidth = 200;
        float bgHeight = lines.Count * lineHeight + 10;

        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillRectangle(bgBrush, x, y, bgWidth, bgHeight);

        using var font = new Font("微软雅黑", fontSize, FontStyle.Regular);
        using var brush = new SolidBrush(Color.White);

        for (int i = 0; i < lines.Count; i++)
        {
            g.DrawString(lines[i], font, brush, x + 8, y + 8 + i * lineHeight);
        }
    }

    private void DrawLegendSimple(Bitmap bitmap, float fontSize)
    {
        var legendItems = new List<(string Text, Color Color)>
        {
            ("完整 (绿)", Color.FromArgb(255, 100, 220, 100)),
            ("较完整 (蓝)", Color.FromArgb(255, 100, 200, 255)),
            ("完整性差 (黄)", Color.FromArgb(255, 255, 200, 80)),
            ("较破碎 (橙)", Color.FromArgb(255, 255, 140, 80)),
            ("破碎 (红)", Color.FromArgb(255, 255, 80, 80)),
            ("▲ 结构面", Color.White)
        };

        float lineHeight = fontSize + 4;
        float x = 10;
        float y = bitmap.Height - (legendItems.Count + 1) * lineHeight - 10;
        float bgWidth = 160;
        float bgHeight = legendItems.Count * lineHeight + 12;

        y = Math.Max(10, y);

        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillRectangle(bgBrush, x, y, bgWidth, bgHeight);

        using var font = new Font("微软雅黑", fontSize * 0.85f, FontStyle.Regular);
        for (int i = 0; i < legendItems.Count; i++)
        {
            var (text, color) = legendItems[i];
            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, x + 8, y + 6 + i * lineHeight);
        }
    }

    private void DrawIntegritySegmentsList(Bitmap bitmap, ImageAnalysisResult result, float fontSize)
    {
        if (result.IntegritySegments == null || result.IntegritySegments.Count == 0)
            return;

        // 在右上角绘制完整性分段文字列表
        float lineHeight = fontSize + 6;
        float x = bitmap.Width - 380;
        float y = 10;
        x = Math.Max(10, x);

        var lines = new List<string> { "完整性分段:" };
        foreach (var seg in result.IntegritySegments)
            lines.Add(seg.ToString());

        float bgWidth = 370;
        float bgHeight = lines.Count * lineHeight + 15;

        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillRectangle(bgBrush, x, y, bgWidth, bgHeight);

        using var font = new Font("微软雅黑", fontSize, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        using var normalFont = new Font("微软雅黑", fontSize * 0.85f, FontStyle.Regular);

        g.DrawString(lines[0], font, brush, x + 8, y + 8);
        for (int i = 1; i < lines.Count; i++)
            g.DrawString(lines[i], normalFont, brush, x + 8, y + 8 + (i + 1) * lineHeight * 0.9f);
    }

    private static string GetIntegrityLevelText(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Unknown => "未评定",
            IntegrityLevel.Intact => "完整",
            IntegrityLevel.RelativelyIntact => "较完整",
            IntegrityLevel.Poor => "完整性差",
            IntegrityLevel.RelativelyBroken => "较破碎",
            IntegrityLevel.Broken => "破碎",
            _ => level.ToString()
        };
    }

    private string GetAnnotatedImagePath(string originalPath)
    {
        string directory = Path.GetDirectoryName(originalPath) ?? "";
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            directory = Path.GetTempPath();

        string resultDir = Path.Combine(directory, "分析结果");
        try
        {
            if (!Directory.Exists(resultDir))
                Directory.CreateDirectory(resultDir);
        }
        catch (Exception)
        {
            // 创建失败时回退到原图目录（权限不足、路径非法等）
            resultDir = directory;
        }

        string fileName = Path.GetFileNameWithoutExtension(originalPath);
        string extension = Path.GetExtension(originalPath);
        if (string.IsNullOrEmpty(extension))
            extension = ".jpg";
        return Path.Combine(resultDir, $"{fileName}_annotated{extension}");
    }
}
