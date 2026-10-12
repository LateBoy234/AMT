using System.IO;
using System.Windows;
using System.Windows.Threading;
using AMP.Domain.Instrument;
using AMP.Domain.Motion;
using AMP.Infrastructure.Data.Configuration;
using AMP.UI.DependencyInjection;
using AMP.UI.ViewModels;
using AMP.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.UI;

/// <summary>
/// 应用入口：读取配置 → 构建 DI 容器 → 显示主壳窗口；
/// 运动平台与仪器在 Shell 中异步连接（模拟模式必定成功，真机未接入时给出明确日志）。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>全局服务提供者。</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>本次运行使用的全局配置。</summary>
    public static AmtOptions Options { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底：任何未处理异常都写入诊断文件，便于无控制台的 WPF 排查。
        DispatcherUnhandledException += (_, args) =>
        {
            WriteFatal(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteFatal(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteFatal(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };

        try
        {
            Options = OptionsStore.Load();

            var collection = new ServiceCollection();
            collection.AddAmpServices(Options);
            Services = collection.BuildServiceProvider();

            var shellViewModel = Services.GetRequiredService<ShellViewModel>();
            var shell = new ShellWindow { DataContext = shellViewModel, Title = Options.WindowTitle };
            MainWindow = shell;
            shell.Show();
        }
        catch (Exception ex)
        {
            WriteFatal(ex, "OnStartup");
            MessageBox.Show($"启动失败：{ex.Message}", "AMP", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Services.GetService<IMotionController>()?.DisconnectAsync().GetAwaiter().GetResult();
            Services.GetService<IInstrument>()?.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // 退出清理失败不影响进程结束
        }

        base.OnExit(e);
    }

    private static void WriteFatal(Exception? ex, string source)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            var detail = $"[{DateTimeOffset.Now:O}] {source}: {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "fatal-error.log"), detail);
        }
        catch
        {
            // 诊断写入失败只能忽略
        }
    }
}
