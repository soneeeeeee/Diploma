// Из заявки RoomSpec делает помещение и его стены с новыми идентификаторами.

using System;
using System.Collections.Generic;

namespace XrInterior.ProjectData
{
    public sealed class GeneratedRoom
    {
        public Room Room { get; set; }
        public List<Wall> Walls { get; set; }
    }

    public static class RoomGenerator
    {
        /// <exception cref="ArgumentException">Если параметры заявки недопустимы.</exception>
        public static GeneratedRoom Generate(RoomSpec spec)
        {
            List<string> errors = RoomTemplates.Validate(spec);
            if (errors.Count > 0)
                throw new ArgumentException("Недопустимые параметры помещения: " + string.Join("; ", errors));

            float height = spec.Height ?? RoomTemplates.GetDefaults(spec.Template).Height;
            List<PlanPoint> boundary = RoomTemplates.BuildBoundary(spec);

            var room = new Room
            {
                Id = IdGenerator.New("room"),
                Name = string.IsNullOrWhiteSpace(spec.Name) ? "Помещение" : spec.Name.Trim(),
                Floor = spec.Floor,
                Elevation = spec.Elevation,
                Boundary = boundary,
                Height = height,
                Template = spec.Template,
                TemplateParams = RoomTemplates.MergeParams(spec),
                State = spec.State
            };

            var walls = new List<Wall>();
            for (int i = 0; i < boundary.Count; i++)
            {
                walls.Add(new Wall
                {
                    Id = IdGenerator.New("wall"),
                    RoomId = room.Id,
                    A = boundary[i],
                    B = boundary[(i + 1) % boundary.Count],
                    Height = height,
                    Thickness = spec.WallThickness,
                    State = spec.State
                });
            }

            return new GeneratedRoom { Room = room, Walls = walls };
        }
    }
}
