// Создание нового проекта из шаблона помещения (сценарий VR, раздел 4.1 ТЗ; FR-007).

using System;

namespace XrInterior.ProjectData
{
    public static class TemplateProjectBuilder
    {
        /// <summary>
        /// Создаёт проект с источником Template и одним помещением по шаблону, делает его текущим.
        /// Если параметры недопустимы, бросает ArgumentException и проект НЕ создаёт.
        /// </summary>
        public static Project CreateFromTemplate(IProjectService projects, string projectName, RoomSpec roomSpec,
            string buildingId = null)
        {
            if (projects == null) throw new ArgumentNullException(nameof(projects));

            // Сначала проверяем и генерируем помещение, чтобы при ошибке не остался пустой проект.
            GeneratedRoom generated = RoomGenerator.Generate(roomSpec);

            Project project = projects.Create(projectName, ProjectSource.Template, buildingId);
            project.Rooms.Add(generated.Room);
            project.Walls.AddRange(generated.Walls);
            projects.MarkChanged();
            return project;
        }
    }
}
