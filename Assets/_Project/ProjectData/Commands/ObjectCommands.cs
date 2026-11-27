// Первые команды редактора: добавить объект, удалить объект, изменить положение/поворот/масштаб,
// а также составная команда. Остальные команды (стены, проёмы, материалы...) появятся в своих задачах.
//
// Правила для любой команды:
//  - Execute вызывается и при первом выполнении, и при повторе (redo), поэтому должна работать многократно;
//  - если Execute не может выполниться, он бросает исключение, НЕ изменив проект наполовину;
//  - Undo возвращает проект ровно в то состояние, которое было до Execute.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    /// <summary>Добавляет готовый объект в проект.</summary>
    public sealed class AddObjectCommand : ICommand
    {
        private readonly PlacedObject _object;

        public AddObjectCommand(PlacedObject obj, string name = "Добавить объект")
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            _object = obj;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            if (project.Objects.FindIndex(o => o.Id == _object.Id) >= 0)
                throw new InvalidOperationException("Объект с id '" + _object.Id + "' уже есть в проекте.");
            project.Objects.Add(_object);
        }

        public void Undo(Project project)
        {
            int index = project.Objects.FindIndex(o => o.Id == _object.Id);
            if (index >= 0) project.Objects.RemoveAt(index);
        }
    }

    /// <summary>
    /// Удаляет объект вместе со всем, что на него ссылается (состояния в вариантах, замены материала),
    /// чтобы не оставлять битых ссылок (раздел 6.1 ТЗ). Undo возвращает всё на прежние места.
    /// </summary>
    public sealed class RemoveObjectCommand : ICommand
    {
        private struct Taken<T>
        {
            public int Index;
            public T Item;
        }

        private readonly string _objectId;
        private PlacedObject _removed;
        private int _removedIndex;
        private List<Taken<MaterialOverride>> _overrides;
        private List<KeyValuePair<Variant, List<Taken<ObjectState>>>> _states;

        public RemoveObjectCommand(string objectId, string name = "Удалить объект")
        {
            if (string.IsNullOrWhiteSpace(objectId)) throw new ArgumentException("Не указан id объекта.");
            _objectId = objectId;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            int index = project.Objects.FindIndex(o => o.Id == _objectId);
            if (index < 0) throw new InvalidOperationException("Объект '" + _objectId + "' не найден.");

            _removedIndex = index;
            _removed = project.Objects[index];
            project.Objects.RemoveAt(index);

            _overrides = TakeAll(project.MaterialOverrides, m => m.TargetId == _objectId);

            _states = new List<KeyValuePair<Variant, List<Taken<ObjectState>>>>();
            foreach (Variant variant in project.Variants)
            {
                List<Taken<ObjectState>> taken = TakeAll(variant.ObjectStates, s => s.ObjectId == _objectId);
                if (taken.Count > 0)
                    _states.Add(new KeyValuePair<Variant, List<Taken<ObjectState>>>(variant, taken));
            }
        }

        public void Undo(Project project)
        {
            project.Objects.Insert(_removedIndex, _removed);
            PutBack(project.MaterialOverrides, _overrides);
            foreach (var pair in _states) PutBack(pair.Key.ObjectStates, pair.Value);
        }

        // Вынимает из списка все подходящие элементы, запоминая их места (по возрастанию индекса).
        private static List<Taken<T>> TakeAll<T>(List<T> list, Predicate<T> match)
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

        // Возвращает элементы на прежние места (вставляем по возрастанию индекса).
        private static void PutBack<T>(List<T> list, List<Taken<T>> taken)
        {
            foreach (Taken<T> t in taken) list.Insert(t.Index, t.Item);
        }
    }

    /// <summary>Меняет положение, поворот и масштаб объекта.</summary>
    public sealed class SetObjectTransformCommand : ICommand
    {
        private readonly string _objectId;
        private readonly Vec3 _position;
        private readonly Vec3 _rotation;
        private readonly Vec3 _scale;
        private Vec3 _oldPosition;
        private Vec3 _oldRotation;
        private Vec3 _oldScale;

        public SetObjectTransformCommand(string objectId, Vec3 position, Vec3 rotation, Vec3 scale,
            string name = "Переместить объект")
        {
            if (string.IsNullOrWhiteSpace(objectId)) throw new ArgumentException("Не указан id объекта.");
            _objectId = objectId;
            _position = position;
            _rotation = rotation;
            _scale = scale;
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            PlacedObject obj = Find(project);
            _oldPosition = obj.Position;
            _oldRotation = obj.Rotation;
            _oldScale = obj.Scale;
            obj.Position = _position;
            obj.Rotation = _rotation;
            obj.Scale = _scale;
        }

        public void Undo(Project project)
        {
            PlacedObject obj = Find(project);
            obj.Position = _oldPosition;
            obj.Rotation = _oldRotation;
            obj.Scale = _oldScale;
        }

        private PlacedObject Find(Project project)
        {
            PlacedObject obj = project.Objects.Find(o => o.Id == _objectId);
            if (obj == null) throw new InvalidOperationException("Объект '" + _objectId + "' не найден.");
            return obj;
        }
    }

    /// <summary>
    /// Несколько команд как одно действие (одна запись в истории). Если одна из команд не удалась,
    /// уже выполненные откатываются. Пригодится для группового перемещения (FR-110).
    /// </summary>
    public sealed class CompositeCommand : ICommand
    {
        private readonly List<ICommand> _commands;

        public CompositeCommand(string name, IEnumerable<ICommand> commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            _commands = new List<ICommand>(commands);
            if (_commands.Count == 0) throw new ArgumentException("Составная команда не может быть пустой.");
            Name = name;
        }

        public string Name { get; }

        public void Execute(Project project)
        {
            int done = 0;
            try
            {
                for (; done < _commands.Count; done++) _commands[done].Execute(project);
            }
            catch
            {
                for (int i = done - 1; i >= 0; i--) _commands[i].Undo(project);
                throw;
            }
        }

        public void Undo(Project project)
        {
            for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(project);
        }
    }
}
