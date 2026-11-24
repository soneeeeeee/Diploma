using System;
using UnityEngine;
using XRInterior.Contracts;

public class ModeService : MonoBehaviour, IModeService
{
    [Header("Ссылки (перетащить в Inspector)")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GameObject virtualEnvironment;

    public AppMode CurrentMode { get; private set; } = AppMode.MR;
    public event Action<AppMode> ModeChanged;

    private void Start()
    {
        SetMode(CurrentMode);
    }

    public void SetMode(AppMode mode)
    {
        CurrentMode = mode;

        if (mode == AppMode.MR)
        {
            passthroughLayer.enabled = true;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            virtualEnvironment.SetActive(false);
        }
        else
        {
            passthroughLayer.enabled = false;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            virtualEnvironment.SetActive(true);
        }

        Debug.Log("MODE: " + mode);
        ModeChanged?.Invoke(mode);
    }

    public void TransferProject(string projectId, AppMode targetMode)
    {
        // Заглушка: полноценный перенос делается в A-09 и A-10.
        Debug.Log("TransferProject: " + projectId + " -> " + targetMode);
        SetMode(targetMode);
    }

    [ContextMenu("Toggle")]
    public void Toggle()
    {
        SetMode(CurrentMode == AppMode.MR ? AppMode.VR : AppMode.MR);
    }
}