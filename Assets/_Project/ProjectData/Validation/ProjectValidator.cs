// Проверка целостности данных (раздел 6.1 ТЗ). Возвращает список понятных сообщений об ошибках;
// пустой список - проект корректен.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public static class ProjectValidator
    {
        private const float Eps = 0.001f;

        public static List<string> Validate(Project p)
        {
            var errors = new List<string>();
            if (p == null)
            {
                errors.Add("Проект равен null.");
                return errors;
            }

            if (p.Version != Project.CurrentSchemaVersion)
                errors.Add("Версия схемы " + p.Version + ", ожидалась " + Project.CurrentSchemaVersion + ".");
            if (string.IsNullOrWhiteSpace(p.Id)) errors.Add("У проекта нет id.");
            if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("У проекта нет названия.");

            // 1. Уникальность id во всём проекте.
            var allIds = new HashSet<string>();
            void Register(string kind, string id)
            {
                if (string.IsNullOrWhiteSpace(id)) { errors.Add(kind + ": пустой id."); return; }
                if (!allIds.Add(id)) errors.Add("Повторяющийся id '" + id + "' (" + kind + ").");
            }

            var rooms = new HashSet<string>();
            var walls = new Dictionary<string, Wall>();
            var openings = new Dictionary<string, Opening>();
            var objects = new HashSet<string>();

            foreach (var r in p.Rooms) { Register("помещение", r.Id); if (r.Id != null) rooms.Add(r.Id); }
            foreach (var w in p.Walls) { Register("стена", w.Id); if (w.Id != null && !walls.ContainsKey(w.Id)) walls[w.Id] = w; }
            foreach (var o in p.Openings) { Register("проём", o.Id); if (o.Id != null && !openings.ContainsKey(o.Id)) openings[o.Id] = o; }
            foreach (var o in p.Objects) { Register("объект", o.Id); if (o.Id != null) objects.Add(o.Id); }
            foreach (var l in p.Lights) Register("светильник", l.Id);
            foreach (var v in p.Variants) Register("вариант", v.Id);
            foreach (var s in p.SpaceSnapshots)
            {
                Register("снимок пространства", s.Id);
                foreach (var pl in s.Planes) Register("плоскость снимка", pl.Id);
                foreach (var vo in s.Volumes) Register("объём снимка", vo.Id);
            }

            // 1а. Снимки пространства: метки объёмов и качество.
            foreach (var s in p.SpaceSnapshots)
            {
                foreach (var vo in s.Volumes)
                {
                    if (!VolumeLabels.IsValid(vo.Label))
                        errors.Add("Объём снимка '" + vo.Id + "': недопустимая метка '" + vo.Label + "'.");
                }
                if (s.Quality < 0 || s.Quality > 100)
                    errors.Add("Снимок '" + s.Id + "': quality должно быть от 0 до 100.");
            }

            // 2. Помещения.
            foreach (var r in p.Rooms)
            {
                if (r.Boundary == null || r.Boundary.Count < 3)
                    errors.Add("Помещение '" + r.Id + "': в контуре меньше трёх точек.");
                if (r.Height <= 0f) errors.Add("Помещение '" + r.Id + "': высота должна быть больше нуля.");
            }

            // 3. Стены и проёмы.
            foreach (var w in p.Walls)
            {
                if (w.RoomId != null && !rooms.Contains(w.RoomId))
                    errors.Add("Стена '" + w.Id + "': помещение '" + w.RoomId + "' не найдено.");
                if (w.Thickness <= 0f) errors.Add("Стена '" + w.Id + "': толщина должна быть больше нуля.");
                if (w.Height <= 0f) errors.Add("Стена '" + w.Id + "': высота должна быть больше нуля.");
                if (WallLength(w) < Eps) errors.Add("Стена '" + w.Id + "': точки A и B совпадают.");

                foreach (string oid in w.OpeningIds)
                {
                    if (!openings.TryGetValue(oid, out Opening op))
                        errors.Add("Стена '" + w.Id + "': проём '" + oid + "' не найден.");
                    else if (op.WallId != w.Id)
                        errors.Add("Проём '" + oid + "' указан в стене '" + w.Id + "', но его wallId = '" + op.WallId + "'.");
                }
            }

            foreach (var op in p.Openings)
            {
                if (!walls.TryGetValue(op.WallId ?? "", out Wall wall))
                {
                    errors.Add("Проём '" + op.Id + "': стена '" + op.WallId + "' не найдена.");
                    continue;
                }
                if (!wall.OpeningIds.Contains(op.Id))
                    errors.Add("Проём '" + op.Id + "' не перечислен в openingIds стены '" + wall.Id + "'.");
                if (op.Width <= 0f || op.Height <= 0f)
                    errors.Add("Проём '" + op.Id + "': ширина и высота должны быть больше нуля.");
                if (op.Offset < -Eps || op.Offset + op.Width > WallLength(wall) + Eps)
                    errors.Add("Проём '" + op.Id + "' выходит за границы стены '" + wall.Id + "' по длине.");
                if (op.SillHeight + op.Height > wall.Height + Eps)
                    errors.Add("Проём '" + op.Id + "' выше стены '" + wall.Id + "'.");
            }

            // 4. Объекты, материалы, варианты.
            foreach (var o in p.Objects)
                if (string.IsNullOrWhiteSpace(o.CatalogId))
                    errors.Add("Объект '" + o.Id + "': не указан catalogId.");

            foreach (var m in p.MaterialOverrides)
            {
                if (!allIds.Contains(m.TargetId ?? ""))
                    errors.Add("Замена материала: элемент '" + m.TargetId + "' не найден.");
                if (string.IsNullOrWhiteSpace(m.MaterialId))
                    errors.Add("Замена материала для '" + m.TargetId + "': не указан materialId.");
            }

            var variantIds = new HashSet<string>();
            foreach (var v in p.Variants)
            {
                variantIds.Add(v.Id ?? "");
                foreach (var st in v.ObjectStates)
                    if (!objects.Contains(st.ObjectId ?? ""))
                        errors.Add("Вариант '" + v.Id + "': объект '" + st.ObjectId + "' не найден.");
            }
            if (p.Variants.Count > 0 && !variantIds.Contains(p.ActiveVariantId ?? ""))
                errors.Add("activeVariantId '" + p.ActiveVariantId + "' не найден среди вариантов.");

            // 5. Результаты сравнения: элемент может быть из проекта ИЛИ из снимка (для "лишнего").
            foreach (var c in p.ComparisonResults)
                if (!allIds.Contains(c.ElementId ?? ""))
                    errors.Add("Результат сравнения: элемент '" + c.ElementId + "' не найден.");

            return errors;
        }

        private static float WallLength(Wall w)
        {
            float dx = w.B.X - w.A.X;
            float dz = w.B.Z - w.A.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
