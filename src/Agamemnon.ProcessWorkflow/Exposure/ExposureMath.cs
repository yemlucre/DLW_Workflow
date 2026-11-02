using System;

namespace Agamemnon.ProcessWorkflow;

/// <summary>
/// 曝光换算（纯函数）。门宽单位 62.5 µs/个：1 门宽 = 625 ticks（精确），16 门宽 = 1 ms 整。
/// 像素模式门宽需为 16 的倍数（整毫秒编码）是本层的产品策略。
/// </summary>
public static class ExposureMath
{
    public const double GateWidthMicroseconds = 62.5;
    private const long TicksPerGate = 625;   // 1 tick = 0.1 µs → 62.5 µs = 625 ticks，无精度损失

    /// <summary>门宽数量 → 曝光时长（精确）。</summary>
    public static TimeSpan Exposure(int gateCount)
    {
        if (gateCount < 0) throw new ArgumentOutOfRangeException(nameof(gateCount), "门宽数量不能为负。");
        return TimeSpan.FromTicks((long)gateCount * TicksPerGate);
    }

    /// <summary>段数 × 每段门宽 → 计划曝光总时长。</summary>
    public static TimeSpan TotalExposure(int segments, int gateCount)
    {
        if (segments < 0) throw new ArgumentOutOfRangeException(nameof(segments), "段数不能为负。");
        return TimeSpan.FromTicks((long)segments * gateCount * TicksPerGate);
    }

    /// <summary>像素模式合法门宽：正数且为 16 的倍数。</summary>
    public static bool IsValidPixelGateCount(int gateCount) => gateCount > 0 && gateCount % 16 == 0;
}
