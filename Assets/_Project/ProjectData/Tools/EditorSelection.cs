// Выбор элемента в редакторе (B-07, FR-103): предмет, стена, пол или потолок помещения.
// Не зависит от Unity: одинаково работает в VR, MR и на телефоне.

using System;

namespace XrInterior.ProjectData
{
    public enum SelectionKind { None, Object, Wall, Floor, Ceiling }

    public sealed class EditorSelection
    {
        public const string PartFloor = "floor";
        public const string PartCeiling = "ceiling";

        /// <summary>Id предмета, стены или помещения (если выбран пол или потолок).</summary>
        public string SelectedId { get; private set; }

        /// <summary>null для предмета и стены; "floor" или "ceiling" для частей помещения.</summary>
        public string SelectedPart { get; private set; }

        public bool HasSelection { get { return SelectedId != null; } }

        /// <summary>Уникальный ключ выбора: id или id#часть. Нужен, чтобы подсвечивать выбранное.</summary>
        public string Key { get { return SelectedId == null ? null : (SelectedPart == null ? SelectedId : SelectedId + "#" + SelectedPart); } }

        /// <summary>Вызывается при смене выбора; параметр: Key нового выбора или null, если выбор снят.</summary>
        public event Action<string> SelectionChanged;

        public void Select(string id) { Set(id, null); }

        public void SelectRoomPart(string roomId, string part) { Set(roomId, part); }

        public void Clear() { Set(null, null); }

        private void Set(string id, string part)
        {
            if (id == null) part = null;
            if (SelectedId == id && SelectedPart == part) return;
            SelectedId = id;
            SelectedPart = part;
            Action<string> handler = SelectionChanged;
            if (handler != null) handler(Key);
        }

        public SelectionKind KindOf(Project project)
        {
            if (project == null || SelectedId == null) return SelectionKind.None;
            if (SelectedPart == PartFloor) return SelectionKind.Floor;
            if (SelectedPart == PartCeiling) return SelectionKind.Ceiling;
            if (project.Objects.Exists(o => o.Id == SelectedId)) return SelectionKind.Object;
            if (project.Walls.Exists(w => w.Id == SelectedId)) return SelectionKind.Wall;
            return SelectionKind.None;
        }

        /// <summary>Выбирает следующий по списку предмет проекта (по кругу). Возвращает его id или null, если предметов нет.</summary>
        public string SelectNext(Project project) { return Cycle(project, SelectionKind.Object, 1); }

        /// <summary>Выбирает следующий (step = 1) или предыдущий (step = -1) предмет или стену по кругу. Возвращает id или null.</summary>
        public string Cycle(Project project, SelectionKind kind, int step)
        {
            if (project == null) { Clear(); return null; }
            if (step == 0) step = 1;

            if (kind == SelectionKind.Object)
            {
                if (project.Objects.Count == 0) { Clear(); return null; }
                int index = SelectedPart == null ? project.Objects.FindIndex(o => o.Id == SelectedId) : -1;
                int next = Wrap(index, step, project.Objects.Count);
                Select(project.Objects[next].Id);
                return SelectedId;
            }

            if (kind == SelectionKind.Wall)
            {
                if (project.Walls.Count == 0) { Clear(); return null; }
                int index = SelectedPart == null ? project.Walls.FindIndex(w => w.Id == SelectedId) : -1;
                int next = Wrap(index, step, project.Walls.Count);
                Select(project.Walls[next].Id);
                return SelectedId;
            }

            return null;
        }

        // Из позиции -1 («ничего не выбрано») шаг вперёд даёт первый элемент, шаг назад - последний.
        private static int Wrap(int index, int step, int count)
        {
            if (index < 0) return step > 0 ? 0 : count - 1;
            return ((index + step) % count + count) % count;
        }

        /// <summary>Снимает выбор, если выбранного элемента больше нет в проекте (удалён или отменено добавление).</summary>
        public void Refresh(Project project)
        {
            if (SelectedId == null) return;
            if (project == null) { Clear(); return; }

            bool exists;
            if (SelectedPart != null) exists = project.Rooms.Exists(r => r.Id == SelectedId);
            else exists = project.Objects.Exists(o => o.Id == SelectedId) || project.Walls.Exists(w => w.Id == SelectedId);
            if (!exists) Clear();
        }
    }
}
