using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using AMP.Application.Logging;

namespace AMP.UI.Services;

/// <summary>运行日志行（绑定到日志面板）。</summary>
public sealed record RunLogLine(string Time, string Level, string Message);

/// <summary>
/// 运行日志收集器：订阅 <see cref="FlowLogForwarder.Log"/>，
/// 把任意线程产生的日志调度到 UI 线程并追加到 <see cref="Lines"/>（各流程页共用同一实例）。
/// </summary>
public sealed class RunLogStore
{
    private const int MaxLines = 200;

    private readonly Dispatcher _dispatcher;

    public RunLogStore(FlowLogForwarder forwarder)
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        forwarder.Log += OnLog;
    }

    public ObservableCollection<RunLogLine> Lines { get; } = [];

    public void Clear() => Lines.Clear();

    private void OnLog(object? sender, LogMessageEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            Lines.Add(new RunLogLine(e.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"), e.Level, e.Message));
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }
        });
    }
}
