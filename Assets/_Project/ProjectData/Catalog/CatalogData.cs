// Содержимое каталога, чтение из JSON и проверка целостности (B-08).

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace XrInterior.ProjectData
{
    public class CatalogFormatException : Exception
    {
        public CatalogFormatException(string message, Exception inner = null) : base(message, inner) { }
    }

    public sealed class CatalogData
    {
        public List<CatalogCategory> Categories { get; set; } = new List<CatalogCategory>();
        public List<CatalogItem> Items { get; set; } = new List<CatalogItem>();
        public List<MaterialInfo> Materials { get; set; } = new List<MaterialInfo>();
    }

    public static class CatalogSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            },
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            NullValueHandling = NullValueHandling.Ignore
        };

        // В файле поверхности материала записаны списком слов: "surfaces": ["floor", "wall"].
        private sealed class MaterialDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<string> Surfaces { get; set; }
        }

        private sealed class DataDto
        {
            public List<CatalogCategory> Categories { get; set; }
            public List<CatalogItem> Items { get; set; }
            public List<MaterialDto> Materials { get; set; }
        }

        public static CatalogData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new CatalogFormatException("Файл каталога пуст.");

            DataDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<DataDto>(json, Settings);
            }
            catch (JsonException ex)
            {
                throw new CatalogFormatException("Файл каталога повреждён: " + ex.Message, ex);
            }
            if (dto == null) throw new CatalogFormatException("Файл каталога пуст.");

            var data = new CatalogData();
            if (dto.Categories != null) data.Categories = dto.Categories;
            if (dto.Items != null) data.Items = dto.Items;

            if (dto.Materials != null)
            {
                foreach (MaterialDto m in dto.Materials)
                {
                    MaterialSurface surfaces = MaterialSurface.None;
                    if (m.Surfaces != null)
                    {
                        foreach (string name in m.Surfaces)
                        {
                            MaterialSurface parsed;
                            if (!Enum.TryParse(name, true, out parsed))
                                throw new CatalogFormatException(
                                    "Материал '" + m.Id + "': неизвестная поверхность '" + name + "'.");
                            surfaces |= parsed;
                        }
                    }
                    data.Materials.Add(new MaterialInfo { Id = m.Id, Name = m.Name, Surfaces = surfaces });
                }
            }
            return data;
        }
    }

    public static class CatalogValidator
    {
        /// <summary>Список ошибок каталога; пустой список - каталог корректен.</summary>
        public static List<string> Validate(CatalogData data)
        {
            var errors = new List<string>();
            if (data == null)
            {
                errors.Add("Каталог не задан.");
                return errors;
            }

            var categoryIds = new HashSet<string>();
            foreach (CatalogCategory c in data.Categories)
            {
                if (string.IsNullOrWhiteSpace(c.Id)) { errors.Add("Категория без id."); continue; }
                if (!categoryIds.Add(c.Id)) errors.Add("Повторяющийся id категории '" + c.Id + "'.");
                if (string.IsNullOrWhiteSpace(c.Name)) errors.Add("У категории '" + c.Id + "' нет названия.");
            }

            var materialIds = new HashSet<string>();
            foreach (MaterialInfo m in data.Materials)
            {
                if (string.IsNullOrWhiteSpace(m.Id)) { errors.Add("Материал без id."); continue; }
                if (!materialIds.Add(m.Id)) errors.Add("Повторяющийся id материала '" + m.Id + "'.");
                if (string.IsNullOrWhiteSpace(m.Name)) errors.Add("У материала '" + m.Id + "' нет названия.");
                if (m.Surfaces == MaterialSurface.None) errors.Add("У материала '" + m.Id + "' не указаны поверхности.");
            }

            var itemIds = new HashSet<string>();
            foreach (CatalogItem item in data.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Id)) { errors.Add("Предмет без id."); continue; }
                if (!itemIds.Add(item.Id)) errors.Add("Повторяющийся id предмета '" + item.Id + "'.");
                if (string.IsNullOrWhiteSpace(item.Name)) errors.Add("У предмета '" + item.Id + "' нет названия.");
                if (string.IsNullOrWhiteSpace(item.PrefabKey)) errors.Add("У предмета '" + item.Id + "' нет prefabKey.");
                if (!categoryIds.Contains(item.CategoryId ?? ""))
                    errors.Add("Предмет '" + item.Id + "': категория '" + item.CategoryId + "' не найдена.");
                if (!(item.Size.X > 0f) || !(item.Size.Y > 0f) || !(item.Size.Z > 0f))
                    errors.Add("Предмет '" + item.Id + "': размеры должны быть больше нуля.");
                if (item.Placement == PlacementRule.Wall && item.MountHeight < 0f)
                    errors.Add("Предмет '" + item.Id + "': высота крепления не может быть отрицательной.");
                foreach (string materialId in item.AllowedMaterialIds)
                    if (!materialIds.Contains(materialId))
                        errors.Add("Предмет '" + item.Id + "': материал '" + materialId + "' не найден.");
            }
            return errors;
        }
    }
}
