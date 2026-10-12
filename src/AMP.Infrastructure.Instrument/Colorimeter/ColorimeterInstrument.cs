using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.Infrastructure.Instrument.Colorimeter;

/// <summary>
/// 色差计适配器（真实协议骨架）：演示「同一接口、不同型号仪器」的第二实现。
/// </summary>
public sealed class ColorimeterInstrument : IInstrument
{
    private readonly IAppLogger _logger;
    private bool _connected;

    public ColorimeterInstrument(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string VendorName => "Colorimeter";

    public string ModelName => "COLORIMETER-SERIES";

    public bool IsConnected => _connected;

    public Task<OperationResult> ConnectAsync(CancellationToken ct = default)
    {
        // TODO 真实接入：蓝牙/串口协议。
        _logger.Warn("色差计：未检测到设备（真实协议未接入），请切换 Simulation 模式。");
        return Task.FromResult(OperationResult.Fail("未检测到色差计（协议骨架未接入真机）；当前请使用数据库模拟（Simulation）模式。"));
    }

    public Task<OperationResult> DisconnectAsync(CancellationToken ct = default)
    {
        _connected = false;
        return Task.FromResult(OperationResult.Ok("已断开。"));
    }

    public Task<OperationResult> CalibrateAsync(CalibrationKind kind, CancellationToken ct = default)
        => Task.FromResult(OperationResult.Fail("色差计未连接。"));

    public Task<MeasureData> MeasureAsync(string? standardSet = null, CancellationToken ct = default)
        => throw new InvalidOperationException("色差计未连接（协议骨架未接入真机），请使用 Simulation 模式。");
}
