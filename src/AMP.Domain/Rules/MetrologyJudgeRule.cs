using AMP.Domain.Data;
using AMP.Domain.Instrument;

namespace AMP.Domain.Rules;

/// <summary>
/// 计量判断规则：实测与标准参考对比，计算 ΔL/Δa/Δb/ΔE（Lab 欧氏距离）并按阈值判级。
/// <para>骨架实现把「标准数据集」中的行当作 Lab 参考值；真实项目由光谱经 D65/CIE 观察者换算。</para>
/// </summary>
public sealed class MetrologyJudgeRule : IMetrologyJudgeRule
{
    private readonly double _grade1DeltaE;
    private readonly double _grade2DeltaE;

    public MetrologyJudgeRule(double grade1DeltaE, double grade2DeltaE)
    {
        _grade1DeltaE = grade1DeltaE;
        _grade2DeltaE = grade2DeltaE;
    }

    public MetrologyJudgement Judge(int sampleIndex, MeasureData measured, StandardRow standard)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(standard);

        var measuredLab = ResolveLab(measured.Values, measured.Lab);
        var standardLab = ResolveLab(standard.Values, standard.Lab);
        if (measuredLab is null || standardLab is null)
        {
            throw new InvalidOperationException($"样品 {sampleIndex} 缺少 Lab 数据，无法计量判断。");
        }

        var deltaL = measuredLab.L - standardLab.L;
        var deltaA = measuredLab.a - standardLab.a;
        var deltaB = measuredLab.b - standardLab.b;
        var deltaE = Math.Sqrt(deltaL * deltaL + deltaA * deltaA + deltaB * deltaB);

        var level = deltaE <= _grade1DeltaE
            ? MetrologyLevel.ClassOne
            : deltaE <= _grade2DeltaE
                ? MetrologyLevel.ClassTwo
                : MetrologyLevel.Failed;

        return new MetrologyJudgement(sampleIndex, deltaL, deltaA, deltaB, deltaE, level);
    }

    /// <summary>优先取显式 Lab，否则取前 3 个通道当作 (L, a, b)。</summary>
    private static LabValue? ResolveLab(IReadOnlyList<float> values, LabValue? lab)
        => lab ?? (values.Count >= 3 ? new LabValue(values[0], values[1], values[2]) : null);
}
