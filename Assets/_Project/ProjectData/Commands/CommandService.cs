// Реализация ICommandService: выполнение команд и история undo/redo (задача B-06, FR-106).
// Любое изменение проекта проходит через Execute. Не зависит от Unity.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public sealed class CommandService : ICommandService
    {
        private readonly IProjectService _projects;
        private readonly List<ICommand> _undo = new List<ICommand>();
        private readonly List<ICommand> _redo = new List<ICommand>();

        /// <param name="capacity">Сколько команд помнить. Не меньше 50 (FR-106).</param>
        public CommandService(IProjectService projects, int capacity = 100)
        {
            if (projects == null) throw new ArgumentNullException(nameof(projects));
            if (capacity < 50)
                throw new ArgumentOutOfRangeException(nameof(capacity), "История должна хранить не менее 50 команд (FR-106).");

            _projects = projects;
            Capacity = capacity;
            // При открытии другого проекта старая история теряет смысл.
            _projects.ProjectLoaded += OnProjectLoaded;
        }

        public int Capacity { get; }
        public int UndoCount { get { return _undo.Count; } }
        public int RedoCount { get { return _redo.Count; } }

        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        public string NextUndoName { get { return _undo.Count > 0 ? _undo[_undo.Count - 1].Name : null; } }
        public string NextRedoName { get { return _redo.Count > 0 ? _redo[_redo.Count - 1].Name : null; } }

        public event Action HistoryChanged;

        public void Execute(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Project project = RequireProject();

            // Если команда упадёт, до следующей строки дело не дойдёт: история и проект остаются как были.
            command.Execute(project);

            _undo.Add(command);
            if (_undo.Count > Capacity) _undo.RemoveAt(0);
            _redo.Clear();

            _projects.MarkChanged();
            RaiseHistoryChanged();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            Project project = RequireProject();

            ICommand command = _undo[_undo.Count - 1];
            command.Undo(project);
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(command);

            _projects.MarkChanged();
            RaiseHistoryChanged();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            Project project = RequireProject();

            ICommand command = _redo[_redo.Count - 1];
            command.Execute(project);
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(command);

            _projects.MarkChanged();
            RaiseHistoryChanged();
        }

        /// <summary>Очищает историю (не входит в интерфейс).</summary>
        public void Clear()
        {
            if (_undo.Count == 0 && _redo.Count == 0) return;
            _undo.Clear();
            _redo.Clear();
            RaiseHistoryChanged();
        }

        private void OnProjectLoaded(Project project)
        {
            Clear();
        }

        private Project RequireProject()
        {
            Project project = _projects.Current;
            if (project == null) throw new InvalidOperationException("Нет открытого проекта.");
            return project;
        }

        private void RaiseHistoryChanged()
        {
            Action handler = HistoryChanged;
            if (handler != null) handler();
        }
    }
}
