using RockCore.Core.Enums;

namespace RockCore.Core.Models;

/// <summary>
/// 图像分析结果，包含从岩芯照片中提取的所有关键参数
/// </summary>
public class ImageAnalysisResult
{
    /// <summary>
    /// 分析状态：成功/失败
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 原始图像路径
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// 岩芯盒边界矩形 (x, y, width, height)
    /// </summary>
    public (int X, int Y, int Width, int Height)? CoreBoxRect { get; set; }

    /// <summary>
    /// 像素/厘米换算系数
    /// </summary>
    public double PixelPerCm { get; set; }

    /// <summary>
    /// 岩芯柱片段列表
    /// </summary>
    public List<CorePiece> CorePieces { get; set; } = new();

    /// <summary>
    /// 检测到的节理/裂隙列表
    /// </summary>
    public List<JointInfo> Joints { get; set; } = new();

    /// <summary>
    /// 结构面列表（存入数据库）
    /// </summary>
    public List<StructuralPlane> StructuralPlanes { get; set; } = new();

    /// <summary>
    /// RQD值 (%)
    /// </summary>
    public double RQD { get; set; }

    /// <summary>
    /// 节理总数
    /// </summary>
    public int JointCount { get; set; }

    /// <summary>
    /// 平均节理间距 (cm)
    /// </summary>
    public double AvgJointSpacingCm { get; set; }

    /// <summary>
    /// 是否松散破碎
    /// </summary>
    public bool IsFragmented { get; set; }

    /// <summary>
    /// 综合完整性等级
    /// </summary>
    public IntegrityLevel IntegrityLevel { get; set; }

    /// <summary>
    /// 结构面发育程度（不发育/轻度发育/中等发育/较发育/发育/很发育）
    /// </summary>
    public string StructuralDevelopment { get; set; } = string.Empty;

    /// <summary>
    /// 完整性指数 (0~1)
    /// </summary>
    public double IntegrityIndex { get; set; }

    /// <summary>
    /// 图像标注数据，用于在原图上绘制分析结果
    /// </summary>
    public ImageAnnotationData? AnnotationData { get; set; }

    /// <summary>
    /// 完整性等级分段列表（按完整性等级分组，与结构面发育程度独立）
    /// </summary>
    public List<IntegritySegment> IntegritySegments { get; set; } = new();

    /// <summary>
    /// 结构面发育程度分段列表（按结构面发育程度分组，与完整性等级独立）
    /// </summary>
    public List<DevelopmentSegment> DevelopmentSegments { get; set; } = new();

    /// <summary>
    /// 用户原始标注数据（训练数据，用于未来自动识别功能）
    /// 保存用户手动绘制的比例尺、岩芯线等真实标注信息
    /// </summary>
    public ManualAnnotationTrainingData? TrainingData { get; set; }
}

/// <summary>
/// 用户手动标注的训练数据
/// 保存用户绘制的原始标注信息，作为未来自动识别功能的训练样本
/// 所有坐标均为图像原始像素坐标
/// </summary>
public class ManualAnnotationTrainingData
{
    /// <summary>
    /// 原始图像宽度（像素）
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// 原始图像高度（像素）
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// 钻孔深度起点（m）
    /// </summary>
    public double DepthStart { get; set; }

    /// <summary>
    /// 钻孔深度终点（m）
    /// </summary>
    public double DepthEnd { get; set; }

    /// <summary>
    /// 像素/厘米换算系数（从比例尺得出）
    /// </summary>
    public double PixelPerCm { get; set; }

    /// <summary>
    /// 比例尺起点（图像原始像素坐标）
    /// </summary>
    public (double X, double Y)? ScalePoint1 { get; set; }

    /// <summary>
    /// 比例尺终点（图像原始像素坐标）
    /// </summary>
    public (double X, double Y)? ScalePoint2 { get; set; }

    /// <summary>
    /// 用户绘制的岩芯段列表（原始折线点，图像像素坐标）
    /// </summary>
    public List<TrainingCorePiece> CorePieces { get; set; } = new();

    /// <summary>
    /// 标注创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 训练用岩芯段数据（用户原始绘制的折线点）
/// </summary>
public class TrainingCorePiece
{
    /// <summary>
    /// 岩芯段编号（如 P1, P2）
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 折线总长度（像素）
    /// </summary>
    public double LengthPixel { get; set; }

    /// <summary>
    /// 折线总长度（厘米，基于比例尺换算）
    /// </summary>
    public double LengthCm { get; set; }

    /// <summary>
    /// 折线点列表（图像原始像素坐标）
    /// </summary>
    public List<PolylinePoint> PolylinePoints { get; set; } = new();
}

/// <summary>
/// 折线点（用于 JSON 序列化，替代 ValueTuple 解决序列化丢失问题）
/// </summary>
public class PolylinePoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public PolylinePoint() { }

    public PolylinePoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public void Deconstruct(out double x, out double y)
    {
        x = X;
        y = Y;
    }

    public static implicit operator (double X, double Y)(PolylinePoint p) => (p.X, p.Y);
    public static implicit operator PolylinePoint((double X, double Y) p) => new(p.X, p.Y);
}

/// <summary>
/// 岩芯柱片段
/// </summary>
public class CorePiece
{
        /// <summary>
        /// 片段边界矩形（外接矩形，用于兼容性）
        /// </summary>
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// 折线实际起点（像素坐标）
        /// </summary>
        public double StartX { get; set; }
        public double StartY { get; set; }

        /// <summary>
        /// 折线实际终点（像素坐标）
        /// </summary>
        public double EndX { get; set; }
        public double EndY { get; set; }

        /// <summary>
        /// Y轴中心点（像素坐标），用于识别岩芯行
        /// </summary>
        public double YCenter { get; set; }

        /// <summary>
        /// 不规则轮廓点列表（相对于ROI坐标，为岩芯的真实边缘）
        /// 如果为空，则退化为矩形表示
        /// </summary>
        public List<ContourPoint> ContourPoints { get; set; } = new();

        /// <summary>
        /// 片段长度 (cm)
        /// </summary>
        public double LengthCm { get; set; }

        /// <summary>
        /// 是否为完整岩芯段（长度>=10cm）——保留字段供向后兼容，标注显示不再使用
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
        /// 用户标注的岩芯段标签（如"P1"、"P2"）
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// 平均颜色
        /// </summary>
        public (double H, double S, double V) AverageColor { get; set; }
    }

/// <summary>
/// 节理/裂隙信息
/// </summary>
public class JointInfo
{
    /// <summary>
    /// 裂隙在照片中的深度位置 (像素)
    /// </summary>
    public double DepthInPhoto { get; set; }

    /// <summary>
    /// 裂隙类型：横向(节理)/斜向(剪切)/纵向(拉伸)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 裂隙宽度 (cm)
    /// </summary>
    public double WidthCm { get; set; }

    /// <summary>
    /// 裂隙宽度 (像素)，来自用户框选的真实像素距离，不使用伪换算
    /// </summary>
    public double WidthPixel { get; set; }

    /// <summary>
    /// 裂隙长度 (像素)
    /// </summary>
    public double LengthPixel { get; set; }

    /// <summary>
    /// 是否为贯通节理（长度>50%岩芯直径）
    /// </summary>
    public bool IsThrough { get; set; }

    /// <summary>
    /// 角度（霍夫线检测得到的角度）
    /// </summary>
    public double Angle { get; set; }

    /// <summary>
    /// 起点坐标
    /// </summary>
    public (double X1, double Y1, double X2, double Y2) Line { get; set; }

    /// <summary>
    /// 裂隙不规则轮廓点（相对于ROI坐标，为裂隙的真实像素点路径）
    /// 如果为空则退化为 Line 直线表示
    /// </summary>
    public List<ContourPoint> ContourPoints { get; set; } = new();
}

/// <summary>
/// 完整性分段输出
/// </summary>
public class IntegritySegment
{
    /// <summary>
    /// 分段深度起始（米）
    /// </summary>
    public double DepthStart { get; set; }

    /// <summary>
    /// 分段深度结束（米）
    /// </summary>
    public double DepthEnd { get; set; }

    /// <summary>
    /// 完整性等级
    /// </summary>
    public IntegrityLevel Level { get; set; }

    /// <summary>
    /// 判定依据（对照规范表的描述，包含节理数）
    /// </summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 本段岩芯实际长度（cm），来自用户标注比例尺换算；无比例尺则为 0
    /// </summary>
    public double LengthCm { get; set; }

    /// <summary>
    /// 本段结构面发育程度（依据 DL/T 5894-2025 表 F.0.4）
    /// 不发育 / 轻度发育 / 中等发育 / 较发育 / 发育 / 很发育
    /// </summary>
    public string Development { get; set; } = string.Empty;

    /// <summary>
    /// 本段内节理（结构面）数量 —— 仅计数，不伪造工程参数
    /// </summary>
    public int JointCount { get; set; }

    /// <summary>
    /// 本段在照片中的中心 X（像素，用于图片标绘标签位置）
    /// </summary>
    public double ImageCenterX { get; set; }

    /// <summary>
    /// 本段在照片中的中心 Y（像素，用于图片标绘标签位置）
    /// </summary>
    public double ImageCenterY { get; set; }

    /// <summary>
    /// 本段在照片中的左边界（像素，用于图片标绘标签位置）
    /// </summary>
    public double ImageLeft { get; set; }

    /// <summary>
    /// 本段在照片中的上边界（像素，用于图片标绘标签位置）
    /// </summary>
    public double ImageTop { get; set; }

    /// <summary>
    /// 本段在照片中的右边界（像素）
    /// </summary>
    public double ImageRight { get; set; }

    /// <summary>
    /// 本段在照片中的下边界（像素）
    /// </summary>
    public double ImageBottom { get; set; }

    /// <summary>
    /// 标签锚点 X（像素）——分段起始端（浅端）第一个piece的起点上方
    /// 用于标注图中标签的定位，比 ImageLeft 更准确
    /// </summary>
    public double LabelAnchorX { get; set; }

    /// <summary>
    /// 标签锚点 Y（像素）——分段起始端（浅端）第一个piece的起点上方
    /// </summary>
    public double LabelAnchorY { get; set; }

    /// <summary>
    /// 本段包含的 piece 编号（如 "P1,P2,P3"，用于图片标绘标签说明）
    /// </summary>
    public string PieceLabels { get; set; } = string.Empty;

    /// <summary>
    /// 格式化输出字符串（用于主界面显示）
    /// </summary>
    public override string ToString()
    {
        string levelText = Level switch
        {
            IntegrityLevel.Unknown => "未评定",
            IntegrityLevel.Intact => "完整",
            IntegrityLevel.RelativelyIntact => "较完整",
            IntegrityLevel.Poor => "完整性差",
            IntegrityLevel.RelativelyBroken => "较破碎",
            IntegrityLevel.Broken => "破碎",
            _ => Level.ToString()
        };
        return $"{DepthStart:F2}m - {DepthEnd:F2}m：{levelText} / {Development}（{Basis}，节理数 {JointCount}）";
    }
}

/// <summary>
/// 结构面发育程度分段输出（与完整性等级独立计算）
/// 判定依据：DL/T 5894-2025 表 F.0.4
/// </summary>
public class DevelopmentSegment
{
    /// <summary>
    /// 分段深度起始（米）
    /// </summary>
    public double DepthStart { get; set; }

    /// <summary>
    /// 分段深度结束（米）
    /// </summary>
    public double DepthEnd { get; set; }

    /// <summary>
    /// 结构面发育程度
    /// 不发育 / 轻度发育 / 中等发育 / 较发育 / 发育 / 很发育
    /// </summary>
    public string Development { get; set; } = string.Empty;

    /// <summary>
    /// 判定依据（对照规范表的描述，包含节理数和平均间距）
    /// </summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 本段岩芯实际长度（cm）
    /// </summary>
    public double LengthCm { get; set; }

    /// <summary>
    /// 本段平均结构面间距（cm）
    /// </summary>
    public double AvgSpacingCm { get; set; }

    /// <summary>
    /// 本段内节理（结构面）数量
    /// </summary>
    public int JointCount { get; set; }

    /// <summary>
    /// 本段在照片中的中心 X（像素）
    /// </summary>
    public double ImageCenterX { get; set; }

    /// <summary>
    /// 本段在照片中的中心 Y（像素）
    /// </summary>
    public double ImageCenterY { get; set; }

    /// <summary>
    /// 本段在照片中的左边界（像素）
    /// </summary>
    public double ImageLeft { get; set; }

    /// <summary>
    /// 本段在照片中的上边界（像素）
    /// </summary>
    public double ImageTop { get; set; }

    /// <summary>
    /// 本段在照片中的右边界（像素）
    /// </summary>
    public double ImageRight { get; set; }

    /// <summary>
    /// 本段在照片中的下边界（像素）
    /// </summary>
    public double ImageBottom { get; set; }

    /// <summary>
    /// 标签锚点 X（像素）——分段起始端（浅端）第一个piece的起点上方
    /// </summary>
    public double LabelAnchorX { get; set; }

    /// <summary>
    /// 标签锚点 Y（像素）——分段起始端（浅端）第一个piece的起点上方
    /// </summary>
    public double LabelAnchorY { get; set; }

    /// <summary>
    /// 本段包含的 piece 编号
    /// </summary>
    public string PieceLabels { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{DepthStart:F2}m - {DepthEnd:F2}m：{Development}（{Basis}，节理数 {JointCount}，平均间距 {AvgSpacingCm:F1}cm）";
    }
}
