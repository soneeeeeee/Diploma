// Размещение предметов, висящих на стене (картины, полки, B-07). Дополняет PlacementService:
//  - сторона стены выбирается по тому, где указал пользователь, а при неоднозначности (точка у самой стены)
//    остаётся прежней, поэтому предмет на внутренней стене не «прыгает» между сторонами;
//  - высоту подвеса можно менять (по умолчанию берётся из каталога);
//  - положение вдоль стены привязывается к сетке.
// Проект не меняется. Не зависит от Unity.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public static class WallPlacement
    {
        /// <summary>Если точка дальше этого расстояния от оси стены, сторона берётся по ней, иначе по подсказке.</summary>
        public const float SideSwitchDistance = 0.3f;

        private const float Eps = 0.001f;
        private const float RadToDeg = 180f / (float)Math.PI;

        /// <param name="rawTarget">Точка плана БЕЗ привязки к сетке.</param>
        /// <param name="facingHint">Куда сейчас смотрит предмет (градусы). Используется, чтобы не менять сторону стены.</param>
        /// <param name="mountHeight">Высота центра задней стороны над полом. null - из каталога.</param>
        public static PlacementResult Place(Project project, CatalogItem item, PlanPoint rawTarget, int floor,
            SnapSettings snap, float? facingHint = null, float? mountHeight = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (item == null) throw new ArgumentNullException(nameof(item));

            Wall wall = null;
            float bestDistance = float.MaxValue, bestAlong = 0f;
            foreach (Wall w in PlacementService.WallsOnFloor(project, floor))
            {
                float along;
                float d = PlanGeometry.DistanceToSegment(rawTarget, w.A, w.B, out along);
                if (d < bestDistance) { bestDistance = d; bestAlong = along; wall = w; }
            }
            if (wall == null || bestDistance > PlacementService.WallSnapDistance)
                return PlacementResult.Fail("Рядом нет стены (ближе " +
                                            PlacementService.WallSnapDistance.ToString("0.#") + " м).");

            float length = WallRules.Length(wall);
            if (length < Eps) return PlacementResult.Fail("Стена нулевой длины.");
            float dx = (wall.B.X - wall.A.X) / length, dz = (wall.B.Z - wall.A.Z) / length;
            float nx = -dz, nz = dx;

            float side = (rawTarget.X - wall.A.X) * nx + (rawTarget.Z - wall.A.Z) * nz;
            bool positive;
            if (Math.Abs(side) > SideSwitchDistance || !facingHint.HasValue) positive = side >= 0f;
            else
            {
                float h = facingHint.Value * (float)Math.PI / 180f;
                positive = (float)Math.Sin(h) * nx + (float)Math.Cos(h) * nz >= 0f;
            }
            if (!positive) { nx = -nx; nz = -nz; }

            float along2 = bestAlong;
            if (snap != null) along2 = GridSnap.Snap(along2, snap.PositionStep);
            return PlaceOn(project, item, wall, along2, nx, nz, floor, snap, mountHeight);
        }

        /// <summary>Ставит предмет на конкретную сторону конкретной стены (нормаль nx, nz - в комнату).</summary>
        public static PlacementResult PlaceOn(Project project, CatalogItem item, Wall wall, float along,
            float nx, float nz, int floor, SnapSettings snap, float? mountHeight)
        {
            float length = WallRules.Length(wall);
            if (length < Eps) return PlacementResult.Fail("Стена нулевой длины.");
            float dx = (wall.B.X - wall.A.X) / length, dz = (wall.B.Z - wall.A.Z) / length;

            float width = item.Size.X, height = item.Size.Y;
            float half = wall.Thickness / 2f;
            if (length - 2f * half < width - Eps)
                return PlacementResult.Fail("Стена слишком короткая для этого предмета.");
            along = Math.Max(width / 2f + half, Math.Min(length - width / 2f - half, along));

            Room room = PlacementService.RoomOf(project, wall);
            float baseY = room != null ? room.Elevation : 0f;

            float mount = mountHeight ?? item.MountHeight;
            if (snap != null) mount = GridSnap.Snap(mount, snap.PositionStep);
            mount = Math.Max(height / 2f, Math.Min(wall.Height - height / 2f, mount));
            float bottom = mount - height / 2f, top = mount + height / 2f;
            if (bottom < -Eps || top > wall.Height + Eps)
                return PlacementResult.Fail("Предмет не помещается по высоте стены.");

            foreach (Opening o in WallRules.OpeningsOf(project, wall.Id))
            {
                bool overlapsAlong = along - width / 2f < o.Offset + o.Width - Eps && o.Offset < along + width / 2f - Eps;
                bool overlapsHeight = bottom < o.SillHeight + o.Height - Eps && o.SillHeight < top - Eps;
                if (overlapsAlong && overlapsHeight)
                    return PlacementResult.Fail("Предмет перекрыл бы " + (o.Type == OpeningType.Door ? "дверь." : "окно."));
            }

            float surfaceX = wall.A.X + dx * along + nx * half;
            float surfaceZ = wall.A.Z + dz * along + nz * half;
            if (!InsideAnyRoom(project, floor, surfaceX + nx * 0.05f, surfaceZ + nz * 0.05f))
                return PlacementResult.Fail("С этой стороны стены нет помещения.");

            return new PlacementResult
            {
                IsValid = true,
                Position = new Vec3(surfaceX, baseY + mount, surfaceZ),
                RotationY = (float)Math.Atan2(nx, nz) * RadToDeg,
                RoomId = room != null ? room.Id : null,
                WallId = wall.Id
            };
        }

        /// <summary>Допуск совпадения стены с границей помещения, метры (больше половины толщины обычной стены).</summary>
        public const float BoundaryTolerance = 0.2f;

        /// <summary>
        /// Основная стена: оба её конца лежат на одной стороне контура какого-либо помещения.
        /// Такие стены создаёт шаблон комнаты, их нельзя двигать, крутить, менять и удалять (только красить и добавлять окна и двери).
        /// Перегородки внутри комнаты - не основные.
        /// </summary>
        public static bool IsRoomBoundaryWall(Project project, Wall wall)
        {
            if (project == null || wall == null) return false;
            foreach (Room r in project.Rooms)
            {
                int n = r.Boundary.Count;
                for (int i = 0; i < n; i++)
                {
                    PlanPoint a = r.Boundary[i], b = r.Boundary[(i + 1) % n];
                    float along;
                    if (PlanGeometry.DistanceToSegment(wall.A, a, b, out along) <= BoundaryTolerance
                        && PlanGeometry.DistanceToSegment(wall.B, a, b, out along) <= BoundaryTolerance)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Находит стену, на которой висит предмет (ближайшая к нему стена, вплотную). null, если такой нет.</summary>
        public static Wall FindWallOf(Project project, PlacedObject obj, int floor)
        {
            Wall best = null;
            float bestD = float.MaxValue;
            foreach (Wall w in PlacementService.WallsOnFloor(project, floor))
            {
                float along;
                float d = PlanGeometry.DistanceToSegment(new PlanPoint(obj.Position.X, obj.Position.Z), w.A, w.B, out along);
                if (d < bestD) { bestD = d; best = w; }
            }
            return best != null && bestD <= best.Thickness / 2f + 0.1f ? best : null;
        }

        private static bool InsideAnyRoom(Project project, int floor, float x, float z)
        {
            foreach (Room r in project.Rooms)
                if (r.Floor == floor && PlanGeometry.Contains(r.Boundary, x, z)) return true;
            return false;
        }
    }
}
