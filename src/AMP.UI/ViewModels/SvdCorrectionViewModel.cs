using AMP.Application.Flows;
using AMP.UI.Services;

namespace AMP.UI.ViewModels;

/// <summary>流程一：SVD 修正界面。</summary>
public sealed partial class SvdCorrectionViewModel : FlowPageViewModelBase
{
    public SvdCorrectionViewModel(FlowOrchestrator orchestrator, FlowContextFactory contextFactory, RunLogStore logStore)
        : base(orchestrator, contextFactory, logStore)
    {
    }

    public override string Key => "SvdCorrection";

    public override string Title => "SVD修正";

    public override string Description => "流程一：测量色砖并计算 SVD 修正矩阵，为后续计量提供修正系数。";
}
