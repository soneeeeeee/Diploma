// Прямоугольная "коробка" на плане с диапазоном высот. Нужна для проверки пересечений (B-09).
// Положение в плане - повёрнутый прямоугольник; пересечение двух таких прямоугольников определяется
// методом разделяющих осей. Касание коробок пересечением НЕ считается.

using System;

namespace XrInterior.ProjectData
{
    public struct OrientedBox
    {
        public float CenterX;
        public float CenterZ;
        /// <summary>Половина размера вдоль "вправо" коробки.</summary>
        public float HalfWidth;
        /// <summary>Половина размера вдоль "вперёд" коробки.</summary>
        public float HalfDepth;
        public float YawDegrees;
        public float YMin;
        public float YMax;

        private const float Eps = 0.001f;

        public OrientedBox(float centerX, float centerZ, float halfWidth, float halfDepth,
            float yawDegrees, float yMin, float yMax)
        {
            CenterX = centerX;
            CenterZ = centerZ;
            HalfWidth = halfWidth;
            HalfDepth = halfDepth;
            YawDegrees = yawDegrees;
            YMin = yMin;
            YMax = yMax;
        }

        public bool Intersects(OrientedBox other)
        {
            // По высоте коробки должны перекрываться больше, чем на допуск.
            if (YMin >= other.YMax - Eps || other.YMin >= YMax - Eps) return false;

            float a = YawDegrees * (float)Math.PI / 180f;
            float b = other.YawDegrees * (float)Math.PI / 180f;
            float aRx = (float)Math.Cos(a), aRz = -(float)Math.Sin(a);   // "вправо"
            float aFx = (float)Math.Sin(a), aFz = (float)Math.Cos(a);    // "вперёд"
            float bRx = (float)Math.Cos(b), bRz = -(float)Math.Sin(b);
            float bFx = (float)Math.Sin(b), bFz = (float)Math.Cos(b);

            float dx = other.CenterX - CenterX;
            float dz = other.CenterZ - CenterZ;

            // Четыре оси проверки: две оси первой коробки и две оси второй.
            float[,] axes = { { aRx, aRz }, { aFx, aFz }, { bRx, bRz }, { bFx, bFz } };
            for (int i = 0; i < 4; i++)
            {
                float ax = axes[i, 0], az = axes[i, 1];

                float radiusA = HalfWidth * Math.Abs(aRx * ax + aRz * az) + HalfDepth * Math.Abs(aFx * ax + aFz * az);
                float radiusB = other.HalfWidth * Math.Abs(bRx * ax + bRz * az)
                                + other.HalfDepth * Math.Abs(bFx * ax + bFz * az);
                float distance = Math.Abs(dx * ax + dz * az);

                if (radiusA + radiusB - distance <= Eps) return false; // ось разделяет коробки (или они только касаются)
            }
            return true;
        }
    }

    public static class ItemBounds
    {
        /// <summary>
        /// Коробка предмета с учётом точки привязки (pivot): пол - центр основания; стена - центр задней стороны;
        /// потолок - центр верхней стороны.
        /// </summary>
        public static OrientedBox ForItem(CatalogItem item, Vec3 position, float yawDegrees, Vec3 scale)
        {
            float sx = item.Size.X * Math.Abs(scale.X);
            float sy = item.Size.Y * Math.Abs(scale.Y);
            float sz = item.Size.Z * Math.Abs(scale.Z);
            float yaw = yawDegrees * (float)Math.PI / 180f;
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);

            switch (item.Placement)
            {
                case PlacementRule.Wall:
                    return new OrientedBox(position.X + fx * sz / 2f, position.Z + fz * sz / 2f,
                        sx / 2f, sz / 2f, yawDegrees, position.Y - sy / 2f, position.Y + sy / 2f);

                case PlacementRule.Ceiling:
                    return new OrientedBox(position.X, position.Z, sx / 2f, sz / 2f,
                        yawDegrees, position.Y - sy, position.Y);

                default:
                    return new OrientedBox(position.X, position.Z, sx / 2f, sz / 2f,
                        yawDegrees, position.Y, position.Y + sy);
            }
        }

        /// <summary>Коробка стены: толщина - поперёк, длина - вдоль, от пола помещения до верха стены.</summary>
        public static OrientedBox ForWall(Wall wall, float baseY)
        {
            float dx = wall.B.X - wall.A.X;
            float dz = wall.B.Z - wall.A.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            float yaw = (float)Math.Atan2(-dz, dx) * 180f / (float)Math.PI; // "вправо" коробки совпадает с направлением стены

            return new OrientedBox((wall.A.X + wall.B.X) / 2f, (wall.A.Z + wall.B.Z) / 2f,
                length / 2f, wall.Thickness / 2f, yaw, baseY, baseY + wall.Height);
        }
    }
}
