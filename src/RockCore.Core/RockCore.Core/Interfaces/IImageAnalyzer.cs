using RockCore.Core.Models;

namespace RockCore.Core.Interfaces;

/// <summary>
/// 图像分析器接口（自动识别模式）。
/// 负责从原始图像中自动识别岩芯段、节理、比例尺等标注信息。
/// 后续的完整性分析与结构面计算由 RuleEngineImageAnalyzer 统一处理。
/// 
/// 实现方式：
///   - 基于训练数据（TrainingData）的识别模型
///   - 训练数据来源：人工标注产生的 ManualAnnotationTrainingData
/// 
/// 当前状态：接口已定义，实现开发中。
/// </summary>
public interface IImageAnalyzer
{
    /// <summary>
    /// 分析器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 版本号
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 自动识别图像中的岩芯标注信息
    /// </summary>
    /// <param name="imagePath">原始图像路径</param>
    /// <param name="depthStart">钻孔深度起点 (m)</param>
    /// <param name="depthEnd">钻孔深度终点 (m)</param>
    /// <returns>
    /// 识别结果：
    ///   - Success=false 时表示识别失败或功能未启用
    ///   - Success=true 时返回识别出的岩芯段、节理、比例尺等标注数据
    /// </returns>
    Task<ImageAnalysisResult> AnalyzeAsync(string imagePath, double depthStart, double depthEnd);
}
