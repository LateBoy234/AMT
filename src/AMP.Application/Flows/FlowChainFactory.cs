using AMP.Domain.Flows;

namespace AMP.Application.Flows;

/// <summary>
/// 流程链工厂：按配置键（如 [SvdCorrection, MetrologyJudge, RepeatabilityTest]）从 DI 注册的
 /// 全部 <see cref="IFlowStep"/> 中解析出流程链。单独流程 = 配置只有一个键；组合 = 多个键任意排序。
/// </summary>
public sealed class FlowChainFactory
{
    private readonly Func<string, IFlowStep> _resolver;

    public FlowChainFactory(IEnumerable<IFlowStep> steps, Func<string, IFlowStep>? resolver = null)
    {
        var stepList = steps?.ToList() ?? throw new ArgumentNullException(nameof(steps));
        _resolver = resolver
            ?? (key => stepList.FirstOrDefault(s => s.Key == key)
                ?? throw new KeyNotFoundException($"未注册流程步骤「{key}」。"));
    }

    /// <summary>按配置键组装流程链。</summary>
    public IReadOnlyList<IFlowStep> Build(IReadOnlyList<string> keys)
    {
        if (keys is null || keys.Count == 0)
        {
            throw new ArgumentException("流程链配置为空，至少需要一个流程步骤。", nameof(keys));
        }

        return keys.Select(_resolver).ToList();
    }
}
