using AMP.Domain.Flows;
using AMP.Domain.Logging;

namespace AMP.Application.Flows;

/// <summary>流程链运行结果。</summary>
public sealed record FlowRunResult(bool IsSuccess, bool IsCanceled, int ExecutedStepCount, string Message)
{
    public static FlowRunResult Ok(int executed, string message) => new(true, false, executed, message);

    public static FlowRunResult Canceled(string message) => new(false, true, 0, message);

    public static FlowRunResult Failed(string message) => new(false, false, 0, message);
}

/// <summary>流程链运行结束事件参数。</summary>
public sealed class FlowRunFinishedEventArgs : EventArgs
{
    public FlowRunFinishedEventArgs(FlowRunResult result) => Result = result;

    public FlowRunResult Result { get; }
}

/// <summary>
/// 流程编排器：按配置好的流程链顺序执行步骤。
/// <para>
/// - 每个步骤完成后触发 <see cref="StepCompleted"/>，UI 的导航服务据此自动切到下一个流程界面；
/// - 整条链结束触发 <see cref="RunFinished"/>；
/// - 链内容由配置决定（单独/组合/任意顺序），运行时更换通过 <see cref="SetChain"/>。
/// </para>
/// </summary>
public sealed class FlowOrchestrator
{
    private readonly FlowChainFactory _chainFactory;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;

    public FlowOrchestrator(FlowChainFactory chainFactory, IAppLogger logger)
    {
        _chainFactory = chainFactory ?? throw new ArgumentNullException(nameof(chainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>步骤完成事件（携带步骤与其结果）。</summary>
    public event EventHandler<FlowStepCompletedEventArgs>? StepCompleted;

    /// <summary>整条流程链结束事件。</summary>
    public event EventHandler<FlowRunFinishedEventArgs>? RunFinished;

    /// <summary>当前流程链。</summary>
    public IReadOnlyList<IFlowStep> Chain { get; private set; } = Array.Empty<IFlowStep>();

    /// <summary>当前正在执行的步骤下标（-1 表示未运行）。</summary>
    public int CurrentStepIndex { get; private set; } = -1;

    /// <summary>是否正在运行。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>最近一次运行的各步骤结果（按步骤 Key 索引）。</summary>
    public IReadOnlyDictionary<string, FlowResult> LastResults { get; private set; }
        = new Dictionary<string, FlowResult>();

    /// <summary>按配置键设置流程链（可单独/可组合/任意顺序）。</summary>
    public void SetChain(IReadOnlyList<string> keys)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("流程运行中，不能更换流程链。");
        }

        Chain = _chainFactory.Build(keys);
        LastResults = new Dictionary<string, FlowResult>();
        CurrentStepIndex = -1;
    }

    /// <summary>
    /// 从指定步骤开始运行流程链。
    /// </summary>
    /// <param name="context">流程运行上下文（运动/仪器/选项/日志）。</param>
    /// <param name="startStepIndex">起始步骤下标（单独流程=0；组合流程从当前界面对应步骤开始）。</param>
    public async Task<FlowRunResult> RunAsync(FlowContext context, int startStepIndex = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Chain.Count == 0)
        {
            return FlowRunResult.Failed("流程链为空，请先设置流程链。");
        }

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return FlowRunResult.Failed("已有流程运行中。");
        }

        var startedAt = DateTimeOffset.Now;
        try
        {
            IsRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            var executed = 0;
            var succeeded = true;
            var message = string.Empty;
            var start = Math.Clamp(startStepIndex, 0, Chain.Count - 1);

            for (var i = start; i < Chain.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                CurrentStepIndex = i;
                var step = Chain[i];
                _logger.Info($"[流程编排] 开始执行步骤 {i + 1}/{Chain.Count}：{step.Name}。");
                var result = await step.ExecuteAsync(context, token).ConfigureAwait(false);
                LastResults = new Dictionary<string, FlowResult>(LastResults) { [step.Key] = result };
                executed++;
                StepCompleted?.Invoke(this, new FlowStepCompletedEventArgs(step, result));

                if (!result.IsSuccess)
                {
                    succeeded = false;
                    message = result.Message;
                    break;
                }
            }

            var runResult = succeeded
                ? FlowRunResult.Ok(executed, $"流程执行完成，共 {executed} 个步骤（耗时 {DateTimeOffset.Now - startedAt:g}）。")
                : FlowRunResult.Failed(message);
            RunFinished?.Invoke(this, new FlowRunFinishedEventArgs(runResult));
            return runResult;
        }
        catch (OperationCanceledException)
        {
            var canceled = FlowRunResult.Canceled("流程已停止。");
            RunFinished?.Invoke(this, new FlowRunFinishedEventArgs(canceled));
            return canceled;
        }
        finally
        {
            IsRunning = false;
            CurrentStepIndex = -1;
            _cts?.Dispose();
            _cts = null;
            _gate.Release();
        }
    }

    /// <summary>请求停止流程。</summary>
    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 运行已结束
        }
    }
}
