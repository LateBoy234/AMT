using AMP.Domain.Data;
using AMP.Domain.Instrument;

namespace AMP.Domain.Rules;

/// <summary>
/// 重复性规则：同一位置多次测量相对白板目标计算 ΔE，输出均值、标准差（样本标准差）与最大值。
/// </summary>
public sealed class RepeatabilityRule : IRepeatabilityRule
{
    public RepeatabilityResult Calculate(IReadOnlyList<MeasureData> measured, StandardRow target)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(target);

        if (measured.Count < 2)
        {
            throw new InvalidOperationException($"重复性测试至少需要 2 次测量，当前 {measured.Count} 次。");
        }

        var targetLab = target.Lab
            ?? (target.Values.Count >= 3 ? new LabValue(target.Values[0], target.Values[1], target.Values[2]) : null);
        if (targetLab is null)
        {
            throw new InvalidOperationException("重复性目标（白板）缺少 Lab 数据。");
        }

        var deltas = new List<double>(measured.Count);
        foreach (var sample in measured)
        {
            var lab = sample.Lab
                ?? (sample.Values.Count >= 3 ? new LabValue(sample.Values[0], sample.Values[1], sample.Values[2]) : null);
            if (lab is null)
            {
                continue;
            }

            var dL = lab.L - targetLab.L;
            var da = lab.a - targetLab.a;
            var db = lab.b - targetLab.b;
            deltas.Add(Math.Sqrt(dL * dL + da * da + db * db));
        }

        if (deltas.Count < 2)
        {
            throw new InvalidOperationException("重复性测试有效测量不足 2 次。");
        }

        var mean = deltas.Average();
        var variance = deltas.Sum(d => (d - mean) * (d - mean)) / (deltas.Count - 1);
        var stdDev = Math.Sqrt(variance);
        var max = deltas.Max();

        return new RepeatabilityResult(
            deltas.Count,
            mean,
            stdDev,
            max,
            $"重复性测试完成：{deltas.Count} 次测量，ΔE 均值 {mean:F3}，标准差 {stdDev:F3}，最大值 {max:F3}。");
    }
}
