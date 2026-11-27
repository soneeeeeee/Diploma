using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class CommandServiceTests
    {
        private string _dir;

        private static string SamplePath =>
            Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/control_project.json");

        [SetUp]
        public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "xri_" + Guid.NewGuid().ToString("N")); }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        private ProjectService NewProjects() { return new ProjectService(new ProjectStorage(_dir)); }

        private static PlacedObject NewObj(string id)
        {
            return new PlacedObject { Id = id, CatalogId = "chair_01", Position = new Vec3(1, 0, 2) };
        }

        // Снимок проекта для сравнения (без времени последнего изменения).
        private static string Snap(Project p)
        {
            JObject o = JObject.Parse(ProjectSerializer.ToJson(p));
            o.Remove("updatedAt");
            return o.ToString();
        }

        [Test]
        public void FiftyActions_ExecuteUndoRedo_WithoutDesync()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);

            var snaps = new List<string> { Snap(projects.Current) };

            for (int i = 0; i < 20; i++)
            {
                cmds.Execute(new AddObjectCommand(NewObj("obj_" + i)));
                snaps.Add(Snap(projects.Current));
            }
            for (int i = 0; i < 20; i++)
            {
                cmds.Execute(new SetObjectTransformCommand("obj_" + i, new Vec3(i, 0, 1), new Vec3(0, 15 * i, 0), Vec3.One));
                snaps.Add(Snap(projects.Current));
            }
            for (int i = 0; i < 10; i++)
            {
                cmds.Execute(new RemoveObjectCommand("obj_" + (i * 2)));
                snaps.Add(Snap(projects.Current));
            }

            Assert.AreEqual(50, snaps.Count - 1);

            for (int k = 50; k >= 1; k--)
            {
                cmds.Undo();
                Assert.AreEqual(snaps[k - 1], Snap(projects.Current), "после отмены шага " + k);
            }
            Assert.IsFalse(cmds.CanUndo);

            for (int k = 1; k <= 50; k++)
            {
                cmds.Redo();
                Assert.AreEqual(snaps[k], Snap(projects.Current), "после повтора шага " + k);
            }
            Assert.IsFalse(cmds.CanRedo);
        }

        [Test]
        public void Execute_ClearsRedoHistory()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);

            cmds.Execute(new AddObjectCommand(NewObj("a")));
            cmds.Undo();
            Assert.IsTrue(cmds.CanRedo);

            cmds.Execute(new AddObjectCommand(NewObj("b")));
            Assert.IsFalse(cmds.CanRedo);
        }

        [Test]
        public void Capacity_DropsOldestCommands()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects, 50);

            for (int i = 0; i < 60; i++) cmds.Execute(new AddObjectCommand(NewObj("obj_" + i)));

            int undone = 0;
            while (cmds.CanUndo) { cmds.Undo(); undone++; }

            Assert.AreEqual(50, undone);
            Assert.AreEqual(10, projects.Current.Objects.Count);
        }

        [Test]
        public void RemoveObject_CleansReferences_AndUndoRestoresEverything()
        {
            var projects = NewProjects();
            projects.Import(SamplePath);
            var cmds = new CommandService(projects);

            projects.Current.MaterialOverrides.Add(
                new MaterialOverride { TargetId = "obj_table_1", Slot = "surface", MaterialId = "mat_wood_oak_01" });
            string before = Snap(projects.Current);

            cmds.Execute(new RemoveObjectCommand("obj_table_1"));

            Assert.IsEmpty(ProjectValidator.Validate(projects.Current));
            Assert.IsFalse(projects.Current.Objects.Any(o => o.Id == "obj_table_1"));
            Assert.IsFalse(projects.Current.MaterialOverrides.Any(m => m.TargetId == "obj_table_1"));
            foreach (Variant v in projects.Current.Variants)
                Assert.IsFalse(v.ObjectStates.Any(s => s.ObjectId == "obj_table_1"));

            cmds.Undo();
            Assert.AreEqual(before, Snap(projects.Current));
        }

        [Test]
        public void FailingCommand_DoesNotChangeHistoryOrProject()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);

            cmds.Execute(new AddObjectCommand(NewObj("obj_1")));
            Assert.Throws<InvalidOperationException>(() => cmds.Execute(new AddObjectCommand(NewObj("obj_1"))));

            Assert.AreEqual(1, cmds.UndoCount);
            Assert.AreEqual(1, projects.Current.Objects.Count);
        }

        [Test]
        public void CompositeCommand_RollsBackOnFailure()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);
            cmds.Execute(new AddObjectCommand(NewObj("obj_1")));
            string before = Snap(projects.Current);

            var composite = new CompositeCommand("Два объекта", new ICommand[]
            {
                new AddObjectCommand(NewObj("obj_2")),
                new AddObjectCommand(NewObj("obj_1")) // повтор id - упадёт
            });

            Assert.Throws<InvalidOperationException>(() => cmds.Execute(composite));
            Assert.AreEqual(before, Snap(projects.Current));
            Assert.AreEqual(1, cmds.UndoCount);
        }

        [Test]
        public void LoadingProject_ClearsHistory()
        {
            var projects = NewProjects();
            Project p = projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);
            cmds.Execute(new AddObjectCommand(NewObj("obj_1")));
            Assert.IsTrue(cmds.CanUndo);

            projects.Load(p.Id);

            Assert.IsFalse(cmds.CanUndo);
            Assert.IsFalse(cmds.CanRedo);
        }

        [Test]
        public void Commands_RaiseEvents_AndSetNames()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);
            int history = 0, changed = 0;
            cmds.HistoryChanged += () => history++;
            projects.ProjectChanged += () => changed++;

            cmds.Execute(new AddObjectCommand(NewObj("obj_1")));
            Assert.AreEqual(1, history);
            Assert.AreEqual(1, changed);
            Assert.AreEqual("Добавить объект", cmds.NextUndoName);

            cmds.Undo();
            Assert.AreEqual(2, history);
            Assert.AreEqual(2, changed);
            Assert.IsNull(cmds.NextUndoName);
            Assert.AreEqual("Добавить объект", cmds.NextRedoName);
        }

        [Test]
        public void SetTransform_UndoRestoresExactValues()
        {
            var projects = NewProjects();
            projects.Create("T", ProjectSource.Template);
            var cmds = new CommandService(projects);
            cmds.Execute(new AddObjectCommand(NewObj("obj_1")));

            cmds.Execute(new SetObjectTransformCommand("obj_1", new Vec3(5, 0, 6), new Vec3(0, 90, 0), Vec3.One));
            Assert.AreEqual(5f, projects.Current.Objects[0].Position.X);
            Assert.AreEqual(90f, projects.Current.Objects[0].Rotation.Y);

            cmds.Undo();
            Assert.AreEqual(1f, projects.Current.Objects[0].Position.X);
            Assert.AreEqual(2f, projects.Current.Objects[0].Position.Z);
            Assert.AreEqual(0f, projects.Current.Objects[0].Rotation.Y);
        }
    }
}
