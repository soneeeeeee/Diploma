// Обнаружение пересечений (B-09, FR-105): со стенами, с другими предметами, с реальной геометрией.
// Проект не меняется. Пересечения со стенами и предметами - предупреждения; пересечение с реальной
// геометрией помечено как блокирующее (FR-207: нельзя подтвердить размещение внутри реального объекта).
// Проёмы (двери, окна) в стенах не учитываются: предмет не может стоять в проёме.

using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public enum CollisionKind { Wall, Object, Reality }

    public sealed class Collision
    {
        public CollisionKind Kind { get; set; }
        /// <summary>Id стены или предмета, с которым пересечение (для реальности - null).</summary>
        public string OtherId { get; set; }
        public string Message { get; set; }
        /// <summary>true для пересечений с реальной геометрией.</summary>
        public bool IsBlocking { get { return Kind == CollisionKind.Reality; } }
    }

    public sealed class CollisionReport
    {
        private readonly List<Collision> _items = new List<Collision>();

        public IReadOnlyList<Collision> Collisions { get { return _items; } }
        public bool HasCollisions { get { return _items.Count > 0; } }

        public bool HasBlocking
        {
            get
            {
                foreach (Collision c in _items) if (c.IsBlocking) return true;
                return false;
            }
        }

        /// <summary>Все сообщения через точку с запятой (для уведомления пользователю).</summary>
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                foreach (Collision c in _items) parts.Add(c.Message);
                return string.Join("; ", parts);
            }
        }

        /// <summary>Только сообщения о блокирующих пересечениях.</summary>
        public string BlockingSummary
        {
            get
            {
                var parts = new List<string>();
                foreach (Collision c in _items) if (c.IsBlocking) parts.Add(c.Message);
                return string.Join("; ", parts);
            }
        }

        internal void Add(Collision c) { _items.Add(c); }
    }

    public static class CollisionService
    {
        /// <summary>Предметы ниже этой высоты (ковёр) другим предметам не мешают.</summary>
        public const float FlatItemMaxHeight = 0.05f;

        /// <summary>Проверяет предмет, который собираются поставить (результат PlacementService.Place).</summary>
        public static CollisionReport Check(Project project, CatalogItem item, PlacementResult placement,
            ICatalogService catalog, IRealityCollisionChecker reality = null, int floor = 1)
        {
            OrientedBox box = ItemBounds.ForItem(item, placement.Position, placement.RotationY, Vec3.One);
            return CheckBox(project, box, item.Size.Y, null, catalog, reality, floor);
        }

        /// <summary>Проверяет предмет, уже стоящий в проекте (себя он не считает).</summary>
        public static CollisionReport CheckObject(Project project, PlacedObject obj, ICatalogService catalog,
            IRealityCollisionChecker reality = null, int floor = 1)
        {
            CatalogItem item = catalog.GetItem(obj.CatalogId);
            if (item == null) return new CollisionReport();

            OrientedBox box = ItemBounds.ForItem(item, obj.Position, obj.Rotation.Y, obj.Scale);
            return CheckBox(project, box, item.Size.Y * System.Math.Abs(obj.Scale.Y), obj.Id, catalog, reality, floor);
        }

        private static CollisionReport CheckBox(Project project, OrientedBox box, float itemHeight,
            string ignoreObjectId, ICatalogService catalog, IRealityCollisionChecker reality, int floor)
        {
            var report = new CollisionReport();

            // 1. Стены.
            foreach (Wall wall in PlacementService.WallsOnFloor(project, floor))
            {
                Room room = PlacementService.RoomOf(project, wall);
                float baseY = room != null ? room.Elevation : 0f;
                if (ItemBounds.ForWall(wall, baseY).Intersects(box))
                    report.Add(new Collision
                    {
                        Kind = CollisionKind.Wall,
                        OtherId = wall.Id,
                        Message = "Пересекается со стеной."
                    });
            }

            // 2. Другие предметы.
            foreach (PlacedObject other in project.Objects)
            {
                if (other.Id == ignoreObjectId) continue;
                CatalogItem otherItem = catalog.GetItem(other.CatalogId);
                if (otherItem == null) continue;

                float otherHeight = otherItem.Size.Y * System.Math.Abs(other.Scale.Y);
                if (itemHeight <= FlatItemMaxHeight || otherHeight <= FlatItemMaxHeight) continue; // ковёр и т. п.

                OrientedBox otherBox = ItemBounds.ForItem(otherItem, other.Position, other.Rotation.Y, other.Scale);
                if (otherBox.Intersects(box))
                    report.Add(new Collision
                    {
                        Kind = CollisionKind.Object,
                        OtherId = other.Id,
                        Message = "Пересекается с предметом «" + otherItem.Name + "»."
                    });
            }

            // 3. Реальная геометрия (если сервис A подключён).
            if (reality != null)
            {
                string reason;
                float centerY = (box.YMin + box.YMax) / 2f;
                if (reality.IsBlocked(box.CenterX, centerY, box.CenterZ,
                        box.HalfWidth * 2f, box.YMax - box.YMin, box.HalfDepth * 2f, box.YawDegrees, out reason))
                    report.Add(new Collision
                    {
                        Kind = CollisionKind.Reality,
                        OtherId = null,
                        Message = reason ?? "Пересекается с реальным объектом."
                    });
            }

            return report;
        }
    }
}
