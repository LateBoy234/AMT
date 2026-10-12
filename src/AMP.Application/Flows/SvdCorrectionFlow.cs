using AMP.Application.Measurement;
using AMP.Domain.Data;
using AMP.Domain.Flows;
using AMP.Domain.Instrument;
using AMP.Domain.Rules;

namespace AMP.Application.Flows;

/// <summary>
/// 流程一：SVD 修正。测量若干色砖，用砖块实测值与标准数据计算修正矩阵。
/// </summary>
public sealed class SvdCorrectionFlow : FlowStepBase
{
    private readonly PointMeasurementRunner _runner;
    private readonly ISvdCorrectionRule _rule;
    private readonly IStandardDataStore _standardStore;

    public SvdCorrectionFlow(PointMeasurementRunner runner, ISvdCorrectionRule rule, IStandardDataStore standardStore)
        : base("SvdCorrection", "SVD修正", "测量色砖并计算 SVD 修正矩阵。")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _standardStore = standardStore ?? throw new ArgumentNullException(nameof(standardStore));
    }

    protected override async Task<FlowResult> OnExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var options = context.Options;
        var standards = await _standardStore.LoadAsync(options.SvdStandardSet, ct).ConfigureAwait(false);
        if (standards.Count == 0)
        {
            return FlowResult.Fail($"标准数据集「{options.SvdStandardSet}」为空，无法执行 SVD 修正。");
        }

        var brickCount = Math.Min(options.SvdBrickCount, standards.Count);
        var measured = new List<MeasureData>(brickCount);
        for (var i = 0; i < brickCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var point = new PointSpec(i + 1, (i + 1) * options.XStep, 0, options.ZPressPosition, $"SVD砖块{i + 1}");
            context.Logger.Info($"[SVD修正] 测量砖块 {i + 1}/{brickCount}（X={point.X:F0}）。");
            measured.Add(await _runner.RunPointAsync(context, point, options.SvdStandardSet, ct).ConfigureAwait(false));
        }

        var correction = _rule.Calculate(measured, standards.Take(brickCount).ToList());
        return FlowResult.Ok(
            $"SVD修正完成（{brickCount} 块砖），修正矩阵已就绪。",
            $"测量砖块：{brickCount} 块",
            correction.Message);
    }
}
