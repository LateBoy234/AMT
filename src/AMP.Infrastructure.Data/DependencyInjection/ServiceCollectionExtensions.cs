using AMP.Domain.Data;
using AMP.Domain.Logging;
using AMP.Infrastructure.Data.Configuration;
using AMP.Infrastructure.Data.Logging;
using AMP.Infrastructure.Data.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.Infrastructure.Data.DependencyInjection;

/// <summary>
/// 数据基础设施服务注册：配置、SQLite 存储（运动状态/标准数据/测量结果）、文件日志。
/// <para>注意：IAppLogger 在此不注册——由 UI 组合根把 <see cref="FlowLogForwarder"/> 包装在 FileAppLogger 之上注册。</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataServices(this IServiceCollection services, AmtOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(options.Database);
        services.AddSingleton(options.Logging);

        services.AddSingleton<SqliteDataStore>();
        services.AddSingleton<IMotionStateStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IStandardDataStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IMeasureResultStore>(sp => sp.GetRequiredService<SqliteDataStore>());

        services.AddSingleton(sp => new FileAppLogger(sp.GetRequiredService<LoggingOptions>()));
        return services;
    }
}
