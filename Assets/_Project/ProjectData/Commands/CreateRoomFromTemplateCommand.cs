// Добавляет в существующий проект новое помещение по шаблону. Можно отменить и повторить.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public sealed class CreateRoomFromTemplateCommand : ICommand
    {
        private readonly GeneratedRoom _generated;

        /// <exception cref="ArgumentException">Если параметры заявки недопустимы.</exception>
        public CreateRoomFromTemplateCommand(RoomSpec spec, string name = "Создать помещение")
        {
            // Помещение и id создаются сразу, поэтому после "повторить" id остаются теми же.
            _generated = RoomGenerator.Generate(spec);
            Name = name;
        }

        public string Name { get; }

        /// <summary>Id создаваемого помещения.</summary>
        public string RoomId { get { return _generated.Room.Id; } }

        public void Execute(Project project)
        {
            if (project.Rooms.FindIndex(r => r.Id == _generated.Room.Id) >= 0)
                throw new InvalidOperationException("Помещение с id '" + _generated.Room.Id + "' уже есть в проекте.");

            project.Rooms.Add(_generated.Room);
            project.Walls.AddRange(_generated.Walls);
        }

        public void Undo(Project project)
        {
            var wallIds = new HashSet<string>();
            foreach (Wall w in _generated.Walls) wallIds.Add(w.Id);

            project.Walls.RemoveAll(w => wallIds.Contains(w.Id));
            project.Rooms.RemoveAll(r => r.Id == _generated.Room.Id);
        }
    }
}
