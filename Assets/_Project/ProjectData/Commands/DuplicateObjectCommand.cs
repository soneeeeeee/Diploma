// Дублирование предмета (B-07, FR-103). Идёт через ICommandService, поэтому работают undo/redo.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    /// <summary>
    /// Создаёт копию предмета в новом месте: новый id, тот же предмет каталога, поворот, масштаб, слой
    /// и замены материалов. Копия всегда «проектируемая» (Proposed) и не входит в группу оригинала.
    /// Undo убирает копию вместе с её заменами материалов.
    /// </summary>
    public sealed class DuplicateObjectCommand : ICommand
    {
        private readonly string _sourceId;
        private readonly Vec3 _position;
        private readonly string _newId;

        private PlacedObject _copy;
        private readonly List<MaterialOverride> _addedOverrides = new List<MaterialOverride>();

        public DuplicateObjectCommand(string sourceId, Vec3 position, string newId = null,
            string name = "Дублировать объект")
        {
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("Не указан id объекта.");
            _sourceId = sourceId;
            _position = position;
            _newId = string.IsNullOrWhiteSpace(newId) ? IdGenerator.New("obj") : newId;
            Name = name;
        }

        public string Name { get; }

        /// <summary>Id создаваемой копии (известен сразу, до выполнения команды).</summary>
        public string NewObjectId { get { return _newId; } }

        public void Execute(Project project)
        {
            PlacedObject source = project.Objects.Find(o => o.Id == _sourceId);
            if (source == null)
                throw new InvalidOperationException("Объект '" + _sourceId + "' не найден в проекте.");
            if (project.Objects.Exists(o => o.Id == _newId))
                throw new InvalidOperationException("Объект с id '" + _newId + "' уже есть в проекте.");

            _copy = new PlacedObject
            {
                Id = _newId,
                CatalogId = source.CatalogId,
                Position = _position,
                Rotation = source.Rotation,
                Scale = source.Scale,
                Layer = source.Layer,
                State = ElementState.Proposed,
                GroupId = null
            };
            project.Objects.Add(_copy);

            _addedOverrides.Clear();
            foreach (MaterialOverride m in new List<MaterialOverride>(project.MaterialOverrides))
            {
                if (m.TargetId != _sourceId) continue;
                var copy = new MaterialOverride { TargetId = _newId, Slot = m.Slot, MaterialId = m.MaterialId };
                project.MaterialOverrides.Add(copy);
                _addedOverrides.Add(copy);
            }
        }

        public void Undo(Project project)
        {
            foreach (MaterialOverride m in _addedOverrides) project.MaterialOverrides.Remove(m);
            _addedOverrides.Clear();
            if (_copy != null) project.Objects.Remove(_copy);
            _copy = null;
        }
    }
}
