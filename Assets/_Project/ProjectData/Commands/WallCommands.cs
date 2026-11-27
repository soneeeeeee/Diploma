// Команды для стен: добавить, изменить положение, удалить (задача B-11, FR-112).

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    // Общий помощник: вынуть из списка подходящие элементы, запомнив их места, и вернуть обратно.
    internal struct Taken<T>
    {
        public int Index;
        public T Item;
    }

    internal static class ListTaker
    {
        public static List<Taken<T>> TakeAll<T>(List<T> list, Predicate<T> match)
        {
            var taken = new List<Taken<T>>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!match(list[i])) continue;
                taken.Add(new Taken<T> { Index = i, Item = list[i] });
                list.RemoveAt(i);
            }
            taken.Reverse();
            return taken;
        }

        public static void PutBack<T>(List<T> list, List<Taken<T>> taken)
        {
            foreach (Taken<T> t in taken) list.Insert(t.Index, t.Item);
        }
    }

    /// <summary>Добавляет стену по двум точкам (например, перегородку).</summary>
    public sealed class AddWallCommand : ICommand
    {
        private readonly Wall _wall;

        public AddWallCommand(PlanPoint a, PlanPoint b, float height = 2.7f, float thickness = 0.1f,
            string roomId = null, ElementState state = ElementState.Proposed, string name = "Добавить стену")
        {
            if (!(WallRules.Length(a, b) >= WallRules.MinWallLength))
                throw new ArgumentException("Стена слишком короткая (меньше 0,1 м).");
            if (!(height > 0f) || !(thickness > 0f))
                throw new ArgumentException("Высота и толщина стены должны быть больше нуля.");

            _wall = new Wall
            {
                Id = IdGenerator.New("wall"),
                RoomId = roomId,
                A = a,
                B = b,
                Height = height,
                Thickness = thickness,
                State = state
            };
            Name = name;
        }

        public string Name { get; }

        /// <summary>Id создаваемой стены.</summary>
        public string WallId { get { return _wall.Id; } }

        public void Execute(Project project)
        {
            if (_wall.RoomId != null && project.Rooms.FindIndex(r => r.Id == _wall.RoomId) < 0)
                throw new InvalidOperationException("Помещение '" + _wall.RoomId + "' не найдено.");
            if (project.Walls.FindIndex(w => w.Id == _wall.Id) >= 0)
                throw new InvalidOperationException("Стена с id '" + _wall.Id + "' уже есть в проекте.");
            project.Walls.Add(_wall);
        }

        public void Undo(Project project)
        {
            project.Walls.RemoveAll(w => w.Id == _wall.Id);
        }
    }

    /// <summary>
    /// Меняет положение концов стены. Если после этого какой-то проём перестанет помещаться в стену,
    /// команда отклоняется и ничего не меняет.
    /// </summary>
    public sealed class MoveWallCommand : ICommand
    {
        private readonly string _wallId;
        private readonly PlanPoint _a;
        private readonly PlanPoint _b;
        private PlanPoint _oldA;
        private PlanPoint _oldB;

        public MoveWallCommand(string wallId, PlanPoint a, PlanPoint b, string name = "Изменить положение стены")
        {
            if (string.IsNullOrWhiteSpace(wallId)) throw new ArgumentException("Не указан id стены.");
            if (!(WallRules.Length(a, b) >= WallRules.MinWallLength))
                throw new ArgumentException("Стена слишком короткая (меньше 0,1 м).");
            _wallId = wallId;
            _a = a;
            _b = b;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            Wall wall = project.Walls.Find(w => w.Id == _wallId);
            if (wall == null) throw new InvalidOperationException("Стена '" + _wallId + "' не найдена.");

            float newLength = WallRules.Length(_a, _b);
            foreach (Opening o in WallRules.OpeningsOf(project, _wallId))
            {
                if (o.Offset + o.Width > newLength + 0.001f)
                    throw new InvalidOperationException(
                        "Стена станет короче, и проём '" + o.Id + "' в неё не поместится.");
            }

            _oldA = wall.A;
            _oldB = wall.B;
            wall.A = _a;
            wall.B = _b;
        }

        public void Undo(Project project)
        {
            Wall wall = project.Walls.Find(w => w.Id == _wallId);
            if (wall == null) return;
            wall.A = _oldA;
            wall.B = _oldB;
        }
    }

    /// <summary>
    /// Удаляет стену вместе со всем, что на неё ссылается: проёмы, замены материала, результаты сравнения.
    /// Отмена возвращает всё на прежние места.
    /// </summary>
    public sealed class RemoveWallCommand : ICommand
    {
        private readonly string _wallId;
        private Wall _removed;
        private int _removedIndex;
        private List<Taken<Opening>> _openings;
        private List<Taken<MaterialOverride>> _overrides;
        private List<Taken<ComparisonResult>> _results;

        public RemoveWallCommand(string wallId, string name = "Удалить стену")
        {
            if (string.IsNullOrWhiteSpace(wallId)) throw new ArgumentException("Не указан id стены.");
            _wallId = wallId;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            int index = project.Walls.FindIndex(w => w.Id == _wallId);
            if (index < 0) throw new InvalidOperationException("Стена '" + _wallId + "' не найдена.");

            _removedIndex = index;
            _removed = project.Walls[index];
            project.Walls.RemoveAt(index);

            _openings = ListTaker.TakeAll(project.Openings, o => o.WallId == _wallId);

            var ids = new HashSet<string> { _wallId };
            foreach (Taken<Opening> t in _openings) ids.Add(t.Item.Id);

            _overrides = ListTaker.TakeAll(project.MaterialOverrides, m => ids.Contains(m.TargetId));
            _results = ListTaker.TakeAll(project.ComparisonResults, c => ids.Contains(c.ElementId));
        }

        public void Undo(Project project)
        {
            project.Walls.Insert(_removedIndex, _removed);
            ListTaker.PutBack(project.Openings, _openings);
            ListTaker.PutBack(project.MaterialOverrides, _overrides);
            ListTaker.PutBack(project.ComparisonResults, _results);
        }
    }
}
