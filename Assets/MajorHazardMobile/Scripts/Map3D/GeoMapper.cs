using System;
using System.Collections.Generic;
using UnityEngine;

namespace LZW.Module.Map3DViewer.Scripts
{
    /// <summary>
    /// 模型中的一个地理锚点：模型里名为 <see cref="name"/> 的节点对应经纬度 (<see cref="longitude"/>, <see cref="latitude"/>)。
    /// </summary>
    [Serializable]
    public class GeoAnchor
    {
        public string name;
        public double longitude;
        public double latitude;

        public GeoAnchor(string name, double longitude, double latitude)
        {
            this.name = name;
            this.longitude = longitude;
            this.latitude = latitude;
        }
    }

    /// <summary>
    /// 经纬度 ⇄ 模型本地坐标（水平面 XZ）的仿射映射，由 ≥3 个锚点最小二乘拟合：
    /// x = a·lon + b·lat + c，z = d·lon + e·lat + f。
    /// 园区范围只有几公里，仿射足以吸收模型的平移 / 旋转 / 缩放 / 镜像；Y（高度）取锚点的平均高度。
    /// </summary>
    public class GeoMapper
    {
        // 以锚点经纬度中心为原点，避免 113.xx 这种大数参与最小二乘导致精度损失
        private readonly double _lon0;
        private readonly double _lat0;

        private readonly double[] _x; // a, b, c
        private readonly double[] _z; // d, e, f

        public float GroundHeight { get; }

        /// <summary>
        /// 拟合残差的最大值（模型本地单位），用于检查锚点是否配错。
        /// </summary>
        public float MaxResidual { get; }

        private GeoMapper(double lon0, double lat0, double[] x, double[] z, float groundHeight, float maxResidual)
        {
            _lon0 = lon0;
            _lat0 = lat0;
            _x = x;
            _z = z;
            GroundHeight = groundHeight;
            MaxResidual = maxResidual;
        }

        /// <param name="geo">锚点经纬度 (lon, lat)</param>
        /// <param name="local">锚点在模型本地坐标中的位置</param>
        public static GeoMapper Fit(IReadOnlyList<Vector2d> geo, IReadOnlyList<Vector3> local)
        {
            var n = geo.Count;
            if (n < 3 || local.Count != n)
                throw new ArgumentException($"需要至少 3 个锚点，实际 {n} 个");

            double lon0 = 0, lat0 = 0, y = 0;
            for (var i = 0; i < n; i++)
            {
                lon0 += geo[i].x;
                lat0 += geo[i].y;
                y += local[i].y;
            }
            lon0 /= n;
            lat0 /= n;

            // 法方程 (AᵀA) p = Aᵀb，A 的每行为 [dLon, dLat, 1]
            var ata = new double[3, 3];
            var atx = new double[3];
            var atz = new double[3];
            for (var i = 0; i < n; i++)
            {
                var row = new[] { geo[i].x - lon0, geo[i].y - lat0, 1d };
                for (var r = 0; r < 3; r++)
                {
                    for (var c = 0; c < 3; c++)
                        ata[r, c] += row[r] * row[c];
                    atx[r] += row[r] * local[i].x;
                    atz[r] += row[r] * local[i].z;
                }
            }

            var px = Solve3(ata, atx);
            var pz = Solve3(ata, atz);
            if (px == null || pz == null)
                throw new ArgumentException("锚点共线，无法拟合");

            var mapper = new GeoMapper(lon0, lat0, px, pz, (float)(y / n), 0f);

            var maxResidual = 0f;
            for (var i = 0; i < n; i++)
            {
                var p = mapper.ToLocal(geo[i].x, geo[i].y);
                maxResidual = Mathf.Max(maxResidual, new Vector2(p.x - local[i].x, p.z - local[i].z).magnitude);
            }

            return new GeoMapper(lon0, lat0, px, pz, mapper.GroundHeight, maxResidual);
        }

        /// <summary>
        /// 经纬度 → 模型本地坐标，高度为锚点平均高度 + heightOffset。
        /// </summary>
        public Vector3 ToLocal(double longitude, double latitude, float heightOffset = 0f)
        {
            var dLon = longitude - _lon0;
            var dLat = latitude - _lat0;
            return new Vector3(
                (float)(_x[0] * dLon + _x[1] * dLat + _x[2]),
                GroundHeight + heightOffset,
                (float)(_z[0] * dLon + _z[1] * dLat + _z[2]));
        }

        /// <summary>
        /// 模型本地坐标 → 经纬度 (lon, lat)，忽略高度。
        /// </summary>
        public Vector2d ToGeo(Vector3 local)
        {
            // 解 2x2：[a b; d e] [dLon dLat]ᵀ = [x - c, z - f]ᵀ
            var det = _x[0] * _z[1] - _x[1] * _z[0];
            var bx = local.x - _x[2];
            var bz = local.z - _z[2];
            var dLon = (bx * _z[1] - _x[1] * bz) / det;
            var dLat = (_x[0] * bz - bx * _z[0]) / det;
            return new Vector2d(_lon0 + dLon, _lat0 + dLat);
        }

        private static double[] Solve3(double[,] m, double[] b)
        {
            var a = (double[,])m.Clone();
            var x = (double[])b.Clone();

            // 带部分主元的高斯消元
            for (var col = 0; col < 3; col++)
            {
                var pivot = col;
                for (var r = col + 1; r < 3; r++)
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                        pivot = r;

                if (Math.Abs(a[pivot, col]) < 1e-18)
                    return null;

                if (pivot != col)
                {
                    for (var c = 0; c < 3; c++)
                        (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                    (x[col], x[pivot]) = (x[pivot], x[col]);
                }

                for (var r = col + 1; r < 3; r++)
                {
                    var f = a[r, col] / a[col, col];
                    for (var c = col; c < 3; c++)
                        a[r, c] -= f * a[col, c];
                    x[r] -= f * x[col];
                }
            }

            var result = new double[3];
            for (var r = 2; r >= 0; r--)
            {
                var sum = x[r];
                for (var c = r + 1; c < 3; c++)
                    sum -= a[r, c] * result[c];
                result[r] = sum / a[r, r];
            }
            return result;
        }
    }

    /// <summary>
    /// 双精度二维向量。经纬度用 float 只有约 1 米精度，这里保持 double。
    /// </summary>
    [Serializable]
    public struct Vector2d
    {
        public double x;
        public double y;

        public Vector2d(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public override string ToString() => $"({x:F6}, {y:F6})";
    }
}
