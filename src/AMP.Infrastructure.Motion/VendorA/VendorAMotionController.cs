using System.Runtime.InteropServices;
using AMP.Domain.Logging;
using AMP.Domain.Motion;

namespace AMP.Infrastructure.Motion.VendorA;

/// <summary>
/// 厂商 A 三轴运动控制器适配器（C++ 动态库 P/Invoke）。
/// <para>
/// 厂商 A 的 DLL（默认文件名 <c>vendor_a_motion.dll</c>）放入输出目录即可接通；
 /// 未放置时 ConnectAsync 返回明确失败信息，可切回 <c>Simulation</c> 模式继续开发。
 /// 各厂商 API 差异全部收敛在本类与 <see cref="NativeMethods"/> 中，上层只看到 <see cref="IMotionController"/>。
 /// </para>
/// </summary>
public sealed class VendorAMotionController : IMotionController
{
    /// <summary>厂商 A 动态库文件名（真实项目替换为厂商提供的 DLL 名）。</summary>
    public const string NativeLibrary = "vendor_a_motion.dll";

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private bool _connected;

    public VendorAMotionController(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string VendorName => "VendorA";

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
            return Task.FromResult(OperationResult.Fail($"未找到厂商 A 运动库 {NativeLibrary}；当前请使用数据库模拟（Simulation）模式。"));
        }

        // TODO 真实接入：按厂商 A 手册映射连接参数（IP/串口/板卡号）。
        var code = NativeMethods.Open();
        lock (_sync)
        {
            _connected = code == 0;
        }

        if (!_connected)
        {
            return Task.FromResult(OperationResult.Fail($"厂商 A 运动库打开失败（错误码 {code}）。"));
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
                NativeMethods.Close();
                _connected = false;
            }
        }

        _logger.Info($"{VendorName}：已断开。");
        return Task.FromResult(OperationResult.Ok("已断开。"));
    }

    public Task<OperationResult> HomeAsync(CancellationToken ct = default)
        => CallNative("回零", () => NativeMethods.Home(), refreshPosition: true);

    public Task<OperationResult> MoveXYAsync(double x, double y, CancellationToken ct = default)
        => CallNative($"XY移动到({x:F1},{y:F1})", () => NativeMethods.MoveXY(x, y), refreshPosition: true);

    public Task<OperationResult> PressZAsync(double z, CancellationToken ct = default)
        => CallNative($"Z轴下压到{z:F1}", () => NativeMethods.PressZ(z), refreshPosition: true);

    public Task<OperationResult> ReleaseGripperAsync(CancellationToken ct = default)
        => CallNative("夹爪释放", NativeMethods.GripperRelease, refreshPosition: false);

    public Task<OperationResult> ClampGripperAsync(CancellationToken ct = default)
        => CallNative("夹爪夹紧", NativeMethods.GripperClamp, refreshPosition: false);

    public Task<OperationResult> RetractZAsync(double distance, CancellationToken ct = default)
        => CallNative($"Z轴回退{distance:F1}", () => NativeMethods.ZRetract(distance), refreshPosition: true);

    public Task<OperationResult> StopAsync(CancellationToken ct = default)
        => CallNative("急停", NativeMethods.Stop, refreshPosition: true);

    private Task<OperationResult> CallNative(string action, Func<int> call, bool refreshPosition)
    {
        lock (_sync)
        {
            if (!_connected)
            {
                return Task.FromResult(OperationResult.Fail("厂商 A 运动控制器未连接。"));
            }

            var code = call();
            if (code != 0)
            {
                return Task.FromResult(OperationResult.Fail($"{action}失败（错误码 {code}）。"));
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
        NativeMethods.GetPos(out var x, out var y, out var z);
        Position = new AxisPosition(x, y, z, DateTimeOffset.Now);
        PositionChanged?.Invoke(this, new AxisPositionChangedEventArgs(Position));
    }

    private static string ResolveLibraryPath()
        => Path.Combine(AppContext.BaseDirectory, NativeLibrary);

    /// <summary>厂商 A 动态库原生入口（按厂商手册声明；错误码 0 = 成功）。</summary>
    private static class NativeMethods
    {
        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Open();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Close();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Home();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int MoveXY(double x, double y);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int PressZ(double z);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GripperRelease();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GripperClamp();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ZRetract(double distance);

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Stop();

        [DllImport(NativeLibrary, CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetPos(out double x, out double y, out double z);
    }
}
