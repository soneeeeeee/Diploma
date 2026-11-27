using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class WallCommandTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "xri_" + Guid.NewGuid().ToString("N")); }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        private ProjectService NewProjects() { return new ProjectService(new ProjectStorage(_dir)); }

        private static string Snap(Project p)
        {
            JObject o = JObject.Parse(ProjectSerializer.ToJson(p));
            o.Remove("updatedAt");
            return o.ToString();
        }

        // Проект с комнатой 4 x 3 м (4 стены; первая стена длиной 4 м).
        private ProjectService NewProjectWithRoom(out CommandService cmds)
        {
            ProjectService projects = NewProjects();
            TemplateProjectBuilder.CreateFromTemplate(projects, "T",
                new RoomSpec { Template = RoomTemplate.Rectangular });
            cmds = new CommandService(projects);
            return projects;
        }

        // Перегородка длиной 3 м поперёк комнаты.
        private static AddWallCommand Partition(Project p)
        {
            return new AddWallCommand(new PlanPoint(2f, 0f), new PlanPoint(2f, 3f), 2.7f, 0.1f, p.Rooms[0].Id);
        }

        [Test]
        public void AddWall_UndoRedo()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);
            string before = Snap(projects.Current);

            cmds.Execute(Partition(projects.Current));
            Assert.AreEqual(5, projects.Current.Walls.Count);
            Assert.IsEmpty(ProjectValidator.Validate(projects.Current));
            string after = Snap(projects.Current);

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));
            cmds.Redo();
            Assert.AreEqual(after, Snap(projects.Current));
        }

        [Test]
        public void AddWall_InvalidInput_IsRejected()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);

            Assert.Throws<ArgumentException>(
                () => new AddWallCommand(new PlanPoint(1f, 1f), new PlanPoint(1f, 1.01f)));

            var unknownRoom = new AddWallCommand(new PlanPoint(0f, 0f), new PlanPoint(1f, 0f), 2.7f, 0.1f, "room_net");
            Assert.Throws<InvalidOperationException>(() => cmds.Execute(unknownRoom));

            Assert.AreEqual(0, cmds.UndoCount);
            Assert.AreEqual(4, projects.Current.Walls.Count);
        }

        [Test]
        public void PartitionWindowDoor_AreSavedAndReopened()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);

            var wall = Partition(projects.Current);
            cmds.Execute(wall);
            cmds.Execute(new AddOpeningCommand(wall.WallId, OpeningType.Window, 0.05f, 1.2f, 1.2f, 0.9f, "mat_glass_01"));
            cmds.Execute(new AddOpeningCommand(wall.WallId, OpeningType.Door, 2.05f, 0.9f, 2.1f, 0f, "mat_door_01"));

            Assert.IsEmpty(ProjectValidator.Validate(projects.Current));

            Project back = NewProjects().Load(projects.Current.Id);
            Wall backWall = back.Walls.Find(w => w.Id == wall.WallId);
            Assert.IsNotNull(backWall);
            Assert.AreEqual(2, backWall.OpeningIds.Count);
            Assert.AreEqual(2, back.Openings.Count);
            Assert.AreEqual(1, back.Openings.Count(o => o.Type == OpeningType.Door));
            Assert.AreEqual(1, back.Openings.Count(o => o.Type == OpeningType.Window));
            Assert.IsEmpty(ProjectValidator.Validate(back));
        }

        [Test]
        public void AddOpening_BreakingRules_IsRejectedAndChangesNothing()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);
            string wallId = projects.Current.Walls[0].Id; // длина 4 м, высота 2,7 м

            cmds.Execute(new AddOpeningCommand(wallId, OpeningType.Door, 0.5f, 0.9f, 2.1f));
            string before = Snap(projects.Current);

            // шире стены
            Assert.Throws<InvalidOperationException>(() =>
                cmds.Execute(new AddOpeningCommand(wallId, OpeningType.Window, 0f, 5f, 1f, 1f)));
            // за краем стены
            Assert.Throws<InvalidOperationException>(() =>
                cmds.Execute(new AddOpeningCommand(wallId, OpeningType.Window, 3.5f, 1f, 1f, 1f)));
            // выше стены
            Assert.Throws<InvalidOperationException>(() =>
                cmds.Execute(new AddOpeningCommand(wallId, OpeningType.Window, 2.5f, 1f, 2f, 1f)));
            // перекрывает дверь
            Assert.Throws<InvalidOperationException>(() =>
                cmds.Execute(new AddOpeningCommand(wallId, OpeningType.Window, 1.0f, 1.2f, 1.2f, 0.9f)));

            Assert.AreEqual(before, Snap(projects.Current));
            Assert.AreEqual(1, cmds.UndoCount);
        }

        [Test]
        public void UpdateOpening_ChangesSizeAndMaterial_UndoRestores()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);
            string wallId = projects.Current.Walls[0].Id;

            var add = new AddOpeningCommand(wallId, OpeningType.Door, 0.5f, 0.9f, 2.1f, 0f, "mat_a");
            cmds.Execute(add);
            string before = Snap(projects.Current);

            cmds.Execute(new UpdateOpeningCommand(add.OpeningId, 1.0f, 1.0f, 2.2f, 0f, "mat_b"));
            Opening o = projects.Current.Openings[0];
            Assert.AreEqual(1.0f, o.Offset, 0.0001f);
            Assert.AreEqual(1.0f, o.Width, 0.0001f);
            Assert.AreEqual(2.2f, o.Height, 0.0001f);
            Assert.AreEqual("mat_b", o.MaterialId);

            string afterUpdate = Snap(projects.Current);
            Assert.Throws<InvalidOperationException>(() =>
                cmds.Execute(new UpdateOpeningCommand(add.OpeningId, 1.0f, 10f, 2.2f, 0f, "mat_b")));
            Assert.AreEqual(afterUpdate, Snap(projects.Current));

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));
        }

        [Test]
        public void RemoveOpening_CleansReferences_AndUndoRestoresEverything()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);
            string wallId = projects.Current.Walls[0].Id;

            var add = new AddOpeningCommand(wallId, OpeningType.Door, 0.5f, 0.9f, 2.1f);
            cmds.Execute(add);
            projects.Current.MaterialOverrides.Add(
                new MaterialOverride { TargetId = add.OpeningId, Slot = "surface", MaterialId = "mat_x" });
            projects.Current.ComparisonResults.Add(new ComparisonResult
            {
                ElementId = add.OpeningId,
                Status = ComparisonStatus.Matched,
                ComparedAt = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)
            });
            string before = Snap(projects.Current);

            cmds.Execute(new RemoveOpeningCommand(add.OpeningId));

            Assert.AreEqual(0, projects.Current.Openings.Count);
            Assert.AreEqual(0, projects.Current.Walls[0].OpeningIds.Count);
            Assert.AreEqual(0, projects.Current.MaterialOverrides.Count);
            Assert.AreEqual(0, projects.Current.ComparisonResults.Count);
            Assert.IsEmpty(ProjectValidator.Validate(projects.Current));

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));
        }

        [Test]
        public void RemoveWall_CleansEverything_AndUndoRestoresEverything()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);

            var wall = Partition(projects.Current);
            cmds.Execute(wall);
            var window = new AddOpeningCommand(wall.WallId, OpeningType.Window, 0.05f, 1.2f, 1.2f, 0.9f);
            var door = new AddOpeningCommand(wall.WallId, OpeningType.Door, 2.05f, 0.9f, 2.1f);
            cmds.Execute(window);
            cmds.Execute(door);

            projects.Current.MaterialOverrides.Add(
                new MaterialOverride { TargetId = wall.WallId, Slot = "surface", MaterialId = "mat_wall" });
            projects.Current.MaterialOverrides.Add(
                new MaterialOverride { TargetId = door.OpeningId, Slot = "surface", MaterialId = "mat_door" });
            projects.Current.ComparisonResults.Add(new ComparisonResult
            {
                ElementId = wall.WallId,
                Status = ComparisonStatus.NotBuilt,
                ComparedAt = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc)
            });
            string before = Snap(projects.Current);

            cmds.Execute(new RemoveWallCommand(wall.WallId));

            Assert.AreEqual(4, projects.Current.Walls.Count);
            Assert.AreEqual(0, projects.Current.Openings.Count);
            Assert.AreEqual(0, projects.Current.MaterialOverrides.Count);
            Assert.AreEqual(0, projects.Current.ComparisonResults.Count);
            Assert.IsEmpty(ProjectValidator.Validate(projects.Current));

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));
        }

        [Test]
        public void MoveWall_UndoRestores_AndShorteningBelowOpeningsIsRejected()
        {
            CommandService cmds;
            ProjectService projects = NewProjectWithRoom(out cmds);

            var wall = Partition(projects.Current);
            cmds.Execute(wall);
            cmds.Execute(new AddOpeningCommand(wall.WallId, OpeningType.Door, 2.05f, 0.9f, 2.1f)); // до 2,95 м
            string before = Snap(projects.Current);

            // сдвиг без изменения длины - можно
            cmds.Execute(new MoveWallCommand(wall.WallId, new PlanPoint(3f, 0f), new PlanPoint(3f, 3f)));
            Wall moved = projects.Current.Walls.Find(w => w.Id == wall.WallId);
            Assert.AreEqual(3f, moved.A.X, 0.0001f);
            string afterMove = Snap(projects.Current);

            // стена укорачивается до 2 м, дверь (до 2,95 м) не помещается - нельзя
            Assert.Throws<InvalidOperationException>(() =>
                cmds.Execute(new MoveWallCommand(wall.WallId, new PlanPoint(3f, 0f), new PlanPoint(3f, 2f))));
            Assert.AreEqual(afterMove, Snap(projects.Current));

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));
        }

        [Test]
        public void Openings_NextToEachOther_AreAllowed()
        {
            var openings = new[]
            {
                new Opening { Id = "o1", Offset = 0f, Width = 0.9f }
            };

            // впритык справа и слева - можно
            Assert.IsNull(WallRules.CheckOpening(4f, 2.7f, openings, 0.9f, 1f, 1f, 1f));
            Assert.IsNull(WallRules.CheckOpening(4f, 2.7f, new Opening[0], 0f, 4f, 2.7f, 0f));

            // чуть заходит на соседний - нельзя
            Assert.IsNotNull(WallRules.CheckOpening(4f, 2.7f, openings, 0.8f, 1f, 1f, 1f));
        }
    }
}
