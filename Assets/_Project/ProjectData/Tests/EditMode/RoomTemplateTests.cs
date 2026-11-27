using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class RoomTemplateTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "xri_" + Guid.NewGuid().ToString("N")); }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        private ProjectService NewProjects() { return new ProjectService(new ProjectStorage(_dir)); }

        private static float Area(List<PlanPoint> b)
        {
            float s = 0f;
            for (int i = 0; i < b.Count; i++)
            {
                int j = (i + 1) % b.Count;
                s += b[i].X * b[j].Z - b[j].X * b[i].Z;
            }
            return Math.Abs(s) / 2f;
        }

        private static float Length(Wall w)
        {
            float dx = w.B.X - w.A.X, dz = w.B.Z - w.A.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static void AssertWallsAreClosedLoop(List<Wall> walls)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                Wall next = walls[(i + 1) % walls.Count];
                Assert.AreEqual(walls[i].B.X, next.A.X, 0.0001f);
                Assert.AreEqual(walls[i].B.Z, next.A.Z, 0.0001f);
            }
        }

        private static string Snap(Project p)
        {
            JObject o = JObject.Parse(ProjectSerializer.ToJson(p));
            o.Remove("updatedAt");
            return o.ToString();
        }

        [Test]
        public void Rectangular_Geometry()
        {
            var spec = new RoomSpec
            {
                Template = RoomTemplate.Rectangular,
                Params = new Dictionary<string, float> { { "width", 4f }, { "depth", 3f } }
            };
            GeneratedRoom g = RoomGenerator.Generate(spec);

            Assert.AreEqual(4, g.Room.Boundary.Count);
            Assert.AreEqual(4, g.Walls.Count);
            Assert.AreEqual(12f, Area(g.Room.Boundary), 0.001f);
            Assert.AreEqual(new[] { 4f, 3f, 4f, 3f }, g.Walls.Select(Length).ToArray());
            AssertWallsAreClosedLoop(g.Walls);
        }

        [Test]
        public void LShaped_Geometry()
        {
            var spec = new RoomSpec
            {
                Template = RoomTemplate.LShaped,
                Params = new Dictionary<string, float>
                {
                    { "width", 6f }, { "depth", 5f }, { "cutWidth", 2f }, { "cutDepth", 2f }
                }
            };
            GeneratedRoom g = RoomGenerator.Generate(spec);

            Assert.AreEqual(6, g.Room.Boundary.Count);
            Assert.AreEqual(6, g.Walls.Count);
            Assert.AreEqual(6f * 5f - 2f * 2f, Area(g.Room.Boundary), 0.001f);
            AssertWallsAreClosedLoop(g.Walls);
        }

        [Test]
        public void Corridor_Geometry()
        {
            var spec = new RoomSpec
            {
                Template = RoomTemplate.Corridor,
                Params = new Dictionary<string, float> { { "length", 10f }, { "width", 2f } }
            };
            GeneratedRoom g = RoomGenerator.Generate(spec);

            Assert.AreEqual(4, g.Walls.Count);
            Assert.AreEqual(20f, Area(g.Room.Boundary), 0.001f);
            AssertWallsAreClosedLoop(g.Walls);
        }

        [Test]
        public void Auditorium_UsesItsOwnDefaults()
        {
            GeneratedRoom g = RoomGenerator.Generate(new RoomSpec { Template = RoomTemplate.Auditorium });

            Assert.AreEqual(RoomTemplate.Auditorium, g.Room.Template);
            Assert.AreEqual(3.2f, g.Room.Height, 0.001f);
            Assert.AreEqual(10f * 7.5f, Area(g.Room.Boundary), 0.001f);
        }

        [Test]
        public void Defaults_AreFilledIntoTemplateParams()
        {
            GeneratedRoom g = RoomGenerator.Generate(new RoomSpec { Template = RoomTemplate.LShaped });

            Assert.IsTrue(g.Room.TemplateParams.ContainsKey("width"));
            Assert.IsTrue(g.Room.TemplateParams.ContainsKey("depth"));
            Assert.IsTrue(g.Room.TemplateParams.ContainsKey("cutWidth"));
            Assert.IsTrue(g.Room.TemplateParams.ContainsKey("cutDepth"));
        }

        [Test]
        public void InvalidParameters_AreRejected()
        {
            var zeroWidth = new RoomSpec
            {
                Template = RoomTemplate.Rectangular,
                Params = new Dictionary<string, float> { { "width", 0f } }
            };
            Assert.Throws<ArgumentException>(() => RoomGenerator.Generate(zeroWidth));

            var bigCut = new RoomSpec
            {
                Template = RoomTemplate.LShaped,
                Params = new Dictionary<string, float> { { "width", 6f }, { "cutWidth", 5.5f } }
            };
            Assert.IsNotEmpty(RoomTemplates.Validate(bigCut));

            var typo = new RoomSpec
            {
                Template = RoomTemplate.Rectangular,
                Params = new Dictionary<string, float> { { "widht", 4f } }
            };
            Assert.IsNotEmpty(RoomTemplates.Validate(typo));

            var shortCorridor = new RoomSpec
            {
                Template = RoomTemplate.Corridor,
                Params = new Dictionary<string, float> { { "length", 1.5f }, { "width", 3f } }
            };
            Assert.IsNotEmpty(RoomTemplates.Validate(shortCorridor));

            Assert.IsNotEmpty(RoomTemplates.Validate(new RoomSpec { Template = RoomTemplate.Custom }));
            Assert.IsNotEmpty(RoomTemplates.Validate(new RoomSpec { Height = 0.5f }));
            Assert.IsNotEmpty(RoomTemplates.Validate(new RoomSpec { WallThickness = 0f }));
        }

        [Test]
        public void SpecValues_GoToRoomAndWalls()
        {
            var spec = new RoomSpec
            {
                Name = "  Кабинет  ",
                Template = RoomTemplate.Rectangular,
                Height = 3f,
                WallThickness = 0.3f,
                Floor = 2,
                Elevation = 3.2f,
                State = ElementState.Proposed
            };
            GeneratedRoom g = RoomGenerator.Generate(spec);

            Assert.AreEqual("Кабинет", g.Room.Name);
            Assert.AreEqual(2, g.Room.Floor);
            Assert.AreEqual(3.2f, g.Room.Elevation, 0.0001f);
            Assert.AreEqual(ElementState.Proposed, g.Room.State);
            foreach (Wall w in g.Walls)
            {
                Assert.AreEqual(g.Room.Id, w.RoomId);
                Assert.AreEqual(3f, w.Height, 0.0001f);
                Assert.AreEqual(0.3f, w.Thickness, 0.0001f);
                Assert.AreEqual(ElementState.Proposed, w.State);
            }
        }

        [Test]
        public void AllTemplates_CreateSaveAndReopen()
        {
            foreach (RoomTemplate template in new[]
                { RoomTemplate.Rectangular, RoomTemplate.LShaped, RoomTemplate.Corridor, RoomTemplate.Auditorium })
            {
                var svc = NewProjects();
                Project p = TemplateProjectBuilder.CreateFromTemplate(
                    svc, "Проект " + template, new RoomSpec { Template = template });

                Assert.AreEqual(ProjectSource.Template, p.Source);
                Assert.AreEqual(1, p.Rooms.Count);
                Assert.IsEmpty(ProjectValidator.Validate(p), template.ToString());

                Project back = NewProjects().Load(p.Id);
                Assert.AreEqual(1, back.Rooms.Count);
                Assert.AreEqual(p.Walls.Count, back.Walls.Count);
                Assert.AreEqual(template, back.Rooms[0].Template);
                Assert.IsEmpty(ProjectValidator.Validate(back), template.ToString());
            }
        }

        [Test]
        public void InvalidSpec_DoesNotCreateProject()
        {
            var svc = NewProjects();
            var bad = new RoomSpec
            {
                Template = RoomTemplate.Rectangular,
                Params = new Dictionary<string, float> { { "width", 0f } }
            };

            Assert.Throws<ArgumentException>(() => TemplateProjectBuilder.CreateFromTemplate(svc, "X", bad));
            Assert.AreEqual(0, svc.ListProjects().Count);
        }

        [Test]
        public void Command_CreateRoom_UndoRedo_KeepsIds()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);
            string before = Snap(projects.Current);

            var cmd = new CreateRoomFromTemplateCommand(new RoomSpec { Template = RoomTemplate.Rectangular });
            cmds.Execute(cmd);
            Assert.AreEqual(1, projects.Current.Rooms.Count);
            Assert.AreEqual(4, projects.Current.Walls.Count);
            Assert.IsEmpty(ProjectValidator.Validate(projects.Current));
            string after = Snap(projects.Current);

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));

            cmds.Redo();
            Assert.AreEqual(after, Snap(projects.Current));
            Assert.AreEqual(cmd.RoomId, projects.Current.Rooms[0].Id);
        }
    }
}
