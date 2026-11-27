// Инструменты редактора (B-07, FR-103, FR-113): добавить, переместить, повернуть, удалить, дублировать.
// Одни и те же вызовы использует и XR-слой (контроллеры Quest в VR и MR), и предпросмотр на ПК.
// Все изменения проходят через ICommandService (undo/redo). Не зависит от Unity.
//
// Правила:
//  - перемещение привязывается к сетке и правилам каталога; предмет на стене двигается вдоль стены и по высоте
//    и не меняет сторону стены сам по себе;
//  - пересечения (со стенами, предметами и реальной геометрией) не мешают действию: они возвращаются как
//    предупреждение, а предмет подсвечивается красным. Запрет подтверждения размещения в реальном объекте
//    (FR-207) включается свойством BlockOnReality - им воспользуется шаг «Подтвердить»;
//  - предмет при добавлении появляется ВСЕГДА (в свободном месте, а если его нет - в пересечении, красным);
//  - предмет поворачивается шагом из SnapSettings (15°), стена - на 90°; предметы на стене не вращаются;
//  - дубликат ставится рядом с оригиналом вдоль его локальной оси X.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public sealed class ToolResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        /// <summary>Предупреждение (например, пересечение с другим предметом). Действие при этом выполнено.</summary>
        public string Warning { get; private set; }
        /// <summary>Id созданного объекта или стены (для добавления и дублирования).</summary>
        public string ObjectId { get; private set; }

        public static ToolResult Ok(string message, string warning = null, string objectId = null)
        {
            return new ToolResult { Success = true, Message = message, Warning = warning, ObjectId = objectId };
        }

        public static ToolResult Fail(string message)
        {
            return new ToolResult { Success = false, Message = message };
        }
    }

    public sealed class EditorTools
    {
        /// <summary>Зазор между оригиналом и дубликатом, метры.</summary>
        public const float DuplicateGap = 0.1f;

        /// <summary>Шаг перемещения кнопками, метры.</summary>
        public const float MoveStep = 0.25f;

        /// <summary>Шаг изменения длины стены, метры.</summary>
        public const float LengthStep = 0.25f;

        /// <summary>Шаг изменения толщины стены, метры.</summary>
        public const float ThicknessStep = 0.05f;

        public const float MinWallLength = 0.5f;
        public const float MinWallThickness = 0.05f;
        public const float MaxWallThickness = 0.5f;

        /// <summary>Поворот стены, градусы.</summary>
        public const float WallRotationStep = 90f;

        private const float DegToRad = (float)Math.PI / 180f;

        private readonly IProjectService _projects;
        private readonly ICommandService _commands;
        private readonly ICatalogService _catalog;
        private readonly SnapSettings _snap;

        /// <summary>Проверка реальной геометрии (сервис A). null - реальных объектов нет.</summary>
        public IRealityCollisionChecker Reality { get; set; }

        /// <summary>
        /// false (по умолчанию): пересечение с реальным объектом - предупреждение, предмет красный.
        /// true: такое действие отклоняется (запрет подтверждения по FR-207).
        /// </summary>
        public bool BlockOnReality { get; set; }

        public EditorTools(IProjectService projects, ICommandService commands, ICatalogService catalog,
            SnapSettings snap = null)
        {
            if (projects == null) throw new ArgumentNullException(nameof(projects));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            _projects = projects;
            _commands = commands;
            _catalog = catalog;
            _snap = snap ?? SnapSettings.Default;
        }

        // =====================================================================
        // Добавление
        // =====================================================================

        /// <summary>Добавляет предмет рядом с центром помещения.</summary>
        public ToolResult AddFromCatalog(string catalogId)
        {
            return AddFromCatalogCore(catalogId, false, default(PlanPoint));
        }

        /// <summary>Добавляет предмет как можно ближе к точке плана (например, к тому месту, где стоит пользователь).</summary>
        public ToolResult AddFromCatalog(string catalogId, PlanPoint near)
        {
            return AddFromCatalogCore(catalogId, true, near);
        }

        private struct Candidate2
        {
            public float D;
            public PlacementResult Placement;
        }

        private ToolResult AddFromCatalogCore(string catalogId, bool hasNear, PlanPoint nearArg)
        {
            Project p = _projects.Current;
            if (p == null) return ToolResult.Fail("Нет открытого проекта.");
            if (p.Rooms.Count == 0) return ToolResult.Fail("В проекте нет помещения.");
            CatalogItem item = _catalog.GetItem(catalogId);
            if (item == null) return ToolResult.Fail("В каталоге нет предмета " + catalogId + ".");

            Room room = hasNear ? RoomFor(p, nearArg) : p.Rooms[0];
            int floor = room.Floor;
            PlanPoint near = hasNear && PlanGeometry.Contains(room.Boundary, nearArg.X, nearArg.Z)
                ? nearArg
                : PlanGeometry.InteriorPoint(room.Boundary);

            var candidates = new List<Candidate2>();
            if (item.Placement == PlacementRule.Wall) CollectWallCandidates(p, item, room, near, floor, candidates);
            else CollectFloorCandidates(p, item, room, near, floor, candidates);
            candidates.Sort((a, b) => a.D.CompareTo(b.D));

            PlacementResult chosen = null;
            PlacementResult firstValid = null;
            int checkedCount = 0;
            foreach (Candidate2 c in candidates)
            {
                if (firstValid == null) firstValid = c.Placement;
                CollisionReport probe = CollisionService.Check(p, item, c.Placement, _catalog, Reality, floor);
                if (!probe.HasCollisions) { chosen = c.Placement; break; }
                if (++checkedCount >= 600) break;
            }
            if (chosen == null) chosen = firstValid;

            if (chosen == null)
            {
                if (item.Placement == PlacementRule.Wall)
                    return ToolResult.Fail("Для «" + item.Name + "» нет подходящего места на стенах.");

                // Предмет не помещается нигде «чисто»: ставим в центр помещения, он будет красным.
                float y = item.Placement == PlacementRule.Ceiling ? room.Elevation + room.Height : room.Elevation;
                chosen = new PlacementResult
                {
                    IsValid = true,
                    Position = new Vec3(near.X, y, near.Z),
                    RotationY = 0f,
                    RoomId = room.Id
                };
            }

            // Пересечения считаем ДО добавления, иначе предмет «пересечётся» сам с собой.
            CollisionReport report = CollisionService.Check(p, item, chosen, _catalog, Reality, floor);
            PlacedObject created = PlacementService.CreateObject(item, chosen);
            string error = Exec(new AddObjectCommand(created, "Поставить: " + item.Name));
            if (error != null) return ToolResult.Fail("Не удалось поставить: " + error);

            return ToolResult.Ok("Поставлено: " + item.Name + (chosen.WallId != null ? " (на стене)." : "."),
                report.HasCollisions ? report.Summary : null, created.Id);
        }

        private void CollectFloorCandidates(Project p, CatalogItem item, Room room, PlanPoint near, int floor,
            List<Candidate2> into)
        {
            float minX, maxX, minZ, maxZ;
            PlanGeometry.Bounds(room.Boundary, out minX, out maxX, out minZ, out maxZ);
            const float step = 0.25f;
            for (float x = minX + step; x < maxX; x += step)
                for (float z = minZ + step; z < maxZ; z += step)
                {
                    if (!PlanGeometry.Contains(room.Boundary, x, z)) continue;
                    PlacementResult r = PlacementService.Place(p, item, new PlanPoint(x, z), 0f, floor, _snap);
                    if (!r.IsValid) continue;
                    into.Add(new Candidate2 { D = PlanGeometry.Dist(r.Position.X, r.Position.Z, near.X, near.Z), Placement = r });
                }
        }

        private void CollectWallCandidates(Project p, CatalogItem item, Room room, PlanPoint near, int floor,
            List<Candidate2> into)
        {
            foreach (Wall w in PlacementService.WallsOnFloor(p, floor))
            {
                float length = WallRules.Length(w);
                if (length < 0.05f) continue;
                float nx = -(w.B.Z - w.A.Z) / length, nz = (w.B.X - w.A.X) / length;
                for (float along = 0.25f; along < length; along += 0.5f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        PlacementResult r = WallPlacement.PlaceOn(p, item, w, along, nx * side, nz * side, floor, _snap, null);
                        if (!r.IsValid || r.RoomId != room.Id) continue;
                        into.Add(new Candidate2 { D = PlanGeometry.Dist(r.Position.X, r.Position.Z, near.X, near.Z), Placement = r });
                    }
            }
        }

        /// <summary>Добавляет короткую перегородку внутри помещения рядом с точкой near. Id новой стены - в ObjectId.</summary>
        public ToolResult AddPartition(PlanPoint near)
        {
            Project p = _projects.Current;
            if (p == null) return ToolResult.Fail("Нет открытого проекта.");
            if (p.Rooms.Count == 0) return ToolResult.Fail("В проекте нет помещения.");

            Room room = RoomFor(p, near);
            PlanPoint c = PlanGeometry.Contains(room.Boundary, near.X, near.Z) ? near : PlanGeometry.InteriorPoint(room.Boundary);
            float cx = GridSnap.Snap(c.X, _snap.PositionStep), cz = GridSnap.Snap(c.Z, _snap.PositionStep);

            float[] lengths = { 2.0f, 1.5f, 1.0f, 0.5f };
            foreach (float len in lengths)
                for (int vertical = 0; vertical <= 1; vertical++)
                {
                    float hx = vertical == 0 ? len / 2f : 0f, hz = vertical == 0 ? 0f : len / 2f;
                    if (!SegmentInside(room, cx - hx, cz - hz, cx + hx, cz + hz)) continue;

                    int before = p.Walls.Count;
                    string error = Exec(new AddWallCommand(new PlanPoint(cx - hx, cz - hz), new PlanPoint(cx + hx, cz + hz),
                        room.Height, 0.1f, room.Id, ElementState.Proposed, "Добавить перегородку"));
                    if (error != null) return ToolResult.Fail("Не удалось добавить перегородку: " + error);
                    Project now = _projects.Current;
                    string id = now.Walls.Count > before ? now.Walls[now.Walls.Count - 1].Id : null;
                    return ToolResult.Ok("Добавлена перегородка " + len.ToString("0.#") + " м.", null, id);
                }
            return ToolResult.Fail("Здесь нет места для перегородки.");
        }

        /// <summary>Добавляет окно или дверь в первое свободное место стены.</summary>
        public ToolResult AddOpening(string wallId, OpeningType type)
        {
            Project p = _projects.Current;
            if (p == null) return ToolResult.Fail("Нет открытого проекта.");
            Wall wall = p.Walls.Find(w => w.Id == wallId);
            if (wall == null) return ToolResult.Fail("Сначала выберите стену.");

            OpeningSize size = type == OpeningType.Door ? OpeningPresets.Door : OpeningPresets.Window;
            float length = WallRules.Length(wall);
            var existing = new List<Opening>(WallRules.OpeningsOf(p, wallId));

            for (float offset = 0.1f; offset + size.Width <= length - 0.1f + 0.0001f; offset += 0.1f)
            {
                bool free = true;
                foreach (Opening o in existing)
                    if (offset < o.Offset + o.Width + 0.1f && o.Offset < offset + size.Width + 0.1f) { free = false; break; }
                if (!free) continue;

                string error = Exec(new AddOpeningCommand(wallId, type, offset, size.Width, size.Height, size.SillHeight,
                    type == OpeningType.Door ? "mat_wood_door_01" : "mat_glass_01"));
                if (error != null) return ToolResult.Fail("Не удалось добавить: " + error);
                return ToolResult.Ok(type == OpeningType.Door ? "Добавлена дверь." : "Добавлено окно.");
            }
            return ToolResult.Fail("На этой стене нет свободного места.");
        }

        // =====================================================================
        // Перемещение
        // =====================================================================

        /// <summary>Переносит предмет в точку плана (с привязкой к сетке и правилами размещения).</summary>
        /// <param name="mountHeight">Для предметов на стене: высота центра задней стороны над полом. null - прежняя.</param>
        public ToolResult MoveTo(string objectId, PlanPoint target, float? mountHeight = null)
        {
            Project p; PlacedObject obj; CatalogItem item;
            ToolResult fail = Resolve(objectId, out p, out obj, out item);
            if (fail != null) return fail;

            int floor = FloorOf(p);
            PlacementResult placement;
            if (item.Placement == PlacementRule.Wall)
            {
                float height = mountHeight ?? (obj.Position.Y - ElevationOf(p, obj, floor));
                placement = WallPlacement.Place(p, item, target, floor, _snap, obj.Rotation.Y, height);
            }
            else placement = PlacementService.Place(p, item, target, obj.Rotation.Y, floor, _snap);

            if (!placement.IsValid) return ToolResult.Fail("Нельзя переместить: " + placement.Reason);

            Vec3 newRotation = new Vec3(obj.Rotation.X, placement.RotationY, obj.Rotation.Z);
            if (Same(obj.Position, placement.Position) && Math.Abs(obj.Rotation.Y - placement.RotationY) < 0.01f)
                return ToolResult.Ok("Предмет уже на месте.");

            PlacedObject candidate = Clone(obj, placement.Position, newRotation);
            CollisionReport report = CollisionService.CheckObject(p, candidate, _catalog, Reality, floor);
            if (BlockOnReality && report.HasBlocking) return ToolResult.Fail("Нельзя переместить: " + report.BlockingSummary);

            _commands.Execute(new SetObjectTransformCommand(obj.Id, candidate.Position, candidate.Rotation,
                obj.Scale, "Переместить: " + item.Name));
            return ToolResult.Ok("Перемещено: " + item.Name + ".", report.HasCollisions ? report.Summary : null);
        }

        /// <summary>
        /// Сдвигает выбранный элемент на (dx, dz) метров по осям плана (X - вправо, Z - вперёд).
        /// Предмет на стене двигается только вдоль стены; стена целиком.
        /// </summary>
        public ToolResult MoveBy(string id, float dx, float dz)
        {
            Project p = _projects.Current;
            if (p == null) return ToolResult.Fail("Нет открытого проекта.");

            PlacedObject obj = p.Objects.Find(o => o.Id == id);
            if (obj == null)
            {
                Wall wall = p.Walls.Find(w => w.Id == id);
                if (wall == null) return ToolResult.Fail("Элемент не найден.");
                return MoveWall(wall, dx, dz);
            }

            CatalogItem item = _catalog.GetItem(obj.CatalogId);
            if (item == null) return ToolResult.Fail("Предмета '" + obj.CatalogId + "' нет в каталоге, действие недоступно.");

            if (item.Placement == PlacementRule.Wall)
            {
                Wall wall = WallPlacement.FindWallOf(p, obj, FloorOf(p));
                if (wall == null) return ToolResult.Fail("Не найдена стена, на которой висит предмет.");
                float length = WallRules.Length(wall);
                float delta = dx * (wall.B.X - wall.A.X) / length + dz * (wall.B.Z - wall.A.Z) / length;
                if (Math.Abs(delta) < 0.01f)
                    return ToolResult.Fail("Предмет на стене двигается вдоль стены и по высоте.");
                return SlideBy(id, delta);
            }

            return MoveTo(id, new PlanPoint(obj.Position.X + dx, obj.Position.Z + dz));
        }

        /// <summary>Сдвигает предмет, висящий на стене, вдоль стены (от её начала A к концу B при положительном значении).</summary>
        public ToolResult SlideBy(string objectId, float meters)
        {
            Project p; PlacedObject obj; CatalogItem item;
            ToolResult fail = Resolve(objectId, out p, out obj, out item);
            if (fail != null) return fail;
            if (item.Placement != PlacementRule.Wall)
                return ToolResult.Fail("Вдоль стены двигаются только предметы на стене.");

            Wall wall = WallPlacement.FindWallOf(p, obj, FloorOf(p));
            if (wall == null) return ToolResult.Fail("Не найдена стена, на которой висит предмет.");
            float length = WallRules.Length(wall);
            float ux = (wall.B.X - wall.A.X) / length, uz = (wall.B.Z - wall.A.Z) / length;
            return MoveTo(objectId, new PlanPoint(obj.Position.X + ux * meters, obj.Position.Z + uz * meters));
        }

        /// <summary>Поднимает или опускает предмет, висящий на стене.</summary>
        public ToolResult RaiseBy(string objectId, float dy)
        {
            Project p; PlacedObject obj; CatalogItem item;
            ToolResult fail = Resolve(objectId, out p, out obj, out item);
            if (fail != null) return fail;
            if (item.Placement != PlacementRule.Wall)
                return ToolResult.Fail("По высоте двигаются только предметы на стене.");

            float current = obj.Position.Y - ElevationOf(p, obj, FloorOf(p));
            return MoveTo(objectId, new PlanPoint(obj.Position.X, obj.Position.Z), current + dy);
        }

        private const string MainWallMessage = "Основные стены комнаты менять нельзя: их можно только выбрать, покрасить и добавить окно или дверь.";

        private ToolResult MoveWall(Wall wall, float dx, float dz)
        {
            if (WallPlacement.IsRoomBoundaryWall(_projects.Current, wall)) return ToolResult.Fail(MainWallMessage);
            PlanPoint a = new PlanPoint(GridSnap.Snap(wall.A.X + dx, _snap.PositionStep), GridSnap.Snap(wall.A.Z + dz, _snap.PositionStep));
            PlanPoint b = new PlanPoint(GridSnap.Snap(wall.B.X + dx, _snap.PositionStep), GridSnap.Snap(wall.B.Z + dz, _snap.PositionStep));
            string error = Exec(new MoveWallCommand(wall.Id, a, b, "Сдвинуть стену"));
            if (error != null) return ToolResult.Fail("Не удалось сдвинуть стену: " + error);
            return ToolResult.Ok("Стена сдвинута.");
        }

        // =====================================================================
        // Размер стены
        // =====================================================================

        /// <summary>
        /// Меняет длину (конец B отодвигается или приближается к A, начало A стоит на месте) и толщину стены.
        /// Окна и двери остаются на своих местах, поэтому стену нельзя укоротить так, чтобы они вылезли за её конец.
        /// </summary>
        public ToolResult ResizeWall(string wallId, float deltaLength, float deltaThickness)
        {
            Project p = _projects.Current;
            if (p == null) return ToolResult.Fail("Нет открытого проекта.");
            Wall wall = p.Walls.Find(w => w.Id == wallId);
            if (wall == null) return ToolResult.Fail("Сначала выберите стену.");
            if (WallPlacement.IsRoomBoundaryWall(p, wall)) return ToolResult.Fail(MainWallMessage);

            float step = _snap.PositionStep;
            float length = WallRules.Length(wall);
            if (length < 1e-4f) return ToolResult.Fail("У стены нулевая длина.");

            float newLength = GridSnap.Snap(length + deltaLength, step);
            float newThickness = GridSnap.Snap(wall.Thickness + deltaThickness, step);
            newThickness = Math.Max(MinWallThickness, Math.Min(MaxWallThickness, newThickness));

            if (deltaLength != 0f)
            {
                if (newLength < MinWallLength) return ToolResult.Fail("Стена не может быть короче " + MinWallLength.ToString("0.0#") + " м.");
                float occupied = 0f;
                foreach (Opening o in WallRules.OpeningsOf(p, wall.Id)) occupied = Math.Max(occupied, o.Offset + o.Width);
                if (newLength < occupied - 0.0001f) return ToolResult.Fail("Короче нельзя: мешают окно или дверь.");
            }
            else newLength = length;

            if (deltaThickness != 0f && Math.Abs(newThickness - wall.Thickness) < 0.0001f)
                return ToolResult.Fail("Толщина стены ограничена: " + MinWallThickness.ToString("0.00") + "-" + MaxWallThickness.ToString("0.00") + " м.");

            float ux = (wall.B.X - wall.A.X) / length, uz = (wall.B.Z - wall.A.Z) / length;
            PlanPoint newB = deltaLength != 0f
                ? new PlanPoint(GridSnap.Snap(wall.A.X + ux * newLength, step), GridSnap.Snap(wall.A.Z + uz * newLength, step))
                : wall.B;

            string error = Exec(new ResizeWallCommand(wall.Id, wall.A, newB, newThickness, "Изменить стену"));
            if (error != null) return ToolResult.Fail("Не удалось изменить стену: " + error);
            return ToolResult.Ok("Стена: длина " + WallRules.Length(wall).ToString("0.00") + " м, толщина "
                + wall.Thickness.ToString("0.00") + " м.");
        }

        // =====================================================================
        // Поворот
        // =====================================================================

        /// <summary>
        /// Поворачивает предмет вокруг вертикальной оси на заданный угол с привязкой к шагу (по часовой при положительном угле)
        /// либо стену на этот угол вокруг её середины.
        /// </summary>
        public ToolResult RotateBy(string id, float degrees)
        {
            Project p = _projects.Current;
            if (p != null && !p.Objects.Exists(o => o.Id == id))
            {
                Wall wall = p.Walls.Find(w => w.Id == id);
                if (wall != null) return RotateWall(wall, degrees);
            }

            PlacedObject obj; CatalogItem item;
            ToolResult fail = Resolve(id, out p, out obj, out item);
            if (fail != null) return fail;

            if (item.Placement == PlacementRule.Wall)
                return ToolResult.Fail("Предмет на стене не вращается: его поворот задаёт стена.");

            float yaw = GridSnap.SnapYaw(obj.Rotation.Y + degrees, _snap.RotationStepDegrees);
            PlacedObject candidate = Clone(obj, obj.Position, new Vec3(obj.Rotation.X, yaw, obj.Rotation.Z));
            int floor = FloorOf(p);
            CollisionReport report = CollisionService.CheckObject(p, candidate, _catalog, Reality, floor);
            if (BlockOnReality && report.HasBlocking) return ToolResult.Fail("Нельзя повернуть: " + report.BlockingSummary);

            _commands.Execute(new SetObjectTransformCommand(obj.Id, obj.Position, candidate.Rotation,
                obj.Scale, "Повернуть: " + item.Name));
            return ToolResult.Ok("Повёрнуто: " + item.Name + " (" + yaw.ToString("0.#") + "°).",
                report.HasCollisions ? report.Summary : null);
        }

        private ToolResult RotateWall(Wall wall, float degrees)
        {
            if (WallPlacement.IsRoomBoundaryWall(_projects.Current, wall)) return ToolResult.Fail(MainWallMessage);
            float step = _snap.PositionStep;
            float mx = GridSnap.Snap((wall.A.X + wall.B.X) / 2f, step);
            float mz = GridSnap.Snap((wall.A.Z + wall.B.Z) / 2f, step);
            double t = degrees * DegToRad;
            float cos = (float)Math.Cos(t), sin = (float)Math.Sin(t);

            // Тот же знак, что у предметов: положительный угол - по часовой стрелке на плане (X вправо, Z вверх).
            Func<PlanPoint, PlanPoint> rot = pt =>
            {
                float vx = pt.X - mx, vz = pt.Z - mz;
                return new PlanPoint(GridSnap.Snap(mx + vx * cos + vz * sin, step),
                                     GridSnap.Snap(mz - vx * sin + vz * cos, step));
            };

            string error = Exec(new MoveWallCommand(wall.Id, rot(wall.A), rot(wall.B), "Повернуть стену"));
            if (error != null) return ToolResult.Fail("Не удалось повернуть стену: " + error);
            return ToolResult.Ok("Стена повёрнута на " + degrees.ToString("0.#") + "°.");
        }

        // =====================================================================
        // Удаление и дублирование
        // =====================================================================

        /// <summary>Удаляет предмет (вместе с его материалами и состояниями в вариантах) либо стену (вместе с её проёмами).</summary>
        public ToolResult Delete(string id)
        {
            Project p = _projects.Current;
            if (p == null) return ToolResult.Fail("Нет открытого проекта.");

            PlacedObject obj = p.Objects.Find(o => o.Id == id);
            if (obj != null)
            {
                _commands.Execute(new RemoveObjectCommand(obj.Id, "Удалить объект"));
                return ToolResult.Ok("Удалено.");
            }

            Wall wall = p.Walls.Find(w => w.Id == id);
            if (wall != null)
            {
                if (WallPlacement.IsRoomBoundaryWall(p, wall)) return ToolResult.Fail(MainWallMessage);
                string error = Exec(new RemoveWallCommand(wall.Id, "Удалить стену"));
                if (error != null) return ToolResult.Fail("Не удалось удалить стену: " + error);
                return ToolResult.Ok("Стена удалена вместе с её окнами и дверями.");
            }

            return ToolResult.Fail("Объект не найден.");
        }

        /// <summary>Создаёт копию предмета рядом с оригиналом. В ObjectId результата лежит id копии.</summary>
        public ToolResult Duplicate(string objectId)
        {
            Project p; PlacedObject obj; CatalogItem item;
            ToolResult fail = Resolve(objectId, out p, out obj, out item);
            if (fail != null) return fail;

            float distance = item.Size.X * Math.Abs(obj.Scale.X) + DuplicateGap;
            float yaw = obj.Rotation.Y * DegToRad;
            float x = GridSnap.Snap(obj.Position.X + (float)Math.Cos(yaw) * distance, _snap.PositionStep);
            float z = GridSnap.Snap(obj.Position.Z - (float)Math.Sin(yaw) * distance, _snap.PositionStep);
            var position = new Vec3(x, obj.Position.Y, z);

            var command = new DuplicateObjectCommand(obj.Id, position, null, "Дублировать: " + item.Name);
            PlacedObject candidate = Clone(obj, position, obj.Rotation);
            candidate.Id = command.NewObjectId;

            int floor = FloorOf(p);
            CollisionReport report = CollisionService.CheckObject(p, candidate, _catalog, Reality, floor);
            if (BlockOnReality && report.HasBlocking) return ToolResult.Fail("Нельзя дублировать: " + report.BlockingSummary);

            _commands.Execute(command);
            return ToolResult.Ok("Создана копия: " + item.Name + ".",
                report.HasCollisions ? report.Summary : null, command.NewObjectId);
        }

        // ---------- вспомогательное ----------

        private string Exec(ICommand command)
        {
            try { _commands.Execute(command); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        private ToolResult Resolve(string objectId, out Project project, out PlacedObject obj, out CatalogItem item)
        {
            obj = null;
            item = null;
            project = _projects.Current;
            if (project == null) return ToolResult.Fail("Нет открытого проекта.");

            obj = project.Objects.Find(o => o.Id == objectId);
            if (obj == null) return ToolResult.Fail("Объект не найден.");

            item = _catalog.GetItem(obj.CatalogId);
            if (item == null)
                return ToolResult.Fail("Предмета '" + obj.CatalogId + "' нет в каталоге, действие недоступно.");
            return null;
        }

        private static PlacedObject Clone(PlacedObject source, Vec3 position, Vec3 rotation)
        {
            return new PlacedObject
            {
                Id = source.Id,
                CatalogId = source.CatalogId,
                Position = position,
                Rotation = rotation,
                Scale = source.Scale
            };
        }

        private static bool Same(Vec3 a, Vec3 b)
        {
            return Math.Abs(a.X - b.X) < 0.0005f && Math.Abs(a.Y - b.Y) < 0.0005f && Math.Abs(a.Z - b.Z) < 0.0005f;
        }

        private static float ElevationOf(Project p, PlacedObject obj, int floor)
        {
            Wall wall = WallPlacement.FindWallOf(p, obj, floor);
            Room room = wall != null ? PlacementService.RoomOf(p, wall) : null;
            if (room != null) return room.Elevation;
            return p.Rooms.Count > 0 ? p.Rooms[0].Elevation : 0f;
        }

        private static Room RoomFor(Project p, PlanPoint pt)
        {
            foreach (Room r in p.Rooms)
                if (PlanGeometry.Contains(r.Boundary, pt.X, pt.Z)) return r;

            Room best = p.Rooms[0];
            float bestD = float.MaxValue;
            foreach (Room r in p.Rooms)
            {
                PlanPoint c = PlanGeometry.InteriorPoint(r.Boundary);
                float d = PlanGeometry.Dist(c.X, c.Z, pt.X, pt.Z);
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        // Отрезок целиком внутри помещения: проверяем концы и несколько точек между ними.
        private static bool SegmentInside(Room room, float x1, float z1, float x2, float z2)
        {
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                if (!PlanGeometry.Contains(room.Boundary, x1 + (x2 - x1) * t, z1 + (z2 - z1) * t)) return false;
            }
            return true;
        }

        private static int FloorOf(Project p)
        {
            return p.Rooms.Count > 0 ? p.Rooms[0].Floor : 1;
        }
    }
}
