using AMP.Domain.Data;
using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;
using AMP.Infrastructure.Data.Configuration;

namespace AMP.Infrastructure.Instrument.Simulation;

/// <summary>
/// 数据库模拟测量仪器：测量值直接来自数据库标准数据（<see cref="IStandardDataStore"/>），
 /// 按数据集名称（SVD砖块/计量标准/白板）取行、循环使用，并叠加可复现的随机抖动。
/// 每次测量结果写入 <see cref="IMeasureResultStore"/> 便于追溯。
/// </summary>
public sealed class DatabaseSimulatedInstrument : IInstrument
{
    private readonly IStandardDataStore _standardStore;
    private readonly IMeasureResultStore _resultStore;
    private readonly InstrumentOptions _options;
    private readonly IAppLogger _logger;

    private readonly object _sync = new();
    private readonly Dictionary<string, List<StandardRow>> _rowsBySet = new();
    private readonly Dictionary<string, int> _cursorBySet = new();
    private Random? _random;
    private bool _connected;

    public DatabaseSimulatedInstrument(
        IStandardDataStore standardStore,
        IMeasureResultStore resultStore,
        InstrumentOptions options,
        IAppLogger logger)
    {
        _standardStore = standardStore ?? throw new ArgumentNullException(nameof(standardStore));
        _resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string VendorName => "Simulation";

    public string ModelName => _options.ModelName;

    public bool IsConnected => _connected;

    public async Task<OperationResult> ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            var all = await _standardStore.LoadAsync(null, ct).ConfigureAwait(false);
            lock (_sync)
            {
                _rowsBySet.Clear();
                _cursorBySet.Clear();
                foreach (var row in all)
                {
                    if (!_rowsBySet.TryGetValue(row.SetName, out var list))
                    {
                        list = [];
                        _rowsBySet[row.SetName] = list;
                    }

                    list.Add(row);
                }

                _random = new Random(_options.RandomSeed);
                _connected = true;
            }

            _logger.Info($"数据库模拟仪器已连接（标准数据集：{string.Join("、", _rowsBySet.Keys)}）。");
            return OperationResult.Ok($"模拟仪器已连接，加载 {all.Count} 行标准数据。");
        }
        catch (Exception ex)
        {
            _logger.Error("数据库模拟仪器连接失败", ex);
            return OperationResult.Fail($"模拟仪器连接失败：{ex.Message}");
        }
    }

    public Task<OperationResult> DisconnectAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            _connected = false;
        }

        return Task.FromResult(OperationResult.Ok("已断开。"));
    }

    public Task<OperationResult> CalibrateAsync(CalibrationKind kind, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            return Task.FromResult(OperationResult.Fail("模拟仪器未连接。"));
        }

        _logger.Info($"数据库模拟：{(kind == CalibrationKind.White ? "白" : "黑")}校正已模拟完成。");
        return Task.FromResult(OperationResult.Ok($"{kind}校正完成（模拟）。"));
    }

    public async Task<MeasureData> MeasureAsync(string? standardSet = null, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("模拟仪器未连接。");
        }

        StandardRow row;
        lock (_sync)
        {
            var set = ResolveSet(standardSet);
            if (set is null)
            {
                throw new InvalidOperationException("数据库没有可用的标准数据，无法模拟测量。");
            }

            var rows = _rowsBySet[set];
            var cursor = _cursorBySet.TryGetValue(set, out var c) ? c : 0;
            row = rows[cursor % rows.Count];
            _cursorBySet[set] = cursor + 1;
        }

        await Task.Delay(Math.Max(1, 30), ct).ConfigureAwait(false);

        var values = Jitter(row.Values);
        var source = $"数据库标准数据：{row.SetName}/{row.RowIndex}";
        // Lab 型标准（计量标准/白板）：用抖动后的前三个通道生成 Lab，保证 ΔE 有物理感；
        // 反射率型标准（SVD砖块）无 Lab。
        LabValue? lab = row.Values.Count >= 3
            ? new LabValue(ClampLab(values[0], 0, 100), values[1], values[2])
            : null;
        var data = new MeasureData(values, source, DateTimeOffset.Now, lab);

        // 结果落库（失败不影响测量返回）。
        try
        {
            await _resultStore.SaveAsync(new MeasureResultRecord(row.SetName, row.RowIndex, source, values, DateTimeOffset.Now), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn($"模拟测量结果写库失败：{ex.Message}");
        }

        return data;
    }

    private string? ResolveSet(string? standardSet)
    {
        if (!string.IsNullOrWhiteSpace(standardSet) && _rowsBySet.ContainsKey(standardSet))
        {
            return standardSet;
        }

        // 未指定或指定集不存在时，回退到第一个数据集。
        return _rowsBySet.Count > 0 ? _rowsBySet.Keys.First() : null;
    }

    /// <summary>叠加高斯抖动：v' = v × (1 + jitter% × gauss)，夹取到 Lab/反射率合法区间。</summary>
    private float[] Jitter(IReadOnlyList<float> values)
    {
        var result = new float[values.Count];
        Random random;
        lock (_sync)
        {
            random = _random ??= new Random(_options.RandomSeed);
        }

        var jitterPercent = _options.JitterPercent;
        for (var i = 0; i < values.Count; i++)
        {
            var gauss = Gaussian(random);
            var v = values[i] * (1 + jitterPercent / 100.0 * gauss);
            // 通道同时承载反射率(0..100)与 Lab(a/b ∈ -128..127)：统一夹到 [-128, 127]，
            // 避免把负的 Lab 分量错误地夹成 0。
            result[i] = (float)Math.Clamp(v, -128, 127);
        }

        return result;
    }

    /// <summary>Box-Muller 标准正态随机数。</summary>
    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    /// <summary>把抖动后的通道值夹取到 [min, max]，用于 Lab 的 L 分量等有界量。</summary>
    private static float ClampLab(float value, float min, float max)
        => Math.Clamp(value, min, max);
}
