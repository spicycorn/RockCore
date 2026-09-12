using RockCore.Core.Enums;

namespace RockCore.Core.Models;

/// <summary>
/// 图像分析标注数据，存储所有需要在原图上绘制的分析结果信息
/// </summary>
public class ImageAnnotationData
{
    /// <summary>
    /// 岩芯盒边界矩形
    /// </summary>
    public AnnotationRect? CoreBoxRect { get; set; }

    /// <summary>
    /// 刻度尺区域边界
    /// </summary>
    public AnnotationRect? RulerRect { get; set; }

    /// <summary>
    /// 刻度尺读取的像素/厘米换算系数
    /// </summary>
    public double PixelPerCm { get; set; }

    /// <summary>
    /// 刻度尺起始读数（cm）
    /// </summary>
    public double RulerStartCm { get; set; }

    /// <summary>
    /// 岩芯柱分段列表
    /// </summary>
    public List<CorePieceAnnotation> CorePieces { get; set; } = new();

    /// <summary>
    /// 节理/裂隙列表
    /// </summary>
    public List<JointAnnotation> Joints { get; set; } = new();

    /// <summary>
    /// 深度刻度尺信息
    /// </summary>
    public RulerAnnotation? DepthRuler { get; set; }
}

/// <summary>
/// 标注矩形（兼容 Core 项目）
/// </summary>
public class AnnotationRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static AnnotationRect FromXYWH(double x, double y, double w, double h)
        => new() { X = x, Y = y, Width = w, Height = h };
}

/// <summary>
/// 标注点（兼容 Core 项目）
/// </summary>
public class AnnotationPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public static AnnotationPoint FromXY(double x, double y)
        => new() { X = x, Y = y };
}

/// <summary>
/// 轮廓点（用于 JSON 序列化，替代 ValueTuple 解决序列化丢失问题）
/// </summary>
public class ContourPoint
{
    public int X { get; set; }
    public int Y { get; set; }

    public ContourPoint() { }

    public ContourPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void Deconstruct(out int x, out int y)
    {
        x = X;
        y = Y;
    }

    public static implicit operator (int x, int y)(ContourPoint p) => (p.X, p.Y);
    public static implicit operator ContourPoint((int x, int y) p) => new(p.x, p.y);
}

/// <summary>
/// 岩芯片段标注
/// </summary>
public class CorePieceAnnotation
{
    /// <summary>
    /// 分段边界矩形
    /// </summary>
    public AnnotationRect Rect { get; set; } = new();

    /// <summary>
    /// 分段深度起始（米）
    /// </summary>
    public double DepthStart { get; set; }

    /// <summary>
    /// 分段深度结束（米）
    /// </summary>
    public double DepthEnd { get; set; }

    /// <summary>
    /// 片段长度（厘米）
    /// </summary>
    public double LengthCm { get; set; }

    /// <summary>
    /// 是否为完整岩芯段（长度>=10cm）——保留字段，标注图不再使用它判断等级
    /// </summary>
    public bool IsCompletePiece { get; set; }

    /// <summary>
    /// 本段岩体完整程度等级（依据 DL/T 5894-2025 表 F.0.4 由段长判定）
    /// </summary>
    public IntegrityLevel Level { get; set; }

    /// <summary>
    /// 本段结构面发育程度（依据 DL/T 5894-2025 表 F.0.4 由段长判定）
    /// </summary>
    public string Development { get; set; } = string.Empty;

    /// <summary>
    /// 岩芯真实不规则轮廓（相对于原图坐标）
    /// 如果为空则退化为 Rect 矩形表示
    /// </summary>
    public List<ContourPoint> ContourPoints { get; set; } = new();

    /// <summary>
    /// 平均色调（H值 0-180，OpenCV格式）
    /// </summary>
    public double AvgHue { get; set; }

    /// <summary>
    /// 平均饱和度（S值 0-255，OpenCV格式）
    /// </summary>
    public double AvgSaturation { get; set; }
}

/// <summary>
/// 节理/裂隙标注
/// </summary>
public class JointAnnotation
{
    /// <summary>
    /// 裂隙线段起点
    /// </summary>
    public AnnotationPoint Start { get; set; } = new();

    /// <summary>
    /// 裂隙线段终点
    /// </summary>
    public AnnotationPoint End { get; set; } = new();

    /// <summary>
    /// 裂隙类型：横向/斜向/纵向
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 裂隙宽度（厘米）
    /// </summary>
    public double WidthCm { get; set; }

    /// <summary>
    /// 裂隙角度
    /// </summary>
    public double Angle { get; set; }

    /// <summary>
    /// 是否贯通（长度>50%岩芯直径）
    /// </summary>
    public bool IsThrough { get; set; }

    /// <summary>
    /// 对应的全局深度（米）
    /// </summary>
    public double GlobalDepth { get; set; }

    /// <summary>
    /// 裂隙不规则轮廓点（相对于原图坐标，为裂隙的真实像素点路径）
    /// 如果为空则退化为 Start-End 直线表示
    /// </summary>
    public List<ContourPoint> ContourPoints { get; set; } = new();
}

/// <summary>
/// 刻度尺标注
/// </summary>
public class RulerAnnotation
{
    /// <summary>
    /// 刻度尺边界
    /// </summary>
    public AnnotationRect Rect { get; set; } = new();

    /// <summary>
    /// 刻度线位置列表
    /// </summary>
    public List<RulerTick> Ticks { get; set; } = new();

    /// <summary>
    /// 识别的数字标签
    /// </summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// 刻度线
/// </summary>
public class RulerTick
{
    /// <summary>
    /// 刻度线位置（像素）
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// 刻度线代表的深度值（米）
    /// </summary>
    public double Depth { get; set; }

    /// <summary>
    /// 是否为长刻度线（主刻度）
    /// </summary>
    public bool IsMajor { get; set; }
}
