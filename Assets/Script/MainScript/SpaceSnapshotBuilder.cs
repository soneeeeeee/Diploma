using System;
using System.Collections.Generic;
using UnityEngine;
using XRInterior.Contracts;
using XrInterior.ProjectData;

namespace XRInterior.Reality
{
    /// <summary>
    /// Builds SpaceSnapshot (format from B) from RealityService data. Owner: developer A.
    /// Coordinates: scan session space, as MRUK returns them.
    /// RotationY: Y angle at which local X axis (width) points along the plane.
    /// Volume Size: X (width), Y (height), Z (depth).
    /// </summary>
    public static class SpaceSnapshotBuilder
    {
        private const float ParentMaxDistance = 0.30f;

        public static SpaceSnapshot Build(RealityService reality)
        {
            var snap = new SpaceSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                CapturedAt = DateTime.UtcNow,
                FloorHeight = reality.FloorY,
                RoomHeight = reality.RoomHeight,
                Quality = 0
            };

            foreach (var p in reality.GetFloorOutline())
                snap.FloorBoundary.Add(new PlanPoint(p.x, p.y));

            var counters = new Dictionary<SnapshotPlaneKind, int>();
            var walls = new List<(string id, Vector2 a, Vector2 b)>();
            var openings = new List<(SnapshotPlane plane, Vector2 center)>();

            foreach (var rp in reality.GetPlanes())
            {
                if (!TryKind(rp.Type, out var kind)) continue;
                counters.TryGetValue(kind, out int n);
                counters[kind] = n + 1;

                var plane = new SnapshotPlane
                {
                    Id = $"{kind.ToString().ToLowerInvariant()}_{n:D2}",
                    Kind = kind,
                    Center = new Vec3(rp.Center.x, rp.Center.y, rp.Center.z),
                    RotationY = YawOfWidthAxis(rp.Rotation),
                    Size = new Size2(rp.Size.x, rp.Size.y)
                };
                snap.Planes.Add(plane);

                if (kind == SnapshotPlaneKind.Wall)
                {
                    Vector3 half = rp.Rotation * Vector3.right * (rp.Size.x * 0.5f);
                    walls.Add((plane.Id,
                        new Vector2(rp.Center.x - half.x, rp.Center.z - half.z),
                        new Vector2(rp.Center.x + half.x, rp.Center.z + half.z)));
                }
                else if (kind == SnapshotPlaneKind.Door || kind == SnapshotPlaneKind.Window)
                {
                    openings.Add((plane, new Vector2(rp.Center.x, rp.Center.z)));
                }
            }

            foreach (var (plane, c) in openings)
            {
                float best = float.MaxValue;
                string bestId = null;
                foreach (var w in walls)
                {
                    float d = DistanceToSegment(c, w.a, w.b);
                    if (d < best) { best = d; bestId = w.id; }
                }
                plane.ParentPlaneId = best <= ParentMaxDistance ? bestId : null;
            }

            int v = 0;
            foreach (var rv in reality.GetVolumes())
            {
                snap.Volumes.Add(new SnapshotVolume
                {
                    Id = $"volume_{v++:D2}",
                    Label = NormalizeLabel(rv.Label),
                    Center = new Vec3(rv.Center.x, rv.Center.y, rv.Center.z),
                    RotationY = YawOfWidthAxis(rv.Rotation),
                    // MRUK local Z of a volume is height; in the snapshot vertical is Y
                    Size = new Vec3(rv.Size.x, rv.Size.z, rv.Size.y)
                });
            }

            return snap;
        }

        private static bool TryKind(RealSurfaceType t, out SnapshotPlaneKind kind)
        {
            switch (t)
            {
                case RealSurfaceType.Wall: kind = SnapshotPlaneKind.Wall; return true;
                case RealSurfaceType.Floor: kind = SnapshotPlaneKind.Floor; return true;
                case RealSurfaceType.Ceiling: kind = SnapshotPlaneKind.Ceiling; return true;
                case RealSurfaceType.Door: kind = SnapshotPlaneKind.Door; return true;
                case RealSurfaceType.Window: kind = SnapshotPlaneKind.Window; return true;
                default: kind = SnapshotPlaneKind.Wall; return false;
            }
        }

        private static float YawOfWidthAxis(Quaternion rot)
        {
            Vector3 r = rot * Vector3.right;
            float yaw = Mathf.Atan2(-r.z, r.x) * Mathf.Rad2Deg;
            return yaw < 0f ? yaw + 360f : yaw;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        private static string NormalizeLabel(string label)
        {
            return string.IsNullOrEmpty(label) ? "other" : label.ToLowerInvariant();
        }
    }
}
