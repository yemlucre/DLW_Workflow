using System;
using System.Collections.Generic;
using System.Linq;
using Agamemnon.DeviceControl;
using Agamemnon.Drawing;

namespace Agamemnon.ProcessWorkflow;

/// <summary>
/// 设计→加工准备接口：只做几何换算与行程核对，不发任何设备指令。只处理 Enabled 对象
///（Visible/Locked 不排除，对齐现行准备语义）；坐标经项目映射换算为位移台命令坐标（mm）。
/// 位移台未连接或快照超过 2 秒 → Unverified，仍提供数学预览。
/// </summary>
public interface IPreparationService
{
    PreparationReport Prepare(IDesignDocument document, IStageController stage, DateTimeOffset? now = null);
}

public sealed class PreparationService : IPreparationService
{
    public PreparationReport Prepare(IDesignDocument document, IStageController stage, DateTimeOffset? now = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (stage == null) throw new ArgumentNullException(nameof(stage));
        var snapshot = document.Snapshot;
        var mapping = document.Settings.Mapping;
        var clock = now ?? DateTimeOffset.UtcNow;
        var objects = new List<PreparedObject>();
        foreach (var o in snapshot.Objects.Where(o => o.Enabled))
        {
            var transformed = DesignGeometry.Generate(o)
                .Select(path => (IReadOnlyList<DesignPoint3>)Array.AsReadOnly(path.Select(p => DesignMapping.Transform(p, mapping)).ToArray()))
                .ToArray();
            objects.Add(new(o.Id, o.Name, transformed, DesignGeometry.Bounds(transformed), Array.Empty<string>()));
        }
        if (objects.Count == 0)
            return new(PreparationStatus.EmptyDesign, "没有启用（Enabled）的对象。", snapshot.Revision, clock, null, null, objects, Array.Empty<string>());
        var bounds = DesignGeometry.Bounds(objects.SelectMany(o => o.PathsStageMm));
        var warnings = new List<string>();
        if (!stage.IsConnected)
        {
            warnings.Add("位移台未连接：仅数学预览，行程未验证。");
            return new(PreparationStatus.Unverified, warnings[0], snapshot.Revision, clock, null, bounds, objects, warnings);
        }
        var stageSnapshot = stage.Snapshot();
        var axes = stageSnapshot.Axes.ToDictionary(a => a.AxisId, StringComparer.Ordinal);
        var checkedObjects = objects.Select(o => o with
        {
            Violations = Violations(o.BoundsStageMm, axes).ToArray()
        }).ToArray();
        bool outside = checkedObjects.Any(o => o.Violations.Count > 0);
        if (outside)
            return new(PreparationStatus.OutsideTravel, "设计超出位移台行程，见对象违规清单。", snapshot.Revision, clock, stageSnapshot.ObservedAt, bounds, checkedObjects, warnings);
        if (clock - stageSnapshot.ObservedAt > TimeSpan.FromSeconds(2) || stageSnapshot.ObservedAt - clock > TimeSpan.FromSeconds(2))
        {
            warnings.Add("位移台快照超过 2 秒或时间异常：行程判定未验证。");
            return new(PreparationStatus.Unverified, warnings[0], snapshot.Revision, clock, stageSnapshot.ObservedAt, bounds, checkedObjects, warnings);
        }
        return new(PreparationStatus.WithinTravel, "全部启用对象在行程内。", snapshot.Revision, clock, stageSnapshot.ObservedAt, bounds, checkedObjects, warnings);
    }

    private static IEnumerable<string> Violations(DesignBounds bounds, IReadOnlyDictionary<string, StageAxisState> axes)
    {
        foreach (var (id, axis) in axes)
        {
            bool beyond(double min, double max, double value) => value < min - 1e-9 || value > max + 1e-9;
            if (id == "X" && (beyond(axis.MinimumMm, axis.MaximumMm, bounds.Minimum.X) || beyond(axis.MinimumMm, axis.MaximumMm, bounds.Maximum.X)))
                yield return $"轴 X 超出行程 [{axis.MinimumMm}, {axis.MaximumMm}] mm。";
            if (id == "Y" && (beyond(axis.MinimumMm, axis.MaximumMm, bounds.Minimum.Y) || beyond(axis.MinimumMm, axis.MaximumMm, bounds.Maximum.Y)))
                yield return $"轴 Y 超出行程 [{axis.MinimumMm}, {axis.MaximumMm}] mm。";
            if (id == "Z" && (beyond(axis.MinimumMm, axis.MaximumMm, bounds.Minimum.Z) || beyond(axis.MinimumMm, axis.MaximumMm, bounds.Maximum.Z)))
                yield return $"轴 Z 超出行程 [{axis.MinimumMm}, {axis.MaximumMm}] mm。";
        }
    }
}
