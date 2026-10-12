using AMP.Domain.Instrument;
using AMP.Domain.Motion;

namespace AMP.Domain.Data;

/// <summary>
/// 数据库标准数据行。模拟仪器按数据集名称（SetName）取行作为测量值来源。
/// </summary>
public sealed record StandardRow(string SetName, int RowIndex, IReadOnlyList<float> Values, LabValue? Lab = null);

/// <summary>模拟测量结果落库记录。</summary>
public sealed record MeasureResultRecord(
    string SetName,
    int RowIndex,
    string Source,
    IReadOnlyList<float> Values,
    DateTimeOffset Timestamp);

/// <summary>
/// 运动轴状态存储：数据库模拟运动控制器把轴位置持久化到这里，重启可恢复。
/// </summary>
public interface IMotionStateStore
{
    Task<AxisPosition> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(AxisPosition position, CancellationToken ct = default);
}

/// <summary>
/// 标准数据存储：数据库模拟仪器的测量数据来源（SVD砖块 / 计量标准 / 白板）。
/// </summary>
public interface IStandardDataStore
{
    /// <summary>加载标准数据行；<paramref name="setName"/> 为空时返回全部。</summary>
    Task<IReadOnlyList<StandardRow>> LoadAsync(string? setName = null, CancellationToken ct = default);
}

/// <summary>
/// 测量结果存储：模拟测量结果写入数据库，便于追溯。
/// </summary>
public interface IMeasureResultStore
{
    Task SaveAsync(MeasureResultRecord record, CancellationToken ct = default);
}
