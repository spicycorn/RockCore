# RockCore · 岩心围岩类别评价系统

> **Excel 数据导入 + 围岩初步分类 + 三维可视化** 的 Windows 桌面工具。
> 依据 **DL/T 5894-2025《压气储能电站工程地质勘察规范》** 设计，面向**压缩空气储能电站岩心围岩评价**。

<div align="center">
  <img src="docs/icon_preview.png" width="128" height="128" alt="RockCore 图标"/>
</div>

---

## ✨ 功能特性

- **项目 / 钻孔管理**：左侧项目树 + 右侧多标签界面，数据本地 SQLite 持久化。
- **Excel 数据导入（主工作流）**：按《岩心回次统计导入模板》（一钻孔一文件，「数据录入」+「计算成果」双表）导入回次数据；软件按原始数据（回次进尺 / <10cm 碎屑数 / 逐根量测的 ≥10cm 岩心段长）重算全部指标并自动判定完整性，导入前红/黄行预览校验。
- **模板下载渠道**：模板 xlsx 内置在程序集内，软件内 **文件 → 下载导入模板**（或「导入数据表」对话框内「下载导入模板」）即可另存到任意目录，离线可用；便携版 zip 附带 `模板\岩心回次统计导入模板.xlsx`，GitHub Release 也提供模板单独下载。
- **完整性判定（表 F.0.4）**：`n = ≥10cm段数 + 碎屑数 → S = 进尺×100÷n (cm)`，**只按平均间距 S 查判定表**。表中的「结构面发育组数」是岩体的地质描述量（节理有几组），无法由回次取芯数据算出，因此不参与自动判级，仅在规范设置中供人工记录。
- **回次 → 岩体段归并**：规范的评价单元是"岩体性质相对均一的连续段"，回次只是采样单元。导入时先把相邻、平均间距相近的回次归并成岩体段（最小段厚、间距容差可在规范配置调整；岩质变化与深度空洞为强制分界），**先归并、后判级**，再写入完整性分段；回次原始记录完整保留在回次统计中可溯源。
- **三段融合 + 围岩分类（表 F.0.2）**：岩质 / 岩体结构 / 坚硬程度 / 均一性 / 地下水 / 洞轴夹角查表判定，单钻孔 / 多钻孔整体统计。
- **数据源管理**：导入批次溯源，重复导入自动替换同钻孔旧批次，可按批次删除回退。
- **三维可视化**：基于 HelixToolkit 的岩心柱三维模型，按完整性分段着色。
- **规范配置可视化**：岩体结构映射、完整程度划分等参数可调（`rock_specification.json`）。
- **岩心照片附件**：照片按物理段保留（为孔内电视等精细判定预留扩展），不参与判定。

> 判定链路完全基于现场实测原始数据，Excel 公式列仅供现场查看、软件导入时全部重算，结果可复现、可溯源。

---

## 🧱 技术栈

| 层 | 技术 |
|----|------|
| 运行时 | .NET 8（WPF，`net8.0-windows`） |
| UI | WPF + CommunityToolkit.Mvvm（MVVM） |
| 三维 | HelixToolkit.Wpf |
| Excel | ClosedXML（数据表解析） |
| 数据 | SQLite（`Microsoft.Data.Sqlite`） |
| 依赖注入 | Microsoft.Extensions.DependencyInjection |

### 架构（三层 Clean Architecture）

```
src/
├── RockCore.Core/            # 领域层：枚举 / 接口 / 模型 / 判定与分类服务
├── RockCore.Infrastructure/  # 基础设施：SQLite 仓储 + Excel 导入服务
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
- 打标签发布时同时上传 **《岩心回次统计导入模板》xlsx**，可作为模板的在线下载入口（无需下整包）。

> 发布产物走 **GitHub Releases / Artifacts** 分发，**不放入代码库**，仓库保持轻量（源码约 1 MB）。

---

## 📊 开发阶段进度

| 阶段 | 核心交付 | 状态 |
|------|---------|------|
| 阶段 1 | 项目/钻孔数据结构 + 基础界面 + SQLite | ✅ 完成 |
| 阶段 2 | 照片导入解析 + 物理段管理 | ✅ 完成（照片已降级为附件） |
| 阶段 3 | 图像识别 / 人工标注（绘制模式） | 🗄️ 已停用（保留代码待删除） |
| 阶段 4 | 完整性分段编辑 + F.0.2 参数录入 | ✅ 完成 |
| 阶段 5 | 三段融合 + 围岩分类 + 多钻孔统计 | ✅ 完成 |
| 阶段 6 | 三维可视化（HelixToolkit） | ✅ 完成 |
| 阶段 7 | **Excel 数据导入模式（M1）**：模板导入 + 预览校验 + 批次管理 + 模板下载渠道 | ✅ 完成 |
| 阶段 7b | RQD 深度曲线统计视图、错误报告导出（M2） | ⏳ 规划中 |
| 阶段 7c | 孔内电视 / 声波数据接入（M3） | ⏳ 规划中 |
| 阶段 8 | 整体优化 + 异常处理 + 体验打磨 | ⚠️ 持续进行 |

详见 [docs/design.md](docs/design.md) 与 [docs/Excel数据模式重构计划.md](docs/Excel数据模式重构计划.md)。

---

## 📂 目录结构

```
RockCore/
├── RockCore.slnx            # 解决方案
├── .github/workflows/       # GitHub Actions（构建 + 发布便携版）
├── docs/
│   ├── design.md            # 方案设计文档
│   ├── Excel数据模式重构计划.md  # 数据导入模式方案与决策记录
│   ├── templates/           # 岩心回次统计导入模板（xlsx，构建时嵌入 exe，供软件内下载）
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
