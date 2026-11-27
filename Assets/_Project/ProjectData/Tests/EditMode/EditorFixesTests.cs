// Тесты B-07 (часть 3): добавление в любой комнате, стены (сдвиг и поворот), предметы на стене и потолке,
// выбор стены/пола/потолка, пересечения с реальным объектом как предупреждение.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace XrInterior.ProjectData.Tests
{
    public class EditorFixesTests
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

        [SetUp]
        public void SetUp()
        {
            var data = new CatalogData();
            data.Categories.Add(new CatalogCategory { Id = "seating", Name = "Сидения" });
            data.Items.Add(new CatalogItem { Id = "chair", Name = "Стул", CategoryId = "seating", PrefabKey = "f/chair", Size = new Vec3(0.5f, 0.9f, 0.5f) });
            data.Items.Add(new CatalogItem { Id = "table", Name = "Стол", CategoryId = "seating", PrefabKey = "f/table", Size = new Vec3(1.6f, 0.8f, 0.8f) });
            data.Items.Add(new CatalogItem { Id = "giant", Name = "Огромный", CategoryId = "seating", PrefabKey = "f/giant", Size = new Vec3(7f, 1f, 6f) });
            data.Items.Add(new CatalogItem
            {
                Id = "picture", Name = "Картина", CategoryId = "seating", PrefabKey = "f/picture",
                Placement = PlacementRule.Wall, MountHeight = 1.5f, Size = new Vec3(1.0f, 0.7f, 0.05f)
            });
            data.Items.Add(new CatalogItem
            {
                Id = "lamp", Name = "Светильник", CategoryId = "seating", PrefabKey = "f/lamp",
                Placement = PlacementRule.Ceiling, Size = new Vec3(0.4f, 0.3f, 0.4f)
            });
            _catalog = new CatalogService(data);
            Open(RoomTemplate.Rectangular, new Dictionary<string, float>
            {
                { TemplateParam.Width, 6f }, { TemplateParam.Depth, 5f }
            });
        }

        private void Open(RoomTemplate template, Dictionary<string, float> param)
        {
            _dir = Path.Combine(Path.GetTempPath(), "xri_fix_" + Guid.NewGuid().ToString("N"));
            _projects = new ProjectService(new ProjectStorage(_dir));
            TemplateProjectBuilder.CreateFromTemplate(_projects, "Тест",
                new RoomSpec { Name = "Комната", Template = template, Params = param });
            _project = _projects.Current;
            _commands = new CommandService(_projects);
            _tools = new EditorTools(_projects, _commands, _catalog);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (_dir != null && Directory.Exists(_dir)) Directory.Delete(_dir, true); }
            catch (IOException) { }
        }

        private PlacedObject Find(string id) { return _project.Objects.Find(o => o.Id == id); }

        private PlanPoint Centre()
        {
            return PlanGeometry.InteriorPoint(_project.Rooms[0].Boundary);
        }

        // Перегородка посередине комнаты; возвращает её.
        private Wall AddPartition()
        {
            ToolResult r = _tools.AddPartition(Centre());
            Assert.IsTrue(r.Success, r.Message);
            Assert.IsNotNull(r.ObjectId);
            return _project.Walls.Find(w => w.Id == r.ObjectId);
        }

        // ---------- добавление в Г-образной комнате ----------

        [Test]
        public void AddFromCatalog_LShapedRoom_AlwaysSucceeds_AndStaysInsideRoom()
        {
            Open(RoomTemplate.LShaped, new Dictionary<string, float>
            {
                { TemplateParam.Width, 10f }, { TemplateParam.Depth, 8f },
                { TemplateParam.CutWidth, 4f }, { TemplateParam.CutDepth, 3f }
            });

            for (int i = 0; i < 25; i++)
            {
                ToolResult r = _tools.AddFromCatalog(i % 3 == 0 ? "table" : "chair");
                Assert.IsTrue(r.Success, "шаг " + i + ": " + r.Message);
                PlacedObject o = Find(r.ObjectId);
                Assert.IsNotNull(o);
                Assert.IsTrue(PlanGeometry.Contains(_project.Rooms[0].Boundary, o.Position.X, o.Position.Z),
                    "предмет " + i + " оказался вне помещения");
            }
            Assert.AreEqual(25, _project.Objects.Count);
        }

        [Test]
        public void AddFromCatalog_LShapedRoom_LargeCut_Works()
        {
            Open(RoomTemplate.LShaped, new Dictionary<string, float>
            {
                { TemplateParam.Width, 10f }, { TemplateParam.Depth, 8f },
                { TemplateParam.CutWidth, 6f }, { TemplateParam.CutDepth, 5f }
            });
            ToolResult r = _tools.AddFromCatalog("chair");
            Assert.IsTrue(r.Success, r.Message);
            Assert.IsTrue(PlanGeometry.Contains(_project.Rooms[0].Boundary, Find(r.ObjectId).Position.X, Find(r.ObjectId).Position.Z));
        }

        [Test]
        public void AddFromCatalog_PrefersFreeSpot_NoWarning()
        {
            ToolResult first = _tools.AddFromCatalog("chair");
            ToolResult second = _tools.AddFromCatalog("chair");

            Assert.IsTrue(first.Success && second.Success);
            Assert.IsNull(second.Warning, "второй стул должен встать в свободное место");
            Assert.AreEqual(2, _commands.UndoCount);
        }

        [Test]
        public void AddFromCatalog_TooBigForRoom_StillPlaced_WithWarning()
        {
            ToolResult r = _tools.AddFromCatalog("giant");

            Assert.IsTrue(r.Success, r.Message);
            Assert.IsNotNull(r.Warning);
            Assert.AreEqual(1, _project.Objects.Count);
        }

        [Test]
        public void AddFromCatalog_OnRealObject_IsPlaced_WithWarning()
        {
            _tools.Reality = new AlwaysBlocked();

            ToolResult r = _tools.AddFromCatalog("chair");

            Assert.IsTrue(r.Success, r.Message);
            Assert.IsNotNull(r.Warning);
            Assert.AreEqual(1, _project.Objects.Count);
        }

        [Test]
        public void AddFromCatalog_NearPoint_PlacesCloseToIt()
        {
            ToolResult r = _tools.AddFromCatalog("chair", new PlanPoint(1f, 1f));

            Assert.IsTrue(r.Success, r.Message);
            PlacedObject o = Find(r.ObjectId);
            Assert.Less(PlanGeometry.Dist(o.Position.X, o.Position.Z, 1f, 1f), 1.5f);
        }

        [Test]
        public void AddFromCatalog_Picture_HangsOnAWall_AndUndoRemovesIt()
        {
            ToolResult r = _tools.AddFromCatalog("picture");

            Assert.IsTrue(r.Success, r.Message);
            PlacedObject o = Find(r.ObjectId);
            Wall wall = WallPlacement.FindWallOf(_project, o, 1);
            Assert.IsNotNull(wall, "картина должна висеть на стене");
            Assert.AreEqual(1.5f, o.Position.Y, 0.06f);

            _commands.Undo();
            Assert.AreEqual(0, _project.Objects.Count);
        }

        [Test]
        public void AddFromCatalog_CeilingLamp_IsUnderCeiling()
        {
            ToolResult r = _tools.AddFromCatalog("lamp");

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(_project.Rooms[0].Height, Find(r.ObjectId).Position.Y, 0.001f);
        }

        [Test]
        public void AddFromCatalog_UnknownItem_Fails()
        {
            Assert.IsFalse(_tools.AddFromCatalog("нет такого").Success);
            Assert.AreEqual(0, _commands.UndoCount);
        }

        // ---------- реальный объект: красный, не отказ ----------

        [Test]
        public void MoveTo_OntoRealObject_Default_SucceedsWithWarning()
        {
            _tools.AddFromCatalog("chair");
            string id = _project.Objects[0].Id;
            _tools.Reality = new AlwaysBlocked();

            ToolResult r = _tools.MoveTo(id, new PlanPoint(Centre().X + 1f, Centre().Z));

            Assert.IsTrue(r.Success, r.Message);
            Assert.IsNotNull(r.Warning);
        }

        [Test]
        public void MoveTo_OntoRealObject_WithBlockOnReality_IsRejected()
        {
            _tools.AddFromCatalog("chair");
            string id = _project.Objects[0].Id;
            float x = Find(id).Position.X;
            _tools.Reality = new AlwaysBlocked();
            _tools.BlockOnReality = true;

            ToolResult r = _tools.MoveTo(id, new PlanPoint(x + 1f, Find(id).Position.Z));

            Assert.IsFalse(r.Success);
            Assert.AreEqual(x, Find(id).Position.X, 0.001f);
        }

        // ---------- движение кнопками ----------

        [Test]
        public void MoveBy_AllFourDirections_MoveChairByQuarterMetre()
        {
            ToolResult add = _tools.AddFromCatalog("chair");
            string id = add.ObjectId;
            float x0 = Find(id).Position.X, z0 = Find(id).Position.Z;

            Assert.IsTrue(_tools.MoveBy(id, EditorTools.MoveStep, 0f).Success);
            Assert.AreEqual(x0 + 0.25f, Find(id).Position.X, 0.051f);
            Assert.IsTrue(_tools.MoveBy(id, -EditorTools.MoveStep, 0f).Success);
            Assert.AreEqual(x0, Find(id).Position.X, 0.051f);
            Assert.IsTrue(_tools.MoveBy(id, 0f, EditorTools.MoveStep).Success);
            Assert.AreEqual(z0 + 0.25f, Find(id).Position.Z, 0.051f);
            Assert.IsTrue(_tools.MoveBy(id, 0f, -EditorTools.MoveStep).Success);
            Assert.AreEqual(z0, Find(id).Position.Z, 0.051f);
        }

        [Test]
        public void RotateBy_NegativeFifteen_RotatesTheOtherWay()
        {
            string id = _tools.AddFromCatalog("chair").ObjectId;

            Assert.IsTrue(_tools.RotateBy(id, -15f).Success);
            Assert.AreEqual(-15f, Find(id).Rotation.Y, 0.01f);
            Assert.IsTrue(_tools.RotateBy(id, 15f).Success);
            Assert.AreEqual(0f, Find(id).Rotation.Y, 0.01f);
        }

        [Test]
        public void MoveBy_CeilingLamp_MovesAndKeepsHeight()
        {
            string id = _tools.AddFromCatalog("lamp").ObjectId;
            float x0 = Find(id).Position.X;

            ToolResult r = _tools.MoveBy(id, EditorTools.MoveStep, 0f);

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(x0 + 0.25f, Find(id).Position.X, 0.051f);
            Assert.AreEqual(_project.Rooms[0].Height, Find(id).Position.Y, 0.001f);
        }

        // ---------- предметы на стене ----------

        // Картина на конкретной стороне перегородки.
        private PlacedObject HangPicture(Wall wall, int side)
        {
            float length = WallRules.Length(wall);
            float nx = -(wall.B.Z - wall.A.Z) / length * side, nz = (wall.B.X - wall.A.X) / length * side;
            PlacementResult r = WallPlacement.PlaceOn(_project, _catalog.GetItem("picture"), wall, length / 2f, nx, nz, 1,
                SnapSettings.Default, null);
            Assert.IsTrue(r.IsValid, r.Reason);
            PlacedObject o = PlacementService.CreateObject(_catalog.GetItem("picture"), r);
            _project.Objects.Add(o);
            return o;
        }

        [Test]
        public void WallPlacement_ThinWallCentre_KeepsSideFromHint()
        {
            Wall wall = AddPartition();
            float cx = (wall.A.X + wall.B.X) / 2f, cz = (wall.A.Z + wall.B.Z) / 2f;
            CatalogItem picture = _catalog.GetItem("picture");

            PlacementResult front = WallPlacement.Place(_project, picture, new PlanPoint(cx, cz), 1, SnapSettings.Default, 0f);
            PlacementResult back = WallPlacement.Place(_project, picture, new PlanPoint(cx, cz), 1, SnapSettings.Default, 180f);

            Assert.IsTrue(front.IsValid, front.Reason);
            Assert.IsTrue(back.IsValid, back.Reason);
            Assert.AreEqual(0f, front.RotationY, 0.01f);
            Assert.AreEqual(180f, Math.Abs(back.RotationY), 0.01f);
        }

        [Test]
        public void WallPlacement_FarSideOfWall_SwitchesSide()
        {
            Wall wall = AddPartition();
            float cx = (wall.A.X + wall.B.X) / 2f, cz = (wall.A.Z + wall.B.Z) / 2f;
            CatalogItem picture = _catalog.GetItem("picture");

            PlacementResult r = WallPlacement.Place(_project, picture, new PlanPoint(cx, cz - 0.6f), 1, SnapSettings.Default, 0f);

            Assert.IsTrue(r.IsValid, r.Reason);
            Assert.AreEqual(180f, Math.Abs(r.RotationY), 0.01f);
        }

        [Test]
        public void MoveBy_PictureOnPartition_SlidesAlongWall_AndDoesNotFlipSide()
        {
            Wall wall = AddPartition();
            PlacedObject picture = HangPicture(wall, 1);
            float yaw0 = picture.Rotation.Y;
            float x0 = picture.Position.X;

            for (int i = 0; i < 2; i++)
            {
                ToolResult r = _tools.MoveBy(picture.Id, EditorTools.MoveStep, 0f);
                Assert.IsTrue(r.Success, r.Message);
                Assert.AreEqual(yaw0, Find(picture.Id).Rotation.Y, 0.01f, "картина перепрыгнула на другую сторону");
            }
            Assert.AreEqual(x0 + 0.5f, Find(picture.Id).Position.X, 0.101f);
        }

        [Test]
        public void SlideBy_PictureOnPartition_MovesBothWays()
        {
            Wall wall = AddPartition();
            PlacedObject picture = HangPicture(wall, 1);
            float x0 = picture.Position.X;

            Assert.IsTrue(_tools.SlideBy(picture.Id, 0.25f).Success);
            Assert.AreEqual(x0 + 0.25f, Find(picture.Id).Position.X, 0.051f);
            Assert.IsTrue(_tools.SlideBy(picture.Id, -0.5f).Success);
            Assert.AreEqual(x0 - 0.25f, Find(picture.Id).Position.X, 0.051f);
        }

        [Test]
        public void SlideBy_FloorItem_IsRefused()
        {
            string id = _tools.AddFromCatalog("chair").ObjectId;
            Assert.IsFalse(_tools.SlideBy(id, 0.25f).Success);
        }

        [Test]
        public void MoveBy_PictureAcrossTheWall_IsRefused()
        {
            Wall wall = AddPartition();
            PlacedObject picture = HangPicture(wall, 1);

            ToolResult r = _tools.MoveBy(picture.Id, 0f, EditorTools.MoveStep);

            Assert.IsFalse(r.Success);
        }

        [Test]
        public void MoveBy_PictureOnOuterWall_Moves()
        {
            string id = _tools.AddFromCatalog("picture").ObjectId;
            PlacedObject o = Find(id);
            float x0 = o.Position.X, z0 = o.Position.Z;

            // Какая-то из двух осей совпадает с направлением стены: пробуем обе и требуем, что одна сдвигает.
            bool movedX = _tools.MoveBy(id, EditorTools.MoveStep, 0f).Success && Math.Abs(Find(id).Position.X - x0) > 0.1f;
            bool movedZ = _tools.MoveBy(id, 0f, EditorTools.MoveStep).Success && Math.Abs(Find(id).Position.Z - z0) > 0.1f;

            Assert.IsTrue(movedX || movedZ, "картина на внешней стене не двигается");
        }

        [Test]
        public void RaiseBy_ChangesHeight_AndMoveKeepsIt()
        {
            string id = _tools.AddFromCatalog("picture").ObjectId;
            float y0 = Find(id).Position.Y;

            Assert.IsTrue(_tools.RaiseBy(id, 0.25f).Success);
            Assert.AreEqual(y0 + 0.25f, Find(id).Position.Y, 0.051f);

            _tools.MoveBy(id, EditorTools.MoveStep, 0f);
            _tools.MoveBy(id, 0f, EditorTools.MoveStep);
            Assert.AreEqual(y0 + 0.25f, Find(id).Position.Y, 0.051f, "после сдвига высота должна сохраниться");
        }

        [Test]
        public void RaiseBy_FloorItem_IsRefused()
        {
            string id = _tools.AddFromCatalog("chair").ObjectId;
            Assert.IsFalse(_tools.RaiseBy(id, 0.25f).Success);
        }

        // ---------- стены ----------

        [Test]
        public void AddPartition_LShapedRoom_StaysInsideRoom()
        {
            Open(RoomTemplate.LShaped, new Dictionary<string, float>
            {
                { TemplateParam.Width, 10f }, { TemplateParam.Depth, 8f },
                { TemplateParam.CutWidth, 6f }, { TemplateParam.CutDepth, 5f }
            });
            int before = _project.Walls.Count;

            ToolResult r = _tools.AddPartition(Centre());

            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(before + 1, _project.Walls.Count);
            Wall w = _project.Walls.Find(x => x.Id == r.ObjectId);
            Assert.IsNotNull(w);
            Assert.IsTrue(PlanGeometry.Contains(_project.Rooms[0].Boundary, w.A.X, w.A.Z));
            Assert.IsTrue(PlanGeometry.Contains(_project.Rooms[0].Boundary, w.B.X, w.B.Z));
        }

        [Test]
        public void MoveBy_Wall_MovesBothEndsInAllDirections_AndUndoRestores()
        {
            Wall w = AddPartition();
            float ax = w.A.X, az = w.A.Z, bx = w.B.X;

            Assert.IsTrue(_tools.MoveBy(w.Id, 0.25f, 0f).Success);
            Assert.AreEqual(ax + 0.25f, WallById(w.Id).A.X, 0.051f);
            Assert.AreEqual(bx + 0.25f, WallById(w.Id).B.X, 0.051f);
            Assert.IsTrue(_tools.MoveBy(w.Id, -0.5f, 0f).Success);
            Assert.AreEqual(ax - 0.25f, WallById(w.Id).A.X, 0.051f);
            Assert.IsTrue(_tools.MoveBy(w.Id, 0f, 0.25f).Success);
            Assert.IsTrue(_tools.MoveBy(w.Id, 0f, -0.5f).Success);
            Assert.AreEqual(az - 0.25f, WallById(w.Id).A.Z, 0.051f);

            while (_commands.CanUndo && _commands.UndoCount > 1) _commands.Undo();
            Assert.AreEqual(ax, WallById(w.Id).A.X, 0.001f);
            Assert.AreEqual(az, WallById(w.Id).A.Z, 0.001f);
        }

        private Wall WallById(string id) { return _project.Walls.Find(x => x.Id == id); }

        [Test]
        public void RotateBy_Wall_NinetyDegrees_TurnsPartitionAndKeepsLength()
        {
            Wall w = AddPartition();
            float length = WallRules.Length(w);
            bool horizontal = Math.Abs(w.A.Z - w.B.Z) < 0.001f;

            ToolResult r = _tools.RotateBy(w.Id, EditorTools.WallRotationStep);

            Assert.IsTrue(r.Success, r.Message);
            Wall after = WallById(w.Id);
            Assert.AreEqual(length, WallRules.Length(after), 0.06f);
            if (horizontal) Assert.AreEqual(after.A.X, after.B.X, 0.001f);
            else Assert.AreEqual(after.A.Z, after.B.Z, 0.001f);
        }

        [Test]
        public void RotateBy_Wall_PlusThenMinus_ReturnsToStart_AndUndoWorks()
        {
            Wall w = AddPartition();
            float ax = w.A.X, az = w.A.Z, bx = w.B.X, bz = w.B.Z;

            Assert.IsTrue(_tools.RotateBy(w.Id, 90f).Success);
            Assert.IsTrue(_tools.RotateBy(w.Id, -90f).Success);

            Wall after = WallById(w.Id);
            Assert.AreEqual(ax, after.A.X, 0.06f);
            Assert.AreEqual(az, after.A.Z, 0.06f);
            Assert.AreEqual(bx, after.B.X, 0.06f);
            Assert.AreEqual(bz, after.B.Z, 0.06f);

            _commands.Undo();
            _commands.Undo();
            Assert.AreEqual(ax, WallById(w.Id).A.X, 0.001f);
        }

        [Test]
        public void AddOpening_WindowsAndDoors_DoNotOverlap()
        {
            Wall w = _project.Walls[0];
            int before = new List<Opening>(WallRules.OpeningsOf(_project, w.Id)).Count;

            Assert.IsTrue(_tools.AddOpening(w.Id, OpeningType.Window).Success);
            Assert.IsTrue(_tools.AddOpening(w.Id, OpeningType.Door).Success);

            var openings = new List<Opening>(WallRules.OpeningsOf(_project, w.Id));
            Assert.AreEqual(before + 2, openings.Count);
            Opening a = openings[openings.Count - 2], b = openings[openings.Count - 1];
            Assert.IsTrue(a.Offset + a.Width <= b.Offset + 0.0001f || b.Offset + b.Width <= a.Offset + 0.0001f);
        }

        [Test]
        public void AddOpening_NoSelectedWall_Fails()
        {
            Assert.IsFalse(_tools.AddOpening("нет", OpeningType.Window).Success);
        }

        [Test]
        public void Delete_Wall_RemovesIt_AndUndoBringsItBack()
        {
            Wall w = AddPartition();
            int count = _project.Walls.Count;

            Assert.IsTrue(_tools.Delete(w.Id).Success);
            Assert.AreEqual(count - 1, _project.Walls.Count);

            _commands.Undo();
            Assert.AreEqual(count, _project.Walls.Count);
        }

        [Test]
        public void UnknownId_FailsWithoutException_ForNewTools()
        {
            Assert.IsFalse(_tools.MoveBy("нет", 1f, 0f).Success);
            Assert.IsFalse(_tools.RaiseBy("нет", 1f).Success);
            Assert.IsFalse(_tools.RotateBy("нет", 90f).Success);
            Assert.IsFalse(_tools.Delete("нет").Success);
        }

        // ---------- длина и толщина стены ----------

        [Test]
        public void ResizeWall_Longer_MovesEndB_KeepsStartA_AndUndoRestores()
        {
            Wall w = AddPartition();
            float ax = w.A.X, az = w.A.Z, length = WallRules.Length(w);

            ToolResult r = _tools.ResizeWall(w.Id, EditorTools.LengthStep, 0f);

            Assert.IsTrue(r.Success, r.Message);
            Wall after = WallById(w.Id);
            Assert.AreEqual(ax, after.A.X, 0.001f);
            Assert.AreEqual(az, after.A.Z, 0.001f);
            Assert.AreEqual(length + 0.25f, WallRules.Length(after), 0.051f);

            _commands.Undo();
            Assert.AreEqual(length, WallRules.Length(WallById(w.Id)), 0.001f);
        }

        [Test]
        public void ResizeWall_Shorter_ReducesLength()
        {
            Wall w = AddPartition();
            float length = WallRules.Length(w);

            Assert.IsTrue(_tools.ResizeWall(w.Id, -EditorTools.LengthStep, 0f).Success);

            Assert.AreEqual(length - 0.25f, WallRules.Length(WallById(w.Id)), 0.051f);
        }

        [Test]
        public void ResizeWall_TooShort_IsRefused()
        {
            Wall w = AddPartition();
            for (int i = 0; i < 20; i++) _tools.ResizeWall(w.Id, -EditorTools.LengthStep, 0f);

            Assert.GreaterOrEqual(WallRules.Length(WallById(w.Id)), EditorTools.MinWallLength - 0.001f);
            Assert.IsFalse(_tools.ResizeWall(w.Id, -EditorTools.LengthStep, 0f).Success);
        }

        [Test]
        public void ResizeWall_CannotCutOffWindow()
        {
            Wall w = AddPartition();
            Assert.IsTrue(_tools.AddOpening(w.Id, OpeningType.Window).Success);
            Opening o = new List<Opening>(WallRules.OpeningsOf(_project, w.Id))[0];
            float length = WallRules.Length(WallById(w.Id));
            float need = o.Offset + o.Width;

            // Пока окно помещается, можно укорачивать; дальше - отказ.
            ToolResult last = null;
            for (int i = 0; i < 20; i++)
            {
                last = _tools.ResizeWall(w.Id, -EditorTools.LengthStep, 0f);
                if (!last.Success) break;
            }
            Assert.IsFalse(last.Success);
            Assert.GreaterOrEqual(WallRules.Length(WallById(w.Id)), need - 0.001f);
            Assert.LessOrEqual(WallRules.Length(WallById(w.Id)), length);
        }

        [Test]
        public void ResizeWall_Thickness_ChangesByStep_WithinLimits_AndUndoRestores()
        {
            Wall w = AddPartition();
            float t0 = w.Thickness;

            Assert.IsTrue(_tools.ResizeWall(w.Id, 0f, EditorTools.ThicknessStep).Success);
            Assert.AreEqual(t0 + 0.05f, WallById(w.Id).Thickness, 0.011f);
            _commands.Undo();
            Assert.AreEqual(t0, WallById(w.Id).Thickness, 0.001f);

            for (int i = 0; i < 20; i++) _tools.ResizeWall(w.Id, 0f, EditorTools.ThicknessStep);
            Assert.LessOrEqual(WallById(w.Id).Thickness, EditorTools.MaxWallThickness + 0.001f);
            Assert.IsFalse(_tools.ResizeWall(w.Id, 0f, EditorTools.ThicknessStep).Success);
        }

        [Test]
        public void ResizeWall_UnknownWall_Fails()
        {
            Assert.IsFalse(_tools.ResizeWall("нет", 0.25f, 0f).Success);
        }

        // ---------- основные стены комнаты неприкосновенны ----------

        [Test]
        public void MainWalls_AreRecognised_PartitionIsNot()
        {
            foreach (Wall w in _project.Walls)
                Assert.IsTrue(WallPlacement.IsRoomBoundaryWall(_project, w), "стена комнаты должна считаться основной");
            Assert.IsFalse(WallPlacement.IsRoomBoundaryWall(_project, AddPartition()));
        }

        [Test]
        public void MainWalls_AreRecognised_InLShapedRoom()
        {
            Open(RoomTemplate.LShaped, new Dictionary<string, float>
            {
                { TemplateParam.Width, 10f }, { TemplateParam.Depth, 8f },
                { TemplateParam.CutWidth, 4f }, { TemplateParam.CutDepth, 3f }
            });
            foreach (Wall w in _project.Walls)
                Assert.IsTrue(WallPlacement.IsRoomBoundaryWall(_project, w));
        }

        [Test]
        public void MainWall_CannotBeMovedRotatedResizedOrDeleted()
        {
            Wall main = _project.Walls[0];
            float ax = main.A.X, az = main.A.Z;
            int count = _project.Walls.Count;
            int undo = _commands.UndoCount;

            Assert.IsFalse(_tools.MoveBy(main.Id, 0.25f, 0f).Success);
            Assert.IsFalse(_tools.RotateBy(main.Id, 90f).Success);
            Assert.IsFalse(_tools.ResizeWall(main.Id, 0.25f, 0f).Success);
            Assert.IsFalse(_tools.ResizeWall(main.Id, 0f, 0.05f).Success);
            Assert.IsFalse(_tools.Delete(main.Id).Success);

            Assert.AreEqual(ax, WallById(main.Id).A.X, 0.0001f);
            Assert.AreEqual(az, WallById(main.Id).A.Z, 0.0001f);
            Assert.AreEqual(count, _project.Walls.Count);
            Assert.AreEqual(undo, _commands.UndoCount, "отказ не должен попадать в историю");
        }

        [Test]
        public void MainWall_CanStillHaveOpeningAndMaterial()
        {
            Wall main = _project.Walls[0];
            Assert.IsTrue(_tools.AddOpening(main.Id, OpeningType.Window).Success);
        }

        // ---------- выбор: предмет, стена, пол, потолок ----------

        [Test]
        public void Selection_KindsAndKey()
        {
            string objectId = _tools.AddFromCatalog("chair").ObjectId;
            Wall w = _project.Walls[0];
            string roomId = _project.Rooms[0].Id;
            var s = new EditorSelection();

            Assert.AreEqual(SelectionKind.None, s.KindOf(_project));
            s.Select(objectId);
            Assert.AreEqual(SelectionKind.Object, s.KindOf(_project));
            s.Select(w.Id);
            Assert.AreEqual(SelectionKind.Wall, s.KindOf(_project));
            s.SelectRoomPart(roomId, EditorSelection.PartFloor);
            Assert.AreEqual(SelectionKind.Floor, s.KindOf(_project));
            Assert.AreEqual(roomId + "#floor", s.Key);
            s.SelectRoomPart(roomId, EditorSelection.PartCeiling);
            Assert.AreEqual(SelectionKind.Ceiling, s.KindOf(_project));
            s.Clear();
            Assert.IsNull(s.Key);
        }

        [Test]
        public void Selection_FloorThenCeiling_RaisesEventEvenForSameRoom()
        {
            var s = new EditorSelection();
            var keys = new List<string>();
            s.SelectionChanged += k => keys.Add(k);
            string roomId = _project.Rooms[0].Id;

            s.SelectRoomPart(roomId, EditorSelection.PartFloor);
            s.SelectRoomPart(roomId, EditorSelection.PartCeiling);
            s.SelectRoomPart(roomId, EditorSelection.PartCeiling);

            Assert.AreEqual(2, keys.Count);
        }

        [Test]
        public void Selection_CycleWalls_ForwardAndBack_Wraps()
        {
            var s = new EditorSelection();
            int n = _project.Walls.Count;
            Assert.Greater(n, 1);

            string first = s.Cycle(_project, SelectionKind.Wall, 1);
            Assert.AreEqual(_project.Walls[0].Id, first);
            string second = s.Cycle(_project, SelectionKind.Wall, 1);
            Assert.AreEqual(_project.Walls[1].Id, second);
            Assert.AreEqual(first, s.Cycle(_project, SelectionKind.Wall, -1));
            Assert.AreEqual(_project.Walls[n - 1].Id, s.Cycle(_project, SelectionKind.Wall, -1));
            Assert.AreEqual(first, s.Cycle(_project, SelectionKind.Wall, 1));
        }

        [Test]
        public void Selection_CycleObjects_BackFromNothing_SelectsLast()
        {
            _tools.AddFromCatalog("chair");
            _tools.AddFromCatalog("chair");
            var s = new EditorSelection();

            Assert.AreEqual(_project.Objects[1].Id, s.Cycle(_project, SelectionKind.Object, -1));
        }

        [Test]
        public void Selection_Refresh_DropsRemovedWall_AndKeepsRoomPart()
        {
            Wall w = AddPartition();
            var s = new EditorSelection();
            s.Select(w.Id);
            _tools.Delete(w.Id);
            s.Refresh(_project);
            Assert.IsFalse(s.HasSelection);

            s.SelectRoomPart(_project.Rooms[0].Id, EditorSelection.PartFloor);
            s.Refresh(_project);
            Assert.IsTrue(s.HasSelection);
        }
    }
}
