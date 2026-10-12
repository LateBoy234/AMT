using AMP.Domain.Data;
using AMP.Domain.Flows;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.Infrastructure.Motion.Simulation;

/// <summary>
/// 数据库模拟运动控制器：实现 <see cref="IMotionController"/> 全部接口，
 /// 轴位置持久化到 <see cref="IMotionStateStore"/>（重启可恢复），运动过程用分步延时模拟。
/// <para>配置 <c>Motion.Vendor = Simulation</c> 时启用，无硬件即可开发/演示整条测量流程。</para>
/// </summary>
public sealed class DatabaseSimulatedMotionController : IMotionController
{
    private const int AnimationSteps = 5;

    private readonly IMotionStateStore _stateStore;
    private readonly FlowRunOptions _options;
    private readonly IAppLogger _logger;
    private readonly object _sync = new();

    private CancellationTokenSource? _animationCts;
    private bool _connected;

    public DatabaseSimulatedMotionController(IMotionStateStore stateStore, FlowRunOptions options, IAppLogger logger)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string VendorName => "Simulation";

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _connected;
            }
        }
    }

    public AxisPosition Position { get; private set; } = AxisPosition.Origin();

    public event EventHandler<AxisPositionChangedEventArgs>? PositionChanged;

    public async Task<OperationResult> ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            var saved = await _stateStore.LoadAsync(ct).ConfigureAwait(false);
            lock (_sync)
            {
                _connected = true;
                Position = saved;
            }

            PositionChanged?.Invoke(this, new AxisPositionChangedEventArgs(Position));
            _logger.Info("数据库模拟运动平台已连接（轴位置已从数据库恢复）。");
            return OperationResult.Ok($"模拟运动平台已连接（X={Position.X:F1}, Y={Position.Y:F1}, Z={Position.Z:F1}）。");
        }
        catch (Exception ex)
        {
            _logger.Error("数据库模拟运动平台连接失败", ex);
            return OperationResult.Fail($"模拟运动平台连接失败：{ex.Message}");
        }
    }

    public async Task<OperationResult> DisconnectAsync(CancellationToken ct = default)
    {
        await CancelAnimationAsync().ConfigureAwait(false);
        lock (_sync)
        {
            _connected = false;
        }

        await PersistAsync().ConfigureAwait(false);
        _logger.Info("数据库模拟运动平台已断开。");
        return OperationResult.Ok("已断开。");
    }

    public async Task<OperationResult> HomeAsync(CancellationToken ct = default)
    {
        var gate = RequireConnected();
        if (gate is not null)
        {
            return gate;
        }

        return await AnimateToAsync(0, 0, 0, "回零", ct).ConfigureAwait(false);
    }

    public Task<OperationResult> MoveXYAsync(double x, double y, CancellationToken ct = default)
    {
        var gate = RequireConnected();
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        return AnimateToAsync(x, y, CurrentZ(), $"XY移动到({x:F1},{y:F1})", ct);
    }

    public Task<OperationResult> PressZAsync(double z, CancellationToken ct = default)
    {
        var gate = RequireConnected();
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        return AnimateToAsync(CurrentX(), CurrentY(), z, $"Z轴下压到{z:F1}", ct);
    }

    public Task<OperationResult> ReleaseGripperAsync(CancellationToken ct = default)
    {
        var gate = RequireConnected();
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        _logger.Info("数据库模拟：夹爪释放（无实际机构）。");
        return Task.FromResult(OperationResult.Ok("夹爪释放完成（模拟）。"));
    }

    public Task<OperationResult> ClampGripperAsync(CancellationToken ct = default)
    {
        var gate = RequireConnected();
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        _logger.Info("数据库模拟：夹爪夹紧（无实际机构）。");
        return Task.FromResult(OperationResult.Ok("夹爪夹紧完成（模拟）。"));
    }

    public Task<OperationResult> RetractZAsync(double distance, CancellationToken ct = default)
    {
        var gate = RequireConnected();
        if (gate is not null)
        {
            return Task.FromResult(gate);
        }

        var target = CurrentZ() + distance;
        return AnimateToAsync(CurrentX(), CurrentY(), target, $"Z轴回退{distance:F1}", ct);
    }

    public async Task<OperationResult> StopAsync(CancellationToken ct = default)
    {
        await CancelAnimationAsync().ConfigureAwait(false);
        return OperationResult.Ok("运动已停止。");
    }

    private Task<OperationResult> AnimateToAsync(double x, double y, double z, string action, CancellationToken ct)
    {
        var cts = CreateAnimationTokenSource();
        return AnimateCoreAsync(cts, x, y, z, action, ct);
    }

    private async Task<OperationResult> AnimateCoreAsync(
        CancellationTokenSource cts,
        double x,
        double y,
        double z,
        string action,
        CancellationToken ct)
    {
        try
        {
            AxisPosition start;
            lock (_sync)
            {
                start = Position;
            }

            var token = ct;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            token = linked.Token;

            var stepDelay = Math.Max(10, _options.StepDelayMilliseconds);
            for (var step = 1; step <= AnimationSteps; step++)
            {
                token.ThrowIfCancellationRequested();
                var ratio = (double)step / AnimationSteps;
                var next = new AxisPosition(
                    Interpolate(start.X, x, ratio),
                    Interpolate(start.Y, y, ratio),
                    Interpolate(start.Z, z, ratio),
                    DateTimeOffset.Now);
                lock (_sync)
                {
                    Position = next;
                }

                PositionChanged?.Invoke(this, new AxisPositionChangedEventArgs(next));
                await Task.Delay(stepDelay, token).ConfigureAwait(false);
            }

            await PersistAsync().ConfigureAwait(false);
            return OperationResult.Ok($"{action}完成。");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("运动已停止。", 2);
        }
        catch (Exception ex)
        {
            _logger.Error($"{action}失败", ex);
            return OperationResult.Fail($"{action}失败：{ex.Message}");
        }
        finally
        {
            ReleaseAnimationTokenSource(cts);
        }
    }

    private static double Interpolate(double from, double to, double ratio) => from + (to - from) * ratio;

    private double CurrentX()
    {
        lock (_sync)
        {
            return Position.X;
        }
    }

    private double CurrentY()
    {
        lock (_sync)
        {
            return Position.Y;
        }
    }

    private double CurrentZ()
    {
        lock (_sync)
        {
            return Position.Z;
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            AxisPosition snapshot;
            lock (_sync)
            {
                snapshot = Position;
            }

            await _stateStore.SaveAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"轴位置持久化失败：{ex.Message}");
        }
    }

    private CancellationTokenSource CreateAnimationTokenSource()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        lock (_sync)
        {
            previous = _animationCts;
            current = new CancellationTokenSource();
            _animationCts = current;
        }

        CancelAndDispose(previous);
        return current;
    }

    private void ReleaseAnimationTokenSource(CancellationTokenSource cts)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_animationCts, cts))
            {
                _animationCts = null;
            }
        }

        CancelAndDispose(cts);
    }

    private async Task CancelAnimationAsync()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _animationCts;
            _animationCts = null;
        }

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已被释放
            }

            cts.Dispose();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已被释放
        }

        cts.Dispose();
    }

    private OperationResult? RequireConnected()
    {
        lock (_sync)
        {
            return _connected ? null : OperationResult.Fail("请先连接运动平台。");
        }
    }
}
