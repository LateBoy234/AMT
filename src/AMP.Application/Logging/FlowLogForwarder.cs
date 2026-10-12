using AMP.Domain.Logging;

namespace AMP.Application.Logging;

/// <summary>日志消息事件参数（UI 订阅后追加到运行日志面板）。</summary>
public sealed class LogMessageEventArgs : EventArgs
{
    public LogMessageEventArgs(string level, string message, DateTimeOffset timestamp)
    {
        Level = level;
        Message = message;
        Timestamp = timestamp;
    }

    /// <summary>级别（Debug/Info/Warn/Error）。</summary>
    public string Level { get; }

    /// <summary>日志内容。</summary>
    public string Message { get; }

    /// <summary>产生时间。</summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// 日志转发器：把写入的每条日志同时转发给 UI 订阅者（<see cref="Log"/> 事件），
 /// 使应用层/基础设施层的日志无需依赖 WPF 即可上屏。包装的文件日志实现由 DI 注入。
/// </summary>
public sealed class FlowLogForwarder : IAppLogger
{
    private readonly IAppLogger _inner;

    public FlowLogForwarder(IAppLogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>日志事件（任意线程触发，UI 需自行调度到界面线程）。</summary>
    public event EventHandler<LogMessageEventArgs>? Log;

    public void Debug(string message)
    {
        _inner.Debug(message);
        Publish("Debug", message);
    }

    public void Info(string message)
    {
        _inner.Info(message);
        Publish("Info", message);
    }

    public void Warn(string message)
    {
        _inner.Warn(message);
        Publish("Warn", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        _inner.Error(message, exception);
        Publish("Error", exception is null ? message : $"{message}（{exception.Message}）");
    }

    private void Publish(string level, string message)
        => Log?.Invoke(this, new LogMessageEventArgs(level, message, DateTimeOffset.Now));
}
