// Реализация ICatalogService (B-08, FR-101, FR-107).

using System;
using System.Collections.Generic;
using System.IO;

namespace XrInterior.ProjectData
{
    public sealed class CatalogService : ICatalogService
    {
        private readonly List<CatalogCategory> _categories;
        private readonly List<CatalogItem> _items;
        private readonly List<MaterialInfo> _materials;
        private readonly Dictionary<string, CatalogItem> _itemsById = new Dictionary<string, CatalogItem>();
        private readonly Dictionary<string, MaterialInfo> _materialsById = new Dictionary<string, MaterialInfo>();

        /// <exception cref="ArgumentException">Если в каталоге есть ошибки.</exception>
        public CatalogService(CatalogData data)
        {
            List<string> errors = CatalogValidator.Validate(data);
            if (errors.Count > 0)
                throw new ArgumentException("Каталог содержит ошибки: " + string.Join("; ", errors));

            _categories = data.Categories;
            _items = data.Items;
            _materials = data.Materials;
            foreach (CatalogItem item in _items) _itemsById[item.Id] = item;
            foreach (MaterialInfo m in _materials) _materialsById[m.Id] = m;
        }

        public static CatalogService FromJson(string json)
        {
            return new CatalogService(CatalogSerializer.FromJson(json));
        }

        public static CatalogService FromFile(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        public IReadOnlyList<CatalogCategory> GetCategories()
        {
            return _categories.AsReadOnly();
        }

        public IReadOnlyList<CatalogItem> Search(string text = null, string categoryId = null)
        {
            string query = text == null ? "" : text.Trim();
            var result = new List<CatalogItem>();
            foreach (CatalogItem item in _items)
            {
                if (categoryId != null && item.CategoryId != categoryId) continue;
                if (query.Length > 0
                    && item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result.Add(item);
            }
            return result;
        }

        public CatalogItem GetItem(string catalogId)
        {
            CatalogItem item;
            return catalogId != null && _itemsById.TryGetValue(catalogId, out item) ? item : null;
        }

        public IReadOnlyList<MaterialInfo> GetMaterials(MaterialSurface surface = MaterialSurface.All)
        {
            var result = new List<MaterialInfo>();
            foreach (MaterialInfo m in _materials)
                if ((m.Surfaces & surface) != MaterialSurface.None) result.Add(m);
            return result;
        }

        public MaterialInfo GetMaterial(string materialId)
        {
            MaterialInfo m;
            return materialId != null && _materialsById.TryGetValue(materialId, out m) ? m : null;
        }
    }
}
