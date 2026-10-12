using AMP.Application.DependencyInjection;
using AMP.Application.Flows;
using AMP.Application.StateMachines;
using AMP.Domain.Flows;
using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;
using AMP.Infrastructure.Data.Configuration;
using AMP.Infrastructure.Data.DependencyInjection;
using AMP.Infrastructure.Data.Logging;
using AMP.Infrastructure.Instrument.DependencyInjection;
using AMP.Infrastructure.Motion.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    return await RunSmokeAsync();
}
catch (Exception ex)
{
    var detail = $"[致命异常] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
    Console.WriteLine(detail);
    try
    {
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.log"), detail + Environment.NewLine);
    }
    catch
    {
        // 忽略诊断日志写入失败。
    }

    return 1;
}

static async Task<int> RunSmokeAsync()
{
    // 与 UI 组合根一致的 DI 装配（仅去掉 WPF 相关服务）。
    var options = OptionsStore.Load();
    var services = new ServiceCollection();
    services.AddDataServices(options);
    services.AddApplicationServices();
    services.AddMotionServices(options);
    services.AddInstrumentServices(options);
    services.AddSingleton(options.Flow.ToDomain());
    services.AddSingleton<IReadOnlyList<string>>(options.FlowChain); // 流程链配置（FlowOrchestrator 构造时填充）
    services.AddSingleton(options.Motion);
    services.AddSingleton(options.Instrument);
    services.AddSingleton<IAppLogger>(_ => new FileAppLogger(options.Logging));

    await using var provider = services.BuildServiceProvider();

    var motion = provider.GetRequiredService<IMotionController>();
    var instrument = provider.GetRequiredService<IInstrument>();
    var orchestrator = provider.GetRequiredService<FlowOrchestrator>();
    var stateMachine = provider.GetRequiredService<MotionStateMachine>();
    var logger = provider.GetRequiredService<IAppLogger>();

    Console.WriteLine("========================================");
    Console.WriteLine("AMP 冒烟测试（数据库模拟全链路）");
    Console.WriteLine("========================================");
    Console.WriteLine($"运动控制器：{motion.VendorName}");
    Console.WriteLine($"测量仪器：{instrument.VendorName}");

    // 1. 连接（模拟：SQLite 恢复轴位置 + 加载标准数据）。
    var motionConnect = await motion.ConnectAsync();
    Console.WriteLine($"[连接] 运动：{motionConnect.Message}");
    var instrumentConnect = await instrument.ConnectAsync();
    Console.WriteLine($"[连接] 仪器：{instrumentConnect.Message}");
    if (!motionConnect.IsSuccess || !instrumentConnect.IsSuccess)
    {
        Console.WriteLine("设备连接失败，终止冒烟测试。");
        return 1;
    }

    // 2. 流程链（由 DI 注册的配置键列表填充，验证配置驱动路径）。
    Console.WriteLine($"[流程链] {string.Join(" → ", orchestrator.Chain.Select(s => s.Name))}");

    orchestrator.StepCompleted += (_, e) =>
        Console.WriteLine($"  [步骤完成] {e.Step.Name}：{(e.Result.IsSuccess ? e.Result.Message : "失败：" + e.Result.Message)}");

    var contextFactory = new ContextFactory(motion, instrument, provider.GetRequiredService<FlowRunOptions>(), logger);

    // 3. 从第一个步骤开始运行完整流程链。
    Console.WriteLine("[运行] 开始…");
    var runResult = await orchestrator.RunAsync(contextFactory.Create(), 0);
    Console.WriteLine($"[运行] {(runResult.IsSuccess ? "成功" : runResult.IsCanceled ? "已取消" : "失败")}：{runResult.Message}");

    // 4. 各步骤结果摘要。
    Console.WriteLine("[结果] 各步骤摘要：");
    foreach (var kv in orchestrator.LastResults)
    {
        Console.WriteLine($"  ◆ {kv.Key}：{kv.Value.Message}");
        foreach (var line in kv.Value.Summary)
        {
            Console.WriteLine($"      {line}");
        }
    }

    // 5. 状态机终态与轴位置（模拟运动已持久化到 SQLite）。
    Console.WriteLine($"[状态机] 终态：{stateMachine.CurrentState.ToDisplayName()}");
    Console.WriteLine($"[坐标] X={motion.Position.X:F1}  Y={motion.Position.Y:F1}  Z={motion.Position.Z:F1}");

    // 6. 断开连接（再次持久化轴位置）。
    await motion.DisconnectAsync();
    await instrument.DisconnectAsync();

    Console.WriteLine("========================================");
    Console.WriteLine(runResult.IsSuccess ? "冒烟测试通过 ✅" : "冒烟测试失败 ❌");
    return runResult.IsSuccess ? 0 : 1;
}

/// <summary>复刻 UI 层的流程上下文工厂（控制台工程不能引用 WPF 工程）。</summary>
internal sealed class ContextFactory(IMotionController motion, IInstrument instrument, FlowRunOptions runOptions, IAppLogger logger)
{
    public FlowContext Create() => new(motion, instrument, runOptions, logger);
}
