// Типы данных каталога (T-02, B-08). Модели и материалы (сами файлы) делает C; B хранит их описания.
//
// СОГЛАШЕНИЕ О МОДЕЛЯХ (для C-01):
//   Размеры Size: X - ширина, Y - высота, Z - глубина, метры. Передняя сторона предмета смотрит вдоль +Z.
//   Pivot (точка привязки):
//     - предмет на полу: центр основания, на полу;
//     - предмет на стене: центр ЗАДНЕЙ стороны (той, что касается стены);
//     - предмет на потолке: центр верхней стороны.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    /// <summary>Куда предмет можно ставить (FR-102).</summary>
    public enum PlacementRule { Floor, Wall, Ceiling, Any }

    /// <summary>К каким поверхностям применим материал (FR-107). Можно комбинировать.</summary>
    [Flags]
    public enum MaterialSurface
    {
        None = 0,
        Floor = 1,
        Wall = 2,
        Ceiling = 4,
        Object = 8,
        All = Floor | Wall | Ceiling | Object
    }

    public class CatalogCategory
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class CatalogItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string CategoryId { get; set; }

        /// <summary>Габариты в метрах: X - ширина, Y - высота, Z - глубина.</summary>
        public Vec3 Size { get; set; } = Vec3.One;

        /// <summary>Ключ префаба, по которому клиент находит модель (формат ключа решат B и C).</summary>
        public string PrefabKey { get; set; }

        /// <summary>Id материалов, которые можно применять к предмету. Пустой список - менять материал нельзя.</summary>
        public List<string> AllowedMaterialIds { get; set; } = new List<string>();

        public PlacementRule Placement { get; set; } = PlacementRule.Floor;

        /// <summary>
        /// Для предметов на полу: прижимать задней стороной к стене, если стена рядом (шкаф, диван).
        /// Если стены далеко, предмет ставится свободно.
        /// </summary>
        public bool AlignToWall { get; set; } = false;

        /// <summary>Для настенных предметов: высота центра задней стороны над полом, метры.</summary>
        public float MountHeight { get; set; } = 1.4f;
    }

    public class MaterialInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public MaterialSurface Surfaces { get; set; } = MaterialSurface.All;
    }
}
