using System;
using System.Collections.Generic;
using Agamemnon.Drawing;

namespace Agamemnon.ProcessWorkflow;

/// <summary>准备状态（对齐现行语义）：行程内 / 超出行程 / 未验证（位移台未连接或快照过期） / 设计为空。</summary>
public enum PreparationStatus { WithinTravel, OutsideTravel, Unverified, EmptyDesign }

/// <summary>单个启用对象的准备结果：舞台坐标路径 + 包围盒 + 违规清单（空=无违规）。</summary>
public sealed record PreparedObject(Guid Id, string Name,
    IReadOnlyList<IReadOnlyList<DesignPoint3>> PathsStageMm, DesignBounds BoundsStageMm,
    IReadOnlyList<string> Violations);

/// <summary>准备报告：整体状态/消息、文档版本、检查时间、位移台观测时间、整体包围盒、逐对象结果与警告。</summary>
public sealed record PreparationReport(PreparationStatus Status, string Message, long ContentRevision,
    DateTimeOffset CheckedAt, DateTimeOffset? StageObservedAt, DesignBounds? Bounds,
    IReadOnlyList<PreparedObject> Objects, IReadOnlyList<string> Warnings)
{
    /// <summary>报告是否仍可信：文档版本未变，且位移台观测时间与给定时刻相差在 2 秒内（对称容差，容忍读取顺序带来的微小偏差）。</summary>
    public bool IsCurrent(DateTimeOffset now, long contentRevision) => ContentRevision == contentRevision &&
        StageObservedAt.HasValue && now - StageObservedAt.Value <= TimeSpan.FromSeconds(2) &&
        StageObservedAt.Value - now <= TimeSpan.FromSeconds(2);
}
