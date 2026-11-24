using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRFixIt.XR
{
    /// <summary>Where the user is in the sit/stand cycle.</summary>
    public enum SeatState
    {
        Standing,
        Sitting,   // animating towards the chair
        Seated,
        StandingUp // animating away from the chair
    }

    /// <summary>
    /// Manages seated/standing state for the XR Origin. Sitting and standing can be triggered by the
    /// desk button (poke, ray or mouse click), the chair itself, or a keyboard shortcut, and are animated.
    /// While seated, walking, turning and teleporting are off and the head is tethered to the chair,
    /// while 6DoF head and hand tracking stay active.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(900)] // before HeadCollisionBlocker (1000): tether the head to the seat first, then resolve collisions
    public class ChairSeatController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Rig")]
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Behaviour[] locomotionBehaviours;
        [SerializeField] private DesktopFallbackLocomotion desktopFallback;

        [Header("Chair")]
        [SerializeField] private XRSimpleInteractable chairInteractable;
        [SerializeField] private Transform seatEyeAnchor;
        [SerializeField] private Transform standAnchor;
        [SerializeField] private Renderer[] chairRenderers;
        [SerializeField] private Color hoverEmissionColor = new Color(0.15f, 0.45f, 0.65f, 1f);

        [Header("Desk button")]
        [SerializeField] private XRSimpleInteractable seatButton;
        [SerializeField] private TMP_Text seatButtonLabel;
        [SerializeField] private Renderer seatButtonRenderer;
        [SerializeField] private string sitLabel = "DUDUK";
        [SerializeField] private string standLabel = "BERDIRI";
        [SerializeField] private Color sitColor = new Color(0.20f, 0.75f, 0.35f, 1f);
        [SerializeField] private Color standColor = new Color(0.95f, 0.60f, 0.15f, 1f);

        [Header("Input")]
        [SerializeField] private bool startSeated = true;
        [SerializeField] private Key toggleSeatKey = Key.F;
        [SerializeField] private float toggleCooldown = 0.35f;
        [SerializeField] private bool allowDesktopMouseClick = true;

        [Header("Animation")]
        [SerializeField] private float sitDuration = 1.1f;
        [SerializeField] private float standDuration = 0.9f;
        [Tooltip("Eye height above the floor when standing. Match DesktopFallbackLocomotion's desktop eye height.")]
        [SerializeField] private float standEyeHeight = 1.62f;

        [Header("Seated lock")]
        [Tooltip("How far the head may lean away from the seat before it is held back.")]
        [SerializeField] private float seatLeanRadius = 0.3f;
        [SerializeField] private float seatLeanHeight = 0.25f;

        private MaterialPropertyBlock propertyBlock;
        private Coroutine transition;
        private float lastToggleTime = -10f;
        private static readonly List<UnityEngine.XR.InputDevice> XrControllers = new List<UnityEngine.XR.InputDevice>();
        private bool wasXrPrimaryPressed;

        /// <summary>Current phase of the sit/stand cycle.</summary>
        public SeatState State { get; private set; } = SeatState.Standing;

        /// <summary>True from the moment sitting begins until standing has finished.</summary>
        public bool IsSeated => State != SeatState.Standing;

        /// <summary>True while the sit or stand animation is playing.</summary>
        public bool IsTransitioning => State == SeatState.Sitting || State == SeatState.StandingUp;

        private bool CanAnimate =>
            isActiveAndEnabled && xrOrigin != null && xrOrigin.Camera != null && seatEyeAnchor != null && standAnchor != null;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (chairInteractable == null) chairInteractable = GetComponent<XRSimpleInteractable>();
            if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
            if (characterController == null && xrOrigin != null) characterController = xrOrigin.GetComponent<CharacterController>();
            if (desktopFallback == null && xrOrigin != null) desktopFallback = xrOrigin.GetComponent<DesktopFallbackLocomotion>();
        }

        private void OnEnable()
        {
            Subscribe(chairInteractable, OnChairSelectEntered, true);
            Subscribe(seatButton, OnButtonSelectEntered, false);
            Application.onBeforeRender += HoldHeadToSeat;

            // Disabling the object stops the animation coroutine; never stay stuck mid-transition.
            if (State == SeatState.Sitting) SitImmediate();
            else if (State == SeatState.StandingUp) StandImmediate();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= HoldHeadToSeat;
            Unsubscribe(chairInteractable, OnChairSelectEntered, true);
            Unsubscribe(seatButton, OnButtonSelectEntered, false);
            SetHoverVisual(false);
        }

        private void Start()
        {
            if (startSeated) SitImmediate();
            else StandImmediate();
        }

        private void Update()
        {
            if (CheckXrControllerToggle())
            {
                ToggleSeat();
                return;
            }

            if (Keyboard.current != null && Keyboard.current[toggleSeatKey].wasPressedThisFrame)
            {
                ToggleSeat();
                return;
            }

            if (allowDesktopMouseClick && !DesktopFallbackLocomotion.IsXRDisplayActive())
            {
                CheckDesktopMouseClick();
            }
        }

        private void LateUpdate() => HoldHeadToSeat();

        // ---------------------------------------------------------------- public API

        /// <summary>Toggles between seated and standing, ignoring requests during an animation or cooldown.</summary>
        public void ToggleSeat()
        {
            if (IsTransitioning || Time.unscaledTime - lastToggleTime < toggleCooldown)
            {
                return;
            }
            lastToggleTime = Time.unscaledTime;

            if (State == SeatState.Seated) StandUp();
            else SitDown();
        }

        /// <summary>Starts sitting down on the chair (animated unless <paramref name="instant"/>).</summary>
        public void SitDown(bool instant = false)
        {
            if (IsSeated && State != SeatState.StandingUp) return;

            StopTransition();
            if (instant || !CanAnimate)
            {
                SitImmediate();
                return;
            }
            transition = StartCoroutine(SitRoutine());
        }

        /// <summary>Starts standing up beside the chair (animated unless <paramref name="instant"/>).</summary>
        public void StandUp(bool instant = false)
        {
            if (State == SeatState.Standing || State == SeatState.StandingUp) return;

            StopTransition();
            if (instant || !CanAnimate)
            {
                StandImmediate();
                return;
            }
            transition = StartCoroutine(StandRoutine());
        }

        // ---------------------------------------------------------------- state changes

        private void SitImmediate()
        {
            StopTransition();
            State = SeatState.Seated;
            SetLocomotionEnabled(false);
            SetDesktopSeated(true);
            AlignCameraToSeat();
            RefreshButton();
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(RealignSeatAfterFirstFrame());
            }
        }

        private void StandImmediate()
        {
            StopTransition();
            State = SeatState.Standing;
            SetLocomotionEnabled(false);
            SetDesktopSeated(false);
            PlaceAtStandAnchor();
            SetLocomotionEnabled(true);
            RefreshButton();
        }

        private IEnumerator SitRoutine()
        {
            State = SeatState.Sitting;
            SetLocomotionEnabled(false);
            SetDesktopSeated(true); // switch input mode first so the camera pose is the seated one
            RefreshButton();

            Transform cam = xrOrigin.Camera.transform;
            Vector3 from = cam.position;
            Vector3 to = seatEyeAnchor.position;
            float fromYaw = YawOf(cam.forward);
            float toYaw = YawOf(seatEyeAnchor.forward);

            // Step over to the chair first, then lower onto it.
            for (float t = 0f; t < 1f; t += Time.deltaTime / sitDuration)
            {
                float slide = Phase(t, 0f, 0.65f);
                float lower = Phase(t, 0.25f, 1f);
                ApplyPose(new Vector3(Mathf.Lerp(from.x, to.x, slide), Mathf.Lerp(from.y, to.y, lower), Mathf.Lerp(from.z, to.z, slide)),
                    Mathf.LerpAngle(fromYaw, toYaw, Phase(t, 0f, 1f)));
                yield return null;
            }

            AlignCameraToSeat();
            State = SeatState.Seated;
            transition = null;
            RefreshButton();
        }

        private IEnumerator StandRoutine()
        {
            State = SeatState.StandingUp;
            RefreshButton();

            Transform cam = xrOrigin.Camera.transform;
            Vector3 from = cam.position;
            Vector3 to = new Vector3(standAnchor.position.x, standAnchor.position.y + standEyeHeight, standAnchor.position.z);
            float fromYaw = YawOf(cam.forward);
            float toYaw = YawOf(standAnchor.forward);

            // Rise out of the chair first, then step away from it.
            for (float t = 0f; t < 1f; t += Time.deltaTime / standDuration)
            {
                float rise = Phase(t, 0f, 0.6f);
                float slide = Phase(t, 0.35f, 1f);
                ApplyPose(new Vector3(Mathf.Lerp(from.x, to.x, slide), Mathf.Lerp(from.y, to.y, rise), Mathf.Lerp(from.z, to.z, slide)),
                    Mathf.LerpAngle(fromYaw, toYaw, Phase(t, 0f, 1f)));
                yield return null;
            }

            // Only now hand control back: walking input and the CharacterController stay off until we land.
            SetDesktopSeated(false);
            PlaceAtStandAnchor();
            SetLocomotionEnabled(true);
            State = SeatState.Standing;
            transition = null;
            RefreshButton();
        }

        private void StopTransition()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }
        }

        // ---------------------------------------------------------------- placement

        private void ApplyPose(Vector3 cameraPosition, float yawDegrees)
        {
            xrOrigin.MatchOriginUpCameraForward(Vector3.up, Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward);
            xrOrigin.MoveCameraToWorldLocation(cameraPosition);
        }

        private IEnumerator RealignSeatAfterFirstFrame()
        {
            yield return null;
            if (State == SeatState.Seated)
            {
                AlignCameraToSeat();
            }
        }

        private void AlignCameraToSeat()
        {
            if (xrOrigin == null || seatEyeAnchor == null)
            {
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(seatEyeAnchor.forward, Vector3.up);
            if (flatForward.sqrMagnitude > 0.0001f)
            {
                xrOrigin.MatchOriginUpCameraForward(Vector3.up, flatForward.normalized);
            }

            xrOrigin.MoveCameraToWorldLocation(seatEyeAnchor.position);
            Physics.SyncTransforms();
        }

        /// <summary>Moves the user to the clear floor spot beside the chair.</summary>
        private void PlaceAtStandAnchor()
        {
            if (xrOrigin == null || standAnchor == null)
            {
                return;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(standAnchor.forward, Vector3.up);
            if (flatForward.sqrMagnitude > 0.0001f)
            {
                xrOrigin.MatchOriginUpCameraForward(Vector3.up, flatForward.normalized);
            }

            if (xrOrigin.Camera != null)
            {
                Vector3 camOffset = xrOrigin.Camera.transform.position - xrOrigin.transform.position;
                xrOrigin.transform.position = new Vector3(
                    standAnchor.position.x - camOffset.x,
                    standAnchor.position.y,
                    standAnchor.position.z - camOffset.z);
            }
            else
            {
                xrOrigin.transform.position = standAnchor.position;
            }

            Physics.SyncTransforms();
        }

        /// <summary>While seated the head may lean a little, but cannot leave the chair (simulator, headset or otherwise).</summary>
        private void HoldHeadToSeat()
        {
            if (State != SeatState.Seated || xrOrigin == null || xrOrigin.Camera == null || seatEyeAnchor == null)
            {
                return;
            }

            Vector3 offset = xrOrigin.Camera.transform.position - seatEyeAnchor.position;
            Vector3 flat = new Vector3(offset.x, 0f, offset.z);
            Vector3 correction = Vector3.zero;

            if (flat.sqrMagnitude > seatLeanRadius * seatLeanRadius)
            {
                correction -= flat - flat.normalized * seatLeanRadius;
            }
            correction.y = Mathf.Clamp(offset.y, -seatLeanHeight, seatLeanHeight) - offset.y;

            if (correction.sqrMagnitude > 1e-8f)
            {
                xrOrigin.transform.position += correction;
            }
        }

        private void SetDesktopSeated(bool seated)
        {
            if (desktopFallback != null)
            {
                desktopFallback.SetSeated(seated);
            }
        }

        private void SetLocomotionEnabled(bool enabledState)
        {
            if (characterController != null)
            {
                characterController.enabled = enabledState;
            }
            if (locomotionBehaviours == null)
            {
                return;
            }
            for (int i = 0; i < locomotionBehaviours.Length; i++)
            {
                if (locomotionBehaviours[i] != null)
                {
                    locomotionBehaviours[i].enabled = enabledState;
                }
            }
        }

        // ---------------------------------------------------------------- input

        private void CheckDesktopMouseClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                return;
            }

            Ray ray = xrOrigin.Camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            // The button works any time; the chair only to sit down (so a stray click while seated never stands you up).
            if (Owns(seatButton, hit.collider) || (!IsSeated && Owns(chairInteractable, hit.collider)))
            {
                ToggleSeat();
            }
        }

        private static bool Owns(XRBaseInteractable interactable, Collider col)
        {
            return interactable != null && col != null && interactable.colliders.Contains(col);
        }

        private void OnChairSelectEntered(SelectEnterEventArgs _) => ToggleSeat();

        private void OnButtonSelectEntered(SelectEnterEventArgs _) => ToggleSeat();

        private void OnChairHoverEntered(HoverEnterEventArgs _) => SetHoverVisual(true);

        private void OnChairHoverExited(HoverExitEventArgs _) => SetHoverVisual(false);

        private void Subscribe(XRSimpleInteractable interactable, UnityEngine.Events.UnityAction<SelectEnterEventArgs> onSelect, bool hover)
        {
            if (interactable == null)
            {
                return;
            }
            interactable.selectEntered.AddListener(onSelect);
            if (hover)
            {
                interactable.hoverEntered.AddListener(OnChairHoverEntered);
                interactable.hoverExited.AddListener(OnChairHoverExited);
            }
        }

        private void Unsubscribe(XRSimpleInteractable interactable, UnityEngine.Events.UnityAction<SelectEnterEventArgs> onSelect, bool hover)
        {
            if (interactable == null)
            {
                return;
            }
            interactable.selectEntered.RemoveListener(onSelect);
            if (hover)
            {
                interactable.hoverEntered.RemoveListener(OnChairHoverEntered);
                interactable.hoverExited.RemoveListener(OnChairHoverExited);
            }
        }

        private bool CheckXrControllerToggle()
        {
            XrControllers.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Controller,
                XrControllers);

            bool pressed = false;
            for (int i = 0; i < XrControllers.Count; i++)
            {
                if (XrControllers[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool value) && value)
                {
                    pressed = true;
                    break;
                }
            }

            bool risingEdge = pressed && !wasXrPrimaryPressed;
            wasXrPrimaryPressed = pressed;
            return risingEdge;
        }

        private static bool IsXrHintMode()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return DesktopFallbackLocomotion.IsXRDisplayActive();
#endif
        }

        // ---------------------------------------------------------------- visuals

        private void RefreshButton()
        {
            bool canSit = State == SeatState.Standing;
            bool busy = IsTransitioning;

            if (seatButtonLabel != null)
            {
                string action = busy ? "..." : canSit ? sitLabel : standLabel;
                string hint = IsXrHintMode() ? "A / X" : toggleSeatKey.ToString();
                seatButtonLabel.text = busy ? action : $"{action}\n<size=60%>[{hint}]</size>";
            }

            if (seatButtonRenderer != null)
            {
                seatButtonRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, canSit ? sitColor : standColor);
                seatButtonRenderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void SetHoverVisual(bool highlighted)
        {
            if (chairRenderers == null || propertyBlock == null)
            {
                return;
            }
            Color color = highlighted ? hoverEmissionColor : Color.black;
            for (int i = 0; i < chairRenderers.Length; i++)
            {
                Renderer r = chairRenderers[i];
                if (r == null)
                {
                    continue;
                }
                r.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, color);
                r.SetPropertyBlock(propertyBlock);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static float Phase(float t, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

        private static float YawOf(Vector3 forward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            return flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0f;
        }
    }
}
