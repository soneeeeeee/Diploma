// Правила применения материалов (B-12, FR-107): куда какой материал можно положить и что сейчас лежит.
//
// Слоты (поле Slot у MaterialOverride):
//   помещение: "floor" (пол), "ceiling" (потолок);
//   стена:     "surface" (вся стена целиком, одна сторона);
//   предмет:   "surface" (весь предмет целиком).
// Проёмы (окна, двери) хранят материал в собственном поле Opening.MaterialId (B-11), здесь не участвуют.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    /// <summary>Названия слотов, чтобы не писать строки вручную.</summary>
    public static class MaterialSlots
    {
        public const string Floor = "floor";
        public const string Ceiling = "ceiling";
        public const string Surface = "surface";
    }

    public static class MaterialRules
    {
        private enum TargetKind { None, Room, Wall, Object }

        /// <summary>
        /// Проверяет, можно ли положить материал на цель в слот. Возвращает null, если можно,
        /// иначе понятный текст ошибки. materialId = null означает «сбросить материал» и всегда допустим
        /// (если цель и слот корректны).
        /// </summary>
        public static string Check(Project project, ICatalogService catalog,
            string targetId, string slot, string materialId)
        {
            if (project == null) return "Проект не задан.";
            if (catalog == null) return "Каталог не задан.";
            if (string.IsNullOrWhiteSpace(targetId)) return "Не указан элемент, к которому применяется материал.";
            if (string.IsNullOrWhiteSpace(slot)) return "Не указана часть элемента (слот).";

            PlacedObject obj;
            MaterialSurface required;
            string error = ResolveTarget(project, targetId, slot, out obj, out required);
            if (error != null) return error;

            if (materialId == null) return null;

            MaterialInfo material = catalog.GetMaterial(materialId);
            if (material == null) return "Материал '" + materialId + "' не найден в каталоге.";

            if (obj != null)
            {
                CatalogItem item = catalog.GetItem(obj.CatalogId);
                if (item == null) return "Предмет '" + obj.CatalogId + "' не найден в каталоге.";
                if (!item.AllowedMaterialIds.Contains(materialId))
                    return "Материал '" + materialId + "' не подходит для предмета '" + item.Name + "'.";
                return null;
            }

            if ((material.Surfaces & required) == MaterialSurface.None)
                return "Материал '" + material.Name + "' не подходит для этой поверхности.";
            return null;
        }

        /// <summary>Id материала, который сейчас положен на цель в слот, или null (материал по умолчанию).</summary>
        public static string GetMaterialId(Project project, string targetId, string slot)
        {
            if (project == null) return null;
            foreach (MaterialOverride m in project.MaterialOverrides)
                if (m.TargetId == targetId && m.Slot == slot) return m.MaterialId;
            return null;
        }

        /// <summary>
        /// Материалы, которые можно предложить пользователю для этой цели и слота (для панели выбора, C-08).
        /// Если цель или слот некорректны, список пустой. Никогда не возвращает null.
        /// </summary>
        public static IReadOnlyList<MaterialInfo> GetAllowedMaterials(Project project, ICatalogService catalog,
            string targetId, string slot)
        {
            var result = new List<MaterialInfo>();
            if (project == null || catalog == null) return result;

            PlacedObject obj;
            MaterialSurface required;
            if (ResolveTarget(project, targetId, slot, out obj, out required) != null) return result;

            if (obj != null)
            {
                CatalogItem item = catalog.GetItem(obj.CatalogId);
                if (item == null) return result;
                foreach (string id in item.AllowedMaterialIds)
                {
                    MaterialInfo m = catalog.GetMaterial(id);
                    if (m != null) result.Add(m);
                }
                return result;
            }

            result.AddRange(catalog.GetMaterials(required));
            return result;
        }

        // Определяет, что это за цель, и какая поверхность каталога нужна. Возвращает текст ошибки или null.
        private static string ResolveTarget(Project project, string targetId, string slot,
            out PlacedObject obj, out MaterialSurface required)
        {
            obj = null;
            required = MaterialSurface.None;
            if (string.IsNullOrWhiteSpace(targetId)) return "Не указан элемент, к которому применяется материал.";

            TargetKind kind = TargetKind.None;
            if (project.Rooms.Exists(r => r.Id == targetId)) kind = TargetKind.Room;
            else if (project.Walls.Exists(w => w.Id == targetId)) kind = TargetKind.Wall;
            else
            {
                obj = project.Objects.Find(o => o.Id == targetId);
                if (obj != null) kind = TargetKind.Object;
            }

            switch (kind)
            {
                case TargetKind.Room:
                    if (slot == MaterialSlots.Floor) { required = MaterialSurface.Floor; return null; }
                    if (slot == MaterialSlots.Ceiling) { required = MaterialSurface.Ceiling; return null; }
                    return "У помещения материал можно задать для слотов 'floor' и 'ceiling', а не '" + slot + "'.";
                case TargetKind.Wall:
                    if (slot == MaterialSlots.Surface) { required = MaterialSurface.Wall; return null; }
                    return "У стены материал задаётся для слота 'surface', а не '" + slot + "'.";
                case TargetKind.Object:
                    if (slot == MaterialSlots.Surface) { required = MaterialSurface.Object; return null; }
                    return "У предмета материал задаётся для слота 'surface', а не '" + slot + "'.";
                default:
                    return "Элемент '" + targetId + "' не найден в проекте.";
            }
        }
    }
}
