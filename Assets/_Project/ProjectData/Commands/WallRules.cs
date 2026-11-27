// Правила для стен и проёмов (задача B-11, FR-112). Используются командами,
// а интерфейс может вызывать CheckOpening заранее, чтобы показать "недопустимое действие".

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public struct OpeningSize
    {
        public float Width;
        public float Height;
        public float SillHeight;

        public OpeningSize(float width, float height, float sillHeight)
        {
            Width = width;
            Height = height;
            SillHeight = sillHeight;
        }
    }

    /// <summary>Стандартные размеры для кнопок «добавить дверь / окно».</summary>
    public static class OpeningPresets
    {
        public static OpeningSize Door { get { return new OpeningSize(0.9f, 2.1f, 0f); } }
        public static OpeningSize Window { get { return new OpeningSize(1.2f, 1.2f, 0.9f); } }
    }

    public static class WallRules
    {
        public const float MinWallLength = 0.1f;
        public const float MinOpeningSize = 0.1f;
        private const float Eps = 0.001f;

        public static float Length(PlanPoint a, PlanPoint b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static float Length(Wall wall)
        {
            return Length(wall.A, wall.B);
        }

        /// <summary>Проёмы указанной стены (кроме одного, если задан exceptOpeningId).</summary>
        public static List<Opening> OpeningsOf(Project project, string wallId, string exceptOpeningId = null)
        {
            var result = new List<Opening>();
            foreach (Opening o in project.Openings)
                if (o.WallId == wallId && o.Id != exceptOpeningId) result.Add(o);
            return result;
        }

        /// <summary>
        /// Проверяет, можно ли разместить проём. Возвращает null, если можно, иначе текст причины.
        /// otherOpenings - остальные проёмы этой же стены.
        /// </summary>
        public static string CheckOpening(float wallLength, float wallHeight, IEnumerable<Opening> otherOpenings,
            float offset, float width, float height, float sillHeight)
        {
            if (IsBad(offset) || IsBad(width) || IsBad(height) || IsBad(sillHeight))
                return "Размеры проёма заданы некорректно.";
            if (width < MinOpeningSize || height < MinOpeningSize)
                return "Ширина и высота проёма должны быть не меньше 0,1 м.";
            if (sillHeight < 0f)
                return "Высота нижнего края проёма не может быть отрицательной.";
            if (offset < -Eps || offset + width > wallLength + Eps)
                return "Проём выходит за границы стены по длине.";
            if (sillHeight + height > wallHeight + Eps)
                return "Проём выше стены.";

            foreach (Opening o in otherOpenings)
            {
                // Проёмы впритык (конец одного = начало другого) не считаются пересекающимися.
                if (offset < o.Offset + o.Width - Eps && o.Offset < offset + width - Eps)
                    return "Проём перекрывает другой проём ('" + o.Id + "').";
            }
            return null;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
