using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agamemnon.DeviceControl;
using Agamemnon.Drawing;
using Agamemnon.ProcessWorkflow;

// Function/Agamemnon.ProcessWorkflow 工艺流程包的无硬件检查：包隔离、预设管理、曝光换算、
// 设计→加工准备（行程/新鲜度语义）、作业编排（前置校验/段生成/干跑/停止如实）。全部仿真设备驱动。
internal static class ProcessWorkflowPackageChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        PackageIsolation(check);
        PresetChecks(check);
        ExposureChecks(check);
        PreparationChecks(check);
        OrchestrationChecks(check);
    }

    private static void PackageIsolation(Action<bool, string> check)
    {
        var assembly = typeof(JobOrchestrator).Assembly;
        check(assembly.GetName().Name == "Agamemnon.ProcessWorkflow", "workflow package assembly is Agamemnon.ProcessWorkflow");
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        check(refs.Contains("Agamemnon.Drawing") && refs.Contains("Agamemnon.DeviceControl")
            && !refs.Any(n => n != null && n.StartsWith("Agamemnon.") && n != "Agamemnon.Drawing" && n != "Agamemnon.DeviceControl"),
            "workflow package composes exactly Drawing and DeviceControl");
        check(typeof(IPresetCatalog).IsAssignableFrom(typeof(PresetCatalog)) && typeof(IPreparationService).IsAssignableFrom(typeof(PreparationService))
            && typeof(IJobOrchestrator).IsAssignableFrom(typeof(JobOrchestrator)), "workflow implementations satisfy their interfaces");
    }

    private static void PresetChecks(Action<bool, string> check)
    {
        var catalog = new PresetCatalog();
        long revision = catalog.Revision;
        var preset = catalog.Add(new ProcessingPreset { Name = "矢量-A" });
        check(catalog.Presets.Count == 1 && catalog.Revision == revision + 1 && preset.Name == "矢量-A", "add stores validated preset and bumps revision");
        Rejects<ArgumentException>(() => catalog.Add(new ProcessingPreset { Name = "矢量-A" }), check, "duplicate preset name rejected");
        Rejects<ArgumentException>(() => ProcessingPreset.Validate(new ProcessingPreset { Name = " " }), check, "blank preset name rejected");
        Rejects<ArgumentException>(() => ProcessingPreset.Validate(new ProcessingPreset { Name = "x", StageSpeedMmPerS = 0 }), check, "non-positive stage speed rejected");
        Rejects<ArgumentException>(() => ProcessingPreset.Validate(new ProcessingPreset { Name = "x", GateCount = -1 }), check, "negative gate count rejected");
        Rejects<ArgumentException>(() => ProcessingPreset.Validate(new ProcessingPreset { Name = "x", Mode = ProcessingMode.Pixel, GateCount = 8 }), check, "pixel gate not multiple of sixteen rejected");
        Rejects<ArgumentException>(() => ProcessingPreset.Validate(new ProcessingPreset { Name = "x", Mode = ProcessingMode.Pixel, PixelSpacingMm = 0 }), check, "zero pixel spacing rejected");
        var copied = catalog.Copy(preset.Id, "矢量-B");
        check(copied.Id != preset.Id && copied.Name == "矢量-B" && catalog.Presets.Count == 2, "copy clones with new identity");
        catalog.Rename(copied.Id, "矢量-C");
        Rejects<ArgumentException>(() => catalog.Rename(copied.Id, "矢量-A"), check, "rename onto existing name rejected");
        catalog.Select(copied.Id);
        check(catalog.SelectedId == copied.Id, "selection persists");
        catalog.Update(copied with { StageSpeedMmPerS = 25 });
        check(catalog.Presets.Single(p => p.Id == copied.Id).StageSpeedMmPerS == 25, "update replaces preset fields");
        catalog.Remove(copied.Id);
        check(catalog.SelectedId == null && catalog.Presets.Count == 1, "removing the selected preset clears selection");
    }

    private static void ExposureChecks(Action<bool, string> check)
    {
        check(ExposureMath.Exposure(16) == TimeSpan.FromMilliseconds(1), "sixteen gates equal exactly one millisecond");
        check(ExposureMath.Exposure(0) == TimeSpan.Zero && ExposureMath.Exposure(1) == TimeSpan.FromTicks(625), "gate width is 62.5 microseconds exactly");
        Rejects<ArgumentOutOfRangeException>(() => ExposureMath.Exposure(-1), check, "negative exposure rejected");
        check(ExposureMath.TotalExposure(5, 16) == TimeSpan.FromMilliseconds(5), "total exposure multiplies segments by gates");
        check(ExposureMath.IsValidPixelGateCount(16) && !ExposureMath.IsValidPixelGateCount(8)
            && !ExposureMath.IsValidPixelGateCount(0) && !ExposureMath.IsValidPixelGateCount(-16), "pixel gate rule is positive multiples of sixteen");
    }

    private static void PreparationChecks(Action<bool, string> check)
    {
        var preparation = new PreparationService();
        var doc = SampleDocument();
        using var stage = new SimulatedStage();
        check(preparation.Prepare(new DesignDocument(), stage).Status == PreparationStatus.EmptyDesign, "empty design reports EmptyDesign");
        var disabledOnly = new DesignDocument();
        disabledOnly.Add(new DesignObject { Kind = DesignObjectKind.Line, Name = "停用", Enabled = false, Points = Line(-2, 2) });
        check(preparation.Prepare(disabledOnly, stage).Status == PreparationStatus.EmptyDesign, "disabled-only design reports EmptyDesign");

        var unverified = preparation.Prepare(doc, stage);
        check(unverified.Status == PreparationStatus.Unverified && unverified.StageObservedAt == null && unverified.Objects.Count == 2
            && unverified.Warnings.Any(w => w.Contains("未连接")), "disconnected stage reports Unverified with math preview");

        stage.Connect();
        var within = preparation.Prepare(doc, stage);
        check(within.Status == PreparationStatus.WithinTravel && within.StageObservedAt.HasValue && within.Bounds.HasValue
            && within.Objects.All(o => o.Violations.Count == 0), "connected stage verifies travel for in-range design");
        check(within.IsCurrent(DateTimeOffset.UtcNow.AddSeconds(1), doc.Snapshot.Revision), "fresh report is current within two seconds");
        doc.Move(doc.Snapshot.Objects[0].Id, 0.001, 0);
        check(!within.IsCurrent(DateTimeOffset.UtcNow, doc.Snapshot.Revision), "stale revision invalidates the report");
        doc.Undo();

        var mapped = new DesignDocument();
        mapped.UpdateSettings(new DesignProjectSettings { Mapping = new DesignCoordinateMapping { StageOriginMm = new(3, 0, 0) } });
        mapped.Add(new DesignObject { Kind = DesignObjectKind.Line, Name = "短线", Points = Line(0, 2) });
        var mappedReport = preparation.Prepare(mapped, stage);
        check(Near(mappedReport.Bounds.Value.Minimum.X, 3) && Near(mappedReport.Bounds.Value.Maximum.X, 5), "mapping transforms design to stage coordinates");

        var outside = new DesignDocument();
        outside.Add(new DesignObject { Kind = DesignObjectKind.Rectangle, Name = "出界", Position = new(20, 0, 0) });
        var outsideReport = preparation.Prepare(outside, stage);
        check(outsideReport.Status == PreparationStatus.OutsideTravel
            && outsideReport.Objects.Single().Violations.Any(v => v.Contains("轴 X")), "out-of-travel object reports the violating axis");

        var stale = preparation.Prepare(doc, stage, DateTimeOffset.UtcNow.AddSeconds(5));
        check(stale.Status == PreparationStatus.Unverified && stale.Warnings.Any(w => w.Contains("超过 2 秒")), "stale stage snapshot reports Unverified");
    }

    private static void OrchestrationChecks(Action<bool, string> check)
    {
        var orchestrator = new JobOrchestrator();
        using var stage = new SimulatedStage();
        using var processing = new SimulatedBufferedProcessing();
        stage.Connect(); processing.Connect();
        var vector = new ProcessingPreset { Name = "矢量", Mode = ProcessingMode.Vector, GateCount = 16, StageSpeedMmPerS = 10 };

        var empty = orchestrator.RunAsync(new DesignDocument(), vector, stage, processing).GetAwaiter().GetResult();
        check(!empty.Started && empty.Error.Contains("空"), "empty design refuses to start");
        var unverified = orchestrator.RunAsync(SampleDocument(), vector, new SimulatedStage(), processing).GetAwaiter().GetResult();
        check(!unverified.Started && unverified.Error.Contains("未验证"), "unverified stage refuses to start");
        var offline = orchestrator.RunAsync(SampleDocument(), vector, stage, new SimulatedBufferedProcessing()).GetAwaiter().GetResult();
        check(!offline.Started && offline.Error.Contains("控制器"), "disconnected controller refuses to start");

        var outsideDoc = new DesignDocument();
        outsideDoc.Add(new DesignObject { Kind = DesignObjectKind.Rectangle, Name = "出界", Position = new(20, 0, 0) });
        var outside = orchestrator.RunAsync(outsideDoc, vector, stage, processing).GetAwaiter().GetResult();
        check(!outside.Started && outside.Error.Contains("行程") && !processing.Status.Running, "out-of-travel design refuses to start without touching the controller");

        var cancelled = orchestrator.RunAsync(SampleDocument(), vector, stage, processing, null, new CancellationToken(true)).GetAwaiter().GetResult();
        check(!cancelled.Started && cancelled.Error.Contains("取消"), "pre-cancelled job refuses to start");

        var done = orchestrator.RunAsync(SampleDocument(), vector, stage, processing).GetAwaiter().GetResult();
        check(done.Started && done.Completed && done.TotalSegments == 5 && done.CompletedSegments == 5
            && done.PlannedExposure == TimeSpan.FromMilliseconds(5), "vector job runs five segments with exact planned exposure");

        var pixelDoc = new DesignDocument();
        pixelDoc.Add(new DesignObject { Kind = DesignObjectKind.Line, Name = "像素线", Points = Line(0, 1) });
        var pixel = new ProcessingPreset { Name = "像素", Mode = ProcessingMode.Pixel, GateCount = 16, PixelSpacingMm = 0.25 };
        var pixels = orchestrator.RunAsync(pixelDoc, pixel, stage, processing).GetAwaiter().GetResult();
        check(pixels.Started && pixels.Completed && pixels.TotalSegments == 5, "pixel walk places dwell points every spacing along the path");
        Rejects<ArgumentException>(() => orchestrator.RunAsync(pixelDoc, pixel with { GateCount = 8 }, stage, processing).GetAwaiter().GetResult(), check, "invalid pixel gate refuses before any device action");

        var longDoc = new DesignDocument();
        longDoc.Add(new DesignObject { Kind = DesignObjectKind.Line, Name = "长线", Points = Line(0, 8) });
        var longPixel = pixel with { PixelSpacingMm = 0.005 };
        var dryTask = Task.Run(() => orchestrator.RunAsync(longDoc, longPixel, stage, processing).GetAwaiter().GetResult());
        WaitFor(check, () => processing.Status.Running, "long pixel job enters running state");
        check(processing.Status.Running && !processing.Status.LaserRequested, "default run stays dry without laser request");
        check(processing.Stop(), "stop request accepted during run");
        var dry = dryTask.GetAwaiter().GetResult();
        check(dry.Started && dry.Stopped && !dry.Completed && dry.CompletedSegments < dry.TotalSegments,
            "stopped job reports honest partial completion");

        var laserTask = Task.Run(() => orchestrator.RunAsync(pixelDoc, pixel, stage, processing, new JobOptions(AllowLaserOutput: true)).GetAwaiter().GetResult());
        WaitFor(check, () => processing.Status.Running, "laser-flagged job enters running state");
        check(processing.Status.LaserRequested, "AllowLaserOutput=true requests laser in the program");
        var laser = laserTask.GetAwaiter().GetResult();
        check(laser.Completed, "laser-flagged short job completes");
    }

    private static void WaitFor(Action<bool, string> check, Func<bool> condition, string name)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(5);
        check(condition(), name);
    }

    private static DesignDocument SampleDocument()
    {
        var doc = new DesignDocument();
        doc.Create(DesignObjectKind.Rectangle);
        doc.Add(new DesignObject { Kind = DesignObjectKind.Line, Name = "线", Points = Line(-2, 2) });
        return doc;
    }
    private static IReadOnlyList<DesignPoint3> Line(double from, double to)
        => Array.AsReadOnly(new[] { new DesignPoint3(from, 0, 0), new DesignPoint3(to, 0, 0) });
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;

    private static void Rejects<TException>(Action action, Action<bool, string> check, string name) where TException : Exception
    {
        bool threw = false;
        try { action(); } catch (TException) { threw = true; } catch (Exception) { threw = false; }
        check(threw, name);
    }
}
