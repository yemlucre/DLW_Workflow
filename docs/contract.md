# Agamemnon.ProcessWorkflow 工艺流程包契约

检查日期：2026-11-02。能力 ID：`process.workflow_package`（implemented，agentExposed=false）。本文件是 `Function/Agamemnon.ProcessWorkflow`（现独立仓库 DLW_Workflow）的接口契约与边界记录。

## 定位与边界

- **职责**：组合 Drawing 的设计数据 + DeviceControl 的设备接口的工艺编排层：预设管理、曝光换算、设计→加工准备、作业编排。
- **依赖**：恰好 `Agamemnon.Drawing` + `Agamemnon.DeviceControl`（回归强制）；net8.0 纯 BCL。
- **不在包内**：设备协议细节（属 DeviceControl/适配器）、UI、预设持久化文件格式（PresetCatalog 为进程内目录，宿主自行保存）、旧键（burst.*）映射。

## 公开接口

| 接口/类型 | 实现 | 说明 |
|---|---|---|
| `ProcessingPreset` + `ProcessingMode` | record | 预设：模式（矢量/像素）、台速 mm/s、门宽数量、像素间距 mm；`Validate`（名称 1–80、台速 0–1000、像素门宽为正 16 倍数、间距 0–1） |
| `IPresetCatalog` | `PresetCatalog` | 进程内目录：Add/Copy/Rename/Update/Remove/Select；名称唯一；Revision 随变化递增；删除选中项清除选择 |
| `ExposureMath`（静态） | — | 62.5 µs/门宽精确换算（1 门宽=625 ticks、16 门宽=1 ms 整）；`TotalExposure(段数, 门宽)`；`IsValidPixelGateCount` |
| `IPreparationService` | `PreparationService` | 文档+位移台 → `PreparationReport`：仅 Enabled 对象（树序）经项目映射换算为舞台坐标 mm；逐轴行程核对；四态（WithinTravel/OutsideTravel/Unverified/EmptyDesign）；`IsCurrent`（版本一致 + 观测 ≤2 秒对称容差）；未连接仍给数学预览 |
| `IJobOrchestrator` | `JobOrchestrator` | 前置校验（空设计/超行程/未验证/控制器未连接/预取消 → `Started=false` 不动设备）→ 段生成 → `IBufferedProcessingController.RunAsync`；结果如实映射（Completed/Stopped/Cancelled + 完成段数 + 计划曝光精确） |

## 段生成与安全策略

- 矢量：折线相邻点成段（零长跳过）；每段门宽=预设门宽、速度=预设台速。
- 像素：按 `PixelSpacingMm` 沿路径全程累计布驻留点（起点必布）；每点一段（From=To）；驻留段速度字段为台速占位（驻留时长由门宽决定，不代表驻留期间运动）。
- 计划曝光 = 段数 × 门宽 × 62.5 µs（TimeSpan 精确值）。
- `JobOptions.AllowLaserOutput` 默认 false（干跑）；true 才在缓冲程序中请求激光输出。

## 验证

2026-11-02：上游主仓库根 `test.ps1` Debug 与 Release 全量无硬件回归 **1103 项通过**（本包 44 项 `tests/ProcessWorkflowPackageChecks.cs`：包组合关系、预设 CRUD/校验、曝光精确性（16 门宽=1 ms 整）、准备四态（映射换算/出界轴命名/断连数学预览/过期快照/版本失效）、编排前置拒绝（空/未验证/断连控制器/超行程不动设备/预取消）、矢量段计数与精确计划曝光、像素布点、干跑/出光标志运行中可见、停止如实部分完成）；本仓库独立运行同一检查集。无设备操作。
