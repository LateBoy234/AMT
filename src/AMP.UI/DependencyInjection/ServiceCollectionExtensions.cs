using AMP.Application.DependencyInjection;
using AMP.Application.Logging;
using AMP.Domain.Flows;
using AMP.Domain.Logging;
using AMP.Infrastructure.Data.Configuration;
using AMP.Infrastructure.Data.DependencyInjection;
using AMP.Infrastructure.Data.Logging;
using AMP.Infrastructure.Instrument.DependencyInjection;
using AMP.Infrastructure.Motion.DependencyInjection;
using AMP.UI.Services;
using AMP.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.UI.DependencyInjection;

/// <summary>
/// UI 组合根：装配数据/应用/运动/仪器四个基础设施层，再注册界面服务与视图模型。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAmpServices(this IServiceCollection services, AmtOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddDataServices(options);
        services.AddApplicationServices();
        services.AddMotionServices(options);
        services.AddInstrumentServices(options);

        // 领域层流程运行选项（由配置映射）。
        services.AddSingleton(options.Flow.ToDomain());

        // 流程链配置（IReadOnlyList<string>）：FlowOrchestrator 构造时据此填充 Chain。
        services.AddSingleton<IReadOnlyList<string>>(options.FlowChain);

        // 子配置节：厂商/模拟适配器按需解析（如 DatabaseSimulatedInstrument 依赖 InstrumentOptions）。
        services.AddSingleton(options.Motion);
        services.AddSingleton(options.Instrument);

        // 日志：FlowLogForwarder 把文件日志同时转发给 UI 日志面板（RunLogStore 订阅）。
        services.AddSingleton(sp => new FlowLogForwarder(sp.GetRequiredService<FileAppLogger>()));
        services.AddSingleton<IAppLogger>(sp => sp.GetRequiredService<FlowLogForwarder>());

        // 消息总线（弱引用）与导航。
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<RunLogStore>();
        services.AddSingleton<FlowContextFactory>();
        services.AddSingleton<FlowNavigationService>();

        // 视图模型：单例，保证跨页面导航时状态与订阅保持。
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SvdCorrectionViewModel>();
        services.AddSingleton<MetrologyJudgeViewModel>();
        services.AddSingleton<RepeatabilityTestViewModel>();
        return services;
    }
}
