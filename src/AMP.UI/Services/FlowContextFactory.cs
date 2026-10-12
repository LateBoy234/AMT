using AMP.Domain.Flows;
using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.UI.Services;

/// <summary>
/// 流程上下文工厂：每次运行开始时按当前 DI 单例设备创建 <see cref="FlowContext"/>，
/// 保证编排器/流程/状态机共用同一套运动控制器与仪器实例。
/// </summary>
public sealed class FlowContextFactory
{
    private readonly IMotionController _motion;
    private readonly IInstrument _instrument;
    private readonly FlowRunOptions _options;
    private readonly IAppLogger _logger;

    public FlowContextFactory(IMotionController motion, IInstrument instrument, FlowRunOptions options, IAppLogger logger)
    {
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
        _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FlowContext Create() => new(_motion, _instrument, _options, _logger);
}
