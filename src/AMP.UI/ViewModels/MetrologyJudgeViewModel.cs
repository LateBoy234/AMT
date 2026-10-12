using AMP.Application.Flows;
using AMP.UI.Services;

namespace AMP.UI.ViewModels;

/// <summary>流程二：计量判断界面。</summary>
public sealed partial class MetrologyJudgeViewModel : FlowPageViewModelBase
{
    public MetrologyJudgeViewModel(FlowOrchestrator orchestrator, FlowContextFactory contextFactory, RunLogStore logStore)
        : base(orchestrator, contextFactory, logStore)
    {
    }

    public override string Key => "MetrologyJudge";

    public override string Title => "计量判断";

    public override string Description => "流程二：样品与标准参考对比，输出 ΔL/Δa/Δb/ΔE 并判定计量等级（一级/二级/不良）。";
}
