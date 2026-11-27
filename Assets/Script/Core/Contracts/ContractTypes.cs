using UnityEngine;

namespace XRInterior.Contracts
{
    /// <summary>Режим отображения Quest.</summary>
    public enum AppMode
    {
        VR,
        MR
    }

    /// <summary>Состояние трекинга.</summary>
    public enum TrackingState
    {
        Unknown,
        Good,
        Limited,
        Lost
    }

    /// <summary>Тип реальной поверхности из скана.</summary>
    public enum RealSurfaceType
    {
        Floor,
        Ceiling,
        Wall,
        Door,
        Window,
        Other
    }

    /// <summary>«система координат сессии сканирования (как отдаёт MRUK).</summary>
    public struct RealPlane
    {
        public string Id;
        public RealSurfaceType Type;
        public Vector3 Center;
        public Quaternion Rotation;
        public Vector2 Size; // ширина, высота
    }

    /// <summary>Крупный объём из скана (стол, шкаф и т.п.).</summary>
    public struct RealVolume
    {
        public string Id;
        public string Label;
        public Vector3 Center;
        public Quaternion Rotation;
        public Vector3 Size;
    }

    /// <summary>Результат совмещения проекта с реальностью.</summary>
    public struct AlignmentResult
    {
        public bool Success;
        public Vector3 Offset;
        public float RotationY;   // градусы вокруг оси Y
        public float Quality;     // 0..100
        public string AnchorId;
    }

    /// <summary>Статус элемента после сравнения с реальностью.</summary>
    public enum ComparisonStatus
    {
        Matched,   // совпало
        NotBuilt,  // есть в проекте, нет в реальности
        Extra,     // есть в реальности, нет в проекте
        Accepted,  // принято пользователем
        Ignored    // отклонено пользователем
    }

    /// <summary>Одна запись результата сравнения.</summary>
    public struct ComparisonItem
    {
        public string ElementId;
        public ComparisonStatus Status;
        public float DeviationMeters;
        public float DeviationDegrees;
    }
}