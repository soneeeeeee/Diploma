using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using XrInterior.ProjectData;

namespace XrInterior.ProjectData.Tests
{
    public class SnapshotValidationTests
    {
        private static string SamplePath =>
            Path.Combine(Application.dataPath, "_Project/ProjectData/Samples/control_project.json");

        private static Project LoadSample()
        {
            return ProjectSerializer.FromJson(File.ReadAllText(SamplePath));
        }

        [Test]
        public void ControlProject_HasNoSnapshotErrors()
        {
            Project p = LoadSample();

            var errors = ProjectValidator.Validate(p);

            Assert.IsFalse(errors.Any(e => e.Contains("метка") || e.Contains("quality")));
        }

        [Test]
        public void UnknownVolumeLabel_IsRejected()
        {
            Project p = LoadSample();
            p.SpaceSnapshots[0].Volumes[0].Label = "banana";

            var errors = ProjectValidator.Validate(p);

            Assert.IsTrue(errors.Any(e => e.Contains("метка")));
        }

        [Test]
        public void SnapshotQualityOutOfRange_IsRejected()
        {
            Project p = LoadSample();
            p.SpaceSnapshots[0].Quality = 150;

            var errors = ProjectValidator.Validate(p);

            Assert.IsTrue(errors.Any(e => e.Contains("quality")));
        }
    }
}
