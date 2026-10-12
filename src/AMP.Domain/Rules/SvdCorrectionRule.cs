using AMP.Domain.Data;
using AMP.Domain.Instrument;

namespace AMP.Domain.Rules;

/// <summary>
/// SVD 修正规则的骨架实现：用「正规方程 + 岭正则」的最小二乘求解修正矩阵
 /// C（ChannelCount × ChannelCount），使 实测 × C ≈ 标准。
/// <para>
/// 说明：真实项目请替换为 MathNet.Numerics 的 SVD 分解（<c>Matrix.PseudoInverse</c>）实现，
 /// 接口与结果契约保持不变，上层流程代码无需改动。
/// </para>
/// </summary>
public sealed class SvdCorrectionRule : ISvdCorrectionRule
{
    private const double RidgeLambda = 1e-6;

    public CorrectionResult Calculate(IReadOnlyList<MeasureData> measured, IReadOnlyList<StandardRow> standards)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(standards);

        if (measured.Count == 0)
        {
            throw new InvalidOperationException("SVD 修正缺少砖块实测数据。");
        }

        if (standards.Count == 0)
        {
            throw new InvalidOperationException("SVD 修正缺少标准数据。");
        }

        var channelCount = Math.Min(measured[0].Values.Count, standards[0].Values.Count);
        if (channelCount <= 0)
        {
            throw new InvalidOperationException("SVD 修正通道数为 0。");
        }

        var rowCount = Math.Min(measured.Count, standards.Count);

        // M：实测矩阵（rowCount × channelCount），S：标准矩阵（rowCount × channelCount）。
        var m = new double[rowCount, channelCount];
        var s = new double[rowCount, channelCount];
        for (var i = 0; i < rowCount; i++)
        {
            for (var k = 0; k < channelCount; k++)
            {
                m[i, k] = measured[i].Values[k];
                s[i, k] = standards[i].Values[k];
            }
        }

        // 正规方程：A = MᵀM + λI，B = MᵀS；对 B 的每一列解 A·c = b。
        var a = new double[channelCount, channelCount];
        var b = new double[channelCount, channelCount];
        for (var i = 0; i < rowCount; i++)
        {
            for (var p = 0; p < channelCount; p++)
            {
                for (var q = 0; q < channelCount; q++)
                {
                    a[p, q] += m[i, p] * m[i, q];
                    b[p, q] += m[i, p] * s[i, q];
                }
            }
        }

        for (var k = 0; k < channelCount; k++)
        {
            a[k, k] += RidgeLambda;
        }

        // 解算修正矩阵（行主序铺平：Matrix[j * channelCount + k] = 第 j 目标通道的第 k 个系数）。
        var matrix = new double[channelCount * channelCount];
        for (var j = 0; j < channelCount; j++)
        {
            var rhs = new double[channelCount];
            for (var k = 0; k < channelCount; k++)
            {
                rhs[k] = b[k, j];
            }

            var solution = SolveLinearSystem(a, rhs);
            for (var k = 0; k < channelCount; k++)
            {
                matrix[j * channelCount + k] = solution[k];
            }
        }

        // 残差 RMSE = || 实测 × C − 标准 || / √(行数×通道数)。
        var squaredSum = 0d;
        for (var i = 0; i < rowCount; i++)
        {
            for (var j = 0; j < channelCount; j++)
            {
                var predicted = 0d;
                for (var k = 0; k < channelCount; k++)
                {
                    predicted += m[i, k] * matrix[j * channelCount + k];
                }

                var error = predicted - s[i, j];
                squaredSum += error * error;
            }
        }

        var rmse = Math.Sqrt(squaredSum / (rowCount * channelCount));
        var message = $"修正矩阵 {channelCount}×{channelCount} 计算完成（{rowCount} 块砖），残差 RMSE = {rmse:F4}。";
        return new CorrectionResult(channelCount, matrix, rmse, message);
    }

    /// <summary>高斯-若尔当消元解线性方程组（带列主元）。</summary>
    private static double[] SolveLinearSystem(double[,] a, double[] b)
    {
        var n = b.Length;
        var aug = new double[n, n + 1];
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                aug[r, c] = a[r, c];
            }

            aug[r, n] = b[r];
        }

        for (var col = 0; col < n; col++)
        {
            // 选主元
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(aug[r, col]) > Math.Abs(aug[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(aug[pivot, col]) < 1e-15)
            {
                continue; // 奇异（岭正则后不应发生），该列解保持 0。
            }

            if (pivot != col)
            {
                for (var c = 0; c <= n; c++)
                {
                    (aug[col, c], aug[pivot, c]) = (aug[pivot, c], aug[col, c]);
                }
            }

            // 归一化主元行
            var diag = aug[col, col];
            for (var c = col; c <= n; c++)
            {
                aug[col, c] /= diag;
            }

            // 消去其他行
            for (var r = 0; r < n; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var factor = aug[r, col];
                if (Math.Abs(factor) < 1e-15)
                {
                    continue;
                }

                for (var c = col; c <= n; c++)
                {
                    aug[r, c] -= factor * aug[col, c];
                }
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = aug[i, n];
        }

        return x;
    }
}
