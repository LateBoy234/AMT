using AMP.Domain.Motion;
using AMP.Infrastructure.Data.Configuration;
using AMP.Infrastructure.Motion.Simulation;
using AMP.Infrastructure.Motion.VendorA;
using AMP.Infrastructure.Motion.VendorB;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.Infrastructure.Motion.DependencyInjection;

/// <summary>
/// 运动控制服务注册：按配置把 <see cref="IMotionController"/> 解析为具体厂商适配器或数据库模拟器。
/// <para>切换厂商/模拟只需改 appsettings.json 的 <c>Motion.Vendor</c>，业务与界面代码零改动。</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMotionServices(this IServiceCollection services, AmtOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<DatabaseSimulatedMotionController>();
        services.AddSingleton<IMotionController>(sp =>
        {
            var vendor = options.Motion.Vendor?.Trim().ToLowerInvariant();
            return vendor switch
            {
                "vendora" or "a" => new VendorAMotionController(sp.GetRequiredService<AMP.Domain.Logging.IAppLogger>()),
                "vendorb" or "b" => new VendorBMotionController(sp.GetRequiredService<AMP.Domain.Logging.IAppLogger>()),
                _ => (IMotionController)sp.GetRequiredService<DatabaseSimulatedMotionController>(),
            };
        });

        return services;
    }
}
