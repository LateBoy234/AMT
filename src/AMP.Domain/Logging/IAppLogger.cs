namespace AMP.Domain.Logging;

/// <summary>
/// 应用日志抽象。实现位于基础设施层（文件日志），
 /// 应用层的流程日志通过转发器同时发布给 UI 订阅者。
/// </summary>
public interface IAppLogger
{
    void Debug(string message);

    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
