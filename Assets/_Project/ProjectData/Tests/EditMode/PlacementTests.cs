using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class PlacementTests
    {
        // Комната 4 x 3 м, стены толщиной 0,2 м. Стены по порядку:
        // [0] (0,0)-(4,0) южная, [1] (4,0)-(4,3) восточная, [2] (4,3)-(0,3) северная, [3] (0,3)-(0,0) западная.
        private static Project RoomProject()
        {
            Project p = ProjectFactory.Create("T", ProjectSource.Template);
            GeneratedRoom g = RoomGenerator.Generate(new RoomSpec { Template = RoomTemplate.Rectangular });
            p.Rooms.Add(g.Room);
            p.Walls.AddRange(g.Walls);
            return p;
        }

        private static CatalogItem Item(PlacementRule rule, float sx, float sy, float sz,
            bool align = false, float mount = 1.4f)
        {
            return new CatalogItem
            {
                Id = "test_item",
                Name = "Тест",
                Placement = rule,
                Size = new Vec3(sx, sy, sz),
                AlignToWall = align,
                MountHeight = mount
            };
        }

        [Test]
        public void Floor_InsideRoom_IsValid()
        {
            Project p = RoomProject();
            PlacementResult r = PlacementService.Place(p, Item(PlacementRule.Floor, 0.5f, 0.9f, 0.5f),
                new PlanPoint(2f, 1.5f), 90f);

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(2f, r.Position.X, 0.001f);
            Assert.AreEqual(0f, r.Position.Y, 0.001f);
            Assert.AreEqual(1.5f, r.Position.Z, 0.001f);
            Assert.AreEqual(90f, r.RotationY, 0.001f);
            Assert.AreEqual(p.Rooms[0].Id, r.RoomId);
        }

        [Test]
        public void Floor_OutsideRoom_Fails()
        {
            PlacementResult r = PlacementService.Place(RoomProject(), Item(PlacementRule.Floor, 0.5f, 0.9f, 0.5f),
                new PlanPoint(10f, 10f));
            Assert.IsFalse(r.IsValid);
            Assert.IsNotEmpty(r.Reason);
        }

        [Test]
        public void Floor_CloseToWall_FailsWhenTouchingWallThickness()
        {
            Project p = RoomProject();
            CatalogItem chair = Item(PlacementRule.Floor, 0.5f, 0.9f, 0.5f);

            // угол на расстоянии 0,15 м от оси стены (толщина 0,2 м) - можно
            Assert.IsTrue(PlacementService.Place(p, chair, new PlanPoint(0.4f, 1.5f)).IsValid);
            // угол на расстоянии 0,05 м от оси - уже в толще стены
            Assert.IsFalse(PlacementService.Place(p, chair, new PlanPoint(0.3f, 1.5f)).IsValid);
            // угол за пределами комнаты
            Assert.IsFalse(PlacementService.Place(p, chair, new PlanPoint(0.15f, 1.5f)).IsValid);
        }

        [Test]
        public void Floor_TallerThanRoom_Fails()
        {
            PlacementResult r = PlacementService.Place(RoomProject(), Item(PlacementRule.Floor, 0.5f, 3.0f, 0.5f),
                new PlanPoint(2f, 1.5f));
            Assert.IsFalse(r.IsValid);
        }

        [Test]
        public void AlignToWall_SnapsToWallAndFacesRoom()
        {
            Project p = RoomProject();
            CatalogItem wardrobe = Item(PlacementRule.Floor, 1.0f, 2.1f, 0.55f, align: true);

            PlacementResult r = PlacementService.Place(p, wardrobe, new PlanPoint(2f, 0.7f), 45f);

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(2f, r.Position.X, 0.001f);
            Assert.AreEqual(0f, r.Position.Y, 0.001f);
            // 0,1 (половина стены) + 0,275 (половина глубины шкафа)
            Assert.AreEqual(0.375f, r.Position.Z, 0.001f);
            Assert.AreEqual(0f, r.RotationY, 0.001f);   // лицом в комнату (+Z), пользовательский поворот игнорируется
            Assert.AreEqual(p.Walls[0].Id, r.WallId);
        }

        [Test]
        public void AlignToWall_FarFromWalls_IsPlacedFreely()
        {
            Project p = RoomProject();
            CatalogItem wardrobe = Item(PlacementRule.Floor, 1.0f, 2.1f, 0.55f, align: true);

            PlacementResult r = PlacementService.Place(p, wardrobe, new PlanPoint(2f, 1.5f), 45f);

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(2f, r.Position.X, 0.001f);
            Assert.AreEqual(1.5f, r.Position.Z, 0.001f);
            Assert.AreEqual(45f, r.RotationY, 0.001f);
            Assert.IsNull(r.WallId);
        }

        [Test]
        public void WallItem_SnapsToWall_AtMountHeight_FacingRoom()
        {
            Project p = RoomProject();
            CatalogItem picture = Item(PlacementRule.Wall, 0.8f, 0.6f, 0.04f, mount: 1.4f);

            PlacementResult r = PlacementService.Place(p, picture, new PlanPoint(3.9f, 1.5f));

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(3.9f, r.Position.X, 0.001f);   // 4 - 0,1: поверхность стены со стороны комнаты
            Assert.AreEqual(1.5f, r.Position.Z, 0.001f);
            Assert.AreEqual(1.4f, r.Position.Y, 0.001f);
            Assert.AreEqual(-90f, r.RotationY, 0.01f);     // смотрит в комнату (-X)
            Assert.AreEqual(p.Walls[1].Id, r.WallId);
        }

        [Test]
        public void WallItem_NoWallNearby_Fails()
        {
            PlacementResult r = PlacementService.Place(RoomProject(),
                Item(PlacementRule.Wall, 0.8f, 0.6f, 0.04f), new PlanPoint(2f, 1.5f));
            Assert.IsFalse(r.IsValid);
            Assert.IsNotEmpty(r.Reason);
        }

        [Test]
        public void WallItem_OverWindow_Fails_ButBesideItIsOk()
        {
            Project p = RoomProject();
            Wall south = p.Walls[0];
            p.Openings.Add(new Opening
            {
                Id = "op1", WallId = south.Id, Type = OpeningType.Window,
                Offset = 1.5f, Width = 1.2f, Height = 1.2f, SillHeight = 0.9f
            });
            south.OpeningIds.Add("op1");

            CatalogItem picture = Item(PlacementRule.Wall, 0.8f, 0.6f, 0.04f, mount: 1.4f);

            Assert.IsFalse(PlacementService.Place(p, picture, new PlanPoint(2.1f, 0.1f)).IsValid);
            Assert.IsTrue(PlacementService.Place(p, picture, new PlanPoint(0.8f, 0.1f)).IsValid);
        }

        [Test]
        public void WallItem_TooHighForWall_Fails()
        {
            PlacementResult r = PlacementService.Place(RoomProject(),
                Item(PlacementRule.Wall, 0.8f, 0.6f, 0.04f, mount: 2.5f), new PlanPoint(3.9f, 1.5f));
            Assert.IsFalse(r.IsValid);
        }

        [Test]
        public void Ceiling_PlacedAtRoomHeight()
        {
            Project p = RoomProject();
            PlacementResult r = PlacementService.Place(p,
                Item(PlacementRule.Ceiling, 0.5f, 0.15f, 0.5f), new PlanPoint(2f, 1.5f));

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(2.7f, r.Position.Y, 0.001f);

            Assert.IsFalse(PlacementService.Place(p,
                Item(PlacementRule.Ceiling, 0.5f, 3.0f, 0.5f), new PlanPoint(2f, 1.5f)).IsValid);
        }

        [Test]
        public void CreateObject_RequiresValidPlacement()
        {
            CatalogItem chair = Item(PlacementRule.Floor, 0.5f, 0.9f, 0.5f);
            PlacementResult bad = PlacementResult.Fail("нельзя");
            Assert.Throws<InvalidOperationException>(() => PlacementService.CreateObject(chair, bad));
        }

        [Test]
        public void ThreeCategories_PlacedByTheirRules_AndUndoable()
        {
            string dir = Path.Combine(Path.GetTempPath(), "xri_" + Guid.NewGuid().ToString("N"));
            try
            {
                CatalogService catalog = CatalogService.FromFile(
                    Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/catalog_sample.json"));

                var projects = new ProjectService(new ProjectStorage(dir));
                TemplateProjectBuilder.CreateFromTemplate(projects, "T", new RoomSpec { Template = RoomTemplate.Rectangular });
                var cmds = new CommandService(projects);
                Project project = projects.Current;

                // три предмета с тремя разными правилами: пол, стена, потолок
                var targets = new[]
                {
                    new { Id = "chair_wood_01",   Target = new PlanPoint(2f, 1.5f) },
                    new { Id = "picture_wall_01", Target = new PlanPoint(3.9f, 1.5f) },
                    new { Id = "lamp_ceiling_01", Target = new PlanPoint(2f, 1.5f) }
                };

                foreach (var t in targets)
                {
                    CatalogItem item = catalog.GetItem(t.Id);
                    PlacementResult r = PlacementService.Place(project, item, t.Target);
                    Assert.IsTrue(r.IsValid, t.Id + ": " + r.Reason);
                    cmds.Execute(new AddObjectCommand(PlacementService.CreateObject(item, r)));
                }

                Assert.AreEqual(3, project.Objects.Count);
                Assert.IsEmpty(ProjectValidator.Validate(project));

                cmds.Undo();
                cmds.Undo();
                cmds.Undo();
                Assert.AreEqual(0, project.Objects.Count);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
