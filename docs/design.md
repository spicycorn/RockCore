# 岩心围岩类别评价软件 — 方案设计文档

> ⚠️ **历史文档（v3.1，绘制模式时代）**：本文档描述的是早期"岩芯照片 + 人工标注/图像识别"工作流的设计。
> 自 v1.1 起软件已切换为 **Excel 数据导入模式**（照片降级为附件，绘制/标注/分析结果界面已移除），
> 判定原理（表 F.0.4 完整性 + 表 F.0.2 围岩分类）保持不变。
> **现行权威文档**：[Excel数据模式重构计划.md](Excel数据模式重构计划.md)。本文档第 8 章（三段融合与判定规则）仍然有效，其余章节仅作历史参考。

> **版本**: v3.1
> **适用规范**: DL/T 5894-2025《压气储能电站工程地质勘察规范》
> **适用项目**: 压缩空气储能电站岩心围岩评价
> **更新要点**:
> 1. NuGet 包可用性实测报告
> 2. 图像识别引擎全面升级（多特征融合 + 规则引擎 + 可插拔ML接口）
> 3. 严格对照表 F.0.4（岩体完整程度划分）和表 F.0.2（围岩初步分类）
> 4. 增加岩芯盒刻度尺自动解析与像素-厘米换算
> 5. 新增人工标注与自动标注双模式并行架构，自动标注基于人工标注训练数据持续优化
> 6. 新增训练数据库设计，用于存储人工标注经验并驱动智能识别模型

---

## 目录

1. [NuGet 包可用性测试报告](#1-nuget-包可用性测试报告)
2. [项目概述](#2-项目概述)
3. [技术栈与依赖（含版本号）](#3-技术栈与依赖含版本号)
4. [软件架构设计](#4-软件架构设计)
5. [数据库设计](#5-数据库设计)
6. [核心功能逻辑设计](#6-核心功能逻辑设计)
7. [图像识别引擎（v2.0 全面升级）](#7-图像识别引擎v20-全面升级)
8. [三段融合算法与围岩分类判定（严格对照规范表）](#8-三段融合算法与围岩分类判定严格对照规范表)
9. [三维可视化（按分析段着色）](#9-三维可视化按分析段着色)
10. [界面设计](#10-界面设计)
11. [分阶段开发检查点](#11-分阶段开发检查点)
12. [关键风险与注意事项](#12-关键风险与注意事项)
13. [文件结构](#13-文件结构)
14. [自动标注功能（基于人工标注训练数据的智能识别）](#14-自动标注功能基于人工标注训练数据的智能识别)

---

## 1. NuGet 包可用性测试报告

### 1.1 测试环境

| 项目 | 版本 |
|------|------|
| 操作系统 | Windows 11 |
| .NET SDK | 10.0.301 |
| NuGet 源 | `https://api.nuget.org/v3/index.json`（官方） |
| 网络 | 直连官方源 |

### 1.2 测试结果

| 包名 | 测试版本 | 实际安装版本 | 状态 | 耗时 | 说明 |
|------|---------|-------------|------|------|------|
| **HelixToolkit.Wpf** | 2.26.1 | 2.27.0 | ✅ 成功 | 3.54s | 三维渲染。注意：此包目标框架为 .NET Framework 4.6.1+，在 .NET 10 WPF 项目中可用但会出 NU1701 兼容性警告，不影响功能。 |
| **OpenCvSharp4** | 4.10.0.20240103 | 4.10.0.20240615 | ✅ 成功 | 12.39s | OpenCV C# 绑定。核心图像处理引擎。 |
| **OpenCvSharp4.runtime.win** | 4.10.0.20240615 | 4.10.0.20240615 | ✅ 成功 | 19.07s | OpenCV Windows 本地二进制运行时（含 OpenCV 的 DLL）。 |
| **ClosedXML** | 0.103.2 | 0.104.0 | ✅ 成功 | 11.15s | Excel 读写。自动附带 DocumentFormat.OpenXml、SixLabors.Fonts 等依赖。 |
| **Microsoft.Data.Sqlite** | 8.0.8 | 8.0.8 | ✅ 成功 | 10.28s | SQLite 数据访问。附带 SQLitePCLRaw 整套本地库。 |
| **SixLabors.ImageSharp** | 3.1.5 | 3.1.5 | ✅ 成功 | 3.30s | 纯托管图像处理库。作为 OpenCV 的备选/辅助方案。 |
| **Microsoft.Extensions.DependencyInjection** | 8.0.0 | 8.0.0 | ✅ 默认 | — | 依赖注入。.NET SDK 自带。 |
| **CommunityToolkit.Mvvm** | 8.2.2 | 8.2.2 | ✅ 默认 | — | MVVM 框架。.NET SDK 推荐 NuGet。 |

### 1.3 构建验证

```
dotnet build --configuration Release
结果: 0 个错误，8 个警告（全部为 NU1701 目标框架兼容警告，不影响功能）
```

### 1.4 网络中断备选方案

> **如果官方源下载失败**，可采用以下降级方案（任选其一，按推荐度排序）：

| 方案 | 操作 | 适用场景 |
|------|------|---------|
| **方案A（推荐）** | 配置国内镜像源（如华为云 `https://repo.huaweicloud.com/repository/nuget/v3/index.json` 或 Azure 中国 `https://nuget.cdn.azure.cn/v3/index.json`） | 网络可访问国内 CDN |
| **方案B** | 离线恢复：从已安装好的开发机 `%userprofile%\.nuget\packages` 目录整体复制到目标机同名目录，`dotnet restore` 会直接命中本地缓存 | 完全离线部署 |
| **方案C** | 本地文件夹源：将所需 `.nupkg` 文件统一放入 `./nuget-local/` 目录，在 `NuGet.config` 中添加 `<add key="local" value=".\nuget-local" />` | 企业内网 |
| **方案D** | 用 System.Drawing.Common（Windows 专用）+ System.Windows.Media.Imaging（WPF 内置） 替代 OpenCvSharp + SixLabors.ImageSharp；Excel 用 NPOI（更老但稳定）替代 ClosedXML | 极端网络受限 |

> **建议提前执行一次 `dotnet restore`** 并把 `%userprofile%\.nuget\packages` 打包备份（约 100~200MB）。

### 1.5 推荐 NuGet.config（放解决方案根目录）

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="huaweicloud" value="https://repo.huaweicloud.com/repository/nuget/v3/index.json" protocolVersion="3" />
    <add key="azure-cn" value="https://nuget.cdn.azure.cn/v3/index.json" protocolVersion="3" />
  </packageSources>
  <activePackageSource>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </activePackageSource>
  <fallbackPackageSources>
    <add key="huaweicloud" value="https://repo.huaweicloud.com/repository/nuget/v3/index.json" />
    <add key="azure-cn" value="https://nuget.cdn.azure.cn/v3/index.json" />
  </fallbackPackageSources>
</configuration>
```

---

## 2. 项目概述

本软件用于压缩空气储能电站项目的岩芯围岩类别评价工作。

### 2.1 核心业务流程

```
创建项目
    → 创建钻孔（填写方位角、孔深、地下水埋深等）
    → 导入岩芯照片（自动从文件名解析箱号/深度范围）
    → 图像识别引擎（识别岩芯完整性/节理/结构面/RQD，自动判定完整性等级）
    → 用户按任意深度分段，填写岩质类型 + 岩体结构类型
    → 三段融合算法（物理段 ∩ 信息段 → 分析段）
    → 查表判定围岩分类（I~V 类）
    → 多钻孔整体统计分析
    → 三维可视化展示（钻孔岩芯柱 + 结构面 + 地平面 + 方位标注）
    → 生成报告（HTML / Word / Excel）
```

### 2.2 核心输入来源分析

| 输入项 | 来源 | 自动/手动 |
|--------|------|----------|
| 项目/钻孔基本信息 | 用户填写 | 手动 |
| 照片物理段边界 | 文件名解析（如「第1箱0m-9m.jpg」） | 自动 + 可修正 |
| 结构面发育组数 | 图像自动识别 + 用户复核 | 半自动 |
| 结构面间距(cm) | 图像自动识别 + 刻度尺像素换算 | 半自动 |
| RQD(%) | 图像自动识别（岩芯长度统计） | 自动 + 可修正 |
| 岩质类型 | 用户下拉选择（表 F.0.2） | 手动 |
| 岩体结构类型 | 用户下拉选择（表 F.0.2） | 手动 |
| 地下水状态 | 自动按地下水埋深判定 + 可覆盖 | 半自动 |

### 2.3 核心输出

- **岩芯物理段分析结果**：完整性等级、结构面数、平均节理间距、RQD、松散破碎判定
- **分析段（三段融合）**：深度范围、围岩类别、判定依据、置信度
- **多钻孔整体统计**：各围岩类别累计长度、占比、分布深度
- **三维可视化场景**
- **HTML / Word / Excel 报告**

---

## 3. 技术栈与依赖（含版本号）

| 模块 | 技术方案 | NuGet 包 | 版本 |
|------|---------|---------|------|
| 桌面应用框架 | WPF (.NET 8) | — | net8.0-windows |
| 三维渲染引擎 | Helix Toolkit (WPF) | `HelixToolkit.Wpf` | 2.27.0 |
| 核心图像处理 | OpenCV (OpenCvSharp) | `OpenCvSharp4` | 4.10.0.20240615 |
| OpenCV 本地运行时 | Windows DLL | `OpenCvSharp4.runtime.win` | 4.10.0.20240615 |
| 辅助图像处理 | ImageSharp (纯托管，备用) | `SixLabors.ImageSharp` | 3.1.5 |
| 数据库 | SQLite | `Microsoft.Data.Sqlite` | 8.0.8 |
| Excel 导入导出 | ClosedXML | `ClosedXML` | 0.104.0 |
| Word 导出 | Open XML SDK | `DocumentFormat.OpenXml` | 3.0.1（ClosedXML 依赖） |
| 依赖注入 | DI | `Microsoft.Extensions.DependencyInjection` | 8.0.0 |
| MVVM | CommunityToolkit | `CommunityToolkit.Mvvm` | 8.2.2 |
| JSON 序列化 | System.Text.Json | — | 内置 |
| 目标框架 | net8.0-windows | — | 推荐使用 .NET 8 而非 .NET 10（避免 HelixToolkit NU1701 警告） |

> **重要**: 建议目标框架使用 `net8.0-windows`，因 HelixToolkit.Wpf 为 .NET Framework 包，在 net8.0 中使用兼容，无警告。

---

## 4. 软件架构设计

### 4.1 三段融合架构核心概念

```
物理段(CorePhotos)       = 岩芯盒/照片实际覆盖的深度段（如 0-9m, 9-17m, ...）
                        → 含图像识别结果: 完整性、节理数、节理间距、RQD、结构面

信息段(CoreInfoSegments) = 用户按地质意义在完整岩芯上的任意深度分段
                        → 含岩质类型 + 岩体结构类型（两列下拉选单，选项严格来自表 F.0.2）
                        → 用户可手动覆盖完整性等级

分析段(ClassificationSegments) = 物理段 ∩ 信息段 → 再按地下水埋深细分
                        → 每个分析段有完整的"岩质 + 结构 + 完整性 + 地下水"四要素
                        → 查表 F.0.2 判定围岩类别(I~V)
```

图示（根据示例数据 LGZ-DXK03/3号孔）：

```
深度:     0        3        9        17       25   →  201.3 (m)
          ├────────┼────────┼────────┤ ...
物理段:   │  第1箱(0-9m, RQD≈80%, 较完整)  │ 第2箱(9-17m, RQD≈30%, 破碎) │ ...
          │                              │                               │
信息段:   │ 0-3m 全风化砾砂 │ 3-17.8m 通天岭(硬质岩) │ 17.8-18.3m 花岗岩岩脉 │ ...
          │ (软质岩,碎裂)   │ (硬质岩,整体块状)       │                      │
          │                │                                            │
地下水:   │ <──── 干燥 ────> │ <───────── 孔口以下 Xm 处为有地下水 ──────> │
          (需要用户填写 地下水埋深)
          │
融合后分析段:
          0-3m:     软质岩·碎裂结构·(完整程度待识别)·干燥 → 查表 → III?IV?类
          3-9m:     硬质岩·整体块状·(完整程度=较完整)·干燥 → 查表 → II?III?类
          9-17.8m:  硬质岩·整体块状·(完整程度=破碎)·干燥 → 查表 → IV?V?类
          17.8-18.3m: 花岗岩岩脉·需用户填写岩质/结构
          ...
```

### 4.2 分层架构

```
┌──────────────────────────────────────────────────────────────┐
│  Presentation Layer (Views + ViewModels)                       │
│  - 主窗口(树+多标签)                                           │
│  - 岩芯信息编辑器（核心：深度标尺 + 物理段/信息段标记）         │
│  - 图像识别结果展示与修正窗口                                  │
│  - 三维可视化窗口                                              │
│  - 报告预览/导出                                               │
├──────────────────────────────────────────────────────────────┤
│  Application Layer (Services)                                   │
│  - ProjectService / BoreholeService                            │
│  - PhotoImportService（文件名解析 → CorePhotos）                │
│  - ImageAnalysisService（核心图像识别引擎 v2.0）                 │
│  - InfoSegmentService（用户信息段 CRUD）                        │
│  - ClassificationService（三段融合 + 查表判定）                  │
│  - ThreeDimSceneService                                        │
│  - ReportService (HTML/Word/Excel)                             │
├──────────────────────────────────────────────────────────────┤
│  Core Layer (Models + Interfaces)                               │
│  - Models: Project, Borehole, CorePhoto, CoreInfoSegment,      │
│            StructuralPlane, ClassificationSegment,             │
│            AnalysisMetrics（每物理段的识别指标）                 │
│  - Interfaces: IImageAnalyzer（可插拔，规则引擎/ML 模型切换）    │
│  - Enums: IntegrityLevel, RockType, RockStructureType,          │
│           SurroundingRockClass（严格对应规范表）                │
├──────────────────────────────────────────────────────────────┤
│  Infrastructure Layer                                            │
│  - Data: SQLite Repositories + DbContext                       │
│  - ImageAnalysis: OpenCvSharp 规则引擎 v2.0                    │
│       · 岩芯盒区域检测 (CoreBoxDetector)                        │
│       · 刻度尺解析 (RulerParser — 像素/厘米换算)                │
│       · 岩芯柱段识别 (CoreSegmentation)                         │
│       · 节理/裂隙检测 (JointDetector)                           │
│       · 结构面参数提取 (StructuralPlaneAnalyzer)                │
│       · 颜色/风化分析 (WeatheringAnalyzer)                      │
│       · RQD 计算 (RQDCalculator)                                │
│       · 综合完整性等级判定 (IntegrityClassifier)                │
│  - ThreeDim: HelixToolkit 场景构建                              │
│  - Reports: HTML/Word/Excel 生成器                              │
└──────────────────────────────────────────────────────────────┘
```

---

## 5. 数据库设计

### 5.1 数据表概览

| 表名 | 中文名称 | 关键字段 |
|------|---------|---------|
| Projects | 项目表 | Id, Name, Phase, CaveAxisAzimuth, CreatedAt |
| Boreholes | 钻孔表 | Id, ProjectId, Number, OrificeElevation, TotalDepth, GroundwaterDepth, Azimuth, InclinationAngle, OrificeX/Y/Z |
| CorePhotos | 物理段表 | Id, BoreholeId, FileName, DepthStart/End, BoxNumber, IntegrityLevel, IntegrityIndex, RQD, JointCount, AvgJointSpacingCm, IsFragmented, AnalysisStatus, IsUserModified, AnalysisResultJson |
| CoreInfoSegments | 信息段表 | Id, BoreholeId, DepthStart/End, RockTypeName, RockStructureType, ManualIntegrityOverride, GroundwaterOverride, Remarks |
| StructuralPlanes | 结构面表 | Id, CorePhotoId, PlaneType, Strike, Dip, DepthInPhoto, GlobalDepth, ApertureWidthCm, Roughness, ImageX/Y, IsUserModified |
| ClassificationSegments | 分析段表 | Id, BoreholeId, DepthStart/End, RockClass(I~V), IntegrityLevel, RockTypeName, RockStructureType, GroundwaterCondition, ConfidenceScore, Basis |
| ProjectSummaryStatistics | 项目统计表 | Id, ProjectId, TotalCoreLength, StatisticsJson |
| AnalysisMetrics | 图像识别原始指标 | Id, CorePhotoId, MetricKey, MetricValue, MetricUnit — 存原始像素/厘米测量数据，用于追溯 |
| TrainingSamples | 训练样本表 | Id, CorePhotoId, ImageHash, ImageWidth, ImageHeight, SampleType, QualityScore, CreatedAt — 人工标注结果作为训练数据 |
| TrainingRulerAnnotations | 比例尺标注训练 | Id, SampleId, StartPointX/Y, EndPointX/Y, ActualLengthCm, PixelPerCm |
| TrainingCoreSegmentAnnotations | 岩芯段标注训练 | Id, SampleId, SegmentIndex, ContourPointsJson, DepthStart/End, LengthCm |
| TrainingJointAnnotations | 节理标注训练 | Id, SampleId, JointIndex, StartPointX/Y, EndPointX/Y, WidthPx, WidthCm, JointType |
| TrainingModelVersions | 模型版本 | Id, ModelName, Version, TrainingSampleCount, AccuracyMetrics, TrainedAt, IsActive |

### 5.2 枚举设计（严格对照规范表）

#### 5.2.1 IntegrityLevel（完整性等级，表 F.0.4）

```csharp
public enum IntegrityLevel
{
    [Description("完整")]     // 结构面发育组数 1~2，间距 >100cm
    Intact,
    [Description("较完整")]    // 结构面发育组数 2~3，间距 50~100cm
    RelativelyIntact,
    [Description("完整性差")]   // 结构面发育组数 2~3，间距 30~50cm
    Poor,
    [Description("较破碎")]     // 结构面发育组数 2~3，间距 10~30cm
    RelativelyBroken,
    [Description("破碎")]       // 结构面发育组数 >3 或无序，间距 <10cm
    Broken,
    [Description("用户指定")]    // 用户手动覆盖时使用
    UserOverride
}
```

#### 5.2.2 RockType（岩质类型，表 F.0.2 第一列）

```csharp
public enum RockType
{
    [Description("硬质岩")]
    HardRock,
    [Description("软质岩")]
    SoftRock,
    [Description("极软岩")]
    VerySoftRock
}
```

#### 5.2.3 RockStructureType（岩体结构类型，表 F.0.2 第二列）

```csharp
public enum RockStructureType
{
    [Description("整体状或巨厚层状结构")]
    Massive,
    [Description("块状结构")]
    Blocky,
    [Description("次块状结构")]
    SubBlocky,
    [Description("厚层状或中厚层状结构")]
    ThickLayered,
    [Description("互层状结构")]
    Interbedded,
    [Description("薄层状结构")]
    ThinLayered,
    [Description("镶嵌结构")]
    Mosaic,
    [Description("块裂结构")]
    BlockyFractured,
    [Description("碎裂结构")]
    Cataclastic,
    [Description("碎块状或碎屑状散体结构")]
    Granular
}
```

#### 5.2.4 SurroundingRockClass（围岩类别，表 F.0.2 输出）

```csharp
public enum RockClass
{
    [Description("I 类")]
    I,
    [Description("II 类")]
    II,
    [Description("III 类")]
    III,
    [Description("IV 类")]
    IV,
    [Description("V 类")]
    V
}
```

#### 5.2.5 GroundwaterCondition

```csharp
public enum GroundwaterCondition
{
    [Description("干燥")]
    Dry,
    [Description("潮湿/渗水")]
    Damp,
    [Description("滴水/有地下水")]
    Wet
}
```

---

## 6. 核心功能逻辑设计

### 6.1 照片文件名解析（PhotoImportService）

#### 解析规则（按优先级匹配，正则可扩展）

```
规则 1：含「第X箱」格式 + 深度范围
    模式: 「第(\d+)箱.*?(\d+(?:\.\d+)?)\s*m\s*[-~至]\s*(\d+(?:\.\d+)?)\s*m」
    例: 「第1箱0m-9m.jpg」 → 箱号=1, DepthStart=0, DepthEnd=9

规则 2：纯深度格式
    模式: 「(\d+(?:\.\d+)?)\s*[-~至]\s*(\d+(?:\.\d+)?)\s*m?」
    例: 「0-9.3m.jpg」 → DepthStart=0, DepthEnd=9.3
    例: 「103-108.jpg」(3号孔压水目录) → DepthStart=103, DepthEnd=108

规则 3：兜底（文件名中找任意两个数字，小的为起点，大的为终点）
```

#### 验证规则

- 解析出 DepthStart < DepthEnd
- 同钻孔内物理段不重叠，按深度排序
- 相邻间隙 > 5cm 高亮提示（可能漏拍），但不阻塞
- 深度范围 ≤ 钻孔 TotalDepth
- **用户可手动编辑每段边界**（修正后 IsUserModified = true）

#### 输出

写入 `CorePhotos` 表，字段：`BoreholeId, FileName, RelativePath, DepthStart, DepthEnd, SegmentLength, BoxNumber, AnalysisStatus="未分析"`

---

### 6.2 信息段管理（InfoSegmentService）

#### CRUD 操作

| 操作 | 说明 | 校验 |
|------|------|------|
| Add | 新增一个信息段，用户填写岩质类型+岩体结构 | 不与现有段重叠 |
| Update | 修改深度范围或属性 | 同左 |
| Delete | 删除分段，会自动把信息段合并为相邻段的属性 | — |
| Split | 在指定深度处将一个信息段拆为两段 | 深度必须在段内 |
| Merge | 合并相邻两段，继承第一段属性 | 必须相邻 |

#### 与物理段的关系

信息段和物理段**相互独立**，仅在「三段融合」时才求交集。用户在岩芯编辑器中可看到：

- **灰色水平条**：物理段边界，显示箱号 + 深度
- **彩色色块**：信息段，按岩质类型着色（硬质岩=蓝色系，软质岩=橙色系）
- **蓝色虚线**：信息段边界（可拖拽调整）
- **水平蓝线**：地下水埋深线
- **红色圆点/短线**：结构面标记（悬停显示结构面详情）

---

## 7. 图像识别引擎（v2.0 全面升级 — 人工标注 + 自动标注双模式）

> **目标**：自动识别出表 F.0.4 需要的关键参数：
> ① 结构面发育组数（1~2 / 2~3 / >3）
> ② 结构面间距（cm）
> ③ 是否松散破碎
> 并输出 完整性等级（完整/较完整/完整性差/较破碎/破碎）
> 同时识别 RQD（%）辅助验证
>
> **双模式架构**：
> - **人工标注模式**：用户手动框选比例尺、岩芯段、节理线，结果精度最高，作为"金标准"训练数据
> - **自动标注模式**：基于人工标注积累的训练数据进行智能识别，精度随样本量增长而提升
> - 两种模式共用后续的完整性判定、三段融合、围岩分类等分析逻辑

### 7.0 双模式工作流总览

```
                        ┌─────────────────────┐
                        │   选择分析模式       │
                        └─────────┬───────────┘
                                  │
                 ┌────────────────┴────────────────┐
                 ▼                                 ▼
        ┌──────────────────┐              ┌──────────────────┐
        │  ✏️ 人工标注模式   │              │  🤖 自动标注模式   │
        │  （高精度基准）    │              │  （训练数据驱动）  │
        └─────────┬────────┘              └─────────┬────────┘
                  │                                 │
                  ▼                                 ▼
        用户手动绘制标注                   算法自动识别标注
        · 比例尺线                      · 岩芯盒检测
        · 岩芯段轮廓                    · 岩芯柱分割
        · 节理线                        · 节理检测
                  │                                 │
                  └────────────────┬────────────────┘
                                   ▼
                        统一分析结果计算
                        · 完整性等级判定
                        · RQD 计算
                        · 结构面统计
                        · 写入训练数据库（人工标注结果）
```

### 7.1 图像识别 v2.0 流水线（OpenCvSharp4）

```
输入: CorePhoto 物理段图像（如「第1箱0m-9m.jpg」）
  │
  ├──→ 步骤 1: 岩芯盒区域检测 CoreBoxDetector
  │        ├─ 灰度化 + 边缘检测 (Canny)
  │        ├─ 霍夫直线检测 → 识别岩芯盒黑色外框
  │        ├─ 裁剪: 提取岩芯盒内部有效区域
  │        └─ 输出: boxRect, rotationAngle(倾角修正)
  │
  ├──→ 步骤 2: 刻度尺解析 RulerParser
  │        ├─ 定位左侧或底部白色刻度尺区域
  │        ├─ OCR 识别刻度数字（0,10,20,...,100）
  │        ├─ 计算 像素/厘米 换算系数: pixelPerCm
  │        ├─ 若刻度尺识别失败 → 回退到岩芯直径标定（~7cm 钻孔芯径）
  │        └─ 输出: pixelPerCm, scaleConfidence
  │
  ├──→ 步骤 3: 岩芯柱分割 CoreColumnSegmentation
  │        ├─ 色彩分割（岩芯褐色/灰色 vs 砂底黄色 vs 白色盒体）
  │        ├─ K-Means 聚类(K=3): 岩芯/砂/背景
  │        ├─ 形态学操作（闭运算填充裂隙）
  │        ├─ 连通域标记 → 提取每一段岩芯柱的矩形框
  │        ├─ 按深度排序
  │        └─ 输出: List<CorePiece> = {x,y,w,h,length_cm, is_complete_piece}
  │
  ├──→ 步骤 4: 节理/裂隙检测 JointDetector
  │        ├─ 在每段岩芯柱内:
  │        │   ├─ Sobel 算子提取纵向/横向边缘
  │        │   ├─ 霍夫线检测(HoughLinesP) → 过滤出斜向/横向裂隙
  │        │   ├─ 裂隙宽度估算: 二值化膨胀 → 像素宽度 / pixelPerCm → cm
  │        │   ├─ 裂隙类型判定: 横向(节理) / 斜向(剪切) / 纵向(拉伸)
  │        │   ├─ 裂隙长度 > 50% 岩芯直径 → 视为贯通节理
  │        │   └─ 输出: List<Joint> = {depth_cm, type, width_cm, is_through}
  │        ├─ 节理统计:
  │        │   ├─ jointCount = 裂隙总数
  │        │   ├─ jointGroupCount = 按方向聚类后的组数（K-Means 方向聚类）
  │        │   └─ avgJointSpacing = 相邻节理平均间距(cm)
  │        └─ 写入 StructuralPlanes 表
  │
  ├──→ 步骤 5: RQD 计算 RQDCalculator
  │        ├─ RQD(%) = (所有长度≥10cm 的完整岩芯段总长) / 本次进尺总长 × 100
  │        └─ 输入: step3 的 CorePiece.length_cm; 输出: rqdPercent
  │
  ├──→ 步骤 6: 松散/破碎判定 FragmentationAnalyzer
  │        ├─ 碎块计数: CorePiece.length_cm < 5cm 的段数比例
  │        ├─ 岩芯获取率 = 所有 CorePiece 总长 / 进尺总长
  │        ├─ 颜色方差: 岩芯区域内颜色方差（高方差→混杂破碎）
  │        ├─ 综合判定 isFragmented = (rqdPercent < 25) OR (碎块比例 > 60%)
  │        └─ 输出: fragmentationIndex(0~1)
  │
  ├──→ 步骤 7: 颜色/风化分析 WeatheringAnalyzer
  │        ├─ 岩芯区域 HSV 直方图分析
  │        ├─ 平均色调 → 辅助判断岩性（红/灰/杂色）
  │        ├─ 风化程度: 红褐色占比高 → 强风化; 均一灰色 → 微风化
  │        └─ 输出: dominantColor, weatheringHint（仅提示，不参与自动判定）
  │
  └──→ 步骤 8: 综合完整性等级判定 IntegrityClassifier
           ├─ 输入: jointGroupCount, avgJointSpacingCm, rqdPercent, fragmentationIndex, isFragmented
           ├─ 查表 F.0.4 + 启发式规则:
           
               · 若 isFragmented = true → 直接判为「破碎」或「较破碎」
               · 若 rqdPercent ≥ 90 且 avgJointSpacing > 100cm → 「完整」
               · 若 rqdPercent 70~90 且 avgJointSpacing 50~100cm → 「较完整」
               · 若 rqdPercent 50~70 且 avgJointSpacing 30~50cm → 「完整性差」
               · 若 rqdPercent 25~50 且 avgJointSpacing 10~30cm → 「较破碎」
               · 若 rqdPercent < 25 或 jointGroupCount > 3 → 「破碎」
               · 中间状态 → 取相邻等级，置信度降低
           
           ├─ 置信度计算:
               Confidence = 0.5 × (1 - |RQD_实际 - RQD_标准|/50)
                         + 0.3 × 刻度尺识别可信度
                         + 0.2 × (1 - 节理识别误检率)
           
           └─ 输出: IntegrityLevel(枚举), integrityIndex(0~1), confidence
```

### 7.2 表 F.0.4 岩体完整程度划分 — 程序化对照表

| 岩体完整程度 | 结构面发育组数 | 结构面间距(cm) | 典型 RQD(%) | 典型状态 |
|-------------|--------------|----------------|------------|---------|
| **完整** | 1~2 | >100 | 90~100 | 长柱状、极少裂隙 |
| **较完整** | 1~2 | 100~50 | 75~90 | 柱状、少量裂隙 |
| **完整性差** | 2~3 | 50~30 | 50~75 | 短柱状、多条裂隙 |
| **较破碎** | 2~3 | 30~10 | 25~50 | 碎块为主 |
| **破碎** | >3 或无序 | <10 | 0~25 | 松散破碎 |

### 7.3 用户修正与结果保留

- 用户在「图像分析结果」标签页看到：
  - 原图 + 自动识别的**节理/裂隙红色标记叠加层**
  - 每张照片的完整性等级、结构面数、平均间距、RQD
  - 结构面列表（可逐条删除、编辑走向/倾角）
- 用户修改后，`CorePhotos.IsUserModified = 1`，下次重新分析时**不覆盖用户修正**
- 用户可点击「⟳ 重识别此照片」单独重跑一张图
- 所有图像识别原始指标存入 `AnalysisMetrics` 表，便于追溯和调参

### 7.4 可插拔 ML 接口（预留 v3.0）

```csharp
public interface IImageAnalyzer
{
    string Name { get; }        // "RuleEngine-v2.0" / "MLModel-CoreNet-v1.0"
    string Version { get; }
    Task<ImageAnalysisResult> AnalyzeAsync(string imagePath);
}

// 未来可替换为:
// - 基于 YOLO 的岩芯/裂隙目标检测模型
// - 基于 UNet 的裂隙语义分割模型
// 只需实现此接口并通过 DI 替换注册
```

---

## 8. 三段融合算法与围岩分类判定（严格对照规范表）

### 8.1 三段融合算法

```
输入:
  A = CorePhotos[钻孔i]   // 物理段，含图像识别结果
  B = CoreInfoSegments[钻孔i] // 信息段，用户填岩质+岩体结构
  G = GroundwaterDepth      // 地下水埋深

步骤 1: 收集所有关键深度点
  criticalDepths = { 0 }
                 ∪ { a.DepthStart, a.DepthEnd for a in A }
                 ∪ { b.DepthStart, b.DepthEnd for b in B }
                 ∪ { G }
                 ∪ { TotalDepth }
  → 排序去重: [d0, d1, d2, ..., dn]

步骤 2: 对每个小区间 [di, di+1], 查找对应的 A 段和 B 段
  segA = A.First(a => a.DepthStart ≤ di && a.DepthEnd ≥ di+1)
  segB = B.First(b => b.DepthStart ≤ di && b.DepthEnd ≥ di+1)
  找不到 → 标记该段信息缺失，跳过或等用户填写

步骤 3: 地下水状态判定
  if di > G → "有地下水"
  elif di+1 ≤ G → "干燥"
  else → 不应出现（因 G 已在 criticalDepths 中）

步骤 4: 完整性等级选择
  if segB.ManualIntegrityOverride != null → 使用用户覆盖值
  else → 使用 segA.IntegrityLevel

步骤 5: 查表判定围岩类别（严格对照表 F.0.2，见 §8.2）

步骤 6: 写入 ClassificationSegments
  字段: DepthStart, DepthEnd, RockClass, IntegrityLevel,
        RockTypeName, RockStructureType, GroundwaterCondition,
        ConfidenceScore, Basis
```

### 8.2 围岩初步分类判定规则（表 F.0.2 程序化对照表）

表 F.0.2 核心判断逻辑如下：

```
查表函数: Classify(rockType, structureType, integrityLevel, hasGroundwater) → RockClass

规则 1: 硬质岩 + 整体状或巨厚层状 + 完整 → I~II 类
规则 2: 硬质岩 + 块状结构 + 完整 → II~III 类
规则 3: 硬质岩 + 次块状结构 + 完整 → II~III 类
规则 4: 硬质岩 + 厚层状或中厚层状 + 较完整 → II~III 类
规则 5: 硬质岩 + 互层状结构 + (完整/较完整)
           → 洞轴线与岩层走向夹角小于30°时定 IV 类，否则 II~III 类
规则 6: 硬质岩 + 薄层状结构 + 较完整 → IV~III 类
           (若岩质均一，无软弱夹层时，可定 III 类)
规则 7: 硬质岩 + 镶嵌结构 + 完整性差 → III 类
规则 8: 硬质岩 + 块裂结构 + 完整性差 → IV 类
规则 9: 硬质岩 + 碎裂结构 + 较破碎 → IV~V 类
           (有地下水时定 V 类)
规则 10: 硬质岩 + 碎块状或碎屑状结构 + 破碎 → V 类

规则 11: 软质岩 + 整体状或巨厚层状 + 完整 → III~IV 类
            (有地下水时定 IV 类；软岩定 IV 类)
规则 12: 软质岩 + 块状或次块状 + 较完整 → IV~V 类
            (无地下水时定 IV 类；有地下水时定 V 类)
规则 13: 软质岩 + 厚层、中厚层或互层状 + 较完整 → IV~V 类
            (无地下水时定 IV 类；有地下水时定 V 类)
规则 14: 软质岩 + 薄层状或块裂状 + 完整性差 → V~IV 类
            (较软岩无地下水时定 IV 类)
规则 15: 软质岩 + 碎裂结构 + 较破碎 → V~IV 类
            (较软岩无地下水时定 IV 类)
规则 16: 软质岩 + 碎块状或碎屑状散体 + 破碎 → V 类

补充规则（地下水修正）:
  · 地下水埋深 ≤ 段深度 → hasGroundwater = true
  · 规则 9 / 12 / 13 在有地下水时向差一级调整
  · 硬质岩+完整 受地下水影响较小，可不变; 软质岩+破碎受地下水影响最大

补充规则（围岩类别区间取值）:
  · 当规则出现 "II~III" 等区间时，按完整性等级细判:
      完整性更接近「完整」→ 取 II；更接近「较完整」下限 → 取 III
  · 置信度低 → 提示用户手动确认
```

### 8.3 判定依据（Basis 字段生成）

每个分析段生成一条人类可读的判定依据文本：

```
例1: "硬质岩·整体块状结构·完整·干燥 → 查表规则1 → II 类"
例2: "硬质岩·互层状结构·较完整·干燥，洞轴线与岩层夹角>30° → 查表规则5 → II类"
例3: "软质岩·碎裂结构·较破碎·有地下水 → 查表规则15 + 地下水修正 → V类（建议工程加强支护）"
```

### 8.4 多钻孔整体统计

```
汇总所有钻孔的 ClassificationSegments:

总统计:
  对每类围岩(I~V):
    累计长度 = Σ segment.Length
    累计占比 = 累计长度 / 项目总岩芯长度
    出现钻孔数 = 包含该类的钻孔数
    深度分布 = [minDepth, maxDepth] 区间

钻孔对比:
  表格: 每个钻孔 → 每类围岩长度/占比
  柱状图: 各钻孔围岩类别占比对比

薄弱段提示:
  IV/V 类围岩集中段列表 → "ZK-001 深 9~17.8m 为 IV 类，长 8.8m；
                              ZK-001 深 17.8~25m 为 V 类，长 7.2m"
储气库选址建议:
  "储气库应避开 IV/V 类围岩段，优先选择完整/较完整(II~III)段"
```

---

## 9. 三维可视化（按分析段着色）

### 9.1 场景元素

场景元素分为**固定元素**（不受钻孔倾角/方位角影响）和**钻孔元素**（随钻孔倾斜变换）两类：

| 元素 | 类型 | 视觉表现 | 数据来源 |
|------|------|---------|---------|
| **3D 透明网格空间** | 固定 | 地面（Y=0）+ 后墙（Z=-extent）+ 侧墙（X=-extent），半透明灰线网格，主网格线加粗；任意角度均可读取深度与水平距离 | 动态计算 |
| **光照系统** | 固定 | 主定向光 + 补光 + 环境光 | — |
| **世界坐标轴** | 固定 | 东/X（红）、上/Y（绿）、北/Z（蓝）三轴箭头及轴名标签 | — |
| **坐标轴刻度** | 固定 | 沿三轴按 `tickInterval` 生成的刻度线 + 数值标签 | 动态计算 |
| **孔口标记** | 固定 | 灰色细圆环 + 中心点 | — |
| **钻孔岩芯柱** | 钻孔 | 按 ClassificationSegments 生成圆柱段；颜色按着色模式动态变化 | ClassificationSegments |
| **物理段边界** | 钻孔 | 细灰色圆环 | CorePhotos |
| **结构面** | 钻孔 | 半透明红色矩形平面，按 Strike/Dip 旋转 | StructuralPlanes |
| **地下水** | 钻孔 | 半透明蓝色椭圆片 + 蓝色边界线 + "地下水" 文字标注 | Boreholes.GroundwaterDepth |
| **深度刻度** | 钻孔 | 主/次短横线从钻孔右侧延伸，主刻度带深度数值标签 | 动态计算 |
| **钻孔信息面板** | 固定 | 右上角2D面板：编号、孔深、X/Y/Z坐标、方位角、倾角、地下水深度 | Borehole模型实时绑定 |
| **图例面板** | 固定 | 左上角2D面板，动态图例项根据着色模式切换 | ViewModel动态生成 |

> **注**：HelixViewport3D 内置的 ViewCube 与 Title 已关闭，避免界面出现冗余的“红色方块”与标题。

### 9.2 着色模式

三种着色模式，通过ComboBox切换，切换后自动重新生成场景：

| 模式 | 颜色映射依据 | 图例内容 |
|------|-------------|---------|
| **按围岩类别** | RockClass (I~V) → 蓝绿→红渐变 | I~V类 |
| **按完整性等级** | IntegrityLevel → 绿色→红色渐变 | 完整/较完整/完整性差/较破碎/破碎 |
| **按岩质类型** | RockType → 蓝/褐/灰色 | 硬质岩/软质岩 |

### 9.3 钻孔倾斜变换

钻孔元素（岩芯柱、结构面、地下水、深度刻度）会根据钻孔的倾角和方位角进行三维旋转变换，网格空间、坐标轴、孔口标记等固定元素保持不动：

- 变换中心：孔口位置 (0, 0, 0)
- 变换顺序：先绕Y轴旋转方位角（确定倾斜方向），再绕X轴旋转倾角（确定倾斜角度）
- 数据来源：`Borehole.InclinationAngle` 和 `Borehole.Azimuth`

### 9.4 交互

采用 HelixViewport3D 默认的 Trackball + Inspect 相机控制器，适配长钻孔大场景：

- **鼠标左键拖拽** → 旋转视角
- **鼠标滚轮** → 缩放；`ZoomSensitivity` 提高，`FarPlaneDistance` 放大到 `max(10000, depth*50)`，保证长钻孔全貌可见
- **鼠标右键拖拽** → 平移
- **ComboBox下拉** → 切换着色模式，自动重新生成场景
- **生成按钮** → 根据当前选中的钻孔重新构建三维场景
- **刻度间隔输入框** → 用户可自定义深度刻度间隔（米），设为0则自动计算
- **复选框** → 控制结构面/地下水/深度刻度的显示与隐藏

### 9.5 深度与场景范围处理

三维场景的显示深度使用围岩分类数据（ClassificationSegments）的实际最大深度：
- `displayDepth = segments.Max(s => s.DepthEnd)`
- 水平场景范围：`max(30, displayDepth * 1.4)`
- 相机初始距离：`max(sceneSize, depth * 2.2)`，确保深孔全貌可见
- 地平面/网格大小在视口层进一步扩大到 `max(200, depth * 4.0)`
- 刻度间隔动态计算：≤10m→1m, ≤50m→5m, ≤100m→10m, ≤200m→20m, >200m→50m

### 9.6 渲染效果

- 渲染引擎：HelixToolkit.Wpf（基于 WPF 3D）
- 多光源系统：主定向光 + 补光 + 环境光
- 岩芯柱：漫反射材质，BackMaterial 双面渲染
- 结构面：半透明红色材质
- 3D 网格空间：半透明灰色 + 主/次网格线
- 坐标轴刻度：灰/黑色短线 + 彩色数值标签

---

## 10. 界面设计

### 10.1 主窗口布局

```
┌──────────────────────────────────────────────────────────────┐
│ RockCore 岩芯围岩评价软件 v1.0    [文件][编辑][分析][视图][报告][帮助] │
├──────────────┬────────────────────────────────────────────────┤
│ 项目导航树     │  Tab: [岩芯编辑器] [分析结果] [三维可视化] [报告] │
│ (200px)       │ ┌───────────────────────────────────────────┐ │
│ ▼ 项目名称    │ │ 钻孔: ZK-001 孔深: 201.3m 地下水: 26.0m  │ │
│   ├─ ZK-001  │ │ [⟳ 重新分析] [📊 切换视图] [📂 Excel导入]  │ │
│   │  ├─ 第1箱 │ │                                           │ │
│   │  └─ ...   │ │ 左侧: 垂直深度标尺岩芯可视化  右侧: 属性    │ │
│   └─ ZK-002  │ │ 编辑器 + 本段分析预览                       │ │
│               │ │                                           │ │
│ [+新建钻孔]   │ │                                           │ │
│ [导入照片]    │ │                                           │ │
│ [▶分析全部]   │ │                                           │ │
└──────────────┴────────────────────────────────────────────────┘
```

### 10.2 三维可视化标签页

三维可视化标签页由 `ThreeDimView` 承载，采用 HelixViewport3D 作为渲染核心，界面结构如下：

```
┌────────────────────────────────────────────────────────────────────┐
│ [着色模式: 按围岩类别 ▼] [🔄 生成三维展示图] [结构面 ☑] [地下水 ☑] [深度刻度 ☑] 刻度间隔(m): [0] │
├────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐                                              ┌──────────┐ │
│  │ 图例面板      │                                              │ 钻孔信息  │ │
│  │ · I 类 (优质)│    3D 视口：钻孔岩芯柱 + 透明网格空间           │ 编号: ZK-001 │
│  │ · II 类 ...  │              + 坐标轴 + 结构面/地下水          │ 孔深: 201.3m │
│  │              │                                              │ 方位角: ...  │
│  │ 交互说明      │                                              │ ...          │
│  └──────────────┘                                              └──────────┘ │
│                                                                    │
│  底部状态栏：就绪 / 正在生成 ... / 已完成 ZK-001 三维场景 ...                  │
└────────────────────────────────────────────────────────────────────┘
```

**顶部工具栏**：
- 着色模式 ComboBox：直接 TwoWay 绑定 `ColorScheme`，切换后自动重载场景。
- 生成按钮：触发 `ReloadSceneCommand`，根据 `SelectedBorehole` 重新构建场景。
- 复选框：控制结构面、地下水、深度刻度的显示；直接 TwoWay 绑定 ViewModel 属性。
- 刻度间隔输入框：自定义深度刻度间隔（米），0 表示自动计算。

**3D 视口**：
- 透明网格空间：地面 + 后墙 + 侧墙，任意角度可见。
- 世界坐标轴：东/X（红）、上/Y（绿）、北/Z（蓝），带刻度与数值标签。
- 岩芯柱：按分析段着色，可随钻孔方位角/倾角旋转。
- 结构面、地下水、深度刻度：可独立开关。

**信息面板**：
- 左上角图例面板：根据着色模式动态生成。
- 右上角钻孔信息面板：编号、孔深、X/Y/Z、方位角、倾角、地下水深度。
- 底部状态栏：显示当前操作状态与进度。

### 10.3 岩芯信息编辑器（核心特色界面）

```
左面板（垂直岩芯视图，占 60%）
  ┌──────────────┐
  │ 0.0 ───────┐ │  ← 顶部
  │             │ │
  │  第1箱 0-9m │ │  ← 灰色水平标签（物理段边界）
  │  较完整 RQD 78% │  ← 物理段图像识别结果
  │  ← ──────── → │  ← 节理标记（小红线）
  │ 5.1 ─ ─ ─ ─ │ │  ← 蓝色虚线（信息段边界，可拖拽）
  │   硬质岩     │ │  ← 信息段彩色块
  │   整体块状   │ │  ← 岩体结构类型
  │ 9.0 ───────┐ │  ← 第1箱结束 / 第2箱开始
  │   第2箱     │ │
  │   破碎 RQD 32% │
  │   硬质岩     │ │
  │   碎裂结构   │ │
  │ 17.0 ────── │ │
  │  ...         │ │
  │ 26.0 ═══════ │ │  ← 蓝色粗线（地下水埋深）
  │  ...         │ │
  │ 201.3 └───── │ │  ← 底部
  └──────────────┘

右面板（属性编辑 + 预览，占 40%）
  ┌─────────────────────────┐
  │ 当前选中段: 5.1-9.0m     │
  │ 岩质类型: [硬质岩 ▼]     │
  │ 岩体结构: [整体块状结构 ▼] │
  │ 完整性:  [较完整(自动) ▼]  │  ← 可手动覆盖
  │ 地下水:  [干燥(自动) ▼]   │  ← 可手动覆盖
  │ 节理数:  3 条            │
  │ RQD:     78%             │
  │ 平均间距: 42cm            │
  │ 备注:    [______________] │
  │ [保存][取消]              │
  ├─────────────────────────┤
  │ 本段分析预览              │
  │ 围岩类别: II~III 类      │
  │ 判定依据: 硬质岩·整体块状·较完整·干燥 → 规则3 → II类 │
  │ 置信度: 82%              │
  └─────────────────────────┘
```

### 10.4 首次向导

1. 创建项目 → 填写名称/阶段/洞轴线走向
2. 创建第一个钻孔 → 填写编号/孔深/地下水埋深/方位角/倾角
3. 导入岩芯照片文件夹 → 预览解析结果（箱号+深度）
4. 自动运行图像识别 → 展示前 3 张照片识别结果
5. 提示用户：开始在岩芯编辑器按地质意义分段填写岩质/岩体结构

---

## 11. 分阶段开发检查点

| 阶段 | 核心交付 | 检查要点 |
|------|---------|---------|
| **阶段 1** | 项目/钻孔数据结构 + 基础界面 + SQLite | 能创建项目、钻孔、保存、重新加载；左侧项目树 + 右侧多标签可用 |
| **阶段 2** | 照片导入与解析 + 物理段管理 | 导入示例照片（LGZ-DXK03 目录下），能正确解析每张的箱号+深度范围；用户可手动修正边界 |
| **阶段 3** | 图像识别引擎 v2.0 核心：岩芯盒检测 + 刻度尺解析 + 岩芯柱分割 + 结果可视化 | 查看每张照片的岩芯盒裁剪图 + 刻度尺像素/厘米换算系数；能识别岩芯柱段数；原图上标注岩芯盒边界、节理裂隙、岩芯分段等信息；可选集成 AI 图像识别（智谱GLM-4V / 通义千问Qwen-VL）提升分析精度 |
| **阶段 3b** | 节理检测 + RQD 计算 + 完整性判定 | 结构面列表、RQD 值、完整性等级自动生成；用户可修正；修正后保留 |
| **阶段 3c** | 人工标注功能 + 训练数据采集 | 用户可手动框选比例尺、岩芯段、节理线；标注结果保存到数据库并作为训练样本采集；人工标注与自动识别功能分离，后续分析逻辑共用 |
| **阶段 3d** | 自动标注功能（基于训练数据的智能识别） | 从所有人工标注经验中学习，自动标注岩芯段和节理；随训练样本增加精度持续提升；与人工标注模式并行，用户可任选其一；自动标注结果可人工修正，修正数据反哺训练库 |
| **阶段 4** | 岩芯信息编辑器 + 用户信息段 CRUD | 能在 0-9m 段中创建 0-5.1m 和 5.1-9m 两个信息段；能拖拽调整边界；下拉选岩质/岩体结构 |
| **阶段 5** | 三段融合算法 + 单钻孔/多钻孔围岩分类 | 交集合并正确；查表判定结果与规范一致；多钻孔整体统计报表生成 |
| **阶段 6** | 三维可视化（Helix Toolkit） | 利用阶段三~五的前期分析结果（结构面、完整性等级、岩性等）生成三维岩芯柱模型；岩芯柱按分析段着色；结构面在三维空间显示正确；交互流畅；渲染效果美观 |
| **阶段 7** | 报告生成（HTML + Word + Excel 导入导出） | 报告包含完整分析数据；Excel 可导入/导出信息段；模板清晰 |
| **阶段 8** | 整体优化 + 异常处理 + 用户体验打磨 | 整体体验流畅、异常完善、视觉专业 |

---

## 12. 关键风险与注意事项

| 风险项 | 影响 | 应对策略 |
|--------|------|---------|
| NuGet 源网络问题 | 无法恢复包 | §1.4 提供 4 种降级方案，推荐提前打包 `%userprofile%\.nuget\packages` 离线备份 |
| 岩芯照片拍摄角度不一致 / 光照差 | 图像识别精度下降 | ① 刻度尺解析失败时回退到芯径标定(7cm)；② 识别置信度低时强制用户手动修正；③ 提供图像对比度增强预处理 |
| 刻度尺 OCR 数字识别不准 | 像素/厘米换算错误 | ① 使用模板匹配（数字0~9的模板）而非通用 OCR；② 刻度线间距均匀，可用间距像素推断而不依赖数字；③ 允许用户手动输入像素/厘米系数 |
| 节理检测假阳性（岩芯自然纹理误判） | 完整性等级偏悲观 | ① 限制裂隙最小长度 >10px；② 颜色变化率阈值筛选；③ 置信度低时默认"较完整"而非"较破碎"；④ 用户可手动删除假阳性结构面 |
| 用户信息段与物理段边界复杂 | 三段融合产生过多小分析段 | ① 最小分析段长度阈值（默认 0.5m，小于此长度的段合并到相邻段）；② 预览融合结果，用户可确认或调整 |
| 三维场景分析段数过多（数百段） | 性能下降 | ① 远处使用 LOD；② 可按钻孔分层展开/折叠；③ 分析段圆柱用实例化渲染 |
| Excel 导入字段不一致 | 导入失败 | 模板设计后与用户确认；导入时提供字段映射预览 + 错误行高亮；允许手动映射 |
| 岩芯照片超大（>10MB/张）加载慢 | 卡顿 | ① 缩略图缓存机制；② 仅在用户点击查看时加载原图；③ 三维场景不使用原图作为纹理 |
| 表 F.0.2 规则存在「II~III」等区间判定 | 结果不唯一 | ① 提供区间选项供用户确认；② 默认取中间值并在 Basis 中注明；③ 置信度低时高亮提示用户复审 |
| 地下水埋深变化（不同季节/不同深度的水位） | 判定受影响 | 支持用户在信息段中对特定深度段手动覆盖地下水状态（`GroundwaterOverride`） |

---

## 13. 文件结构

```
RockCore/
├── RockCore.sln
├── NuGet.config                          // 多源镜像配置
├── docs/
│   └── design.md
│
├── src/
│   ├── RockCore.Core/                    // 核心模型与接口
│   │   ├── Models/
│   │   │   ├── Project.cs
│   │   │   ├── Borehole.cs
│   │   │   ├── CorePhoto.cs              // 物理段
│   │   │   ├── CoreInfoSegment.cs        // 信息段
│   │   │   ├── StructuralPlane.cs
│   │   │   ├── ClassificationSegment.cs  // 分析段
│   │   │   └── AnalysisMetrics.cs
│   │   ├── Enums/
│   │   │   ├── IntegrityLevel.cs         // 完整/较完整/...
│   │   │   ├── RockType.cs               // 硬质岩/软质岩
│   │   │   ├── RockStructureType.cs      // 整体块状/碎裂/...
│   │   │   ├── RockClass.cs              // I~V 类
│   │   │   └── GroundwaterCondition.cs
│   │   ├── Interfaces/
│   │   │   └── IImageAnalyzer.cs         // 可插拔图像分析接口
│   │   └── Services/
│   │       ├── ClassificationService.cs  // 三段融合 + 查表判定（核心）
│   │       └── RockClassificationTable.cs // 表 F.0.2 规则实现
│   │
│   ├── RockCore.Infrastructure/          // 基础设施
│   │   ├── Data/                         // SQLite 数据访问
│   │   │   ├── RockCoreDbContext.cs
│   │   │   ├── EfCore/                   // (可选: 使用 Dapper 或纯 ADO.NET)
│   │   │   └── Repositories/
│   │   ├── ImageAnalysis/                // OpenCvSharp 规则引擎 v2.0
│   │   │   ├── CoreBoxDetector.cs
│   │   │   ├── RulerParser.cs
│   │   │   ├── CoreColumnSegmentation.cs
│   │   │   ├── JointDetector.cs
│   │   │   ├── StructuralPlaneAnalyzer.cs
│   │   │   ├── RQDCalculator.cs
│   │   │   ├── FragmentationAnalyzer.cs
│   │   │   ├── WeatheringAnalyzer.cs
│   │   │   ├── IntegrityClassifier.cs    // 表 F.0.4 规则实现
│   │   │   └── RuleEngineImageAnalyzer.cs // 实现 IImageAnalyzer
│   │   └── Reports/
│   │       ├── HtmlReportGenerator.cs
│   │       ├── WordReportGenerator.cs
│   │       └── ExcelReportGenerator.cs
│   │
│   └── RockCore.Wpf/                     // WPF 界面
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / .cs
│       ├── Views/
│       │   ├── ProjectOverviewView.xaml
│       │   ├── CoreEditorView.xaml       // 岩芯信息编辑器（核心特色）
│       │   ├── ImageAnalysisView.xaml    // 图像识别结果 + 修正
│       │   ├── AnalysisResultView.xaml
│       │   ├── ThreeDimView.xaml         // 三维可视化
│       │   └── ReportView.xaml
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   ├── ThreeDimViewModel.cs
│       │   └── ...
│       ├── ThreeDim/                     // HelixToolkit 场景构建
│       │   ├── SceneBuilder.cs           // 场景元素构建
│       │   ├── ThreeDimSceneService.cs   // 场景数据加载与服务接口
│       │   ├── ColorMapper.cs            // 颜色映射
│       │   └── ColorScheme.cs            // 着色模式枚举
│       ├── Controls/
│       │   ├── DepthRuler.cs             // 自定义深度标尺控件
│       │   └── DraggableSegment.cs       // 可拖拽信息段控件
│       └── Resources/
│
├── samples/
│   └── LGZ-DXK03/                        // 示例数据（参考目录）
│       ├── 3号孔岩心照片/
│       ├── 3号孔岩心编录/
│       ├── 3号孔节理统计/
│       └── 岩性编录+风化带（3号孔）.txt
│
└── tests/
    └── RockCore.Tests/
        ├── PhotoImportServiceTests.cs
        ├── ClassificationServiceTests.cs  // 三段融合算法单测
        └── ImageAnalysisTests.cs          // 图像识别规则引擎单测
```

---

## 14. 自动标注功能（基于人工标注训练数据的智能识别）

> **核心理念**：人工标注与自动标注并行运行。人工标注提供高精度的"金标准"训练数据，自动标注模型从所有人工标注经验中持续学习，随着使用量增长，自动识别精度不断提升，逐步减少人工标注工作量。

### 14.1 双模式并行架构

```
┌──────────────────────────────────────────────────────────────────┐
│                        图像分析入口                               │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│   ┌──────────────────────┐      ┌──────────────────────┐       │
│   │   ✏️ 人工标注模式      │      │   🤖 自动标注模式      │       │
│   │   (Manual Mode)      │      │   (Auto Mode)        │       │
│   └─────────┬────────────┘      └──────────┬───────────┘       │
│             │                              │                   │
│             ▼                              ▼                   │
│   用户手动框选比例尺/岩芯段/节理     基于训练数据自动识别       │
│             │                              │                   │
│             └──────────────┬───────────────┘                   │
│                            │                                   │
│                            ▼                                   │
│                  统一分析结果（共用）                           │
│                  · 完整性等级判定                               │
│                  · 结构面统计                                   │
│                  · RQD 计算                                     │
│                  · 三段融合与围岩分类                           │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### 14.2 训练数据闭环

```
人工标注完成
    │
    ├─→ 保存分析结果到 CorePhotos / StructuralPlanes
    │
    └─→ 写入训练数据库（TrainingSamples）
            │
            │  训练数据结构：
            │  · 图像特征（哈希 + 基本统计）
            │  · 比例尺标注（位置 + pixelPerCm）
            │  · 岩芯段标注（每段轮廓点集 + 深度）
            │  · 节理标注（位置 + 方向 + 宽度）
            │
            ▼
    训练样本积累 → 模型训练/微调 → 自动标注精度提升
            ▲                          │
            │                          │
            └──── 用户修正自动标注结果 ─┘
                 （修正后的数据也作为训练样本）
```

### 14.3 训练数据库设计

#### 14.3.1 数据表概览

| 表名 | 中文名称 | 关键字段 | 用途 |
|------|---------|---------|------|
| TrainingSamples | 训练样本表 | Id, CorePhotoId, ImageHash, ImageWidth, ImageHeight, SampleType, QualityScore, CreatedAt | 每条人工标注记录生成一个训练样本 |
| TrainingRulerAnnotations | 比例尺标注训练数据 | Id, SampleId, StartPointX/Y, EndPointX/Y, ActualLengthCm, PixelPerCm | 人工标注的比例尺位置与换算系数 |
| TrainingCoreSegmentAnnotations | 岩芯段标注训练数据 | Id, SampleId, SegmentIndex, ContourPointsJson, DepthStart, DepthEnd, LengthCm, IntegrityLevelHint | 人工标注的岩芯段轮廓与深度 |
| TrainingJointAnnotations | 节理标注训练数据 | Id, SampleId, JointIndex, StartPointX/Y, EndPointX/Y, WidthPx, WidthCm, JointType | 人工标注的节理位置与参数 |
| TrainingModelVersions | 模型版本表 | Id, ModelName, Version, TrainingSampleCount, AccuracyMetrics, TrainedAt, IsActive | 记录各版本自动标注模型的训练信息 |

#### 14.3.2 训练样本质量分级

| 等级 | 条件 | 权重 |
|------|------|------|
| A级（高质量） | 资深用户标注 + 复核通过 + 结果无争议 | 1.0 |
| B级（标准） | 普通用户标注 + 保存时无修改 | 0.7 |
| C级（待验证） | 自动标注结果 + 用户小幅度修正 | 0.4 |
| D级（低质量） | 标注不完整或有争议 | 0.1 或忽略 |

### 14.4 自动标注算法演进路径

#### 阶段 A：规则引擎基线（已实现）
- OpenCV 传统图像处理（Canny + Hough + 形态学）
- 适用于光照均匀、岩芯清晰的标准照片
- 作为自动标注的兜底方案

#### 阶段 B：训练数据驱动的参数优化（下一阶段）
- 从人工标注样本中学习最优参数组合
- 针对不同场景（不同矿区、不同岩性、不同拍摄条件）自动选择参数集
- 使用决策树 / 随机森林从图像特征匹配最优参数

#### 阶段 C：轻量机器学习模型
- **岩芯盒检测**：基于 HoG + SVM 或小型 CNN
- **岩芯柱分割**：GrabCut + 形状先验（从训练数据学习岩芯形态统计）
- **节理检测**：方向滤波器组 + 分类器判别真伪节理

#### 阶段 D：深度学习模型（长期目标）
- 语义分割模型（UNet / DeepLab）：像素级岩芯与节理解析
- 目标检测模型（YOLO）：岩芯段、节理、刻度尺一键检测
- 模型部署：ONNX Runtime 本地推理，无需云端

### 14.5 自动标注与人工标注的交互设计

| 场景 | 行为 |
|------|------|
| **首次使用（0 训练样本）** | 自动标注按钮置灰，提示"请先进行人工标注积累训练数据" |
| **样本量 < 20** | 自动标注可用但标注"实验性"，结果需用户确认 |
| **样本量 ≥ 20 且 < 100** | 自动标注默认启用，结果高亮"待复核"标记 |
| **样本量 ≥ 100** | 自动标注默认启用，结果直接进入分析流程（用户仍可修改） |
| **用户修正自动标注结果** | 修正前后数据均写入训练库，用于模型迭代 |
| **用户删除自动标注的节理** | 记录为"假阳性"负样本，优化检测阈值 |
| **用户添加漏掉的节理** | 记录为"漏检"正样本，优化召回率 |

### 14.6 自动标注工作流程

```
用户点击「🤖 自动识别分析」按钮
    │
    ▼
加载图像 + 查询当前激活的自动标注模型版本
    │
    ├─→ 无可用模型 / 训练样本不足 → 提示用户，回退到纯规则引擎
    │
    ▼
执行自动标注流水线：
    步骤 1：图像预处理（去噪、对比度增强、倾斜校正）
    步骤 2：岩芯盒区域检测（规则引擎 + 训练参数）
    步骤 3：比例尺识别（模板匹配 + 训练样本学习的刻度特征）
    步骤 4：岩芯段分割（形态学 + 训练数据驱动的参数）
    步骤 5：节理检测（多规则融合 + 分类器过滤假阳性）
    步骤 6：结果后处理（完整性等级判定、分段合并）
    │
    ▼
生成自动标注结果 → 显示在 ManualAnnotationWindow 中
    │
    ├─ 用户确认/修改 → 保存 → 写入训练数据库
    └─ 用户直接取消 → 不保存，不写入训练库
```

### 14.7 模型训练与版本管理

#### 训练触发条件
- 手动触发：用户在设置中点击「重新训练模型」
- 自动触发：每新增 N 条 A/B 级样本后自动触发（N 可配置，默认 50）

#### 版本管理
- 每个训练版本保留完整的模型文件 + 训练集快照 + 准确率评估
- 支持回滚到历史版本
- 新版本上线前需在验证集上准确率不低于当前活跃版本

#### 准确率评估指标
- **岩芯段检测**：IoU（交并比）、精准率、召回率
- **节理检测**：精准率、召回率、F1 值、宽度测量误差
- **比例尺识别**：pixelPerCm 相对误差
- **完整性等级**：与人工标注等级一致率

### 14.8 数据隐私与安全

- 所有训练数据本地存储，不上传云端
- 训练样本可导出/导入，支持多机共享训练成果
- 支持一键清空训练数据库
- 用户可选择是否将某条标注纳入训练（默认纳入）

### 14.9 与现有架构的集成点

| 现有模块 | 集成方式 |
|---------|---------|
| `ManualAnnotationWindow` | 增加「🤖 自动识别」按钮，一键填充自动标注结果供用户调整 |
| `CorePhoto.AnalysisResultJson` | 新增 `Analyzer` 字段标记来源：`Manual` / `RuleEngine` / `AutoLabel-vX.Y` |
| `MainWindow` 工具栏 | 新增「自动识别分析」按钮，与「人工标注分析」并列 |
| 设置面板 | 新增「自动标注」设置页：模型选择、训练管理、参数调整 |
| 数据库 | 新增 5 张训练相关表（见 §14.3.1） |
| `IImageAnalyzer` 接口 | 新增 `AutoLabelAnalyzer` 实现类，封装自动标注流水线 |

---

> **设计文档结束**。如需修改，请直接在文档对应章节编辑，或告知我更新。
