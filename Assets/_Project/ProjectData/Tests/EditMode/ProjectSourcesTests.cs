using System;
using System.IO;
using NUnit.Framework;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    // B-03 (часть этапа 1): проекты всех трёх источников создаются, сохраняются и открываются одинаково.
    // В файле проекта нет признака режима (VR или MR), поэтому открыть его можно в любом режиме.
    public class ProjectSourcesTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "xri_" + Guid.NewGuid().ToString("N")); }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        [Test]
        public void ThreeSources_SaveLoad_KeepSourceAndObjects()
        {
            foreach (ProjectSource source in new[] { ProjectSource.Template, ProjectSource.Scan, ProjectSource.Bim })
            {
                var svc = new ProjectService(new ProjectStorage(_dir));
                Project p = svc.Create("Проект " + source, source);
                p.Objects.Add(new PlacedObject { Id = "obj_1", CatalogId = "chair_01" });
                svc.MarkChanged();

                var other = new ProjectService(new ProjectStorage(_dir));
                Project back = other.Load(p.Id);

                Assert.AreEqual(source, back.Source);
                Assert.AreEqual(1, back.Objects.Count);
                StringAssert.DoesNotContain("\"mode\"", ProjectSerializer.ToJson(back));
            }
        }
    }
}
