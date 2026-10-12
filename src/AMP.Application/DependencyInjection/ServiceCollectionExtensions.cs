using AMP.Application.Flows;
using AMP.Application.Logging;
using AMP.Application.Measurement;
using AMP.Application.StateMachines;
using AMP.Domain.Flows;
using AMP.Domain.Logging;
using AMP.Domain.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.Application.DependencyInjection;

/// <summary>
/// 应用层服务注册：状态机、测量执行器、业务规则、三个流程步骤、流程链工厂与编排器。
/// <para>IAppLogger 由 UI 组合根注册（FlowLogForwarder 包装文件日志），此处不注册。</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MotionStateMachine>();
        services.AddSingleton<PointMeasurementRunner>();

        // 业务规则（领域层纯逻辑）。
        services.AddSingleton<ISvdCorrectionRule, SvdCorrectionRule>();
        services.AddSingleton<IMetrologyJudgeRule>(sp =>
        {
            var options = sp.GetRequiredService<FlowRunOptions>();
            return new MetrologyJudgeRule(options.Grade1DeltaE, options.Grade2DeltaE);
        });
        services.AddSingleton<IRepeatabilityRule, RepeatabilityRule>();

        // 三个流程步骤：既注册具体类型，也注册为 IFlowStep 供 FlowChainFactory 枚举。
        services.AddSingleton<SvdCorrectionFlow>();
        services.AddSingleton<MetrologyJudgeFlow>();
        services.AddSingleton<RepeatabilityTestFlow>();
        services.AddSingleton<IFlowStep>(sp => sp.GetRequiredService<SvdCorrectionFlow>());
        services.AddSingleton<IFlowStep>(sp => sp.GetRequiredService<MetrologyJudgeFlow>());
        services.AddSingleton<IFlowStep>(sp => sp.GetRequiredService<RepeatabilityTestFlow>());

        services.AddSingleton<FlowChainFactory>();
        // 从 DI 读取流程链配置（IReadOnlyList<string>，由组合根注册为 appsettings 的 FlowChain），
        // 构造时填充 Chain；组合根未提供键列表时保持空链并给出明确错误。
        services.AddSingleton<FlowOrchestrator>(sp =>
        {
            var chainKeys = sp.GetService<IReadOnlyList<string>>() ?? Array.Empty<string>();
            var orchestrator = new FlowOrchestrator(
                sp.GetRequiredService<FlowChainFactory>(),
                sp.GetRequiredService<IAppLogger>());
            if (chainKeys.Count > 0)
            {
                orchestrator.SetChain(chainKeys);
            }

            return orchestrator;
        });
        return services;
    }
}
