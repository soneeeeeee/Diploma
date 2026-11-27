// Тесты B-12 (материалы). Каталог собирается прямо в тесте, чтобы не зависеть от файлов.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace XrInterior.ProjectData.Tests
{
    public class MaterialTests
    {
        private ICatalogService _catalog;
        private Project _project;

        [SetUp]
        public void SetUp()
        {
            var data = new CatalogData();
            data.Categories.Add(new CatalogCategory { Id = "seating", Name = "Сидения" });
            data.Categories.Add(new CatalogCategory { Id = "decor", Name = "Декор" });

            data.Materials.Add(new MaterialInfo { Id = "wood", Name = "Дерево", Surfaces = MaterialSurface.Floor | MaterialSurface.Object });
            data.Materials.Add(new MaterialInfo { Id = "paint_blue", Name = "Краска синяя", Surfaces = MaterialSurface.Wall });
            data.Materials.Add(new MaterialInfo { Id = "paint_white", Name = "Краска белая", Surfaces = MaterialSurface.Wall | MaterialSurface.Ceiling });
            data.Materials.Add(new MaterialInfo { Id = "ceiling_white", Name = "Потолок белый", Surfaces = MaterialSurface.Ceiling });
            data.Materials.Add(new MaterialInfo { Id = "fabric", Name = "Ткань", Surfaces = MaterialSurface.Object });
            data.Materials.Add(new MaterialInfo { Id = "leather", Name = "Кожа", Surfaces = MaterialSurface.Object });

            data.Items.Add(new CatalogItem
            {
                Id = "chair", Name = "Стул", CategoryId = "seating", PrefabKey = "furniture/chair",
                AllowedMaterialIds = new List<string> { "fabric", "leather" }
            });
            data.Items.Add(new CatalogItem
            {
                Id = "plant", Name = "Растение", CategoryId = "decor", PrefabKey = "furniture/plant"
            });
            _catalog = new CatalogService(data);

            _project = new Project { Id = "p1", Name = "Тест" };
            _project.Rooms.Add(new Room { Id = "room1", Name = "Комната 1" });
            _project.Rooms.Add(new Room { Id = "room2", Name = "Комната 2" });
            _project.Walls.Add(new Wall { Id = "w1", RoomId = "room1" });
            _project.Walls.Add(new Wall { Id = "w2", RoomId = "room1" });
            _project.Walls.Add(new Wall { Id = "w3", RoomId = "room2" });
            _project.Objects.Add(new PlacedObject { Id = "o1", CatalogId = "chair" });
            _project.Objects.Add(new PlacedObject { Id = "o2", CatalogId = "plant" });
        }

        private SetMaterialCommand Set(string target, string slot, string material)
        {
            return new SetMaterialCommand(target, slot, material, _catalog);
        }

        // ---- применение, замена, undo/redo ----

        [Test]
        public void SetMaterial_OnRoomFloor_AddsOverride()
        {
            Set("room1", "floor", "wood").Execute(_project);

            Assert.AreEqual(1, _project.MaterialOverrides.Count);
            Assert.AreEqual("room1", _project.MaterialOverrides[0].TargetId);
            Assert.AreEqual("floor", _project.MaterialOverrides[0].Slot);
            Assert.AreEqual("wood", _project.MaterialOverrides[0].MaterialId);
        }

        [Test]
        public void SetMaterial_Twice_ReplacesInsteadOfDuplicating()
        {
            Set("w1", "surface", "paint_blue").Execute(_project);
            Set("w1", "surface", "paint_white").Execute(_project);

            Assert.AreEqual(1, _project.MaterialOverrides.Count);
            Assert.AreEqual("paint_white", MaterialRules.GetMaterialId(_project, "w1", "surface"));
        }

        [Test]
        public void Undo_OfFirstApply_RemovesOverride()
        {
            SetMaterialCommand cmd = Set("room1", "floor", "wood");
            cmd.Execute(_project);
            cmd.Undo(_project);

            Assert.AreEqual(0, _project.MaterialOverrides.Count);
        }

        [Test]
        public void Undo_OfReplace_RestoresPreviousMaterial()
        {
            Set("w1", "surface", "paint_blue").Execute(_project);
            SetMaterialCommand replace = Set("w1", "surface", "paint_white");
            replace.Execute(_project);
            replace.Undo(_project);

            Assert.AreEqual(1, _project.MaterialOverrides.Count);
            Assert.AreEqual("paint_blue", MaterialRules.GetMaterialId(_project, "w1", "surface"));
        }

        [Test]
        public void Redo_AfterUndo_AppliesAgain()
        {
            SetMaterialCommand cmd = Set("room1", "floor", "wood");
            cmd.Execute(_project);
            cmd.Undo(_project);
            cmd.Execute(_project);

            Assert.AreEqual(1, _project.MaterialOverrides.Count);
            Assert.AreEqual("wood", MaterialRules.GetMaterialId(_project, "room1", "floor"));
        }

        [Test]
        public void Reset_RemovesOverride_AndUndoRestoresItAtSamePlace()
        {
            Set("room1", "floor", "wood").Execute(_project);
            Set("w1", "surface", "paint_blue").Execute(_project);
            Set("o1", "surface", "fabric").Execute(_project);

            SetMaterialCommand reset = Set("w1", "surface", null);
            reset.Execute(_project);
            Assert.AreEqual(2, _project.MaterialOverrides.Count);
            Assert.IsNull(MaterialRules.GetMaterialId(_project, "w1", "surface"));

            reset.Undo(_project);
            Assert.AreEqual(3, _project.MaterialOverrides.Count);
            Assert.AreEqual("w1", _project.MaterialOverrides[1].TargetId);
            Assert.AreEqual("paint_blue", _project.MaterialOverrides[1].MaterialId);
        }

        [Test]
        public void Reset_WhenNothingApplied_DoesNothing()
        {
            SetMaterialCommand reset = Set("w1", "surface", null);
            reset.Execute(_project);
            reset.Undo(_project);

            Assert.AreEqual(0, _project.MaterialOverrides.Count);
        }

        // ---- правила: стены, потолок, слоты ----

        [Test]
        public void Wall_AcceptsWallMaterial_RejectsFloorOnlyMaterial()
        {
            Set("w1", "surface", "paint_blue").Execute(_project);
            Assert.Throws<InvalidOperationException>(() => Set("w2", "surface", "wood").Execute(_project));
            Assert.AreEqual(1, _project.MaterialOverrides.Count);
        }

        [Test]
        public void Ceiling_AcceptsCeilingMaterial_RejectsWallOnlyMaterial()
        {
            Set("room1", "ceiling", "ceiling_white").Execute(_project);
            Set("room1", "ceiling", "paint_white").Execute(_project);
            Assert.Throws<InvalidOperationException>(() => Set("room1", "ceiling", "paint_blue").Execute(_project));
            Assert.AreEqual("paint_white", MaterialRules.GetMaterialId(_project, "room1", "ceiling"));
        }

        [Test]
        public void WrongSlot_IsRejected()
        {
            Assert.Throws<InvalidOperationException>(() => Set("room1", "surface", "wood").Execute(_project));
            Assert.Throws<InvalidOperationException>(() => Set("w1", "floor", "paint_blue").Execute(_project));
            Assert.Throws<InvalidOperationException>(() => Set("o1", "floor", "fabric").Execute(_project));
            Assert.AreEqual(0, _project.MaterialOverrides.Count);
        }

        [Test]
        public void UnknownTarget_OrMaterial_IsRejected_AndProjectUntouched()
        {
            Assert.Throws<InvalidOperationException>(() => Set("nope", "floor", "wood").Execute(_project));
            Assert.Throws<InvalidOperationException>(() => Set("room1", "floor", "nope").Execute(_project));
            Assert.Throws<InvalidOperationException>(() => Set("", "floor", "wood").Execute(_project));
            Assert.Throws<InvalidOperationException>(() => Set("room1", null, "wood").Execute(_project));
            Assert.AreEqual(0, _project.MaterialOverrides.Count);
        }

        // ---- предметы: допустимые материалы из каталога ----

        [Test]
        public void Object_AcceptsOnlyMaterialsAllowedByCatalog()
        {
            Set("o1", "surface", "fabric").Execute(_project);
            Set("o1", "surface", "leather").Execute(_project);
            Assert.Throws<InvalidOperationException>(() => Set("o1", "surface", "wood").Execute(_project));
            Assert.AreEqual("leather", MaterialRules.GetMaterialId(_project, "o1", "surface"));
        }

        [Test]
        public void Object_WithEmptyAllowedList_CannotChangeMaterial()
        {
            Assert.Throws<InvalidOperationException>(() => Set("o2", "surface", "fabric").Execute(_project));
            Assert.AreEqual(0, _project.MaterialOverrides.Count);
        }

        // ---- помещение целиком ----

        [Test]
        public void ApplyToRoomWalls_CoversOnlyThatRoom_AndUndoIsOneStep()
        {
            ICommand cmd = MaterialCommands.ApplyToRoomWalls(_project, "room1", "paint_blue", _catalog);
            cmd.Execute(_project);

            Assert.AreEqual("paint_blue", MaterialRules.GetMaterialId(_project, "w1", "surface"));
            Assert.AreEqual("paint_blue", MaterialRules.GetMaterialId(_project, "w2", "surface"));
            Assert.IsNull(MaterialRules.GetMaterialId(_project, "w3", "surface"));

            cmd.Undo(_project);
            Assert.AreEqual(0, _project.MaterialOverrides.Count);
        }

        [Test]
        public void ApplyToRoomWalls_WithBadMaterial_ChangesNothing()
        {
            Set("w1", "surface", "paint_white").Execute(_project);
            ICommand cmd = MaterialCommands.ApplyToRoomWalls(_project, "room1", "wood", _catalog);

            Assert.Throws<InvalidOperationException>(() => cmd.Execute(_project));
            Assert.AreEqual(1, _project.MaterialOverrides.Count);
            Assert.AreEqual("paint_white", MaterialRules.GetMaterialId(_project, "w1", "surface"));
        }

        [Test]
        public void ApplyToRoomWalls_RoomWithoutWalls_Throws()
        {
            _project.Rooms.Add(new Room { Id = "empty" });
            Assert.Throws<InvalidOperationException>(
                () => MaterialCommands.ApplyToRoomWalls(_project, "empty", "paint_blue", _catalog));
        }

        // ---- список для панели выбора ----

        [Test]
        public void GetAllowedMaterials_ForObject_IsCatalogList()
        {
            IReadOnlyList<MaterialInfo> list = MaterialRules.GetAllowedMaterials(_project, _catalog, "o1", "surface");
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("fabric", list[0].Id);
            Assert.AreEqual("leather", list[1].Id);
            Assert.AreEqual(0, MaterialRules.GetAllowedMaterials(_project, _catalog, "o2", "surface").Count);
        }

        [Test]
        public void GetAllowedMaterials_ForWallAndFloor_FilterBySurface()
        {
            Assert.AreEqual(2, MaterialRules.GetAllowedMaterials(_project, _catalog, "w1", "surface").Count);
            Assert.AreEqual(1, MaterialRules.GetAllowedMaterials(_project, _catalog, "room1", "floor").Count);
            Assert.AreEqual(0, MaterialRules.GetAllowedMaterials(_project, _catalog, "room1", "surface").Count);
        }

        // ---- связь с удалением предмета (B-06) ----

        [Test]
        public void RemovingObject_DropsItsMaterial_AndUndoBringsItBack()
        {
            Set("o1", "surface", "fabric").Execute(_project);
            var remove = new RemoveObjectCommand("o1");
            remove.Execute(_project);
            Assert.AreEqual(0, _project.MaterialOverrides.Count);

            remove.Undo(_project);
            Assert.AreEqual("fabric", MaterialRules.GetMaterialId(_project, "o1", "surface"));
        }
    }
}
