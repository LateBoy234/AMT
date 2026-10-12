using AMP.Application.Measurement;
using AMP.Domain.Data;
using AMP.Domain.Flows;
using AMP.Domain.Instrument;
using AMP.Domain.Rules;

namespace AMP.Application.Flows;

/// <summary>
/// 流程三：重复性测试。同一位置多次测量白板，计算 ΔE 的均值/标准差/最大值。
/// </summary>
public sealed class RepeatabilityTestFlow : FlowStepBase
{
    private readonly PointMeasurementRunner _runner;
    private readonly IRepeatabilityRule _repeatability;
    private readonly IStandardDataStore _standardStore;

    public RepeatabilityTestFlow(PointMeasurementRunner runner, IRepeatabilityRule repeatability, IStandardDataStore standardStore)
        : base("RepeatabilityTest", "重复性测试", "同一位置多次测量，评估仪器重复性。")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repeatability = repeatability ?? throw new ArgumentNullException(nameof(repeatability));
        _standardStore = standardStore ?? throw new ArgumentNullException(nameof(standardStore));
    }

    protected override async Task<FlowResult> OnExecuteAsync(FlowContext context, CancellationToken ct)
    {
        var options = context.Options;
        var standards = await _standardStore.LoadAsync(options.RepeatStandardSet, ct).ConfigureAwait(false);
        if (standards.Count == 0)
        {
            return FlowResult.Fail($"标准数据集「{options.RepeatStandardSet}」为空，无法执行重复性测试。");
        }

        var target = standards[0];
        var count = Math.Max(2, options.RepeatCount);
        var measured = new List<MeasureData>(count);
        // 固定同一位置测量，突出「重复性」语义。
        var point = new PointSpec(1, options.XStep, options.YStep, options.ZPressPosition, "重复性固定点");

        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            context.Logger.Info($"[重复性测试] 第 {i + 1}/{count} 次测量。");
            measured.Add(await _runner.RunPointAsync(context, point, options.RepeatStandardSet, ct).ConfigureAwait(false));
        }

        var result = _repeatability.Calculate(measured, target);
        return FlowResult.Ok(
            "重复性测试完成。",
            $"测量次数：{count}",
            $"ΔE 均值：{result.MeanDeltaE:F3}",
            $"ΔE 标准差：{result.StdDevDeltaE:F3}",
            $"ΔE 最大值：{result.MaxDeltaE:F3}",
            result.Message);
    }
}
