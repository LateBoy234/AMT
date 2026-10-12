using AMP.Application.Flows;
using AMP.UI.Services;

namespace AMP.UI.ViewModels;

/// <summary>流程三：重复性测试界面。</summary>
public sealed partial class RepeatabilityTestViewModel : FlowPageViewModelBase
{
    public RepeatabilityTestViewModel(FlowOrchestrator orchestrator, FlowContextFactory contextFactory, RunLogStore logStore)
        : base(orchestrator, contextFactory, logStore)
    {
    }

    public override string Key => "RepeatabilityTest";

    public override string Title => "重复性测试";

    public override string Description => "流程三：同一位置多次测量白板，计算 ΔE 的均值、标准差与最大值，评估仪器重复性。";
}
