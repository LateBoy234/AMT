using AMP.Application.Flows;
using AMP.Domain.Flows;
using AMP.UI.ViewModels;

namespace AMP.UI.Services;

/// <summary>
/// 流程导航服务：订阅编排器的 <see cref="FlowOrchestrator.StepCompleted"/>，
/// 组合流程中每完成一个步骤自动跳到下一个流程的界面（单独流程链长度为 1，自然不会跳转）。
/// </summary>
public sealed class FlowNavigationService
{
    private readonly FlowOrchestrator _orchestrator;
    private readonly INavigationService _navigation;

    /// <summary>流程步骤 Key → 对应界面 VM 类型。</summary>
    private static readonly IReadOnlyDictionary<string, Type> StepViewModelMap = new Dictionary<string, Type>
    {
        ["SvdCorrection"] = typeof(SvdCorrectionViewModel),
        ["MetrologyJudge"] = typeof(MetrologyJudgeViewModel),
        ["RepeatabilityTest"] = typeof(RepeatabilityTestViewModel),
    };

    public FlowNavigationService(FlowOrchestrator orchestrator, INavigationService navigation)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _orchestrator.StepCompleted += OnStepCompleted;
    }

    private void OnStepCompleted(object? sender, FlowStepCompletedEventArgs e)
    {
        // 仅当步骤成功且还有下一步时自动跳转；失败时停留在当前界面查看原因。
        if (!e.Result.IsSuccess)
        {
            return;
        }

        var index = IndexOf(e.Step);
        var next = index + 1;
        if (next < 0 || next >= _orchestrator.Chain.Count)
        {
            return;
        }

        var nextStep = _orchestrator.Chain[next];
        if (StepViewModelMap.TryGetValue(nextStep.Key, out var viewModelType))
        {
            _navigation.NavigateTo(viewModelType);
        }
    }

    private int IndexOf(IFlowStep step)
    {
        for (var i = 0; i < _orchestrator.Chain.Count; i++)
        {
            if (_orchestrator.Chain[i].Key == step.Key)
            {
                return i;
            }
        }

        return -1;
    }
}
