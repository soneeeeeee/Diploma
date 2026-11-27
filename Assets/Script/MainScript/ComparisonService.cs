using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using XRInterior.Contracts;
using XrInterior.ProjectData;

namespace XRInterior.Reality
{
    /// <summary>
    /// IComparisonService. Owner: developer A.
    /// A-04: ScanAsync and SaveSnapshot work. Compare/Accept/Ignore are stubs (A-11).
    /// SaveSnapshot keeps the snapshot in LastSnapshot until B adds CreateFromScan.
    /// </summary>
    public class ComparisonService : MonoBehaviour, IComparisonService
    {
        [SerializeField] private RealityService reality;
        [SerializeField] private bool runTestOnStart = true;

        private readonly List<ComparisonItem> _results = new List<ComparisonItem>();

        public SpaceSnapshot LastSnapshot { get; private set; }

        public event Action SnapshotSaved;
        public event Action<IReadOnlyList<ComparisonItem>> ComparisonCompleted;

        private void Awake()
        {
            if (reality == null)
                reality = FindFirstObjectByType<RealityService>();
        }

        private async void Start()
        {
            if (!runTestOnStart) return;
            bool ok = await ScanAsync();
            Debug.Log($"[Compare] scan result: {ok}");
            if (ok) SaveSnapshot();
        }

        public async Task<bool> ScanAsync()
        {
            if (reality == null)
            {
                Debug.LogError("[Compare] RealityService not found in scene");
                return false;
            }
            if (MRUK.Instance == null)
            {
                Debug.LogError("[Compare] MRUK.Instance is null");
                return false;
            }

            // 1) The scene may already be loading (Load Scene On Startup). Wait for it first.
            if (await WaitForPlanes(10f))
                return true;

            // 2) Still no data: ask the device to load the scene.
            Debug.Log("[Compare] no scan data yet, loading from device");
            var result = await MRUK.Instance.LoadSceneFromDevice();
            Debug.Log($"[Compare] LoadSceneFromDevice: {result}");
            if (result != MRUK.LoadDeviceResult.Success &&
                result != MRUK.LoadDeviceResult.DiscoveryOngoing)
                return false;

            return await WaitForPlanes(10f);
        }

        private async Task<bool> WaitForPlanes(float seconds)
        {
            float waited = 0f;
            while (reality.GetPlanes().Count == 0 && waited < seconds)
            {
                await Task.Delay(100);
                waited += 0.1f;
            }
            return reality.GetPlanes().Count > 0;
        }

        public void SaveSnapshot()
        {
            if (reality == null || reality.GetPlanes().Count == 0)
            {
                Debug.LogWarning("[Compare] nothing to save: no scan data");
                return;
            }

            LastSnapshot = SpaceSnapshotBuilder.Build(reality);
            Debug.Log($"[Compare] snapshot saved: id {LastSnapshot.Id}, planes {LastSnapshot.Planes.Count}, volumes {LastSnapshot.Volumes.Count}");
            if (LastSnapshot.Planes.Count > 0)
                Debug.Log($"[Compare] first plane id: {LastSnapshot.Planes[0].Id}");
            foreach (var p in LastSnapshot.Planes)
                if (p.Kind == SnapshotPlaneKind.Door || p.Kind == SnapshotPlaneKind.Window)
                    Debug.Log($"[Compare] opening {p.Id} parent {p.ParentPlaneId ?? "none"}");

            // TODO: when B adds IProjectService.CreateFromScan, pass LastSnapshot there.
            SnapshotSaved?.Invoke();
        }

        public void Compare()
        {
            Debug.Log("[Compare] Compare is not implemented yet (A-11)");
        }

        public IReadOnlyList<ComparisonItem> GetResults() => _results;

        public void Accept(string elementId)
        {
            Debug.Log($"[Compare] Accept {elementId}: not implemented yet (A-11/A-13)");
        }

        public void Ignore(string elementId)
        {
            Debug.Log($"[Compare] Ignore {elementId}: not implemented yet (A-11/A-13)");
        }
    }
}