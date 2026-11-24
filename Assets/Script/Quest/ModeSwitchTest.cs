using UnityEngine;
using UnityEngine.InputSystem;

public class ModeSwitchTest : MonoBehaviour
{
    [SerializeField] private ModeService modeService;
    private InputAction toggleAction;

    private void OnEnable()
    {
        toggleAction = new InputAction("ToggleMode", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        toggleAction.performed += OnToggle;
        toggleAction.Enable();
    }

    private void OnDisable()
    {
        toggleAction.performed -= OnToggle;
        toggleAction.Disable();
    }

    private void OnToggle(InputAction.CallbackContext ctx)
    {
        modeService.Toggle();
    }
}
