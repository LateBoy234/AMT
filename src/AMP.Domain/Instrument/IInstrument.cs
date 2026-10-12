using AMP.Domain.Motion;

namespace AMP.Domain.Instrument;

/// <summary>
/// 测量仪器抽象：不同型号/厂家的仪器（分光光度计、色差计等）以及数据库模拟仪器都实现此接口。
/// <para>
/// <paramref name="standardSet"/> 是数据库模拟模式下的标准数据集名称（如「SVD砖块」「计量标准」「白板」），
 /// 真机实现忽略该参数。该参数放在接口上是为了让流程代码与数据来源完全解耦。
/// </para>
/// </summary>
public interface IInstrument
{
    /// <summary>厂商/模式名称（Spectrophotometer / Colorimeter / Simulation）。</summary>
    string VendorName { get; }

    /// <summary>型号名称（用于界面展示）。</summary>
    string ModelName { get; }

    /// <summary>是否已连接。</summary>
    bool IsConnected { get; }

    /// <summary>建立连接。</summary>
    Task<OperationResult> ConnectAsync(CancellationToken ct = default);

    /// <summary>断开连接。</summary>
    Task<OperationResult> DisconnectAsync(CancellationToken ct = default);

    /// <summary>执行白校正/黑校正。</summary>
    Task<OperationResult> CalibrateAsync(CalibrationKind kind, CancellationToken ct = default);

    /// <summary>
    /// 执行一次测量。
    /// </summary>
    /// <param name="standardSet">数据库模拟模式下使用的标准数据集名称；真机实现忽略。</param>
    Task<MeasureData> MeasureAsync(string? standardSet = null, CancellationToken ct = default);
}
