using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.Domain.Flows;

/// <summary>
/// 流程步骤契约：SVD修正、计量判断、重复性测试各自实现一个步骤。
/// <para>
/// 可单独执行（流程链只有一项）、可任意组合（流程链按配置排序）、
 /// 组合时每步执行完成后触发 <see cref="Completed"/>，由导航服务自动切到下一个流程界面。
/// </para>
/// </summary>
public interface IFlowStep
{
    /// <summary>配置键（如 SvdCorrection / MetrologyJudge / RepeatabilityTest），用于组装流程链。</summary>
    string Key { get; }

    /// <summary>显示名称（如 SVD修正）。</summary>
    string Name { get; }

    /// <summary>步骤说明。</summary>
    string Description { get; }

    /// <summary>本步骤执行完成事件（组合流程据此自动跳转到下一步）。</summary>
    event EventHandler<FlowStepCompletedEventArgs>? Completed;

    /// <summary>执行本步骤。</summary>
    Task<FlowResult> ExecuteAsync(FlowContext context, CancellationToken ct = default);
}

/// <summary>流程步骤完成事件参数。</summary>
public sealed record FlowStepCompletedEventArgs(IFlowStep Step, FlowResult Result);

/// <summary>
/// 流程运行期共享上下文：一条流程链中的所有步骤共用同一个运动控制器、仪器、运行选项与日志。
/// </summary>
public sealed class FlowContext
{
    public FlowContext(IMotionController motion, IInstrument instrument, FlowRunOptions options, IAppLogger logger)
    {
        Motion = motion ?? throw new ArgumentNullException(nameof(motion));
        Instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>运动控制器（按配置注入真机厂商或数据库模拟）。</summary>
    public IMotionController Motion { get; }

    /// <summary>测量仪器（按配置注入真机仪器或数据库模拟）。</summary>
    public IInstrument Instrument { get; }

    /// <summary>流程运行选项（点位、次数、延时等）。</summary>
    public FlowRunOptions Options { get; }

    /// <summary>日志。</summary>
    public IAppLogger Logger { get; }
}

/// <summary>流程步骤执行结果（含供界面展示的摘要行）。</summary>
public sealed record FlowResult(bool IsSuccess, string Message, IReadOnlyList<string> Summary)
{
    public static FlowResult Ok(string message, params string[] summary) => new(true, message, summary);

    public static FlowResult Fail(string message) => new(false, message, Array.Empty<string>());
}

/// <summary>
/// 流程运行选项：三个流程各自的测量数量、点位步进、Z 行程与标准数据集名称。
/// </summary>
public sealed class FlowRunOptions
{
    /// <summary>SVD 修正砖块数。</summary>
    public int SvdBrickCount { get; set; } = 8;

    /// <summary>计量判断样品数。</summary>
    public int MetrologySampleCount { get; set; } = 5;

    /// <summary>重复性测试次数。</summary>
    public int RepeatCount { get; set; } = 10;

    /// <summary>Z 轴下压绝对位置。</summary>
    public double ZPressPosition { get; set; } = 10;

    /// <summary>Z 轴回退相对距离。</summary>
    public double ZRetractDistance { get; set; } = 15;

    /// <summary>单步模拟动作耗时（毫秒），让操作员看清流程。</summary>
    public int StepDelayMilliseconds { get; set; } = 120;

    /// <summary>测量点位 XY 步进。</summary>
    public double XStep { get; set; } = 10;

    /// <summary>测量点位 Y 步进。</summary>
    public double YStep { get; set; } = 10;

    /// <summary>计量判断一级阈值（ΔE）。</summary>
    public double Grade1DeltaE { get; set; } = 0.8;

    /// <summary>计量判断二级阈值（ΔE）。</summary>
    public double Grade2DeltaE { get; set; } = 2.0;

    /// <summary>SVD 修正使用的标准数据集。</summary>
    public string SvdStandardSet { get; set; } = "SVD砖块";

    /// <summary>计量判断使用的标准数据集。</summary>
    public string MetrologyStandardSet { get; set; } = "计量标准";

    /// <summary>重复性测试使用的标准数据集（白板）。</summary>
    public string RepeatStandardSet { get; set; } = "白板";
}

/// <summary>一个测量点位。</summary>
public sealed record PointSpec(int Index, double X, double Y, double Z, string? Remark = null);
