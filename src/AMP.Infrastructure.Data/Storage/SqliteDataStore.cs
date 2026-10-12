using System.Globalization;
using System.Text.Json;
using AMP.Domain.Data;
using AMP.Domain.Instrument;
using AMP.Domain.Motion;
using AMP.Infrastructure.Data.Configuration;
using Microsoft.Data.Sqlite;

namespace AMP.Infrastructure.Data.Storage;

/// <summary>
/// SQLite 存储：同时实现 <see cref="IMotionStateStore"/>（数据库模拟运动的轴位置持久化）、
 /// <see cref="IStandardDataStore"/>（数据库模拟测量的标准数据源）与
 /// <see cref="IMeasureResultStore"/>（模拟测量结果落库）。
/// <para>首次运行自动建表并写入内置标准数据（SVD砖块 8×31 通道、计量标准 5 行 Lab、白板）。</para>
/// </summary>
public sealed class SqliteDataStore : IMotionStateStore, IStandardDataStore, IMeasureResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _dbPath;
    private readonly bool _seedOnFirstRun;
    private readonly object _sync = new();
    private bool _initialized;

    public SqliteDataStore(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dbPath = options.FileName;
        _seedOnFirstRun = options.SeedStandardDataOnFirstRun;
    }

    public string DatabasePath => _dbPath;

    // ---------- IMotionStateStore ----------

    public Task<AxisPosition> LoadAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        lock (_sync)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT x, y, z, updated_at FROM motion_state WHERE id = 1";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(AxisPosition.Origin());
            }

            return Task.FromResult(new AxisPosition(
                reader.GetDouble(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }
    }

    public Task SaveAsync(AxisPosition position, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        EnsureInitialized();
        lock (_sync)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO motion_state (id, x, y, z, updated_at) VALUES (1, @x, @y, @z, @u)
                ON CONFLICT(id) DO UPDATE SET x = @x, y = @y, z = @z, updated_at = @u
                """;
            cmd.Parameters.AddWithValue("@x", position.X);
            cmd.Parameters.AddWithValue("@y", position.Y);
            cmd.Parameters.AddWithValue("@z", position.Z);
            cmd.Parameters.AddWithValue("@u", position.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }
    }

    // ---------- IStandardDataStore ----------

    public Task<IReadOnlyList<StandardRow>> LoadAsync(string? setName = null, CancellationToken ct = default)
    {
        EnsureInitialized();
        lock (_sync)
        {
            var rows = new List<StandardRow>();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(setName))
            {
                cmd.CommandText = "SELECT set_name, row_index, \"values\", lab FROM standard_data ORDER BY set_name, row_index";
            }
            else
            {
                cmd.CommandText = "SELECT set_name, row_index, \"values\", lab FROM standard_data WHERE set_name = @name ORDER BY row_index";
                cmd.Parameters.AddWithValue("@name", setName);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var values = JsonSerializer.Deserialize<float[]>(reader.GetString(2), JsonOptions) ?? [];
                LabValue? lab = null;
                if (!reader.IsDBNull(3))
                {
                    lab = JsonSerializer.Deserialize<LabValue>(reader.GetString(3), JsonOptions);
                }

                rows.Add(new StandardRow(reader.GetString(0), reader.GetInt32(1), values, lab));
            }

            return Task.FromResult<IReadOnlyList<StandardRow>>(rows);
        }
    }

    // ---------- IMeasureResultStore ----------

    public Task SaveAsync(MeasureResultRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureInitialized();
        lock (_sync)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO measure_result (set_name, row_index, source, "values", created_at)
                VALUES (@set, @row, @source, @values, @created)
                """;
            cmd.Parameters.AddWithValue("@set", record.SetName);
            cmd.Parameters.AddWithValue("@row", record.RowIndex);
            cmd.Parameters.AddWithValue("@source", record.Source);
            cmd.Parameters.AddWithValue("@values", JsonSerializer.Serialize(record.Values, JsonOptions));
            cmd.Parameters.AddWithValue("@created", record.Timestamp.ToString("o", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }
    }

    // ---------- 初始化与建表 ----------

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var conn = OpenConnection();
            CreateTables(conn);
            if (_seedOnFirstRun)
            {
                SeedIfEmpty(conn);
            }

            _initialized = true;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private static void CreateTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS motion_state (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL,
                updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS standard_data (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                set_name TEXT NOT NULL, row_index INTEGER NOT NULL,
                "values" TEXT NOT NULL, lab TEXT NULL,
                updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS measure_result (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                set_name TEXT NOT NULL, row_index INTEGER NOT NULL,
                source TEXT NOT NULL, "values" TEXT NOT NULL,
                created_at TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    private void SeedIfEmpty(SqliteConnection conn)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM standard_data";
        if (Convert.ToInt64(countCmd.ExecuteScalar()) > 0)
        {
            return;
        }

        // SVD砖块：8 行 × 31 通道反射率曲线。
        for (var i = 0; i < 8; i++)
        {
            InsertStandardRow(conn, "SVD砖块", i + 1, BuildSvdReflectance(i), null);
        }

        // 计量标准：5 行 Lab 三元组。
        LabValue[] metrologyLabs =
        [
            new(55, 12, 8),
            new(70, -8, 5),
            new(85, -3, 15),
            new(40, 18, -12),
            new(62, 5, -18),
        ];
        for (var i = 0; i < metrologyLabs.Length; i++)
        {
            InsertStandardRow(conn, "计量标准", i + 1,
                [(float)metrologyLabs[i].L, (float)metrologyLabs[i].a, (float)metrologyLabs[i].b],
                metrologyLabs[i]);
        }

        // 白板（重复性测试目标）。
        InsertStandardRow(conn, "白板", 1, [95f, -0.5f, 2f], new LabValue(95, -0.5, 2));
    }

    private static float[] BuildSvdReflectance(int brickIndex)
    {
        var values = new float[31];
        var baseReflectance = 0.04 + 0.85 * (brickIndex + 1) / 8.0;
        for (var k = 0; k < 31; k++)
        {
            var shape = 0.75 + 0.25 * Math.Sin(k * 0.35 + brickIndex * 1.7);
            var v = baseReflectance * shape;
            values[k] = (float)Math.Clamp(v, 0.01, 0.99);
        }

        return values;
    }

    private static void InsertStandardRow(SqliteConnection conn, string setName, int rowIndex, float[] values, LabValue? lab)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO standard_data (set_name, row_index, "values", lab, updated_at)
            VALUES (@set, @row, @values, @lab, @updated)
            """;
        cmd.Parameters.AddWithValue("@set", setName);
        cmd.Parameters.AddWithValue("@row", rowIndex);
        cmd.Parameters.AddWithValue("@values", JsonSerializer.Serialize(values, JsonOptions));
        cmd.Parameters.AddWithValue("@lab", lab is null ? DBNull.Value : JsonSerializer.Serialize(lab, JsonOptions));
        cmd.Parameters.AddWithValue("@updated", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }
}
