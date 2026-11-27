// "Заявка" на создание помещения по шаблону (задача B-10, FR-111).

using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    /// <summary>Имена параметров шаблонов (ключи словаря размеров).</summary>
    public static class TemplateParam
    {
        public const string Width = "width";
        public const string Depth = "depth";
        public const string Length = "length";
        public const string CutWidth = "cutWidth";
        public const string CutDepth = "cutDepth";
    }

    /// <summary>Значения шаблона по умолчанию (для ползунков в интерфейсе и для пропущенных параметров).</summary>
    public sealed class RoomTemplateDefaults
    {
        public RoomTemplateDefaults(float height, Dictionary<string, float> parameters)
        {
            Height = height;
            Params = parameters;
        }

        public float Height { get; private set; }
        public Dictionary<string, float> Params { get; private set; }
    }

    public sealed class RoomSpec
    {
        public string Name { get; set; } = "Помещение";
        public RoomTemplate Template { get; set; } = RoomTemplate.Rectangular;

        /// <summary>Размеры в метрах. Пропущенные параметры берутся из значений по умолчанию.</summary>
        public Dictionary<string, float> Params { get; set; } = new Dictionary<string, float>();

        /// <summary>Высота помещения, м. null - значение по умолчанию для шаблона.</summary>
        public float? Height { get; set; }

        public float WallThickness { get; set; } = 0.2f;
        public int Floor { get; set; } = 1;
        public float Elevation { get; set; } = 0f;
        public ElementState State { get; set; } = ElementState.Existing;
    }
}
