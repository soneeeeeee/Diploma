using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using XRInterior.Contracts;
using XrInterior.ProjectData;

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

        // --- Поля и свойства для контура пола и высоты ---
        private readonly List<Vector2> _floorOutline = new List<Vector2>(); // x, z в мировых координатах скана

        public float FloorY { get; private set; }
        public float CeilingY { get; private set; }
        public float RoomHeight => CeilingY - FloorY;

        /// <summary>Контур пола: вершины по порядку, x и z (в Vector2: x и y).</summary>
        public IReadOnlyList<Vector2> GetFloorOutline() => _floorOutline;
        // -------------------------------------------------

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
                        Id = $"{type}_{_planes.Count:D2}",
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
                        Id = $"{anchor.Label}_{_volumes.Count:D2}",
                        Label = anchor.Label.ToString(),
                        Center = center,
                        Rotation = anchor.transform.rotation,
                        Size = vb.size
                    });
                    _volumeBounds.Add(WorldAabb(anchor.transform, vb));
                }
            }

            ReadFloorOutline();
            LogSummary();
            LogFloorOutline();
            LogWallEnds();

            // проверка снимка
            var snap = SpaceSnapshotBuilder.Build(this);
            Debug.Log($"[Reality] snapshot: planes {snap.Planes.Count}, volumes {snap.Volumes.Count}, boundary {snap.FloorBoundary.Count}, floorY {snap.FloorHeight:F2}, height {snap.RoomHeight:F2}");
            foreach (var sp in snap.Planes)
                if (sp.Kind == SnapshotPlaneKind.Door || sp.Kind == SnapshotPlaneKind.Window)
                    Debug.Log($"[Reality] snapshot {sp.Id} parent {sp.ParentPlaneId ?? "none"}");
            Debug.Log($"[Reality] snapshot json chars: {Newtonsoft.Json.JsonConvert.SerializeObject(snap).Length}");
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

        // --- Методы для чтения и логирования контура пола ---
        private void ReadFloorOutline()
        {
            _floorOutline.Clear();
            MRUKAnchor floor = _room.FloorAnchor;
            MRUKAnchor ceiling = _room.CeilingAnchor;
            FloorY = floor != null ? floor.transform.position.y : 0f;
            CeilingY = ceiling != null ? ceiling.transform.position.y : FloorY;

            if (floor == null || floor.PlaneBoundary2D == null) return;
            foreach (var p in floor.PlaneBoundary2D)
            {
                Vector3 w = floor.transform.TransformPoint(new Vector3(p.x, p.y, 0f));
                _floorOutline.Add(new Vector2(w.x, w.z));
            }

            // единый порядок обхода: против часовой стрелки при виде сверху
            if (SignedArea(_floorOutline) < 0f)
                _floorOutline.Reverse();
        }

        private static float SignedArea(IReadOnlyList<Vector2> pts)
        {
            float s = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % pts.Count];
                s += a.x * b.y - b.x * a.y;
            }
            return s * 0.5f;
        }

        private void LogFloorOutline()
        {
            Debug.Log($"[Reality] floorY {FloorY:F2}, ceilingY {CeilingY:F2}, height {RoomHeight:F2} m, outline points {_floorOutline.Count}");
            float perimeter = 0f, area2 = 0f;
            for (int i = 0; i < _floorOutline.Count; i++)
            {
                Vector2 a = _floorOutline[i];
                Vector2 b = _floorOutline[(i + 1) % _floorOutline.Count];
                float len = Vector2.Distance(a, b);
                perimeter += len;
                area2 += a.x * b.y - b.x * a.y;
                Debug.Log($"[Reality] outline {i}: x {a.x:F2} z {a.y:F2}, edge to next {len:F2} m");
            }
            Debug.Log($"[Reality] outline perimeter {perimeter:F2} m, area {Mathf.Abs(area2) * 0.5f:F2} m2");
        }

        // --- Метод логирования концов стен ---
        private void LogWallEnds()
        {
            int i = 0;
            foreach (var p in _planes)
            {
                if (p.Type != RealSurfaceType.Wall) continue;
                Vector3 right = p.Rotation * Vector3.right;
                Vector3 a = p.Center - right * (p.Size.x * 0.5f);
                Vector3 b = p.Center + right * (p.Size.x * 0.5f);
                Debug.Log($"[Reality] wallEnds {i++}: a ({a.x:F2}, {a.z:F2}) b ({b.x:F2}, {b.z:F2}) len {p.Size.x:F2}");
            }
        }
        // -------------------------------------

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