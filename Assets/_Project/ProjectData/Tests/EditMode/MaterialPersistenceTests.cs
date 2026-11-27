// Тесты B-12: материалы сохраняются при повторном открытии проекта (FR-107).

using System.Collections.Generic;
using NUnit.Framework;

namespace XrInterior.ProjectData.Tests
{
    public class MaterialPersistenceTests
    {
        private ICatalogService _catalog;
        private Project _project;

        [SetUp]
        public void SetUp()
        {
            var data = new CatalogData();
            data.Categories.Add(new CatalogCategory { Id = "seating", Name = "Сидения" });
            data.Materials.Add(new MaterialInfo { Id = "wood", Name = "Дерево", Surfaces = MaterialSurface.Floor | MaterialSurface.Object });
            data.Materials.Add(new MaterialInfo { Id = "paint_blue", Name = "Краска синяя", Surfaces = MaterialSurface.Wall });
            data.Materials.Add(new MaterialInfo { Id = "ceiling_white", Name = "Потолок белый", Surfaces = MaterialSurface.Ceiling });
            data.Materials.Add(new MaterialInfo { Id = "fabric", Name = "Ткань", Surfaces = MaterialSurface.Object });
            data.Items.Add(new CatalogItem
            {
                Id = "chair", Name = "Стул", CategoryId = "seating", PrefabKey = "furniture/chair",
                AllowedMaterialIds = new List<string> { "fabric" }
            });
            _catalog = new CatalogService(data);

            _project = new Project { Id = "p1", Name = "Тест материалов" };
            _project.Rooms.Add(new Room { Id = "room1", Name = "Комната" });
            _project.Walls.Add(new Wall { Id = "w1", RoomId = "room1" });
            _project.Objects.Add(new PlacedObject { Id = "o1", CatalogId = "chair" });

            new SetMaterialCommand("room1", "floor", "wood", _catalog).Execute(_project);
            new SetMaterialCommand("room1", "ceiling", "ceiling_white", _catalog).Execute(_project);
            new SetMaterialCommand("w1", "surface", "paint_blue", _catalog).Execute(_project);
            new SetMaterialCommand("o1", "surface", "fabric", _catalog).Execute(_project);
        }

        private static void AssertSameOverrides(Project expected, Project actual)
        {
            Assert.AreEqual(expected.MaterialOverrides.Count, actual.MaterialOverrides.Count);
            for (int i = 0; i < expected.MaterialOverrides.Count; i++)
            {
                Assert.AreEqual(expected.MaterialOverrides[i].TargetId, actual.MaterialOverrides[i].TargetId);
                Assert.AreEqual(expected.MaterialOverrides[i].Slot, actual.MaterialOverrides[i].Slot);
                Assert.AreEqual(expected.MaterialOverrides[i].MaterialId, actual.MaterialOverrides[i].MaterialId);
            }
        }

        [Test]
        public void Materials_SurviveSaveAndLoad()
        {
            Project loaded = ProjectSerializer.FromJson(ProjectSerializer.ToJson(_project));

            Assert.AreEqual(4, loaded.MaterialOverrides.Count);
            Assert.AreEqual("wood", MaterialRules.GetMaterialId(loaded, "room1", "floor"));
            Assert.AreEqual("ceiling_white", MaterialRules.GetMaterialId(loaded, "room1", "ceiling"));
            Assert.AreEqual("paint_blue", MaterialRules.GetMaterialId(loaded, "w1", "surface"));
            Assert.AreEqual("fabric", MaterialRules.GetMaterialId(loaded, "o1", "surface"));
        }

        [Test]
        public void Materials_AreUnchangedAfterTenSaveCycles()
        {
            Project current = _project;
            for (int i = 0; i < 10; i++)
                current = ProjectSerializer.FromJson(ProjectSerializer.ToJson(current));

            AssertSameOverrides(_project, current);
        }

        [Test]
        public void LoadedProject_StillAcceptsMaterialCommands()
        {
            Project loaded = ProjectSerializer.FromJson(ProjectSerializer.ToJson(_project));

            var change = new SetMaterialCommand("w1", "surface", "paint_blue", _catalog);
            change.Execute(loaded);
            new SetMaterialCommand("room1", "floor", null, _catalog).Execute(loaded);

            Assert.AreEqual(3, loaded.MaterialOverrides.Count);
            Assert.IsNull(MaterialRules.GetMaterialId(loaded, "room1", "floor"));
        }
    }
}
