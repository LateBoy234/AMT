using AMP.Domain.Flows;
using AMP.Domain.Logging;

namespace AMP.Application.Flows;

/// <summary>
/// 流程步骤基类：统一处理异常/取消收尾与 <see cref="IFlowStep.Completed"/> 事件触发。
/// 子类只需实现 <see cref="OnExecuteAsync"/>。
/// </summary>
public abstract class FlowStepBase : IFlowStep
{
    protected FlowStepBase(string key, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("流程步骤 Key 不能为空。", nameof(key));
        }

        Key = key;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? string.Empty;
    }

    /// <inheritdoc />
    public string Key { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public event EventHandler<FlowStepCompletedEventArgs>? Completed;

    /// <inheritdoc />
    public async Task<FlowResult> ExecuteAsync(FlowContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        FlowResult result;
        try
        {
            result = await OnExecuteAsync(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            context.Logger.Warn($"{Name}已取消。");
            result = FlowResult.Fail($"{Name}已取消。");
        }
        catch (Exception ex)
        {
            context.Logger.Error($"{Name}执行异常", ex);
            result = FlowResult.Fail($"{Name}执行异常：{ex.Message}");
        }

        // 组合流程据此自动跳到下一个流程界面。
        Completed?.Invoke(this, new FlowStepCompletedEventArgs(this, result));
        return result;
    }

    /// <summary>子类实现具体业务。</summary>
    protected abstract Task<FlowResult> OnExecuteAsync(FlowContext context, CancellationToken ct);
}
