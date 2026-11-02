using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agamemnon.DeviceControl;
using Agamemnon.Drawing;

namespace Agamemnon.ProcessWorkflow;

/// <summary>
/// 作业编排接口：设计文档 + 预设 → 前置准备校验（行程/新鲜度/连接）→ 段生成（矢量/像素）→ 缓冲执行。
/// 前置不满足返回 Started=false 且不动设备；执行结果如实映射控制器的停止/取消与部分完成。
/// 默认干跑；真实出光需显式 AllowLaserOutput=true。
/// </summary>
public interface IJobOrchestrator
{
    Task<JobResult> RunAsync(IDesignDocument document, ProcessingPreset preset, IStageController stage,
        IBufferedProcessingController processing, JobOptions options = null,
        CancellationToken cancellation = default, DateTimeOffset? now = null);
}

public sealed class JobOrchestrator : IJobOrchestrator
{
    private readonly IPreparationService _preparation;
    public JobOrchestrator(IPreparationService preparation = null)
        => _preparation = preparation ?? new PreparationService();

    public async Task<JobResult> RunAsync(IDesignDocument document, ProcessingPreset preset, IStageController stage,
        IBufferedProcessingController processing, JobOptions options = null,
        CancellationToken cancellation = default, DateTimeOffset? now = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (stage == null) throw new ArgumentNullException(nameof(stage));
        if (processing == null) throw new ArgumentNullException(nameof(processing));
        ProcessingPreset.Validate(preset);
        options ??= new JobOptions();
        var clock = now ?? DateTimeOffset.UtcNow;

        var report = _preparation.Prepare(document, stage, clock);
        if (report.Status == PreparationStatus.EmptyDesign)
            return NotStarted("设计为空：没有启用（Enabled）的对象。");
        if (report.Status == PreparationStatus.OutsideTravel)
        {
            var violations = report.Objects.SelectMany(o => o.Violations).ToList();
            return NotStarted("设计超出位移台行程：" + string.Join("；", violations.Take(3)) + (violations.Count > 3 ? " 等" : ""));
        }
        if (report.Status == PreparationStatus.Unverified || !report.IsCurrent(clock, document.Snapshot.Revision))
            return NotStarted("位移台状态未验证（未连接或快照过期），拒绝执行。");
        if (!processing.IsConnected)
            return NotStarted("加工控制器未连接。");
        if (cancellation.IsCancellationRequested)
            return NotStarted("作业在启动前已取消。");

        var segments = preset.Mode == ProcessingMode.Vector ? VectorSegments(report, preset) : PixelSegments(report, preset);
        if (segments.Count == 0) return NotStarted("没有可执行的段（路径全部为零长）。");
        var exposure = ExposureMath.TotalExposure(segments.Count, preset.GateCount);
        var run = await processing.RunAsync(new BufferedProgram(segments, options.AllowLaserOutput), cancellation).ConfigureAwait(false);
        return new JobResult(true, run.Completed, run.Stopped, run.Cancelled,
            run.CompletedSegments, run.TotalSegments, exposure, run.Error);
    }

    private static JobResult NotStarted(string error) => new(false, false, false, false, 0, 0, TimeSpan.Zero, error);

    /// <summary>矢量模式：折线相邻点成段，零长段跳过；每段门宽=预设门宽，速度=预设台速。</summary>
    private static List<BufferedSegment> VectorSegments(PreparationReport report, ProcessingPreset preset)
    {
        var segments = new List<BufferedSegment>();
        foreach (var o in report.Objects)
            foreach (var path in o.PathsStageMm)
                for (int i = 1; i < path.Count; i++)
                {
                    var a = path[i - 1]; var b = path[i];
                    if (a == b) continue;
                    segments.Add(new BufferedSegment(Points(a), Points(b), preset.GateCount, preset.StageSpeedMmPerS));
                }
        return segments;
    }

    /// <summary>像素模式：沿路径按间距布驻留点（起点必布，其后每累计 PixelSpacingMm 布一点）；每点一段（From=To）。
    /// 驻留段的速度字段为预设台速占位：驻留时长由门宽决定，该字段不代表驻留期间运动。</summary>
    private static List<BufferedSegment> PixelSegments(PreparationReport report, ProcessingPreset preset)
    {
        var segments = new List<BufferedSegment>();
        foreach (var o in report.Objects)
            foreach (var path in o.PathsStageMm)
            {
                segments.Add(new BufferedSegment(Points(path[0]), Points(path[0]), preset.GateCount, preset.StageSpeedMmPerS));
                double spacing = preset.PixelSpacingMm, carried = spacing, travelled = 0;
                for (int i = 1; i < path.Count; i++)
                {
                    var a = path[i - 1]; var b = path[i];
                    double length = Length(a, b);
                    if (length > 0)
                        while (carried <= travelled + length + 1e-12)
                        {
                            double t = (carried - travelled) / length;
                            var p = new DesignPoint3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
                            segments.Add(new BufferedSegment(Points(p), Points(p), preset.GateCount, preset.StageSpeedMmPerS));
                            carried += spacing;
                        }
                    travelled += length;
                }
            }
        return segments;
    }

    private static double Length(DesignPoint3 a, DesignPoint3 b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    private static IReadOnlyDictionary<string, double> Points(DesignPoint3 p)
        => new Dictionary<string, double> { ["X"] = p.X, ["Y"] = p.Y, ["Z"] = p.Z };
}
