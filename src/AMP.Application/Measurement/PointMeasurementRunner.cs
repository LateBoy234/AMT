using AMP.Application.StateMachines;
using AMP.Domain.Flows;
using AMP.Domain.Instrument;

namespace AMP.Application.Measurement;

/// <summary>
/// 单点测量执行器：把 <see cref="FlowContext"/> 中的运动控制器 + 仪器接线到
 /// <see cref="MotionStateMachine"/> 的九个状态动作上，执行
 /// 「XY移动→到位→Z下压→夹爪释放→测量→夹紧→Z回退→完成」并返回测量数据。
 /// 设备来源统一取自流程上下文，避免与 DI 重复注入产生两套实例。
/// </summary>
public sealed class PointMeasurementRunner
{
    private readonly MotionStateMachine _stateMachine;

    public PointMeasurementRunner(MotionStateMachine stateMachine)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
    }

    /// <summary>
    /// 执行一个点位的完整动作序列。
    /// </summary>
    /// <param name="context">流程运行上下文（运动/仪器/选项/日志）。</param>
    /// <param name="point">点位（XY/Z 目标）。</param>
    /// <param name="standardSet">测量使用的标准数据集（数据库模拟模式生效）。</param>
    public async Task<MeasureData> RunPointAsync(
        FlowContext context,
        PointSpec point,
        string? standardSet,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(point);

        var options = context.Options;
        MeasureData? result = null;
        var actions = new PointActions
        {
            MoveXY = t => context.Motion.MoveXYAsync(point.X, point.Y, t),
            OnArrived = t => DelayAsync(options.StepDelayMilliseconds, t),
            PressZ = t => context.Motion.PressZAsync(point.Z, t),
            ReleaseGripper = t => context.Motion.ReleaseGripperAsync(t),
            Measure = async t =>
            {
                var data = await context.Instrument.MeasureAsync(standardSet, t).ConfigureAwait(false);
                result = data;
                context.Logger.Info($"点位 {point.Index}：测量完成（{data.Source}，{data.Values.Count} 通道）。");
            },
            ClampGripper = t => context.Motion.ClampGripperAsync(t),
            RetractZ = t => context.Motion.RetractZAsync(options.ZRetractDistance, t),
            Complete = t => DelayAsync(Math.Max(0, options.StepDelayMilliseconds / 2), t),
        };

        await _stateMachine.RunPointSequenceAsync(actions, ct).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"点位 {point.Index} 未产生测量数据。");
    }

    private static Task DelayAsync(int milliseconds, CancellationToken ct)
        => milliseconds > 0 ? Task.Delay(milliseconds, ct) : Task.CompletedTask;
}
