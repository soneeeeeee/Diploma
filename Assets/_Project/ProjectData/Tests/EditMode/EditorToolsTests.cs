// Тесты B-07 (часть 1): выбор, дублирование, инструменты редактора. Используют настоящие сервисы проекта и команд.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XrInterior.ProjectData.Tests
{
    public class EditorToolsTests
    {
        private sealed class AlwaysBlocked : IRealityCollisionChecker
        {
            public bool IsBlocked(float centerX, float centerY, float centerZ,
                float sizeX, float sizeY, float sizeZ, float yawDegrees, out string reason)
            {
                reason = "Занято реальным объектом.";
                return true;
            }
        }

        private string _dir;
        private ProjectService _projects;
        private CommandService _commands;
        private CatalogService _catalog;
        private EditorTools _tools;
        private Project _project;
        private float _cx;
        private float _cz;

        [SetUp]
        public void SetUp()
        {
            var data = new CatalogData();
            data.Categories.Add(new CatalogCategory { Id = "seating", Name = "Сидения" });
            data.Categories.Add(new CatalogCategory { Id = "decor", Name = "Декор" });
            data.Items.Add(new CatalogItem
            {
                Id = "chair", Name = "Стул", CategoryId = "seating", PrefabKey = "furniture/chair",
                Size = new Vec3(0.5f, 0.9f, 0.5f)
            });
            data.Items.Add(new CatalogItem
            {
                Id = "picture", Name = "Картина", CategoryId = "decor", PrefabKey = "furniture/picture",
                Placement = PlacementRule.Wall, MountHeight = 1.5f, Size = new Vec3(1.0f, 0.7f, 0.05f)
            });
            _catalog = new CatalogService(data);

            _dir = Path.Combine(Path.GetTempPath(), "xri_tools_" + Guid.NewGuid().ToString("N"));
            _projects = new ProjectService(new ProjectStorage(_dir));
            var spec = new RoomSpec
            {
                Name = "Комната",
                Template = RoomTemplate.Rectangular,
                Params = new Dictionary<string, float> { { TemplateParam.Width, 6f }, { TemplateParam.Depth, 5f } }
            };
            TemplateProjectBuilder.CreateFromTemplate(_projects, "Тест", spec);
            _project = _projects.Current;
            _commands = new CommandService(_projects);
            _tools = new EditorTools(_projects, _commands, _catalog);

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (PlanPoint pt in _project.Rooms[0].Boundary)
            {
                minX = Math.Min(minX, pt.X); maxX = Math.Max(maxX, pt.X);
                minZ = Math.Min(minZ, pt.Z); maxZ = Math.Max(maxZ, pt.Z);
            }
            _cx = (minX + maxX) / 2f;
            _cz = (minZ + maxZ) / 2f;
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { }
        }

        private PlacedObject AddChair(string id, float x, float z, float yaw = 0f)
        {
            var o = new PlacedObject
            {
                Id = id, CatalogId = "chair",
                Position = new Vec3(x, 0f, z), Rotation = new Vec3(0f, yaw, 0f)
            };
            _project.Objects.Add(o);
            return o;
        }

        private PlacedObject Find(string id) { return _project.Objects.Find(o => o.Id == id); }

        // ---------- выбор ----------

        [Test]
        public void Selection_SelectAndClear_RaiseEvents()
        {
            var selection = new EditorSelection();
            var seen = new List<string>();
            selection.SelectionChanged += id => seen.Add(id);

            selection.Select("a");
            selection.Select("a");
            selection.Clear();

            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual("a", seen[0]);
            Assert.IsNull(seen[1]);
            Assert.IsFalse(selection.HasSelection);
        }

        [Test]
        public void Selection_SelectNext_CyclesThroughObjects()
        {
            AddChair("o1", _cx, _cz);
            AddChair("o2", _cx + 1f, _cz);
            var selection = new EditorSelection();

            Assert.AreEqual("o1", selection.SelectNext(_project));
            Assert.AreEqual("o2", selection.SelectNext(_project));
            Assert.AreEqual("o1", selection.SelectNext(_project));
        }

        [Test]
        public void Selection_SelectNext_WithoutObjects_ReturnsNull()
        {
            var selection = new EditorSelection();
            Assert.IsNull(selection.SelectNext(_project));
            Assert.IsFalse(selection.HasSelection);
        }

        [Test]
        public void Selection_Refresh_DropsRemovedObject()
        {
            PlacedObject o = AddChair("o1", _cx, _cz);
            var selection = new EditorSelection();
            selection.Select("o1");

            selection.Refresh(_project);
            Assert.IsTrue(selection.HasSelection);

            _project.Objects.Remove(o);
            selection.Refresh(_project);
            Assert.IsFalse(selection.HasSelection);
        }

        // ---------- команда дублирования ----------

        [Test]
        public void DuplicateCommand_CopiesObjectAndMaterials_AndUndoRemovesBoth()
        {
            PlacedObject source = AddChair("o1", _cx, _cz, 30f);
            _project.MaterialOverrides.Add(new MaterialOverride { TargetId = "o1", Slot = "surface", MaterialId = "fabric" });
            var command = new DuplicateObjectCommand("o1", new Vec3(_cx + 1f, 0f, _cz), "o2");

            command.Execute(_project);

            Assert.AreEqual(2, _project.Objects.Count);
            PlacedObject copy = Find("o2");
            Assert.AreEqual("chair", copy.CatalogId);
            Assert.AreEqual(30f, copy.Rotation.Y, 0.001f);
            Assert.AreEqual(ElementState.Proposed, copy.State);
            Assert.AreEqual("fabric", MaterialRules.GetMaterialId(_project, "o2", "surface"));
            Assert.AreEqual("fabric", MaterialRules.GetMaterialId(_project, "o1", "surface"));

            command.Undo(_project);
            Assert.AreEqual(1, _project.Objects.Count);
            Assert.AreSame(source, Find("o1"));
            Assert.AreEqual(1, _project.MaterialOverrides.Count);

            command.Execute(_project);
            Assert.AreEqual(2, _project.Objects.Count);
            Assert.AreEqual(2, _project.MaterialOverrides.Count);
        }

        [Test]
        public void DuplicateCommand_UnknownSource_Throws()
        {
            var command = new DuplicateObjectCommand("nope", new Vec3(0f, 0f, 0f));
            Assert.Throws<InvalidOperationException>(() => command.Execute(_project));
            Assert.AreEqual(0, _project.Objects.Count);
        }

        // ---------- перемещение ----------

        [Test]
        public void MoveTo_ValidTarget_MovesWithSnap_AndUndoRestores()
        {
            AddChair("o1", _cx, _cz);

            ToolResult r = _tools.MoveTo("o1", new PlanPoint(_cx + 1.02f, _cz));

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(_cx + 1.0f, Find("o1").Position.X, 0.051f);
            Assert.AreEqual(1, _commands.UndoCount);

            _commands.Undo();
            Assert.AreEqual(_cx, Find("o1").Position.X, 0.001f);
        }

        [Test]
        public void MoveTo_OutsideRoom_IsRejected_AndNothingChanges()
        {
            AddChair("o1", _cx, _cz);

            ToolResult r = _tools.MoveTo("o1", new PlanPoint(_cx + 100f, _cz + 100f));

            Assert.IsFalse(r.Success);
            Assert.AreEqual(_cx, Find("o1").Position.X, 0.001f);
            Assert.AreEqual(0, _commands.UndoCount);
        }

        [Test]
        public void MoveTo_OntoRealObject_IsBlocked()
        {
            AddChair("o1", _cx, _cz);
            _tools.Reality = new AlwaysBlocked();
            _tools.BlockOnReality = true;   // запрет подтверждения (FR-207); по умолчанию это только предупреждение

            ToolResult r = _tools.MoveTo("o1", new PlanPoint(_cx + 1f, _cz));

            Assert.IsFalse(r.Success);
            Assert.AreEqual(_cx, Find("o1").Position.X, 0.001f);
            Assert.AreEqual(0, _commands.UndoCount);
        }

        [Test]
        public void MoveTo_OntoAnotherObject_SucceedsWithWarning()
        {
            AddChair("o1", _cx, _cz);
            AddChair("o2", _cx + 1.5f, _cz);

            ToolResult r = _tools.MoveTo("o1", new PlanPoint(_cx + 1.5f, _cz));

            Assert.IsTrue(r.Success, r.Message);
            Assert.IsNotNull(r.Warning);
            Assert.AreEqual(_cx + 1.5f, Find("o1").Position.X, 0.051f);
        }

        // ---------- поворот ----------

        [Test]
        public void RotateBy_Fifteen_RotatesAndUndoRestores()
        {
            AddChair("o1", _cx, _cz);

            ToolResult r = _tools.RotateBy("o1", 15f);

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(15f, Find("o1").Rotation.Y, 0.01f);
            _commands.Undo();
            Assert.AreEqual(0f, Find("o1").Rotation.Y, 0.01f);
        }

        [Test]
        public void RotateBy_FourQuarterTurns_ReturnsToStart()
        {
            AddChair("o1", _cx, _cz);

            for (int i = 0; i < 4; i++) Assert.IsTrue(_tools.RotateBy("o1", 90f).Success);

            Assert.AreEqual(0f, Find("o1").Rotation.Y, 0.01f);
            Assert.AreEqual(4, _commands.UndoCount);
        }

        [Test]
        public void RotateBy_WallMountedItem_IsRejected()
        {
            _project.Objects.Add(new PlacedObject { Id = "pic", CatalogId = "picture", Position = new Vec3(_cx, 1.5f, _cz) });

            ToolResult r = _tools.RotateBy("pic", 15f);

            Assert.IsFalse(r.Success);
            Assert.AreEqual(0, _commands.UndoCount);
        }

        // ---------- дублирование и удаление ----------

        [Test]
        public void Duplicate_PlacesCopyNextToOriginal_AndUndoRemovesIt()
        {
            AddChair("o1", _cx, _cz);

            ToolResult r = _tools.Duplicate("o1");

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(2, _project.Objects.Count);
            PlacedObject copy = Find(r.ObjectId);
            Assert.IsNotNull(copy);
            Assert.AreNotEqual("o1", copy.Id);
            Assert.AreEqual(_cx + 0.6f, copy.Position.X, 0.051f);
            Assert.AreEqual(_cz, copy.Position.Z, 0.051f);

            _commands.Undo();
            Assert.AreEqual(1, _project.Objects.Count);
        }

        [Test]
        public void Duplicate_RotatedObject_OffsetFollowsItsLocalAxis()
        {
            AddChair("o1", _cx, _cz, 90f);

            ToolResult r = _tools.Duplicate("o1");

            Assert.IsTrue(r.Success, r.Message);
            PlacedObject copy = Find(r.ObjectId);
            Assert.AreEqual(_cx, copy.Position.X, 0.051f);
            Assert.AreEqual(_cz - 0.6f, copy.Position.Z, 0.051f);
        }

        [Test]
        public void Delete_RemovesObject_AndUndoBringsItBack()
        {
            AddChair("o1", _cx, _cz);

            ToolResult r = _tools.Delete("o1");

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(0, _project.Objects.Count);
            _commands.Undo();
            Assert.AreEqual(1, _project.Objects.Count);
        }

        // ---------- ошибки ----------

        [Test]
        public void UnknownObject_FailsWithoutException_ForEveryTool()
        {
            Assert.IsFalse(_tools.MoveTo("nope", new PlanPoint(_cx, _cz)).Success);
            Assert.IsFalse(_tools.RotateBy("nope", 15f).Success);
            Assert.IsFalse(_tools.Duplicate("nope").Success);
            Assert.IsFalse(_tools.Delete("nope").Success);
            Assert.AreEqual(0, _commands.UndoCount);
        }

        [Test]
        public void ObjectMissingFromCatalog_CannotBeMoved_ButCanBeDeleted()
        {
            _project.Objects.Add(new PlacedObject { Id = "ghost", CatalogId = "not_in_catalog" });

            Assert.IsFalse(_tools.MoveTo("ghost", new PlanPoint(_cx, _cz)).Success);
            Assert.IsFalse(_tools.Duplicate("ghost").Success);
            Assert.IsTrue(_tools.Delete("ghost").Success);
        }
    }
}
