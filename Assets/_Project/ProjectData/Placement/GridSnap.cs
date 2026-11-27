// Привязка к сетке (B-09, FR-104): положение кратно шагу (по умолчанию 0,05 м), поворот кратен шагу (по умолчанию 15°).

using System;

namespace XrInterior.ProjectData
{
    public sealed class SnapSettings
    {
        /// <summary>Шаг положения, метры.</summary>
        public float PositionStep { get; set; } = 0.05f;

        /// <summary>Шаг поворота, градусы.</summary>
        public float RotationStepDegrees { get; set; } = 15f;

        public static SnapSettings Default { get { return new SnapSettings(); } }
    }

    public static class GridSnap
    {
        /// <summary>Округляет значение до ближайшего кратного шагу.</summary>
        public static float Snap(float value, float step)
        {
            if (!(step > 0f)) throw new ArgumentOutOfRangeException(nameof(step), "Шаг должен быть больше нуля.");
            double snapped = Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step;
            return (float)snapped;
        }

        /// <summary>Округляет угол до шага и приводит его к диапазону (-180; 180].</summary>
        public static float SnapYaw(float yawDegrees, float stepDegrees)
        {
            float snapped = Snap(yawDegrees, stepDegrees);
            while (snapped > 180f) snapped -= 360f;
            while (snapped <= -180f) snapped += 360f;
            return snapped;
        }
    }
}
