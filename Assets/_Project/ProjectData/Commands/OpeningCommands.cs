// Команды для проёмов (окна и двери): добавить, изменить, удалить (задача B-11, FR-112).
// Каждая команда сначала проверяет правила (WallRules.CheckOpening) и только потом меняет проект.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public sealed class AddOpeningCommand : ICommand
    {
        private readonly Opening _opening;

        public AddOpeningCommand(string wallId, OpeningType type, float offset, float width, float height,
            float sillHeight = 0f, string materialId = null, ElementState state = ElementState.Proposed,
            string name = null)
        {
            if (string.IsNullOrWhiteSpace(wallId)) throw new ArgumentException("Не указан id стены.");

            _opening = new Opening
            {
                Id = IdGenerator.New("opening"),
                WallId = wallId,
                Type = type,
                Offset = offset,
                Width = width,
                Height = height,
                SillHeight = sillHeight,
                MaterialId = materialId,
                State = state
            };
            Name = name ?? (type == OpeningType.Door ? "Добавить дверь" : "Добавить окно");
        }

        public string Name { get; }

        /// <summary>Id создаваемого проёма.</summary>
        public string OpeningId { get { return _opening.Id; } }

        public void Execute(Project project)
        {
            Wall wall = project.Walls.Find(w => w.Id == _opening.WallId);
            if (wall == null) throw new InvalidOperationException("Стена '" + _opening.WallId + "' не найдена.");

            string error = WallRules.CheckOpening(WallRules.Length(wall), wall.Height,
                WallRules.OpeningsOf(project, wall.Id),
                _opening.Offset, _opening.Width, _opening.Height, _opening.SillHeight);
            if (error != null) throw new InvalidOperationException(error);

            project.Openings.Add(_opening);
            wall.OpeningIds.Add(_opening.Id);
        }

        public void Undo(Project project)
        {
            project.Openings.RemoveAll(o => o.Id == _opening.Id);
            Wall wall = project.Walls.Find(w => w.Id == _opening.WallId);
            if (wall != null) wall.OpeningIds.Remove(_opening.Id);
        }
    }

    /// <summary>Меняет положение, размеры и материал проёма за одно действие.</summary>
    public sealed class UpdateOpeningCommand : ICommand
    {
        private readonly string _openingId;
        private readonly float _offset;
        private readonly float _width;
        private readonly float _height;
        private readonly float _sillHeight;
        private readonly string _materialId;

        private float _oldOffset;
        private float _oldWidth;
        private float _oldHeight;
        private float _oldSillHeight;
        private string _oldMaterialId;

        public UpdateOpeningCommand(string openingId, float offset, float width, float height,
            float sillHeight, string materialId, string name = "Изменить проём")
        {
            if (string.IsNullOrWhiteSpace(openingId)) throw new ArgumentException("Не указан id проёма.");
            _openingId = openingId;
            _offset = offset;
            _width = width;
            _height = height;
            _sillHeight = sillHeight;
            _materialId = materialId;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            Opening opening = project.Openings.Find(o => o.Id == _openingId);
            if (opening == null) throw new InvalidOperationException("Проём '" + _openingId + "' не найден.");
            Wall wall = project.Walls.Find(w => w.Id == opening.WallId);
            if (wall == null) throw new InvalidOperationException("Стена проёма '" + _openingId + "' не найдена.");

            string error = WallRules.CheckOpening(WallRules.Length(wall), wall.Height,
                WallRules.OpeningsOf(project, wall.Id, opening.Id),
                _offset, _width, _height, _sillHeight);
            if (error != null) throw new InvalidOperationException(error);

            _oldOffset = opening.Offset;
            _oldWidth = opening.Width;
            _oldHeight = opening.Height;
            _oldSillHeight = opening.SillHeight;
            _oldMaterialId = opening.MaterialId;

            opening.Offset = _offset;
            opening.Width = _width;
            opening.Height = _height;
            opening.SillHeight = _sillHeight;
            opening.MaterialId = _materialId;
        }

        public void Undo(Project project)
        {
            Opening opening = project.Openings.Find(o => o.Id == _openingId);
            if (opening == null) return;
            opening.Offset = _oldOffset;
            opening.Width = _oldWidth;
            opening.Height = _oldHeight;
            opening.SillHeight = _oldSillHeight;
            opening.MaterialId = _oldMaterialId;
        }
    }

    /// <summary>Удаляет проём вместе с замёнами материала и результатами сравнения, которые на него ссылаются.</summary>
    public sealed class RemoveOpeningCommand : ICommand
    {
        private readonly string _openingId;
        private Opening _removed;
        private int _removedIndex;
        private string _wallId;
        private int _wallListIndex;
        private List<Taken<MaterialOverride>> _overrides;
        private List<Taken<ComparisonResult>> _results;

        public RemoveOpeningCommand(string openingId, string name = "Удалить проём")
        {
            if (string.IsNullOrWhiteSpace(openingId)) throw new ArgumentException("Не указан id проёма.");
            _openingId = openingId;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            int index = project.Openings.FindIndex(o => o.Id == _openingId);
            if (index < 0) throw new InvalidOperationException("Проём '" + _openingId + "' не найден.");

            _removedIndex = index;
            _removed = project.Openings[index];
            project.Openings.RemoveAt(index);

            _wallId = _removed.WallId;
            _wallListIndex = -1;
            Wall wall = project.Walls.Find(w => w.Id == _wallId);
            if (wall != null)
            {
                _wallListIndex = wall.OpeningIds.IndexOf(_openingId);
                if (_wallListIndex >= 0) wall.OpeningIds.RemoveAt(_wallListIndex);
            }

            _overrides = ListTaker.TakeAll(project.MaterialOverrides, m => m.TargetId == _openingId);
            _results = ListTaker.TakeAll(project.ComparisonResults, c => c.ElementId == _openingId);
        }

        public void Undo(Project project)
        {
            project.Openings.Insert(_removedIndex, _removed);

            Wall wall = project.Walls.Find(w => w.Id == _wallId);
            if (wall != null && _wallListIndex >= 0) wall.OpeningIds.Insert(_wallListIndex, _openingId);

            ListTaker.PutBack(project.MaterialOverrides, _overrides);
            ListTaker.PutBack(project.ComparisonResults, _results);
        }
    }
}
