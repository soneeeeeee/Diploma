// Простая геометрия на плане (B-07): точка в многоугольнике, габариты, расстояние до отрезка.
// Не зависит от Unity.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public static class PlanGeometry
    {
        /// <summary>Лежит ли точка внутри многоугольника (подходит для Г-образных и любых невыпуклых помещений).</summary>
        public static bool Contains(IList<PlanPoint> polygon, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                PlanPoint a = polygon[i], b = polygon[j];
                if ((a.Z > z) != (b.Z > z) && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        public static void Bounds(IList<PlanPoint> polygon, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = 0f; maxX = 0f; minZ = 0f; maxZ = 0f;
            if (polygon == null || polygon.Count == 0) return;
            minX = float.MaxValue; maxX = float.MinValue; minZ = float.MaxValue; maxZ = float.MinValue;
            foreach (PlanPoint p in polygon)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            }
        }

        /// <summary>Расстояние от точки до отрезка AB; along - проекция на линию AB в метрах от A (без ограничения).</summary>
        public static float DistanceToSegment(PlanPoint p, PlanPoint a, PlanPoint b, out float along)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-6f)
            {
                along = 0f;
                return Dist(p.X, p.Z, a.X, a.Z);
            }

            along = ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length;
            float c = Math.Max(0f, Math.Min(length, along));
            return Dist(p.X, p.Z, a.X + dx / length * c, a.Z + dz / length * c);
        }

        public static float Dist(float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1, dz = z2 - z1;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Точка внутри помещения, ближайшая к центру его габаритов (для Г-образной формы центр может оказаться снаружи).</summary>
        public static PlanPoint InteriorPoint(IList<PlanPoint> polygon)
        {
            float minX, maxX, minZ, maxZ;
            Bounds(polygon, out minX, out maxX, out minZ, out maxZ);
            float cx = (minX + maxX) / 2f, cz = (minZ + maxZ) / 2f;
            if (Contains(polygon, cx, cz)) return new PlanPoint(cx, cz);

            const float step = 0.25f;
            float bestD = float.MaxValue;
            PlanPoint best = polygon[0];
            for (float x = minX + step; x < maxX; x += step)
                for (float z = minZ + step; z < maxZ; z += step)
                {
                    if (!Contains(polygon, x, z)) continue;
                    float d = Dist(x, z, cx, cz);
                    if (d < bestD) { bestD = d; best = new PlanPoint(x, z); }
                }
            return best;
        }
    }
}
