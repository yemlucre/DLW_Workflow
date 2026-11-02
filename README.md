# DLW_Workflow（Agamemnon 工艺流程包）

Agamemnon「Function 功能包分层」的第四个包：**工艺流程域**——组合设计数据与设备接口的编排层。从主项目 `Function/Agamemnon.ProcessWorkflow` 于 2026-11-02 抽出为独立仓库；为保证自包含可构建，同时携带两个依赖：`Agamemnon.Drawing`（同 [DLW_Drawing](https://github.com/yemlucre/DLW_Drawing)）与 `Agamemnon.DeviceControl`（同 [DLW_DeviceControl](https://github.com/yemlucre/DLW_DeviceControl)）。

## 定位

四块能力，语义对齐主项目已被千项回归验证的工艺规则：

1. **预设管理**：类型化预设（矢量/像素模式、台速、门宽、像素间距）+ 进程内目录（唯一名、版本号、选择）
2. **曝光换算**：62.5 µs/门宽精确换算（1 门宽 = 625 ticks 无精度损失，16 门宽 = 1 ms 整）；像素门宽 16 倍数产品策略在这一层
3. **设计→加工准备**：仅 Enabled 对象 → 项目映射换算为舞台坐标 → 逐轴行程核对；四态（行程内/超出行程/未验证/为空）+ 2 秒新鲜度；未连接仍给数学预览
4. **作业编排**：前置校验（拒绝则不动设备）→ 矢量/像素段生成 → 缓冲执行；默认干跑，停止/取消如实报告部分完成

## 目录

```text
src/Agamemnon.ProcessWorkflow/
├── Presets/       ProcessingPreset（校验）+ IPresetCatalog
├── Exposure/      ExposureMath（精确换算 + 16 倍数规则）
├── Preparation/   IPreparationService（四态 + 新鲜度）
└── Orchestration/ IJobOrchestrator（前置拒绝 + 段生成 + 执行映射）
src/Agamemnon.Drawing/        依赖一：画图域（设计数据/映射）
src/Agamemnon.DeviceControl/  依赖二：设备域（位移台/缓冲加工接口）
tests/                        独立无硬件检查（44 项，与主仓库同一检查集）
docs/contract.md              契约与边界
```

## 公开接口速览

```csharp
var catalog = new PresetCatalog();
var preset = catalog.Add(new ProcessingPreset { Name = "矢量-A", Mode = ProcessingMode.Vector, GateCount = 16 });

TimeSpan oneMillisecond = ExposureMath.Exposure(16);           // 精确 1 ms

var report = new PreparationService().Prepare(designDocument, stage);
// report.Status: WithinTravel / OutsideTravel / Unverified / EmptyDesign

JobResult job = await new JobOrchestrator().RunAsync(designDocument, preset, stage, processing);
// 前置不满足 → job.Started == false（不动设备）；默认干跑；
// job.PlannedExposure == 段数 × 门宽 × 62.5 µs（精确）
```

## 构建与检查

```powershell
dotnet run --project tests\Agamemnon.ProcessWorkflow.Checks.csproj -c Release   # 44 项检查
```

需要 .NET 8 SDK，跨平台、无硬件。

## 验证记录

- 2026-11-02：上游主仓库 Debug/Release 全量无硬件回归 1103 项通过（本包 44 项）；本仓库独立运行同一检查集通过。
- 已知边界：预设目录为进程内实现（宿主自行持久化）；真实驱动适配未做；仿真设备不代表硬件规格。
