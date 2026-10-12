using AMP.Application.Measurement;
using AMP.Domain.Data;
using AMP.Domain.Flows;
using AMP.Domain.Instrument;
using AMP.Domain.Rules;

namespace AMP.Application.Flows;

/// <summary>
/// 流程二：计量判断。测量若干样品并与标准参考对比，输出 ΔL/Δa/Δb/ΔE 与等级（一级/二级/不良）。
/// </summary>
public sealed class MetrologyJudgeFlow : FlowStepBase
{
    private readonly PointMeasurementRunner _runner;
    private readonly IMetrologyJudgeRule _judge;
    private readonly IStandardDataStore _standardStore;

    public MetrologyJudgeFlow(PointMeasurementRunner runner, IMetrologyJudgeRule judge, IStandardDataStore standardStore)
        : base("MetrologyJudge", "计量判断", "样品与标准参考对比，判定计量等级。")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));
        _standardStore = standardStore ?? throw new ArgumentNullException(nameof(standardStore));
    }

    protected override async Task<FlowResult> OnExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var options = context.Options;
        var standards = await _standardStore.LoadAsync(options.MetrologyStandardSet, ct).ConfigureAwait(false);
        if (standards.Count == 0)
        {
            return FlowResult.Fail($"标准数据集「{options.MetrologyStandardSet}」为空，无法执行计量判断。");
        }

        var sampleCount = Math.Min(options.MetrologySampleCount, standards.Count);
        var summary = new List<string>(sampleCount + 1) { $"样品数：{sampleCount}" };
        var classOne = 0;
        var classTwo = 0;
        var failed = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var point = new PointSpec(i + 1, (i + 1) * options.XStep, (i + 1) * options.YStep, options.ZPressPosition, $"计量样品{i + 1}");
            context.Logger.Info($"[计量判断] 测量样品 {i + 1}/{sampleCount}。");
            var data = await _runner.RunPointAsync(context, point, options.MetrologyStandardSet, ct).ConfigureAwait(false);
            var judgement = _judge.Judge(i + 1, data, standards[i]);
            summary.Add($"样品 {i + 1}：ΔL={judgement.DeltaL:+0.000;-0.000}，Δa={judgement.Deltaa:+0.000;-0.000}，"
                + $"Δb={judgement.Deltab:+0.000;-0.000}，ΔE={judgement.DeltaE:F3} → {judgement.Level.ToDisplayName()}");

            switch (judgement.Level)
            {
                case MetrologyLevel.ClassOne:
                    classOne++;
                    break;
                case MetrologyLevel.ClassTwo:
                    classTwo++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        summary.Add($"判定汇总：一级 {classOne} 个，二级 {classTwo} 个，不良 {failed} 个。");
        return FlowResult.Ok($"计量判断完成：{sampleCount} 个样品。", summary.ToArray());
    }
}
