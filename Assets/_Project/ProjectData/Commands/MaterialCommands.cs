// Команды материалов (B-12, FR-107). Идут через ICommandService, поэтому работают undo/redo.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    /// <summary>
    /// Кладёт материал на элемент (помещение, стену, предмет) в слот. materialId = null сбрасывает материал
    /// к умолчанию (запись о замене удаляется). Перед изменением всё проверяется; если проверка не прошла,
    /// бросается InvalidOperationException, а проект остаётся нетронутым.
    /// </summary>
    public sealed class SetMaterialCommand : ICommand
    {
        private enum Change { None, Added, Replaced, Removed }

        private readonly string _targetId;
        private readonly string _slot;
        private readonly string _materialId;
        private readonly ICatalogService _catalog;

        private Change _change;
        private MaterialOverride _added;
        private MaterialOverride _removed;
        private int _removedIndex;
        private string _oldMaterialId;

        public SetMaterialCommand(string targetId, string slot, string materialId,
            ICatalogService catalog, string name = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            _targetId = targetId;
            _slot = slot;
            _materialId = materialId;
            _catalog = catalog;
            Name = name ?? (materialId == null ? "Сбросить материал" : "Применить материал");
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            string error = MaterialRules.Check(project, _catalog, _targetId, _slot, _materialId);
            if (error != null) throw new InvalidOperationException(error);

            List<MaterialOverride> list = project.MaterialOverrides;
            int index = list.FindIndex(m => m.TargetId == _targetId && m.Slot == _slot);

            _change = Change.None;
            _added = null;
            _removed = null;

            if (_materialId == null)
            {
                if (index < 0) return;
                _change = Change.Removed;
                _removed = list[index];
                _removedIndex = index;
                list.RemoveAt(index);
            }
            else if (index >= 0)
            {
                if (list[index].MaterialId == _materialId) return;
                _change = Change.Replaced;
                _oldMaterialId = list[index].MaterialId;
                list[index].MaterialId = _materialId;
            }
            else
            {
                _change = Change.Added;
                _added = new MaterialOverride { TargetId = _targetId, Slot = _slot, MaterialId = _materialId };
                list.Add(_added);
            }
        }

        public void Undo(Project project)
        {
            List<MaterialOverride> list = project.MaterialOverrides;
            switch (_change)
            {
                case Change.Added:
                    list.Remove(_added);
                    break;
                case Change.Replaced:
                    MaterialOverride current = list.Find(m => m.TargetId == _targetId && m.Slot == _slot);
                    if (current != null) current.MaterialId = _oldMaterialId;
                    break;
                case Change.Removed:
                    list.Insert(Math.Min(_removedIndex, list.Count), _removed);
                    break;
            }
        }
    }

    public static class MaterialCommands
    {
        /// <summary>
        /// Один материал на все стены помещения: одна запись в истории, один Undo отменяет всё.
        /// Берутся стены, которые на момент вызова принадлежат помещению (Wall.RoomId).
        /// </summary>
        /// <exception cref="InvalidOperationException">Если у помещения нет стен.</exception>
        public static ICommand ApplyToRoomWalls(Project project, string roomId, string materialId,
            ICatalogService catalog)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            var commands = new List<ICommand>();
            foreach (Wall wall in project.Walls)
                if (wall.RoomId == roomId)
                    commands.Add(new SetMaterialCommand(wall.Id, MaterialSlots.Surface, materialId, catalog));
            if (commands.Count == 0)
                throw new InvalidOperationException("У помещения '" + roomId + "' нет стен.");
            return new CompositeCommand("Материал на все стены помещения", commands);
        }
    }
}
