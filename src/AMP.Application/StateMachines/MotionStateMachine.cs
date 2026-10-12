using AMP.Domain.Motion;

namespace AMP.Application.StateMachines;

/// <summary>
/// 单点测量动作集：状态机在进入每个状态时调用对应的动作委托。
/// 动作由 <c>PointMeasurementRunner</c> 提供（接线运动控制器与仪器），状态机本身不依赖具体设备。
/// </summary>
public sealed class PointActions
{
    public required Func<CancellationToken, Task> MoveXY { get; init; }

    public required Func<CancellationToken, Task> OnArrived { get; init; }

    public required Func<CancellationToken, Task> PressZ { get; init; }

    public required Func<CancellationToken, Task> ReleaseGripper { get; init; }

    public required Func<CancellationToken, Task> Measure { get; init; }

    public required Func<CancellationToken, Task> ClampGripper { get; init; }

    public required Func<CancellationToken, Task> RetractZ { get; init; }

    public required Func<CancellationToken, Task> Complete { get; init; }
}

/// <summary>
/// 运动控制状态机（应用层持有，UI 订阅 <see cref="StateChanged"/> 刷新状态显示）。
/// <para>
/// 状态序列与合法迁移固定：空闲 → XY移动 → XY到位 → Z轴下压 → 夹爪释放 → 测量 → 夹紧 → Z轴回退 → 完成。
/// 每个状态只负责自己的动作与下一状态，非法迁移立即抛错，避免流程状态散落在编排器中。
/// </para>
/// <para>注意：这是「硬件级」状态机（秒级）；与业务流程编排（分钟级）分离，不要合并。</para>
/// </summary>
public sealed class MotionStateMachine
{
    private readonly IReadOnlyDictionary<MotionState, IMotionStateHandler> _states;

    public MotionStateMachine()
    {
        _states = new IMotionStateHandler[]
        {
            new IdleState(),
            new XYMovingState(),
            new XYArrivedState(),
            new ZPressingState(),
            new GripperReleasingState(),
            new MeasuringState(),
            new GripperClampingState(),
            new ZRetractingState(),
            new CompletedState(),
        }.ToDictionary(handler => handler.State);
    }

    /// <summary>当前状态（线程安全：状态只由运行中的状态机修改）。</summary>
    public MotionState CurrentState { get; private set; } = MotionState.Idle;

    /// <summary>状态变化事件（UI 订阅后刷新显示）。</summary>
    public event EventHandler<MotionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 执行一次完整单点序列。前置要求：当前状态为空闲（或上一轮结束的完成态，会自动复位）。
    /// </summary>
    public async Task RunPointSequenceAsync(PointActions actions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (CurrentState != MotionState.Idle)
        {
            if (CurrentState == MotionState.Completed)
            {
                TransitionTo(MotionState.Idle, "单点完成，复位到空闲。");
            }
            else
            {
                throw new InvalidOperationException(
                    $"状态机不在空闲/完成状态，无法开始新点位（当前 {CurrentState.ToDisplayName()}）。");
            }
        }

        MotionState? next = MotionState.XYMoving;
        while (next.HasValue)
        {
            ct.ThrowIfCancellationRequested();
            var handler = _states[next.Value];
            TransitionTo(next.Value, handler.GetHint(actions));
            next = await handler.ExecuteAsync(actions, ct).ConfigureAwait(false);
        }
    }

    /// <summary>复位到空闲（运行成功、取消或异常后）。</summary>
    public void Reset(string hint = "复位到空闲。")
    {
        if (CurrentState != MotionState.Idle)
        {
            TransitionTo(MotionState.Idle, hint);
        }
    }

    private void TransitionTo(MotionState next, string hint)
    {
        var current = _states[CurrentState];
        if (!current.CanTransitionTo(next))
        {
            throw new InvalidOperationException($"运动状态不能从 {CurrentState.ToDisplayName()} 转换到 {next.ToDisplayName()}。");
        }

        CurrentState = next;
        StateChanged?.Invoke(this, new MotionStateChangedEventArgs(next, hint));
    }
}

/// <summary>状态处理器：决定当前状态的动作与下一状态。</summary>
internal interface IMotionStateHandler
{
    MotionState State { get; }

    bool CanTransitionTo(MotionState next);

    string GetHint(PointActions actions);

    Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct);
}

internal abstract class MotionStateHandler : IMotionStateHandler
{
    private readonly IReadOnlySet<MotionState> _allowedNext;

    protected MotionStateHandler(MotionState state, params MotionState[] allowedNext)
    {
        State = state;
        _allowedNext = allowedNext.ToHashSet();
    }

    public MotionState State { get; }

    public bool CanTransitionTo(MotionState next) => _allowedNext.Contains(next);

    public abstract string GetHint(PointActions actions);

    public abstract Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct);
}

internal sealed class IdleState() : MotionStateHandler(MotionState.Idle, MotionState.XYMoving)
{
    public override string GetHint(PointActions actions) => "空闲。";

    public override Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
        => throw new InvalidOperationException("空闲状态不能执行动作。");
}

internal sealed class XYMovingState() : MotionStateHandler(MotionState.XYMoving, MotionState.XYArrived)
{
    public override string GetHint(PointActions actions) => "XY移动。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.MoveXY(ct).ConfigureAwait(false);
        return MotionState.XYArrived;
    }
}

internal sealed class XYArrivedState() : MotionStateHandler(MotionState.XYArrived, MotionState.ZPressing)
{
    public override string GetHint(PointActions actions) => "XY到位。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.OnArrived(ct).ConfigureAwait(false);
        return MotionState.ZPressing;
    }
}

internal sealed class ZPressingState() : MotionStateHandler(MotionState.ZPressing, MotionState.GripperReleasing)
{
    public override string GetHint(PointActions actions) => "Z轴下压。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.PressZ(ct).ConfigureAwait(false);
        return MotionState.GripperReleasing;
    }
}

internal sealed class GripperReleasingState() : MotionStateHandler(MotionState.GripperReleasing, MotionState.Measuring)
{
    public override string GetHint(PointActions actions) => "夹爪释放。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.ReleaseGripper(ct).ConfigureAwait(false);
        return MotionState.Measuring;
    }
}

internal sealed class MeasuringState() : MotionStateHandler(MotionState.Measuring, MotionState.GripperClamping)
{
    public override string GetHint(PointActions actions) => "测量。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.Measure(ct).ConfigureAwait(false);
        return MotionState.GripperClamping;
    }
}

internal sealed class GripperClampingState() : MotionStateHandler(MotionState.GripperClamping, MotionState.ZRetracting)
{
    public override string GetHint(PointActions actions) => "夹紧。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.ClampGripper(ct).ConfigureAwait(false);
        return MotionState.ZRetracting;
    }
}

internal sealed class ZRetractingState() : MotionStateHandler(MotionState.ZRetracting, MotionState.Completed)
{
    public override string GetHint(PointActions actions) => "Z轴回退。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.RetractZ(ct).ConfigureAwait(false);
        return MotionState.Completed;
    }
}

internal sealed class CompletedState() : MotionStateHandler(MotionState.Completed, MotionState.Idle)
{
    public override string GetHint(PointActions actions) => "完成。";

    public override async Task<MotionState?> ExecuteAsync(PointActions actions, CancellationToken ct)
    {
        await actions.Complete(ct).ConfigureAwait(false);
        return null; // 单点序列结束
    }
}
