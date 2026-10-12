namespace AMP.Domain.Instrument;

/// <summary>仪器校正类型。</summary>
public enum CalibrationKind
{
    /// <summary>白校正。</summary>
    White,

    /// <summary>黑校正。</summary>
    Black,
}

/// <summary>Lab 颜色值（计量判断/重复性计算使用）。</summary>
public sealed record LabValue(double L, double a, double b);

/// <summary>
/// 一次测量结果。Values 为多通道测量值（真实项目为光谱 31/36 通道；骨架示例中
 /// SVD 砖块集为 31 通道反射率，计量标准集前 3 通道为 Lab 三元组）。
/// </summary>
public sealed record MeasureData(
    IReadOnlyList<float> Values,
    string Source,
    DateTimeOffset Timestamp,
    LabValue? Lab = null);
