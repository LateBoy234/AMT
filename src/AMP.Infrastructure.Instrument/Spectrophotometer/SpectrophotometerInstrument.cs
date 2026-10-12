using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.Infrastructure.Instrument.Spectrophotometer;

/// <summary>
/// 分光光度计适配器（真实协议骨架）。
/// <para>
/// 接入真实仪器时在此实现串口/USB/网络协议（参考 ThreeNH.AMT 的
 /// SerialPortInstrumentConnectionService + 各仪器驱动）。骨架阶段 ConnectAsync 返回明确失败信息，
 /// 可切回 <c>Simulation</c> 模式继续开发流程。
/// </para>
/// </summary>
public sealed class SpectrophotometerInstrument : IInstrument
{
    private readonly IAppLogger _logger;
    private bool _connected;

    public SpectrophotometerInstrument(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string VendorName => "Spectrophotometer";

    public string ModelName => "SPECTRO-SERIES";

    public bool IsConnected => _connected;

    public Task<OperationResult> ConnectAsync(CancellationToken ct = default)
    {
        // TODO 真实接入：枚举串口/网络设备并握手。
        _logger.Warn("分光光度计：未检测到设备（真实协议未接入），请切换 Simulation 模式。");
        return Task.FromResult(OperationResult.Fail("未检测到分光光度计（协议骨架未接入真机）；当前请使用数据库模拟（Simulation）模式。"));
    }

    public Task<OperationResult> DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        return Task.FromResult(OperationResult.Ok("已断开。"));
    }

    public Task<OperationResult> CalibrateAsync(CalibrationKind kind, CancellationToken ct = default)
        => NotConnectedResult();

    public Task<MeasureData> MeasureAsync(string? standardSet = null, CancellationToken ct = default)
        => throw new InvalidOperationException("分光光度计未连接（协议骨架未接入真机），请使用 Simulation 模式。");

    private Task<OperationResult> NotConnectedResult()
        => Task.FromResult(OperationResult.Fail("分光光度计未连接。"));
}
