using AMP.Domain.Data;
using AMP.Domain.Instrument;

namespace AMP.Domain.Rules;

/// <summary>
/// SVD 修正结果：修正矩阵（ChannelCount × ChannelCount，按行主序铺平）与残差。
/// </summary>
public sealed record CorrectionResult(
    int ChannelCount,
    IReadOnlyList<double> Matrix,
    double ResidualRmse,
    string Message);

/// <summary>计量判断等级。</summary>
public enum MetrologyLevel
{
    /// <summary>一级。</summary>
    ClassOne,

    /// <summary>二级。</summary>
    ClassTwo,

    /// <summary>不良。</summary>
    Failed,
}

public static class MetrologyLevelExtensions
{
    public static string ToDisplayName(this MetrologyLevel level) => level switch
    {
        MetrologyLevel.ClassOne => "一级",
        MetrologyLevel.ClassTwo => "二级",
        _ => "不良",
    };
}

/// <summary>单样品计量判断结果（ΔL/Δa/Δb/ΔE 与等级）。</summary>
public sealed record MetrologyJudgement(
    int SampleIndex,
    double DeltaL,
    double Deltaa,
    double Deltab,
    double DeltaE,
    MetrologyLevel Level);

/// <summary>重复性测试结果。</summary>
public sealed record RepeatabilityResult(
    int SampleCount,
    double MeanDeltaE,
    double StdDevDeltaE,
    double MaxDeltaE,
    string Message);

/// <summary>
/// SVD 修正规则：由砖块实测值与标准值计算修正矩阵。
/// 骨架实现用「正规方程 + 岭正则」的最小二乘，真实项目替换为 MathNet 的 SVD 分解实现。
/// </summary>
public interface ISvdCorrectionRule
{
    CorrectionResult Calculate(IReadOnlyList<MeasureData> measured, IReadOnlyList<StandardRow> standards);
}

/// <summary>
/// 计量判断规则：实测值与标准参考值对比，计算 ΔLab/ΔE 并判级。
/// </summary>
public interface IMetrologyJudgeRule
{
    MetrologyJudgement Judge(int sampleIndex, MeasureData measured, StandardRow standard);
}

/// <summary>
/// 重复性规则：对同一位置的多次测量计算 ΔE 的均值/标准差/最大值。
/// </summary>
public interface IRepeatabilityRule
{
    RepeatabilityResult Calculate(IReadOnlyList<MeasureData> measured, StandardRow target);
}
