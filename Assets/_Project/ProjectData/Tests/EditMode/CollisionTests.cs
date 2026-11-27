using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class CollisionTests
    {
        private static CatalogService Catalog()
        {
            return CatalogService.FromFile(
                Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/catalog_sample.json"));
        }

        // Комната 4 x 3 м; стены: [0] южная (0,0)-(4,0), [1] восточная, [2] северная (4,3)-(0,3), [3] западная.
        private static Project RoomProject()
        {
            Project p = ProjectFactory.Create("T", ProjectSource.Template);
            GeneratedRoom g = RoomGenerator.Generate(new RoomSpec { Template = RoomTemplate.Rectangular });
            p.Rooms.Add(g.Room);
            p.Walls.AddRange(g.Walls);
            return p;
        }

        private static PlacementResult At(float x, float y, float z, float yaw = 0f)
        {
            return new PlacementResult { IsValid = true, Position = new Vec3(x, y, z), RotationY = yaw };
        }

        // Ставит предмет по правилам размещения прямо в проект (без команд).
        private static PlacedObject PlaceInto(Project p, CatalogService catalog, string id, PlanPoint target, float yaw = 0f)
        {
            CatalogItem item = catalog.GetItem(id);
            PlacementResult r = PlacementService.Place(p, item, target, yaw);
            Assert.IsTrue(r.IsValid, id + ": " + r.Reason);
            PlacedObject obj = PlacementService.CreateObject(item, r);
            p.Objects.Add(obj);
            return obj;
        }

        private sealed class FakeReality : IRealityCollisionChecker
        {
            public bool Block;

            public bool IsBlocked(float centerX, float centerY, float centerZ,
                float sizeX, float sizeY, float sizeZ, float yawDegrees, out string reason)
            {
                reason = Block ? "Пересекается с реальным столом." : null;
                return Block;
            }
        }

        // ---------- сетка ----------

        [Test]
        public void GridSnap_Position()
        {
            Assert.AreEqual(1.25f, GridSnap.Snap(1.234f, 0.05f), 0.0001f);
            Assert.AreEqual(1.20f, GridSnap.Snap(1.22f, 0.05f), 0.0001f);
            Assert.AreEqual(-0.10f, GridSnap.Snap(-0.12f, 0.05f), 0.0001f);
            Assert.AreEqual(1.2f, GridSnap.Snap(1.234f, 0.1f), 0.0001f);
        }

        [Test]
        public void GridSnap_Yaw()
        {
            Assert.AreEqual(105f, GridSnap.SnapYaw(100f, 15f), 0.001f);
            Assert.AreEqual(0f, GridSnap.SnapYaw(7f, 15f), 0.001f);
            Assert.AreEqual(15f, GridSnap.SnapYaw(8f, 15f), 0.001f);
            Assert.AreEqual(-15f, GridSnap.SnapYaw(350f, 15f), 0.001f);
            Assert.AreEqual(-165f, GridSnap.SnapYaw(190f, 15f), 0.001f);
            Assert.AreEqual(180f, GridSnap.SnapYaw(-180f, 15f), 0.001f);
        }

        [Test]
        public void GridSnap_InvalidStep_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GridSnap.Snap(1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => GridSnap.SnapYaw(10f, -5f));
        }

        [Test]
        public void Place_WithSnap_GivesGridValues()
        {
            CatalogItem chair = Catalog().GetItem("chair_wood_01");
            PlacementResult r = PlacementService.Place(RoomProject(), chair,
                new PlanPoint(2.013f, 1.487f), 100f, 1, SnapSettings.Default);

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(2.0f, r.Position.X, 0.0001f);
            Assert.AreEqual(1.5f, r.Position.Z, 0.0001f);
            Assert.AreEqual(105f, r.RotationY, 0.001f);
        }

        // ---------- коробки ----------

        [Test]
        public void OrientedBox_Intersects_WorksForRotatedBoxes()
        {
            var a = new OrientedBox(0f, 0f, 0.5f, 0.5f, 0f, 0f, 1f);

            // повёрнутый на 45° квадрат: половина диагонали 0,707 + 0,5 = 1,207
            var farRotated = new OrientedBox(1.3f, 0f, 0.5f, 0.5f, 45f, 0f, 1f);
            var nearRotated = new OrientedBox(1.1f, 0f, 0.5f, 0.5f, 45f, 0f, 1f);
            Assert.IsFalse(a.Intersects(farRotated));
            Assert.IsTrue(a.Intersects(nearRotated));

            // касание гранями - не пересечение
            var touching = new OrientedBox(1.0f, 0f, 0.5f, 0.5f, 0f, 0f, 1f);
            Assert.IsFalse(a.Intersects(touching));

            // над другим по высоте - не пересечение
            var above = new OrientedBox(0f, 0f, 0.5f, 0.5f, 0f, 1f, 2f);
            Assert.IsFalse(a.Intersects(above));
        }

        // ---------- стены ----------

        [Test]
        public void Collision_Wall_Detected()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            CatalogItem chair = catalog.GetItem("chair_wood_01");

            // стул, заходящий в южную стену
            CollisionReport report = CollisionService.Check(p, chair, At(2f, 0f, 0.05f), catalog);

            Assert.IsTrue(report.Collisions.Any(c => c.Kind == CollisionKind.Wall), report.Summary);
            Assert.IsFalse(report.HasBlocking);
        }

        [Test]
        public void Collision_PartitionBetweenCorners_Detected()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            p.Walls.Add(new Wall
            {
                Id = "partition_1", RoomId = p.Rooms[0].Id,
                A = new PlanPoint(2f, 0f), B = new PlanPoint(2f, 3f), Height = 2.7f, Thickness = 0.1f
            });

            // стол 1,6 м: углы далеко от перегородки, но сама перегородка проходит сквозь стол
            CatalogItem table = catalog.GetItem("table_dining_01");
            PlacementResult r = PlacementService.Place(p, table, new PlanPoint(2f, 1.5f));
            Assert.IsTrue(r.IsValid, r.Reason);

            CollisionReport report = CollisionService.Check(p, table, r, catalog);
            Assert.IsTrue(report.Collisions.Any(c => c.Kind == CollisionKind.Wall && c.OtherId == "partition_1"),
                report.Summary);
        }

        [Test]
        public void Collision_TouchingWall_IsNotACollision()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            CatalogItem wardrobe = catalog.GetItem("wardrobe_01");

            PlacementResult r = PlacementService.Place(p, wardrobe, new PlanPoint(2f, 0.7f));
            Assert.IsTrue(r.IsValid, r.Reason);

            CollisionReport report = CollisionService.Check(p, wardrobe, r, catalog);
            Assert.IsFalse(report.HasCollisions, report.Summary);
        }

        // ---------- предметы ----------

        [Test]
        public void Collision_Objects_Detected_GapAndFlatItemsAreOk()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            PlacedObject sofa = PlaceInto(p, catalog, "sofa_3seat_01", new PlanPoint(2f, 2.4f)); // у северной стены
            CatalogItem chair = catalog.GetItem("chair_wood_01");

            // стул заходит на диван
            CollisionReport overlap = CollisionService.Check(p, chair, At(2f, 0f, 1.9f), catalog);
            Assert.IsTrue(overlap.Collisions.Any(c => c.Kind == CollisionKind.Object && c.OtherId == sofa.Id),
                overlap.Summary);

            // стул на расстоянии от дивана
            CollisionReport gap = CollisionService.Check(p, chair, At(2f, 0f, 1.5f), catalog);
            Assert.IsFalse(gap.HasCollisions, gap.Summary);

            // ковёр под стулом не мешает
            p.Objects.Add(new PlacedObject { Id = "rug1", CatalogId = "rug_01", Position = new Vec3(2f, 0f, 1.5f) });
            CollisionReport onRug = CollisionService.Check(p, chair, At(2f, 0f, 1.5f), catalog);
            Assert.IsFalse(onRug.HasCollisions, onRug.Summary);
        }

        [Test]
        public void Collision_WallPicture_AboveLowCabinetIsOk_AboveWardrobeIsNot()
        {
            CatalogService catalog = Catalog();
            CatalogItem picture = catalog.GetItem("picture_wall_01");

            // тумба 0,55 м: картина на высоте 1,1-1,7 м над ней
            Project low = RoomProject();
            PlaceInto(low, catalog, "cabinet_low_01", new PlanPoint(2f, 0.7f));
            PlacementResult pr = PlacementService.Place(low, picture, new PlanPoint(2f, 0.7f));
            Assert.IsTrue(pr.IsValid, pr.Reason);
            Assert.IsFalse(CollisionService.Check(low, picture, pr, catalog).HasCollisions);

            // шкаф 2,1 м: картина оказалась бы внутри шкафа
            Project tall = RoomProject();
            PlaceInto(tall, catalog, "wardrobe_01", new PlanPoint(2f, 0.7f));
            PlacementResult pr2 = PlacementService.Place(tall, picture, new PlanPoint(2f, 0.7f));
            Assert.IsTrue(pr2.IsValid, pr2.Reason);
            Assert.IsTrue(CollisionService.Check(tall, picture, pr2, catalog).Collisions
                .Any(c => c.Kind == CollisionKind.Object));
        }

        [Test]
        public void CheckObject_DoesNotCollideWithItself()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            PlacedObject chair = PlaceInto(p, catalog, "chair_wood_01", new PlanPoint(2f, 1.5f));

            Assert.IsFalse(CollisionService.CheckObject(p, chair, catalog).HasCollisions);

            // второй стул на том же месте - оба видят пересечение
            PlacedObject second = PlaceInto(p, catalog, "chair_wood_01", new PlanPoint(2f, 1.5f));
            Assert.IsTrue(CollisionService.CheckObject(p, chair, catalog).Collisions.Any(c => c.OtherId == second.Id));
            Assert.IsTrue(CollisionService.CheckObject(p, second, catalog).Collisions.Any(c => c.OtherId == chair.Id));
        }

        // ---------- реальность ----------

        [Test]
        public void Collision_Reality_IsBlocking_AndOptional()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            CatalogItem chair = catalog.GetItem("chair_wood_01");
            PlacementResult r = PlacementService.Place(p, chair, new PlanPoint(2f, 1.5f));

            Assert.IsFalse(CollisionService.Check(p, chair, r, catalog, null).HasCollisions);
            Assert.IsFalse(CollisionService.Check(p, chair, r, catalog, new FakeReality { Block = false }).HasCollisions);

            CollisionReport blocked = CollisionService.Check(p, chair, r, catalog, new FakeReality { Block = true });
            Assert.IsTrue(blocked.HasBlocking);
            Assert.AreEqual(1, blocked.Collisions.Count);
            Assert.AreEqual(CollisionKind.Reality, blocked.Collisions[0].Kind);
            StringAssert.Contains("реальным столом", blocked.BlockingSummary);
        }

        [Test]
        public void Collision_ThreeKindsAtOnce()
        {
            Project p = RoomProject();
            CatalogService catalog = Catalog();
            CatalogItem chair = catalog.GetItem("chair_wood_01");
            p.Objects.Add(new PlacedObject { Id = "other_chair", CatalogId = "chair_wood_01", Position = new Vec3(2f, 0f, 0.2f) });

            // у южной стены, поверх другого стула и "внутри реального стола"
            CollisionReport report = CollisionService.Check(p, chair, At(2f, 0f, 0.05f), catalog,
                new FakeReality { Block = true });

            Assert.AreEqual(3, report.Collisions.Select(c => c.Kind).Distinct().Count(), report.Summary);
            Assert.IsTrue(report.HasBlocking);
        }
    }
}
