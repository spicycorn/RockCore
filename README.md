# RockCore · 岩芯围岩类别评价系统

> **岩芯图像分析 + 围岩初步分类 + 三维可视化** 的 Windows 桌面工具。
> 依据 **DL/T 5894-2025《压气储能电站工程地质勘察规范》** 设计，面向**压缩空气储能电站岩芯围岩评价**。

<div align="center">
  <img src="docs/icon_preview.png" width="128" height="128" alt="RockCore 图标"/>
</div>

---

## ✨ 功能特性

- **项目 / 钻孔管理**：左侧项目树 + 右侧多标签界面，数据本地 SQLite 持久化。
- **岩芯照片导入与解析**：批量导入岩芯照片，自动解析箱号 + 深度范围，支持手动修正边界。
- **图像识别引擎（规则引擎）**：岩芯盒检测、刻度尺解析（像素/厘米换算）、岩芯柱分割、节理/结构面识别、RQD 计算、完整性等级判定。
- **人工标注**：手动框选比例尺、岩芯段、节理线，标注结果入库并作为训练样本采集。
- **三段融合 + 围岩分类**：结构面 / 完整性 / 岩性三段融合，查表判定（表 F.0.2），单钻孔 / 多钻孔整体统计。
- **三维可视化**：基于 HelixToolkit 的岩芯柱三维模型，按分析段着色、结构面空间展示。
- **规范配置可视化**：岩体结构映射、完整程度划分等参数可调（`rock_specification.json`）。

> 图像识别当前为**纯规则引擎（无 AI / 无联网）**，离线可用、结果可复现。
> 基于训练数据的**智能自动标注（阶段 3d）** 为规划功能，界面已预留入口（标注"开发中"）。

---

## 🧱 技术栈

| 层 | 技术 |
|----|------|
| 运行时 | .NET 8（WPF，`net8.0-windows`） |
| UI | WPF + CommunityToolkit.Mvvm（MVVM） |
| 三维 | HelixToolkit.Wpf |
| 图像 | OpenCvSharp4（规则引擎） |
| 数据 | SQLite（`Microsoft.Data.Sqlite`） |
| 依赖注入 | Microsoft.Extensions.DependencyInjection |

### 架构（三层 Clean Architecture）

```
src/
├── RockCore.Core/            # 领域层：枚举 / 接口 / 模型 / 分类服务
├── RockCore.Infrastructure/  # 基础设施：SQLite 仓储 + 图像识别引擎
└── RockCore.Wpf/             # 界面层：WPF 视图 / ViewModel / 三维
```

---

## 🏗️ 构建（开发）

> 需要 **Windows 10/11（64 位）** + **.NET 8 SDK**。

```powershell
git clone <你的仓库地址> RockCore
cd RockCore
dotnet restore RockCore.slnx
dotnet build RockCore.slnx -c Release
```

## ▶️ 运行（本地开发）

```powershell
dotnet run --project src/RockCore.Wpf/RockCore.Wpf.csproj
```

## 📦 发布（自包含便携版，免装 .NET）

```powershell
dotnet publish src/RockCore.Wpf/RockCore.Wpf.csproj \
  -c Release -r win-x64 --self-contained true -o publish
```

产物 `publish/RockCore.Wpf.exe` 为**自包含 win-x64 便携版**，在任意 **64 位 Windows 10/11** 上免安装 .NET 直接运行。
> 说明：WPF/.NET 8 仅支持 64 位 Windows 10/11，无法在 32 位 / Win7 / Win8 上运行——这是平台限制，非本工具可选范围。

---

## ☁️ GitHub 自动打包

本仓库内置 **GitHub Actions**（`.github/workflows/build.yml`）：

- 推送到 `main` 分支 → 自动构建并产出自包含便携版 zip（Artifacts）。
- 打标签（如 `v1.0.0`）→ 自动构建并上传到对应 **GitHub Release**，可直接下载 `.exe` 绿色版。

> 发布产物走 **GitHub Releases / Artifacts** 分发，**不放入代码库**，仓库保持轻量（源码约 1 MB）。

---

## 📊 开发阶段进度

| 阶段 | 核心交付 | 状态 |
|------|---------|------|
| 阶段 1 | 项目/钻孔数据结构 + 基础界面 + SQLite | ✅ 完成 |
| 阶段 2 | 照片导入解析 + 物理段管理 | ✅ 完成 |
| 阶段 3 | 图像识别核心（岩芯盒/刻度尺/岩芯柱分割/可视化） | ✅ 完成 |
| 阶段 3b | 节理检测 + RQD + 完整性判定 | ✅ 完成 |
| 阶段 3c | 人工标注 + 训练数据采集 | ✅ 完成 |
| 阶段 3d | 基于训练数据的智能自动标注 | ⚠️ 规划中（界面已预留入口） |
| 阶段 4 | 岩芯信息编辑器 + 信息段 CRUD | ✅ 完成 |
| 阶段 5 | 三段融合 + 围岩分类 + 多钻孔统计 | ✅ 完成 |
| 阶段 6 | 三维可视化（HelixToolkit） | ✅ 完成 |
| 阶段 7 | 报告生成（HTML/Word/Excel 导入导出） | ❌ 未开始 |
| 阶段 8 | 整体优化 + 异常处理 + 体验打磨 | ⚠️ 持续进行 |

详见 [docs/design.md](docs/design.md)。

---

## 📂 目录结构

```
RockCore/
├── RockCore.slnx            # 解决方案
├── .github/workflows/       # GitHub Actions（构建 + 发布便携版）
├── docs/
│   ├── design.md            # 方案设计文档（v3.1）
│   ├── icon_preview.png     # 图标预览
│   └── *.png                # 规范参考图（DL/T 5894-2025）
├── src/                     # 三层源码
│   ├── RockCore.Core/
│   ├── RockCore.Infrastructure/
│   └── RockCore.Wpf/
└── tools/
    └── make_icon.py         # 图标生成脚本（纯标准库）
```

---

## 📄 许可证

[Apache License 2.0](LICENSE) — 详见 `LICENSE` 文件。
> 本软件实现了 DL/T 5894-2025 规范的判定逻辑；该规范本身受其出版方版权约束，请自行确保合规使用。
