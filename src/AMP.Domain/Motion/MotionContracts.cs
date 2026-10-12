namespace AMP.Domain.Motion;

/// <summary>
/// 运动控制状态。对应需求中的固定状态序列：
/// 空闲 → XY移动 → XY到位 → Z轴下压 → 夹爪释放 → 测量 → 夹紧 → Z轴回退 → 完成。
/// 状态行为与合法迁移由应用层 <c>MotionStateMachine</c> 定义，本枚举只是对外状态快照。
/// </summary>
public enum MotionState
{
    /// <summary>空闲。</summary>
    Idle,

    /// <summary>XY 移动中。</summary>
    XYMoving,

    /// <summary>XY 到位。</summary>
    XYArrived,

    /// <summary>Z 轴下压。</summary>
    ZPressing,

    /// <summary>夹爪释放。</summary>
    GripperReleasing,

    /// <summary>测量。</summary>
    Measuring,

    /// <summary>夹紧。</summary>
    GripperClamping,

    /// <summary>Z 轴回退。</summary>
    ZRetracting,

    /// <summary>完成。</summary>
    Completed,
}

public static class MotionStateExtensions
{
    public static string ToDisplayName(this MotionState state) => state switch
    {
        MotionState.Idle => "空闲",
        MotionState.XYMoving => "XY移动",
        MotionState.XYArrived => "XY到位",
        MotionState.ZPressing => "Z轴下压",
        MotionState.GripperReleasing => "夹爪释放",
        MotionState.Measuring => "测量",
        MotionState.GripperClamping => "夹紧",
        MotionState.ZRetracting => "Z轴回退",
        MotionState.Completed => "完成",
        _ => state.ToString(),
    };
}

/// <summary>运动状态变化事件参数。</summary>
public sealed class MotionStateChangedEventArgs : EventArgs
{
    public MotionStateChangedEventArgs(MotionState state, string hint)
    {
        State = state;
        Hint = hint;
    }

    /// <summary>新状态。</summary>
    public MotionState State { get; }

    /// <summary>状态说明（例如当前点位与动作描述）。</summary>
    public string Hint { get; }
}

/// <summary>三轴当前位置。</summary>
public sealed record AxisPosition(double X, double Y, double Z, DateTimeOffset UpdatedAt)
{
    public static AxisPosition Origin(DateTimeOffset? at = null) => new(0, 0, 0, at ?? DateTimeOffset.Now);
}

/// <summary>轴位置变化事件参数（供 UI 实时刷新坐标显示）。</summary>
public sealed class AxisPositionChangedEventArgs : EventArgs
{
    public AxisPositionChangedEventArgs(AxisPosition position) => Position = position;

    public AxisPosition Position { get; }
}
