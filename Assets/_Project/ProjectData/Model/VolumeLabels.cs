using System;

namespace XrInterior.ProjectData   // ← замените на namespace из Entities.cs
{
    public static class VolumeLabels
    {
        public static readonly string[] All =
            { "table", "sofa", "bed", "cabinet", "chair", "other" };

        public static bool IsValid(string label)
        {
            return label != null && Array.IndexOf(All, label) >= 0;
        }
    }
}
