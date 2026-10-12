using System.Diagnostics;
using AMP.Domain.Logging;
using AMP.Infrastructure.Data.Configuration;

namespace AMP.Infrastructure.Data.Logging;

/// <summary>
/// 文件日志实现：按天滚动写入 <c>Logs/amp-yyyyMMdd.log</c>，同时输出到 Trace 便于附加调试器查看。
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly LoggingOptions _options;
    private readonly object _sync = new();

    public FileAppLogger(LoggingOptions options)
    {
        _options = options ?? new LoggingOptions();
        try
        {
            Directory.CreateDirectory(_options.Directory);
        }
        catch
        {
            // 目录不可用时降级为仅 Trace
        }
    }

    public void Debug(string message)
    {
        if (!string.Equals(_options.MinimumLevel, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Write("Debug", message);
    }

    public void Info(string message) => Write("Info", message);

    public void Warn(string message) => Write("Warn", message);

    public void Error(string message, Exception? exception = null)
        => Write("Error", exception is null ? message : $"{message}（{exception.GetType().Name}: {exception.Message}）");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Trace.WriteLine(line);

        try
        {
            lock (_sync)
            {
                var path = Path.Combine(_options.Directory, $"amp-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // 文件写入失败不影响业务
        }
    }
}
