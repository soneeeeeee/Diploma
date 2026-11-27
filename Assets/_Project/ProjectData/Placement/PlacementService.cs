// Правила размещения предметов каталога (B-08, FR-102).
// По точке, на которую указал пользователь, находит допустимое положение и поворот предмета
// либо сообщает причину отказа. Проект при этом НЕ меняется: для добавления используйте
// PlacementService.CreateObject и AddObjectCommand.
//
// Проверяются: помещение, высота, углы предмета (не в стене и не за пределами комнаты), прилипание к стене,
// проёмы. Пересечения с предметами и стенами «насквозь» проверяет CollisionService (B-09).

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public sealed class PlacementResult
    {
        public bool IsValid { get; set; }
        /// <summary>Причина отказа (для уведомления пользователю), если IsValid = false.</summary>
        public string Reason { get; set; }
        public Vec3 Position { get; set; }
        /// <summary>Поворот вокруг вертикальной оси, градусы.</summary>
        public float RotationY { get; set; }
        public string RoomId { get; set; }
        /// <summary>Стена, к которой предмет прижат (если прижат).</summary>
        public string WallId { get; set; }

        public static PlacementResult Fail(string reason)
        {
            return new PlacementResult { IsValid = false, Reason = reason };
        }
    }

    public static class PlacementService
    {
        /// <summary>На каком расстоянии от стены предмет «прилипает» к ней, метры.</summary>
        public const float WallSnapDistance = 1.0f;

        private const float Eps = 0.001f;
        private const float RadToDeg = 180f / (float)Math.PI;
        private const float DegToRad = (float)Math.PI / 180f;

        /// <param name="target">Точка в плане, на которую указал пользователь.</param>
        /// <param name="userYaw">Поворот, выбранный пользователем (для предметов, которые не прижимаются к стене).</param>
        /// <param name="floor">Номер этажа.</param>
        /// <param name="snap">Привязка к сетке (FR-104). null - без привязки.</param>
        public static PlacementResult Place(Project project, CatalogItem item, PlanPoint target,
            float userYaw = 0f, int floor = 1, SnapSettings snap = null)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (snap != null)
            {
                target = new PlanPoint(GridSnap.Snap(target.X, snap.PositionStep),
                                       GridSnap.Snap(target.Z, snap.PositionStep));
                userYaw = GridSnap.SnapYaw(userYaw, snap.RotationStepDegrees);
            }

            switch (item.Placement)
            {
                case PlacementRule.Wall:
                {
                    PlacementResult onWall = PlaceOnWall(project, item, target, floor, false);
                    return onWall ?? PlacementResult.Fail(
                        "Рядом нет стены (ближе " + WallSnapDistance.ToString("0.#") + " м).");
                }

                case PlacementRule.Ceiling:
                    return PlaceOnCeiling(project, item, target, userYaw, floor);

                default:
                    if (item.AlignToWall)
                    {
                        PlacementResult aligned = PlaceOnWall(project, item, target, floor, true);
                        if (aligned != null) return aligned;
                    }
                    return PlaceOnFloor(project, item, target, userYaw, floor);
            }
        }

        /// <summary>Создаёт объект проекта по результату размещения. Добавлять его нужно командой AddObjectCommand.</summary>
        public static PlacedObject CreateObject(CatalogItem item, PlacementResult placement,
            ElementState state = ElementState.Proposed)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (placement == null || !placement.IsValid)
                throw new InvalidOperationException("Нельзя создать объект: размещение недопустимо.");

            return new PlacedObject
            {
                Id = IdGenerator.New("obj"),
                CatalogId = item.Id,
                Position = placement.Position,
                Rotation = new Vec3(0f, placement.RotationY, 0f),
                Layer = LayerIds.Furniture,
                State = state
            };
        }

        // ---------- на пол ----------

        private static PlacementResult PlaceOnFloor(Project project, CatalogItem item, PlanPoint target,
            float yaw, int floor)
        {
            Room room = FindRoom(project, target, floor);
            if (room == null) return PlacementResult.Fail("Точка вне помещения.");
            if (item.Size.Y > room.Height + Eps) return PlacementResult.Fail("Предмет выше помещения.");

            string problem = CheckFootprint(project, room, floor, item, target.X, target.Z, yaw);
            if (problem != null) return PlacementResult.Fail(problem);

            return new PlacementResult
            {
                IsValid = true,
                Position = new Vec3(target.X, room.Elevation, target.Z),
                RotationY = yaw,
                RoomId = room.Id
            };
        }

        // ---------- на потолок ----------

        private static PlacementResult PlaceOnCeiling(Project project, CatalogItem item, PlanPoint target,
            float yaw, int floor)
        {
            Room room = FindRoom(project, target, floor);
            if (room == null) return PlacementResult.Fail("Точка вне помещения.");
            if (item.Size.Y > room.Height + Eps) return PlacementResult.Fail("Предмет длиннее высоты помещения.");

            string problem = CheckFootprint(project, room, floor, item, target.X, target.Z, yaw);
            if (problem != null) return PlacementResult.Fail(problem);

            return new PlacementResult
            {
                IsValid = true,
                Position = new Vec3(target.X, room.Elevation + room.Height, target.Z),
                RotationY = yaw,
                RoomId = room.Id
            };
        }

        // ---------- к стене (настенные предметы и прилипание напольных) ----------

        // Возвращает null, если подходящей стены рядом нет.
        private static PlacementResult PlaceOnWall(Project project, CatalogItem item, PlanPoint target,
            int floor, bool onFloor)
        {
            Wall wall = null;
            float bestDistance = float.MaxValue;
            float bestAlong = 0f;
            foreach (Wall w in WallsOnFloor(project, floor))
            {
                float along;
                float distance = DistanceToSegment(target, w.A, w.B, out along);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAlong = along;
                    wall = w;
                }
            }
            if (wall == null || bestDistance > WallSnapDistance) return null;

            float length = WallRules.Length(wall);
            if (length < Eps) return null;

            // Единичный вектор вдоль стены и нормаль, направленная в ту сторону, где указал пользователь.
            float dx = (wall.B.X - wall.A.X) / length;
            float dz = (wall.B.Z - wall.A.Z) / length;
            float nx = -dz;
            float nz = dx;
            if ((target.X - wall.A.X) * nx + (target.Z - wall.A.Z) * nz < 0f)
            {
                nx = -nx;
                nz = -nz;
            }

            float width = item.Size.X;
            float depth = item.Size.Z;
            float height = item.Size.Y;
            float half = wall.Thickness / 2f;

            if (length - 2f * half < width - Eps)
                return PlacementResult.Fail("Стена слишком короткая для этого предмета.");
            float along2 = Clamp(bestAlong, width / 2f + half, length - width / 2f - half);

            Room room = RoomOf(project, wall);
            float baseY = room != null ? room.Elevation : 0f;

            // Точка на поверхности стены (со стороны пользователя).
            float surfaceX = wall.A.X + dx * along2 + nx * half;
            float surfaceZ = wall.A.Z + dz * along2 + nz * half;

            float bottom, top, px, pz, py;
            if (onFloor)
            {
                bottom = 0f;
                top = height;
                px = surfaceX + nx * depth / 2f;
                pz = surfaceZ + nz * depth / 2f;
                py = baseY;
            }
            else
            {
                bottom = item.MountHeight - height / 2f;
                top = item.MountHeight + height / 2f;
                px = surfaceX;
                pz = surfaceZ;
                py = baseY + item.MountHeight;
            }

            if (bottom < -Eps || top > wall.Height + Eps)
                return PlacementResult.Fail("Предмет не помещается по высоте стены.");

            foreach (Opening o in WallRules.OpeningsOf(project, wall.Id))
            {
                bool overlapsAlong = along2 - width / 2f < o.Offset + o.Width - Eps
                                     && o.Offset < along2 + width / 2f - Eps;
                bool overlapsHeight = bottom < o.SillHeight + o.Height - Eps && o.SillHeight < top - Eps;
                if (overlapsAlong && overlapsHeight)
                    return PlacementResult.Fail("Предмет перекрыл бы " +
                                                (o.Type == OpeningType.Door ? "дверь." : "окно."));
            }

            // Со стороны пользователя должно быть помещение, а не улица.
            if (!IsInsideAnyRoom(project, floor, surfaceX + nx * 0.05f, surfaceZ + nz * 0.05f))
                return PlacementResult.Fail("Предмет оказался бы вне помещения.");

            return new PlacementResult
            {
                IsValid = true,
                Position = new Vec3(px, py, pz),
                RotationY = (float)Math.Atan2(nx, nz) * RadToDeg, // передняя сторона смотрит от стены
                RoomId = room != null ? room.Id : null,
                WallId = wall.Id
            };
        }

        // ---------- геометрия ----------

        // Углы основания предмета должны быть внутри комнаты и не в толще стен.
        private static string CheckFootprint(Project project, Room room, int floor, CatalogItem item,
            float cx, float cz, float yawDegrees)
        {
            float yaw = yawDegrees * DegToRad;
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);   // вперёд
            float rx = (float)Math.Cos(yaw), rz = -(float)Math.Sin(yaw);  // вправо
            float hx = item.Size.X / 2f, hz = item.Size.Z / 2f;

            List<Wall> walls = WallsOnFloor(project, floor);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float x = cx + rx * sx * hx + fx * sz * hz;
                    float z = cz + rz * sx * hx + fz * sz * hz;

                    if (!PointInPolygon(room.Boundary, x, z)) return "Предмет выходит за стены помещения.";

                    foreach (Wall w in walls)
                    {
                        float along;
                        if (DistanceToSegment(new PlanPoint(x, z), w.A, w.B, out along) < w.Thickness / 2f - Eps)
                            return "Предмет упирается в стену.";
                    }
                }
            }
            return null;
        }

        private static Room FindRoom(Project project, PlanPoint p, int floor)
        {
            foreach (Room r in project.Rooms)
                if (r.Floor == floor && PointInPolygon(r.Boundary, p.X, p.Z)) return r;
            return null;
        }

        private static bool IsInsideAnyRoom(Project project, int floor, float x, float z)
        {
            foreach (Room r in project.Rooms)
                if (r.Floor == floor && PointInPolygon(r.Boundary, x, z)) return true;
            return false;
        }

        internal static Room RoomOf(Project project, Wall wall)
        {
            if (wall.RoomId == null) return null;
            return project.Rooms.Find(r => r.Id == wall.RoomId);
        }

        internal static List<Wall> WallsOnFloor(Project project, int floor)
        {
            var floorRooms = new HashSet<string>();
            foreach (Room r in project.Rooms)
                if (r.Floor == floor) floorRooms.Add(r.Id);

            var result = new List<Wall>();
            foreach (Wall w in project.Walls)
                if (w.RoomId == null || floorRooms.Contains(w.RoomId)) result.Add(w);
            return result;
        }

        private static bool PointInPolygon(List<PlanPoint> polygon, float x, float z)
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

        // Расстояние от точки до отрезка; along - проекция точки на линию отрезка (метры от точки A, без ограничения).
        private static float DistanceToSegment(PlanPoint p, PlanPoint a, PlanPoint b, out float along)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-6f)
            {
                along = 0f;
                return Distance(p.X, p.Z, a.X, a.Z);
            }

            along = ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length;
            float c = Clamp(along, 0f, length);
            return Distance(p.X, p.Z, a.X + dx / length * c, a.Z + dz / length * c);
        }

        private static float Distance(float x1, float z1, float x2, float z2)
        {
            float dx = x2 - x1, dz = z2 - z1;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return Math.Max(lo, Math.Min(hi, v));
        }
    }
}
