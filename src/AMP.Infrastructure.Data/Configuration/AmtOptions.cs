using System.Text.Json;
using System.Text.Json.Serialization;
using AMP.Domain.Flows;

namespace AMP.Infrastructure.Data.Configuration;

/// <summary>
/// 全局配置：对应 appsettings.json。四个变化点都在这里切换：
 /// 运动厂商（VendorA/VendorB/Simulation）、仪器类型（Spectrophotometer/Colorimeter/Simulation）、
 /// 流程链组合（FlowChain 数组）、模拟参数。
/// </summary>
public sealed class AmtOptions
{
    public string WindowTitle { get; set; } = "AMP - 自动化测量平台（架构骨架）";

    public MotionOptions Motion { get; set; } = new();

    public InstrumentOptions Instrument { get; set; } = new();

    public FlowOptions Flow { get; set; } = new();

    /// <summary>流程链：可单独（一项）/可组合（多项）/任意顺序。</summary>
    public List<string> FlowChain { get; set; } = ["SvdCorrection", "MetrologyJudge", "RepeatabilityTest"];

    public DatabaseOptions Database { get; set; } = new();

    public LoggingOptions Logging { get; set; } = new();
}

/// <summary>运动控制配置。</summary>
public sealed class MotionOptions
{
    /// <summary>厂商：VendorA / VendorB / Simulation（数据库模拟）。</summary>
    public string Vendor { get; set; } = "Simulation";

    /// <summary>模拟单步动作耗时（毫秒）。</summary>
    public int StepDelayMilliseconds { get; set; } = 120;

    /// <summary>模拟运动速度（用于折算动画步进）。</summary>
    public double Speed { get; set; } = 25;
}

/// <summary>仪器配置。</summary>
public sealed class InstrumentOptions
{
    /// <summary>类型：Spectrophotometer / Colorimeter / Simulation（数据库模拟）。</summary>
    public string Vendor { get; set; } = "Simulation";

    /// <summary>模拟测量值相对标准数据的抖动百分比。</summary>
    public float JitterPercent { get; set; } = 1.5f;

    /// <summary>模拟随机种子（固定值保证结果可复现）。</summary>
    public int RandomSeed { get; set; } = 20261011;

    /// <summary>模拟仪器型号。</summary>
    public string ModelName { get; set; } = "SIM-AMP";
}

/// <summary>流程运行配置（映射到领域层 <see cref="FlowRunOptions"/>）。</summary>
public sealed class FlowOptions
{
    public int SvdBrickCount { get; set; } = 8;

    public int MetrologySampleCount { get; set; } = 5;

    public int RepeatCount { get; set; } = 10;

    public double ZPressPosition { get; set; } = 10;

    public double ZRetractDistance { get; set; } = 15;

    public double XStep { get; set; } = 10;

    public double YStep { get; set; } = 10;

    public double Grade1DeltaE { get; set; } = 0.8;

    public double Grade2DeltaE { get; set; } = 2.0;

    public string SvdStandardSet { get; set; } = "SVD砖块";

    public string MetrologyStandardSet { get; set; } = "计量标准";

    public string RepeatStandardSet { get; set; } = "白板";

    /// <summary>转换为领域层流程运行选项。</summary>
    public FlowRunOptions ToDomain() => new()
    {
        SvdBrickCount = SvdBrickCount,
        MetrologySampleCount = MetrologySampleCount,
        RepeatCount = RepeatCount,
        ZPressPosition = ZPressPosition,
        ZRetractDistance = ZRetractDistance,
        XStep = XStep,
        YStep = YStep,
        Grade1DeltaE = Grade1DeltaE,
        Grade2DeltaE = Grade2DeltaE,
        SvdStandardSet = SvdStandardSet,
        MetrologyStandardSet = MetrologyStandardSet,
        RepeatStandardSet = RepeatStandardSet,
    };
}

/// <summary>数据库配置。</summary>
public sealed class DatabaseOptions
{
    /// <summary>SQLite 文件路径；相对路径以程序基目录为根。</summary>
    public string FileName { get; set; } = "amp.db";

    /// <summary>首次运行是否写入内置标准数据（SVD砖块/计量标准/白板）。</summary>
    public bool SeedStandardDataOnFirstRun { get; set; } = true;
}

/// <summary>日志配置。</summary>
public sealed class LoggingOptions
{
    public string Directory { get; set; } = "Logs";

    public string MinimumLevel { get; set; } = "Information";
}

/// <summary>
/// 配置加载：从 appsettings.json 读取；失败时抛错（启动即暴露配置问题）。
/// 相对路径统一以程序基目录为根解析。
/// </summary>
public static class OptionsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static AmtOptions Load(string? path = null)
    {
        var file = path ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(file))
        {
            return new AmtOptions();
        }

        try
        {
            var json = File.ReadAllText(file);
            var options = JsonSerializer.Deserialize<AmtOptions>(json, JsonOptions) ?? new AmtOptions();
            ResolvePaths(options);
            return options;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"读取配置文件失败：{file}（{ex.Message}）。", ex);
        }
    }

    private static void ResolvePaths(AmtOptions options)
    {
        var baseDir = AppContext.BaseDirectory;
        if (!Path.IsPathRooted(options.Database.FileName))
        {
            options.Database.FileName = Path.Combine(baseDir, options.Database.FileName);
        }

        if (!Path.IsPathRooted(options.Logging.Directory))
        {
            options.Logging.Directory = Path.Combine(baseDir, options.Logging.Directory);
        }
    }
}
