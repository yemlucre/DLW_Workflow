using System;
using System.Collections.Generic;

namespace Agamemnon.ProcessWorkflow;

/// <summary>作业选项：AllowLaserOutput=false（默认）为干跑；true 才在程序中请求激光输出。</summary>
public sealed record JobOptions(bool AllowLaserOutput = false);

/// <summary>作业结果：Started=false 表示前置校验未通过（Error 给出中文原因，未向设备发送任何段）；
/// 执行结果如实映射：Completed/Stopped/Cancelled、完成段数与计划曝光。</summary>
public sealed record JobResult(bool Started, bool Completed, bool Stopped, bool Cancelled,
    int CompletedSegments, int TotalSegments, TimeSpan PlannedExposure, string Error);
