// Изменение длины и толщины стены (B-07, FR-112): новые концы и толщина одной командой, с отменой.

using System;

namespace XrInterior.ProjectData
{
    public sealed class ResizeWallCommand : ICommand
    {
        private readonly string _wallId;
        private readonly PlanPoint _a;
        private readonly PlanPoint _b;
        private readonly float _thickness;
        private PlanPoint _oldA;
        private PlanPoint _oldB;
        private float _oldThickness;

        public ResizeWallCommand(string wallId, PlanPoint a, PlanPoint b, float thickness, string name = "Изменить стену")
        {
            if (string.IsNullOrWhiteSpace(wallId)) throw new ArgumentException("Не указан id стены.");
            if (!(thickness > 0f)) throw new ArgumentException("Толщина стены должна быть больше нуля.");
            _wallId = wallId;
            _a = a;
            _b = b;
            _thickness = thickness;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            Wall wall = Find(project);
            _oldA = wall.A;
            _oldB = wall.B;
            _oldThickness = wall.Thickness;
            wall.A = _a;
            wall.B = _b;
            wall.Thickness = _thickness;
        }

        public void Undo(Project project)
        {
            Wall wall = Find(project);
            wall.A = _oldA;
            wall.B = _oldB;
            wall.Thickness = _oldThickness;
        }

        private Wall Find(Project project)
        {
            Wall wall = project.Walls.Find(w => w.Id == _wallId);
            if (wall == null) throw new InvalidOperationException("Стена '" + _wallId + "' не найдена.");
            return wall;
        }
    }
}
