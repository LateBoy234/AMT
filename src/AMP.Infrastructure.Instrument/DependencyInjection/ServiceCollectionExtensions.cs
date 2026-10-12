using AMP.Domain.Instrument;
using AMP.Infrastructure.Data.Configuration;
using AMP.Infrastructure.Instrument.Colorimeter;
using AMP.Infrastructure.Instrument.Simulation;
using AMP.Infrastructure.Instrument.Spectrophotometer;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.Infrastructure.Instrument.DependencyInjection;

/// <summary>
/// 仪器服务注册：按配置把 <see cref="IInstrument"/> 解析为具体仪器适配器或数据库模拟器。
/// <para>切换仪器类型/模拟只需改 appsettings.json 的 <c>Instrument.Vendor</c>，业务与界面代码零改动。</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInstrumentServices(this IServiceCollection services, AmtOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<DatabaseSimulatedInstrument>();
        services.AddSingleton<IInstrument>(sp =>
        {
            var vendor = options.Instrument.Vendor?.Trim().ToLowerInvariant();
            return vendor switch
            {
                "spectrophotometer" => new SpectrophotometerInstrument(sp.GetRequiredService<AMP.Domain.Logging.IAppLogger>()),
                "colorimeter" => new ColorimeterInstrument(sp.GetRequiredService<AMP.Domain.Logging.IAppLogger>()),
                _ => (IInstrument)sp.GetRequiredService<DatabaseSimulatedInstrument>(),
            };
        });

        return services;
    }
}
