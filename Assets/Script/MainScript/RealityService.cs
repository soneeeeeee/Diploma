using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using XRInterior.Contracts;

namespace XRInterior.Reality
{
    /// <summary>
    /// Реализация IRealityService на данных MRUK. Владелец: разработчик A.
    /// Координаты: мировая система сессии сканирования (как отдаёт MRUK).
    /// Пересчёт в систему проекта делается позже (A-05).
    /// </summary>
    public class RealityService : MonoBehaviour, IRealityService
    {
        private readonly List<RealPlane> _planes = new List<RealPlane>();
        private readonly List<RealVolume> _volumes = new List<RealVolume>();
        private readonly List<Bounds> _volumeBounds = new List<Bounds>();
        private MRUKRoom _room;

        private void Start()
        {
            if (MRUK.Instance == null)
            {
                Debug.LogError("[Reality] MRUK.Instance is null. Is the MRUK component in the scene?");
                return;
            }
            MRUK.Instance.RegisterSceneLoadedCallback(Rebuild);
        }

        private void Rebuild()
        {
            _planes.Clear();
            _volumes.Clear();
            _volumeBounds.Clear();

            _room = MRUK.Instance.GetCurrentRoom();
            if (_room == null)
            {
                Debug.LogWarning("[Reality] No current room (Space Setup not done or no permission?)");
                return;
            }

            foreach (var anchor in _room.Anchors)
            {
                RealSurfaceType type = ToSurfaceType(anchor.Label);

                if (type != RealSurfaceType.Other && anchor.PlaneRect.HasValue)
                {
                    Rect r = anchor.PlaneRect.Value;
                    _planes.Add(new RealPlane
                    {
                        Id = $"{type}_{_planes.Count:D2}", // <-- ИЗМЕНЕНО: уникальный Id для плоскости
                        Type = type,
                        Center = anchor.transform.position,
                        Rotation = anchor.transform.rotation,
                        Size = new Vector2(r.width, r.height)
                    });
                }
                else if (anchor.VolumeBounds.HasValue)
                {
                    Bounds vb = anchor.VolumeBounds.Value;
                    Vector3 center = anchor.transform.TransformPoint(vb.center);
                    _volumes.Add(new RealVolume
                    {
                        Id = $"{anchor.Label}_{_volumes.Count:D2}", // <-- ИЗМЕНЕНО: уникальный Id для объёма
                        Label = anchor.Label.ToString(),
                        Center = center,
                        Rotation = anchor.transform.rotation,
                        Size = vb.size
                    });
                    _volumeBounds.Add(WorldAabb(anchor.transform, vb));
                }
            }

            LogSummary();
        }

        public IReadOnlyList<RealPlane> GetPlanes() => _planes;

        public IReadOnlyList<RealVolume> GetVolumes() => _volumes;

        /// <summary>
        /// true, если bounds вылезает за стены комнаты или пересекает крупный объём.
        /// Упрощение: объёмы проверяются как AABB (прямоугольник по осям мира).
        /// </summary>
        public bool IsBlocked(Bounds bounds)
        {
            if (_room != null)
            {
                Vector3 c = bounds.center, e = bounds.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    if (!_room.IsPositionInRoom(corner, false))
                        return true;
                }
            }

            foreach (var vb in _volumeBounds)
                if (vb.Intersects(bounds))
                    return true;

            return false;
        }

        /// <summary>
        /// Упрощённо: Good, если комната загружена, иначе Unknown.
        /// Настоящее состояние трекинга уточним позже (A-06/A-14).
        /// </summary>
        public TrackingState GetTrackingState()
        {
            return _room != null ? TrackingState.Good : TrackingState.Unknown;
        }

        private static RealSurfaceType ToSurfaceType(MRUKAnchor.SceneLabels label)
        {
            if ((label & MRUKAnchor.SceneLabels.FLOOR) != 0) return RealSurfaceType.Floor;
            if ((label & MRUKAnchor.SceneLabels.CEILING) != 0) return RealSurfaceType.Ceiling;
            if ((label & MRUKAnchor.SceneLabels.WALL_FACE) != 0) return RealSurfaceType.Wall;
            if ((label & MRUKAnchor.SceneLabels.DOOR_FRAME) != 0) return RealSurfaceType.Door;
            if ((label & MRUKAnchor.SceneLabels.WINDOW_FRAME) != 0) return RealSurfaceType.Window;
            return RealSurfaceType.Other;
        }

        private static Bounds WorldAabb(Transform t, Bounds local)
        {
            Vector3 c = local.center, e = local.extents;
            var b = new Bounds(t.TransformPoint(c), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                b.Encapsulate(t.TransformPoint(corner));
            }
            return b;
        }

        private void LogSummary()
        {
            Debug.Log($"[Reality] planes: {_planes.Count}, volumes: {_volumes.Count}");
            foreach (var p in _planes)
                Debug.Log($"[Reality] {p.Type} {p.Id}: width {p.Size.x:F2} m, height {p.Size.y:F2} m, center {p.Center}");
            foreach (var v in _volumes)
                Debug.Log($"[Reality] volume {v.Label} ({v.Id}): size {v.Size.x:F2} x {v.Size.y:F2} x {v.Size.z:F2} m, center {v.Center}");
        }
    }
}