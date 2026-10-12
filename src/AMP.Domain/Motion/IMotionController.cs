namespace AMP.Domain.Motion;

/// <summary>
/// 运动操作结果。与硬件/模拟实现解耦的统一返回契约。
/// </summary>
public sealed record OperationResult(bool IsSuccess, string Message, int Code)
{
    public static OperationResult Ok(string? message = null) => new(true, message ?? "操作成功", 0);

    public static OperationResult Fail(string message, int code = 1) => new(false, message, code);
}

/// <summary>
/// 三轴运动控制器抽象：不同厂家的 C++ 动态库（或数据库模拟）都实现此接口。
/// <para>
/// 接口只描述业务语义动作（移动到 XY、Z 下压、夹爪释放/夹紧、Z 回退），
/// 不暴露任何厂商 API 细节；P/Invoke 与协议差异全部收敛在基础设施层的具体实现里。
/// </para>
/// </summary>
public interface IMotionController
{
    /// <summary>厂商/模式名称（如 VendorA / VendorB / Simulation），用于界面展示与日志。</summary>
    string VendorName { get; }

    /// <summary>是否已建立有效连接。</summary>
    bool IsConnected { get; }

    /// <summary>当前三轴位置。</summary>
    AxisPosition Position { get; }

    /// <summary>轴位置变化事件（模拟器在动画过程中触发，UI 据此刷新坐标）。</summary>
    event EventHandler<AxisPositionChangedEventArgs>? PositionChanged;

    /// <summary>建立连接（厂商 DLL 或数据库模拟）。</summary>
    Task<OperationResult> ConnectAsync(CancellationToken ct = default);

    /// <summary>断开连接并释放底层句柄。</summary>
    Task<OperationResult> DisconnectAsync(CancellationToken ct = default);

    /// <summary>回零（X/Y/Z 依次回原点）。</summary>
    Task<OperationResult> HomeAsync(CancellationToken ct = default);

    /// <summary>XY 平面移动到绝对位置。</summary>
    Task<OperationResult> MoveXYAsync(double x, double y, CancellationToken ct = default);

    /// <summary>Z 轴下压到绝对位置。</summary>
    Task<OperationResult> PressZAsync(double z, CancellationToken ct = default);

    /// <summary>夹爪释放。</summary>
    Task<OperationResult> ReleaseGripperAsync(CancellationToken ct = default);

    /// <summary>夹爪夹紧。</summary>
    Task<OperationResult> ClampGripperAsync(CancellationToken ct = default);

    /// <summary>Z 轴回退相对距离。</summary>
    Task<OperationResult> RetractZAsync(double distance, CancellationToken ct = default);

    /// <summary>急停/取消当前运动。</summary>
    Task<OperationResult> StopAsync(CancellationToken ct = default);
}
