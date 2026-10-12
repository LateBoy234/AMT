using System.Runtime.InteropServices;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.Infrastructure.Motion.VendorB;

/// <summary>
/// 厂商 B 三轴运动控制器适配器（C++ 动态库 P/Invoke）。
/// <para>
/// 与厂商 A 展示差异：DLL 文件名、原生函数名、返回码约定都不同，
 /// 但通过实现同一个 <see cref="IMotionController"/>，上层流程代码完全无感。
/// </para>
/// </summary>
public sealed class VendorBMotionController : IMotionController
{
    /// <summary>厂商 B 动态库文件名。</summary>
    public const string NativeLibrary = "vendor_b_motion.dll";

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private bool _connected;

    public VendorBMotionController(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string VendorName => "VendorB";

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

    public Task<OperationResult> ConnectAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ResolveLibraryPath()))
        {
            _logger.Warn($"{VendorName}：未找到动态库 {NativeLibrary}，请放置厂商 DLL 或切换 Simulation 模式。");
            return Task.FromResult(OperationResult.Fail($"未找到厂商 B 运动库 {NativeLibrary}；当前请使用数据库模拟（Simulation）模式。"));
        }

        // TODO 真实接入：厂商 B 以句柄方式管理连接（返回非零句柄表示成功）。
        var handle = NativeMethods.Init();
        lock (_sync)
        {
            _connected = handle != 0;
        }

        if (!_connected)
        {
            return Task.FromResult(OperationResult.Fail("厂商 B 运动库初始化失败。"));
        }

        RefreshPosition();
        _logger.Info($"{VendorName}：运动控制器已连接。");
        return Task.FromResult(OperationResult.Ok($"{VendorName} 已连接。"));
    }

    public Task<OperationResult> DisconnectAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_connected)
            {
                NativeMethods.Shutdown();
                _connected = false;
            }
        }

        _logger.Info($"{VendorName}：已断开。");
        return Task.FromResult(OperationResult.Ok("已断开。"));
    }

    public Task<OperationResult> HomeAsync(CancellationToken ct = default)
        => CallNative("回零", () => NativeMethods.GoHome(), refreshPosition: true);

    public Task<OperationResult> MoveXYAsync(double x, double y, CancellationToken ct = default)
        => CallNative($"XY移动到({x:F1},{y:F1})", () => NativeMethods.MoveToXY(x, y), refreshPosition: true);

    public Task<OperationResult> PressZAsync(double z, CancellationToken ct = default)
        => CallNative($"Z轴下压到{z:F1}", () => NativeMethods.MoveZTo(z), refreshPosition: true);

    public Task<OperationResult> ReleaseGripperAsync(CancellationToken ct = default)
        => CallNative("夹爪释放", () => NativeMethods.SetGripper(0), refreshPosition: false);

    public Task<OperationResult> ClampGripperAsync(CancellationToken ct = default)
        => CallNative("夹爪夹紧", () => NativeMethods.SetGripper(1), refreshPosition: false);

    public Task<OperationResult> RetractZAsync(double distance, CancellationToken ct = default)
        => CallNative($"Z轴回退{distance:F1}", () => NativeMethods.MoveZBy(distance), refreshPosition: true);

    public Task<OperationResult> StopAsync(CancellationToken ct = default)
        => CallNative("急停", () => NativeMethods.EStop(), refreshPosition: true);

    private Task<OperationResult> CallNative(string action, Func<int> call, bool refreshPosition)
    {
        lock (_sync)
        {
            if (!_connected)
            {
                return Task.FromResult(OperationResult.Fail("厂商 B 运动控制器未连接。"));
            }

            var code = call();
            if (code != 1) // 厂商 B 约定：1 = 成功
            {
                return Task.FromResult(OperationResult.Fail($"{action}失败（返回 {code}）。"));
            }
        }

        if (refreshPosition)
        {
            RefreshPosition();
        }

        return Task.FromResult(OperationResult.Ok($"{action}完成。"));
    }

    private void RefreshPosition()
    {
        NativeMethods.ReadPos(out var x, out var y, out var z);
        Position = new AxisPosition(x, y, z, DateTimeOffset.Now);
        PositionChanged?.Invoke(this, new AxisPositionChangedEventArgs(Position));
    }

    private static string ResolveLibraryPath()
        => Path.Combine(AppContext.BaseDirectory, NativeLibrary);

    /// <summary>厂商 B 动态库原生入口（句柄式 API；返回 1 = 成功）。</summary>
    private static class NativeMethods
    {
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int Init();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern void Shutdown();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int GoHome();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int MoveToXY(double x, double y);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int MoveZTo(double z);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetGripper(int state);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int MoveZBy(double distance);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern int EStop();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.StdCall)]
        public static extern void ReadPos(out double x, out double y, out double z);
    }
}
