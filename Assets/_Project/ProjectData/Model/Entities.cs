// Сущности проекта (раздел 6 ТЗ). Это простые классы-"контейнеры" данных без логики.
// Названия свойств в JSON получаются автоматически: ObjectId -> "objectId".

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XrInterior.ProjectData
{
    /// <summary>
    /// Базовый класс всех сущностей. Поля, которых этот код не знает (например, добавленные
    /// более новой версией приложения), складываются в UnknownFields и записываются обратно
    /// при сохранении. Так файл не теряет данные (раздел 5.3 ТЗ).
    /// </summary>
    public abstract class ExtensibleEntity
    {
        [JsonExtensionData]
        public IDictionary<string, JToken> UnknownFields { get; set; }
    }

    /// <summary>Локальная система координат проекта.</summary>
    public class Reference : ExtensibleEntity
    {
        public Vec3 Origin { get; set; } = Vec3.Zero;
        public Vec3 Forward { get; set; } = Vec3.Forward;
        public float Scale { get; set; } = 1f;
    }

    public class Room : ExtensibleEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>Номер этажа (1, 2, ...).</summary>
        public int Floor { get; set; } = 1;
        /// <summary>Высота пола помещения над нулём проекта, метры.</summary>
        public float Elevation { get; set; } = 0f;
        /// <summary>Контур помещения в плане, по порядку обхода.</summary>
        public List<PlanPoint> Boundary { get; set; } = new List<PlanPoint>();
        public float Height { get; set; } = 2.7f;
        public RoomTemplate Template { get; set; } = RoomTemplate.Custom;
        /// <summary>Параметры шаблона: "width", "depth" и т. д.</summary>
        public Dictionary<string, float> TemplateParams { get; set; } = new Dictionary<string, float>();
        public ElementState State { get; set; } = ElementState.Proposed;
    }

    public class Wall : ExtensibleEntity
    {
        public string Id { get; set; }
        /// <summary>К какому помещению относится; может быть null (отдельная перегородка).</summary>
        public string RoomId { get; set; }
        public PlanPoint A { get; set; }
        public PlanPoint B { get; set; }
        public float Height { get; set; } = 2.7f;
        public float Thickness { get; set; } = 0.2f;
        /// <summary>Id проёмов этой стены. Сами проёмы лежат в Project.Openings.</summary>
        public List<string> OpeningIds { get; set; } = new List<string>();
        public ElementState State { get; set; } = ElementState.Proposed;
    }

    public class Opening : ExtensibleEntity
    {
        public string Id { get; set; }
        public string WallId { get; set; }
        public OpeningType Type { get; set; }
        /// <summary>Расстояние от точки A стены до начала проёма вдоль стены, метры.</summary>
        public float Offset { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        /// <summary>Высота нижнего края над полом (0 для двери, ~0.9 для окна).</summary>
        public float SillHeight { get; set; } = 0f;
        public string MaterialId { get; set; }
        public ElementState State { get; set; } = ElementState.Proposed;
    }

    /// <summary>Размещённый предмет каталога. Назван PlacedObject, чтобы не путать с System.Object / UnityEngine.Object.</summary>
    public class PlacedObject : ExtensibleEntity
    {
        public string Id { get; set; }
        public string CatalogId { get; set; }
        public Vec3 Position { get; set; } = Vec3.Zero;
        /// <summary>Поворот: углы Эйлера в градусах.</summary>
        public Vec3 Rotation { get; set; } = Vec3.Zero;
        public Vec3 Scale { get; set; } = Vec3.One;
        public string Layer { get; set; } = LayerIds.Furniture;
        public ElementState State { get; set; } = ElementState.Proposed;
        /// <summary>Id группы (FR-110). Объекты с одинаковым GroupId двигаются вместе. null - не в группе.</summary>
        public string GroupId { get; set; }
    }

    /// <summary>Светильник проекта (FR-114). Назван ProjectLight, чтобы не путать с UnityEngine.Light.</summary>
    public class ProjectLight : ExtensibleEntity
    {
        public string Id { get; set; }
        public LightKind Kind { get; set; } = LightKind.Point;
        public Vec3 Position { get; set; } = Vec3.Zero;
        public float Intensity { get; set; } = 1f;
        public bool Enabled { get; set; } = true;
        public string Layer { get; set; } = LayerIds.Lighting;
        public ElementState State { get; set; } = ElementState.Proposed;
    }

    /// <summary>Замена материала отдельного элемента (FR-107).</summary>
    public class MaterialOverride : ExtensibleEntity
    {
        /// <summary>Id помещения, стены, проёма или объекта.</summary>
        public string TargetId { get; set; }
        /// <summary>Какая часть: "floor", "ceiling", "surface" и т. п.</summary>
        public string Slot { get; set; }
        public string MaterialId { get; set; }
    }

    /// <summary>Состояние одного объекта внутри варианта планировки.</summary>
    public class ObjectState : ExtensibleEntity
    {
        public string ObjectId { get; set; }
        /// <summary>false - в этом варианте объекта нет.</summary>
        public bool Present { get; set; } = true;
        public Vec3 Position { get; set; } = Vec3.Zero;
        public Vec3 Rotation { get; set; } = Vec3.Zero;
        public Vec3 Scale { get; set; } = Vec3.One;
    }

    /// <summary>Вариант планировки (FR-004): снимок изменяемых состояний объектов.</summary>
    public class Variant : ExtensibleEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<ObjectState> ObjectStates { get; set; } = new List<ObjectState>();
    }

    /// <summary>Привязка проекта к физическому пространству. Хранится отдельно от геометрии.</summary>
    public class Alignment : ExtensibleEntity
    {
        public XrPlatform Platform { get; set; }
        public string AnchorId { get; set; }
        public Vec3 Offset { get; set; } = Vec3.Zero;
        public float RotationY { get; set; } = 0f;
        /// <summary>Оценка качества совмещения, 0..100.</summary>
        public int Quality { get; set; } = 0;
    }

    public class SnapshotPlane : ExtensibleEntity
    {
        public string Id { get; set; }
        public SnapshotPlaneKind Kind { get; set; }
        public Vec3 Center { get; set; } = Vec3.Zero;
        public float RotationY { get; set; } = 0f;
        public Size2 Size { get; set; }
        /// <summary>Для двери/окна: Id плоскости стены, в которой находится проём. null - неизвестно.</summary>
        public string ParentPlaneId { get; set; }
    }

    public class SnapshotVolume : ExtensibleEntity
    {
        public string Id { get; set; }
        /// <summary>Подпись из сканирования: "table", "couch" и т. п.</summary>
        public string Label { get; set; }
        public Vec3 Center { get; set; } = Vec3.Zero;
        public float RotationY { get; set; } = 0f;
        /// <summary>Размеры по X, Y, Z, метры.</summary>
        public Vec3 Size { get; set; } = Vec3.One;
    }

    /// <summary>Снимок результатов сканирования (FR-212). Не заменяет проектную геометрию.</summary>
    public class SpaceSnapshot : ExtensibleEntity
    {
        public string Id { get; set; }
        public System.DateTime CapturedAt { get; set; }
        public List<SnapshotPlane> Planes { get; set; } = new List<SnapshotPlane>();
        public List<SnapshotVolume> Volumes { get; set; } = new List<SnapshotVolume>();
        public float FloorHeight { get; set; } = 0f;
        /// <summary>Контур пола в плане (x, z), по порядку обхода, метры. Пусто - контур не получен.</summary>
        public List<PlanPoint> FloorBoundary { get; set; } = new List<PlanPoint>();
        /// <summary>Высота помещения от пола до потолка, метры. 0 - неизвестна.</summary>
        public float RoomHeight { get; set; } = 0f;
        public int Quality { get; set; } = 0;
    }

    /// <summary>Результат сравнения одного элемента с реальностью (FR-210, FR-216).</summary>
    public class ComparisonResult : ExtensibleEntity
    {
        /// <summary>Id элемента проекта ИЛИ элемента снимка (для "лишнего", которого нет в проекте).</summary>
        public string ElementId { get; set; }
        public ComparisonStatus Status { get; set; }
        /// <summary>Отклонение по расстоянию, метры.</summary>
        public float DeviationDistance { get; set; }
        /// <summary>Отклонение по углу, градусы.</summary>
        public float DeviationAngle { get; set; }
        public System.DateTime ComparedAt { get; set; }
    }
}
