using System;

namespace Agamemnon.ProcessWorkflow;

/// <summary>加工模式：矢量（沿路径走段）或像素（按间距布点驻留）。对应现行"位移台/像素模式"。</summary>
public enum ProcessingMode { Vector, Pixel }

/// <summary>
/// 加工预设（不可变）。单位：台速 mm/s、像素间距 mm；门宽数量单位 62.5 µs/个。
/// 像素模式产品策略：门宽数量需为正且为 16 的倍数（SDK 整毫秒编码约束）——该策略归本层，校验在 Validate。
/// </summary>
public sealed record ProcessingPreset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; }
    public ProcessingMode Mode { get; init; } = ProcessingMode.Vector;
    public double StageSpeedMmPerS { get; init; } = 10;
    public int GateCount { get; init; } = 16;
    public double PixelSpacingMm { get; init; } = 0.01;

    /// <summary>结构校验；非法值抛 ArgumentException，绝不截断或替换。</summary>
    public static ProcessingPreset Validate(ProcessingPreset preset)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80) throw new ArgumentException("预设名称需为 1–80 字符。");
        if (!double.IsFinite(preset.StageSpeedMmPerS) || preset.StageSpeedMmPerS <= 0 || preset.StageSpeedMmPerS > 1000)
            throw new ArgumentException("台速需为大于 0 且不超过 1000 的 mm/s 值。");
        if (preset.GateCount < 0) throw new ArgumentException("门宽数量不能为负。");
        if (preset.Mode == ProcessingMode.Pixel)
        {
            if (!ExposureMath.IsValidPixelGateCount(preset.GateCount))
                throw new ArgumentException("像素模式门宽数量需为正且为 16 的倍数（62.5 µs/个，整毫秒编码）。");
            if (!double.IsFinite(preset.PixelSpacingMm) || preset.PixelSpacingMm <= 0 || preset.PixelSpacingMm > 1)
                throw new ArgumentException("像素间距需为大于 0 且不超过 1 的 mm 值。");
        }
        return preset;
    }
}
