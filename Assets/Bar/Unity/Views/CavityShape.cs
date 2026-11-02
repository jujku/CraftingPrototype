using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bar.Unity
{
    /// <summary>旋转对称内腔的形状：每个高度上的内腔半径。坐标是容器视图节点（ContainerVisual）的局部空间，单位米。由 InnerCavity 网格逐层切片得到；冰块碰撞墙、冰块防挤出、出生点和吧勺搅拌半径都读它，不再各自假设直筒。</summary>
    public sealed class CavityShape
    {
        /// <summary>直线段合并时允许的半径偏差，单位米；人为设定、未标定。</summary>
        public const float SegmentTolerance = .0005f;
        private readonly float[] radii;
        private readonly float first, step;
        /// <summary>旋转轴在局部 XZ 平面上的位置。</summary>
        public Vector2 Axis { get; }
        /// <summary>内腔底部高度。</summary>
        public float Bottom { get; }
        /// <summary>内腔顶部（满口）高度。</summary>
        public float Top { get; }
        /// <summary>是否由真实网格切片得到；为 false 时是包围盒直筒近似。</summary>
        public bool FromMesh { get; }
        /// <summary>切片层数。</summary>
        public int Samples => radii.Length;
        /// <summary>第 i 层的高度。</summary>
        public float HeightOf(int i) => first + step * i;
        /// <summary>第 i 层的半径。</summary>
        public float RadiusOf(int i) => radii[i];
        private CavityShape(Vector2 axis, float bottom, float top, float first, float step, float[] radii, bool fromMesh)
        { Axis = axis; Bottom = bottom; Top = top; this.first = first; this.step = step; this.radii = radii; FromMesh = fromMesh; }

        /// <summary>按内腔网格建立形状。</summary>
        /// <precondition>cavity 指向封闭内腔网格；frame 是冰块物理所在的局部空间（米制、Y 向上）。</precondition>
        /// <effects>网格可读时逐层求切面上离轴最近的点作为该层半径（偏保守）；网格不可读时退回包围盒直筒并 FromMesh=false。不修改网格。</effects>
        public static CavityShape FromCavity(MeshFilter cavity, Transform frame, int samples = 65)
        {
            if (!cavity || !cavity.sharedMesh || !frame) throw new ArgumentException("InnerCavity 缺失");
            var mesh = cavity.sharedMesh;
            if (!mesh.isReadable) return Cylinder(BoundsIn(cavity, frame));
            var source = mesh.vertices;
            var vertices = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++) vertices[i] = frame.InverseTransformPoint(cavity.transform.TransformPoint(source[i]));
            var triangles = mesh.triangles;
            float bottom = float.MaxValue, top = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var v in vertices)
            {
                bottom = Mathf.Min(bottom, v.y); top = Mathf.Max(top, v.y);
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
            }
            var axis = new Vector2((minX + maxX) * .5f, (minZ + maxZ) * .5f);
            // Slice just inside the caps: the flat bottom and top discs lie in the end planes and cross nothing there.
            // A 0.5mm inset overestimates the tiny section of a curved bowl at
            // shallow fills. Start just inside the actual cap instead; liquid
            // ripples and ice must not inherit a larger section from above.
            float inset = Mathf.Min(.000001f, (top - bottom) * .0001f);
            float firstHeight = bottom + inset, last = top - inset, stepHeight = (last - firstHeight) / (samples - 1);
            var radii = new float[samples];
            for (int s = 0; s < samples; s++)
            {
                float h = firstHeight + stepHeight * s, nearest = float.MaxValue;
                for (int t = 0; t < triangles.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        var a = vertices[triangles[t + e]]; var b = vertices[triangles[t + (e + 1) % 3]];
                        if ((a.y - h) * (b.y - h) >= 0) continue;
                        var p = Vector3.Lerp(a, b, (h - a.y) / (b.y - a.y));
                        nearest = Mathf.Min(nearest, new Vector2(p.x - axis.x, p.z - axis.y).magnitude);
                    }
                radii[s] = nearest < float.MaxValue ? nearest : s > 0 ? radii[s - 1] : 0;
            }
            return new CavityShape(axis, bottom, top, firstHeight, stepHeight, radii, true);
        }
        /// <summary>包围盒直筒近似，半径取较短的水平半轴；只在网格不可读时使用。</summary>
        public static CavityShape Cylinder(Bounds bounds)
        {
            float r = Mathf.Min(bounds.extents.x, bounds.extents.z);
            return new CavityShape(new Vector2(bounds.center.x, bounds.center.z), bounds.min.y, bounds.max.y, bounds.min.y, Mathf.Max(bounds.size.y, 1e-5f), new[] { r, r }, false);
        }
        /// <summary>内腔网格在 frame 空间中的包围盒。</summary>
        public static Bounds BoundsIn(MeshFilter cavity, Transform frame)
        {
            var m = cavity.sharedMesh.bounds; var bounds = new Bounds();
            for (int i = 0; i < 8; i++)
            {
                var q = frame.InverseTransformPoint(cavity.transform.TransformPoint(m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                if (i == 0) bounds = new Bounds(q, Vector3.zero); else bounds.Encapsulate(q);
            }
            return bounds;
        }
        /// <summary>任意高度的内腔半径；超出底和顶时取端点值。</summary>
        public float RadiusAt(float y)
        {
            if (radii.Length == 1) return radii[0];
            float f = (y - first) / step;
            if (f <= 0) return radii[0];
            if (f >= radii.Length - 1) return radii[radii.Length - 1];
            int i = (int)f; return Mathf.Lerp(radii[i], radii[i + 1], f - i);
        }
        /// <summary>[y0, y1] 高度区间内最窄处的半径。</summary>
        public float MinRadius(float y0, float y1)
        {
            float r = Mathf.Min(RadiusAt(y0), RadiusAt(y1));
            for (int i = 0; i < radii.Length; i++) { float h = HeightOf(i); if (h > y0 && h < y1) r = Mathf.Min(r, radii[i]); }
            return r;
        }
        /// <summary>最低的高度 y，使得从 y 往上 height 这一段的半径都不小于 halfWidth；放不下时返回 Top。</summary>
        public float LowestFit(float halfWidth, float height)
        {
            for (int i = 0; i < radii.Length; i++)
            {
                float y = i == 0 ? Bottom : HeightOf(i);
                if (y + height > Top + 1e-5f) break;
                if (MinRadius(y, y + height) >= halfWidth) return y;
            }
            return Top;
        }
        /// <summary>内腔体积，单位毫升（按切片积分）。</summary>
        public double VolumeMl
        {
            get
            {
                double ml = 0;
                for (int i = 0; i + 1 < radii.Length; i++) ml += Math.PI * (radii[i] * radii[i] + radii[i] * radii[i + 1] + radii[i + 1] * radii[i + 1]) / 3 * step;
                return ml * 1e6;
            }
        }
        /// <summary>按分层摆放估算能完整放进多少块边长 side 的冰，至少为 1。</summary>
        /// <precondition>side 为正。</precondition>
        /// <effects>从最低能放下一块的高度起每 side 一层；每层按最窄半径与边长之比查圆内放正方形的块数；顶部剩余不少于半块高时再算一层（冰可以斜靠）。既限制显示，也由宿主限制新工具加冰请求；不裁减已存在的核心冰量，修改算法会改变研究输入。</effects>
        public int CubeCapacity(float side)
        {
            float diagonal = side * .70710678f, y = LowestFit(diagonal, side);
            int total = 0;
            while (y + side * .5f <= Top + 1e-5f)
            {
                total += PerLayer(MinRadius(y, Mathf.Min(Top, y + side)) / side);
                y += side;
            }
            return Math.Max(1, total);
        }
        // Squares of unit side that fit in a circle of radius k: known minimal radii for 1–5, then an area estimate.
        private static int PerLayer(float k) => k < .7071f ? 0 : k < 1.118f ? 1 : k < 1.2747f ? 2 : k < 1.4143f ? 3 : k < 1.5f ? 4 : Math.Max(5, (int)(Math.PI * k * k * .62));
        /// <summary>把半径曲线合并成若干直线段（圆台），每段两端的半径偏差不超过 tolerance。</summary>
        public List<(float y0, float r0, float y1, float r1)> Segments(float tolerance = SegmentTolerance)
        {
            var result = new List<(float, float, float, float)>();
            int start = 0, n = radii.Length;
            if (n == 1) { result.Add((Bottom, radii[0], Top, radii[0])); return result; }
            while (start < n - 1)
            {
                int end = start + 1;
                while (end + 1 < n && Fits(start, end + 1, tolerance)) end++;
                result.Add((start == 0 ? Bottom : HeightOf(start), radii[start], end == n - 1 ? Top : HeightOf(end), radii[end]));
                start = end;
            }
            return result;
        }
        private bool Fits(int a, int b, float tolerance)
        {
            for (int i = a + 1; i < b; i++)
                if (Mathf.Abs(Mathf.Lerp(radii[a], radii[b], (float)(i - a) / (b - a)) - radii[i]) > tolerance) return false;
            return true;
        }
        /// <summary>一块碰撞墙板：中心、朝向（z 轴为墙厚方向，y 轴沿母线）和尺寸。</summary>
        public struct WallBox { public Vector3 Center; public Quaternion Rotation; public Vector3 Size; }
        /// <summary>沿每段圆台生成一圈厚墙板，墙板内表面贴着内腔母线；另加一块底板。</summary>
        /// <precondition>count ≥ 3，thickness 为正。</precondition>
        /// <effects>只计算几何，不创建物体；墙板沿母线两端各延长 overlap，盖住段与段之间的缝。</effects>
        public List<WallBox> Walls(int count, float thickness, float tolerance = SegmentTolerance)
        {
            var boxes = new List<WallBox>();
            float overlap = Mathf.Min(thickness * .5f, .005f);
            foreach (var (y0, r0, y1, r1) in Segments(tolerance))
            {
                float dr = r1 - r0, dy = y1 - y0, length = Mathf.Sqrt(dr * dr + dy * dy);
                if (length < 1e-6f) continue;
                float width = 2 * Mathf.Max(r0, r1) * Mathf.Tan(Mathf.PI / count) + thickness;
                for (int i = 0; i < count; i++)
                {
                    float a = i * 2 * Mathf.PI / count;
                    var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var slant = (n * dr + Vector3.up * dy) / length;
                    var outward = (n * dy - Vector3.up * dr) / length;
                    var surface = new Vector3(Axis.x, (y0 + y1) * .5f, Axis.y) + n * ((r0 + r1) * .5f);
                    boxes.Add(new WallBox { Center = surface + outward * thickness * .5f, Rotation = Quaternion.LookRotation(outward, slant), Size = new Vector3(width, length + 2 * overlap, thickness) });
                }
            }
            float floor = 2 * (RadiusAt(Bottom) + thickness);
            boxes.Add(new WallBox { Center = new Vector3(Axis.x, Bottom - thickness * .5f, Axis.y), Rotation = Quaternion.identity, Size = new Vector3(floor, thickness, floor) });
            return boxes;
        }
    }
}
