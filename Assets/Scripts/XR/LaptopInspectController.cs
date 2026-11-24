using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRFixIt.XR
{
    /// <summary>Inspection phase for the desk laptop.</summary>
    public enum LaptopInspectState
    {
        OnDesk,
        MovingToInspect,
        Inspecting,
        ReturningToDesk
    }

    /// <summary>
    /// Lets the user click the laptop on the desk to float it in front of their view facing them,
    /// rotate it with controller thumbsticks (or mouse/arrows on desktop), zoom in/out, and return it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class LaptopInspectController : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly List<UnityEngine.XR.InputDevice> XrControllers = new List<UnityEngine.XR.InputDevice>();

        [Header("References")]
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private XRSimpleInteractable interactable;
        [SerializeField] private Renderer[] laptopRenderers;

        [Header("Inspect Placement")]
        [SerializeField] private float inspectDistance = 0.55f;
        [SerializeField] private float inspectHeightOffset = -0.06f;
        [SerializeField] private float minInspectY = 0.90f;
        [SerializeField] private float transitionDuration = 0.55f;

        [Header("Rotate & Zoom")]
        [SerializeField] private float rotateSpeed = 110f;
        [SerializeField] private float zoomSpeed = 0.9f;
        [SerializeField] private float minZoomScale = 0.75f;
        [SerializeField] private float maxZoomScale = 2.2f;
        [SerializeField] private float mouseRotateSensitivity = 0.35f;
        [SerializeField] private float scrollZoomStep = 0.12f;

        [Header("Input & Feedback")]
        [SerializeField] private bool clickAgainToReturn = false;
        [SerializeField] private Color hoverEmissionColor = new Color(0.12f, 0.38f, 0.58f, 1f);
        [SerializeField] private float hapticAmplitude = 0.22f;
        [SerializeField] private float hapticDuration = 0.04f;

        private MaterialPropertyBlock propertyBlock;
        private Collider ownCollider;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 homeScale;
        private bool hasHomePose;
        private Coroutine transition;
        private float currentZoomScale = 1f;
        private bool wasSecondaryPressed;
        private bool dragStartedOnLaptop;
        private float leftDragDistance;

        /// <summary>Current phase of the laptop inspection cycle.</summary>
        public LaptopInspectState State { get; private set; } = LaptopInspectState.OnDesk;

        /// <summary>True once the laptop has floated in front of the user and can be rotated/zoomed.</summary>
        public bool IsInspecting => State == LaptopInspectState.Inspecting;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            ownCollider = GetComponent<Collider>();
            if (laptopRenderers == null || laptopRenderers.Length == 0)
            {
                laptopRenderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private void Start()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            homeScale = transform.localScale;
            hasHomePose = true;
        }

        private void OnEnable()
        {
            if (interactable == null) return;
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            interactable.selectEntered.AddListener(OnSelectEntered);
        }

        private void OnDisable()
        {
            StopTransition();
            SetHoverHighlight(false);

            if (interactable == null) return;
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void Update()
        {
            if (State == LaptopInspectState.Inspecting)
            {
                HandleXrInspectInput();
            }

            if (!DesktopFallbackLocomotion.IsXRDisplayActive())
            {
                HandleDesktopInput();
            }
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Toggles between floating inspect mode and returning to the desk.</summary>
        public void ToggleInspect()
        {
            if (State == LaptopInspectState.OnDesk) EnterInspectMode();
            else if (State == LaptopInspectState.Inspecting) ReturnToDesk();
        }

        /// <summary>Floats the laptop in front of the user facing them.</summary>
        public void EnterInspectMode()
        {
            if (State != LaptopInspectState.OnDesk) return;

            Camera cam = ResolveCamera();
            if (cam == null) return;

            SetHoverHighlight(false);
            if (ownCollider != null) ownCollider.enabled = false;
            ComputeInspectPose(cam.transform, out Vector3 targetPos, out Quaternion targetRot);
            currentZoomScale = 1f;

            StopTransition();
            transition = StartCoroutine(TransitionRoutine(
                LaptopInspectState.MovingToInspect,
                LaptopInspectState.Inspecting,
                targetPos,
                targetRot,
                homeScale));
        }

        /// <summary>Smoothly returns the laptop to its saved desk pose and scale.</summary>
        public void ReturnToDesk()
        {
            if (!hasHomePose || (State != LaptopInspectState.Inspecting && State != LaptopInspectState.MovingToInspect))
            {
                return;
            }

            StopTransition();
            transition = StartCoroutine(TransitionRoutine(
                LaptopInspectState.ReturningToDesk,
                LaptopInspectState.OnDesk,
                homePosition,
                homeRotation,
                homeScale));
        }

        /// <summary>Sets the uniform inspection zoom multiplier clamped within [minZoomScale, maxZoomScale].</summary>
        public void SetZoomScale(float scale)
        {
            currentZoomScale = Mathf.Clamp(scale, minZoomScale, maxZoomScale);
            if (hasHomePose)
            {
                transform.localScale = homeScale * currentZoomScale;
            }
        }

        // ---------------------------------------------------------------- input handling

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (State == LaptopInspectState.OnDesk)
            {
                SetHoverHighlight(true);
            }
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (interactable == null || !interactable.isHovered)
            {
                SetHoverHighlight(false);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (State == LaptopInspectState.OnDesk)
            {
                EnterInspectMode();
                SendHaptic(args.interactorObject);
            }
            else if (State == LaptopInspectState.Inspecting && clickAgainToReturn)
            {
                ReturnToDesk();
                SendHaptic(args.interactorObject);
            }
        }

        private void HandleXrInspectInput()
        {
            Vector2 rightAxis = ReadControllerThumbstick(UnityEngine.XR.InputDeviceCharacteristics.Right);
            if (rightAxis.sqrMagnitude > 0.0225f)
            {
                ApplyRotationInput(rightAxis.x * rotateSpeed * Time.deltaTime, rightAxis.y * rotateSpeed * Time.deltaTime);
            }

            Vector2 leftAxis = ReadControllerThumbstick(UnityEngine.XR.InputDeviceCharacteristics.Left);
            if (Mathf.Abs(leftAxis.y) > 0.15f)
            {
                SetZoomScale(currentZoomScale + leftAxis.y * zoomSpeed * Time.deltaTime);
            }

            bool secondaryPressed = IsSecondaryButtonPressedOnAnyController();
            if (secondaryPressed && !wasSecondaryPressed)
            {
                ReturnToDesk();
            }
            wasSecondaryPressed = secondaryPressed;
        }

        private void HandleDesktopInput()
        {
            Keyboard kb = Keyboard.current;
            if (State == LaptopInspectState.Inspecting && kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    ReturnToDesk();
                    return;
                }

                float yaw = 0f;
                float pitch = 0f;
                if (kb.leftArrowKey.isPressed) yaw += rotateSpeed * Time.deltaTime;
                if (kb.rightArrowKey.isPressed) yaw -= rotateSpeed * Time.deltaTime;
                if (kb.upArrowKey.isPressed) pitch += rotateSpeed * Time.deltaTime;
                if (kb.downArrowKey.isPressed) pitch -= rotateSpeed * Time.deltaTime;
                if (Mathf.Abs(yaw) > 1e-4f || Mathf.Abs(pitch) > 1e-4f)
                {
                    ApplyRotationInput(yaw, pitch);
                }

                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed)
                {
                    SetZoomScale(currentZoomScale + zoomSpeed * Time.deltaTime);
                }
                else if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed)
                {
                    SetZoomScale(currentZoomScale - zoomSpeed * Time.deltaTime);
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (State == LaptopInspectState.Inspecting)
            {
                float scrollY = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scrollY) > 0.01f)
                {
                    SetZoomScale(currentZoomScale + Mathf.Sign(scrollY) * scrollZoomStep);
                }

                if (mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    ApplyRotationInput(-delta.x * mouseRotateSensitivity, delta.y * mouseRotateSensitivity);
                }
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                dragStartedOnLaptop = !IsPointerBlockedByUi() && RaycastHitsLaptop(mouse.position.ReadValue());
                leftDragDistance = 0f;
            }
            else if (mouse.leftButton.isPressed && State == LaptopInspectState.Inspecting && dragStartedOnLaptop)
            {
                Vector2 delta = mouse.delta.ReadValue();
                leftDragDistance += delta.magnitude;
                if (delta.sqrMagnitude > 0.01f)
                {
                    ApplyRotationInput(-delta.x * mouseRotateSensitivity, delta.y * mouseRotateSensitivity);
                }
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                bool wasClick = dragStartedOnLaptop && leftDragDistance < 8f;
                dragStartedOnLaptop = false;
                if (wasClick && RaycastHitsLaptop(mouse.position.ReadValue()))
                {
                    if (State == LaptopInspectState.OnDesk) EnterInspectMode();
                    else if (State == LaptopInspectState.Inspecting && clickAgainToReturn) ReturnToDesk();
                }
            }
        }

        // ---------------------------------------------------------------- transforms & helpers

        private void ComputeInspectPose(Transform camTransform, out Vector3 targetPos, out Quaternion targetRot)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(camTransform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f) flatForward = Vector3.forward;
            flatForward.Normalize();

            targetPos = camTransform.position + flatForward * inspectDistance + Vector3.up * inspectHeightOffset;
            targetPos.y = Mathf.Max(targetPos.y, minInspectY);

            Vector3 toUser = (camTransform.position - targetPos).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(camTransform.right, Vector3.up);
            if (camRight.sqrMagnitude < 1e-4f) camRight = Vector3.Cross(Vector3.up, flatForward);
            camRight.Normalize();

            Vector3 forwardAlongView = Vector3.Cross(camRight, toUser);
            if (forwardAlongView.sqrMagnitude < 1e-4f)
            {
                forwardAlongView = Vector3.forward;
            }
            targetRot = Quaternion.LookRotation(forwardAlongView.normalized, toUser);
        }

        private void ApplyRotationInput(float yawDegrees, float pitchDegrees)
        {
            transform.Rotate(Vector3.up, -yawDegrees, Space.World);
            Camera cam = ResolveCamera();
            Vector3 pitchAxis = cam != null ? cam.transform.right : Vector3.right;
            transform.Rotate(pitchAxis, pitchDegrees, Space.World);
        }

        private IEnumerator TransitionRoutine(
            LaptopInspectState activeState,
            LaptopInspectState finalState,
            Vector3 targetPos,
            Quaternion targetRot,
            Vector3 targetScale)
        {
            State = activeState;
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 startScale = transform.localScale;

            float duration = Mathf.Max(0.05f, transitionDuration);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPos, targetPos, t),
                    Quaternion.Slerp(startRot, targetRot, t));
                transform.localScale = Vector3.Lerp(startScale, targetScale, t);
                yield return null;
            }

            transform.SetPositionAndRotation(targetPos, targetRot);
            transform.localScale = targetScale;
            if (finalState == LaptopInspectState.OnDesk)
            {
                currentZoomScale = 1f;
                if (ownCollider != null) ownCollider.enabled = true;
            }
            State = finalState;
            transition = null;
        }

        private void StopTransition()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }
        }

        private Camera ResolveCamera()
        {
            if (xrOrigin != null && xrOrigin.Camera != null) return xrOrigin.Camera;
            return Camera.main;
        }

        private bool RaycastHitsLaptop(Vector2 screenPos)
        {
            Camera cam = ResolveCamera();
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            return hit.collider == ownCollider || hit.transform.IsChildOf(transform);
        }

        private static bool IsPointerBlockedByUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static Vector2 ReadControllerThumbstick(UnityEngine.XR.InputDeviceCharacteristics handedness)
        {
            XrControllers.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Controller | handedness,
                XrControllers);

            for (int i = 0; i < XrControllers.Count; i++)
            {
                if (XrControllers[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis))
                {
                    return axis;
                }
            }
            return Vector2.zero;
        }

        private static bool IsSecondaryButtonPressedOnAnyController()
        {
            XrControllers.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                UnityEngine.XR.InputDeviceCharacteristics.Controller,
                XrControllers);

            for (int i = 0; i < XrControllers.Count; i++)
            {
                if (XrControllers[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool pressed) && pressed)
                {
                    return true;
                }
            }
            return false;
        }

        private void SendHaptic(IXRInteractor interactor)
        {
            if (interactor is XRBaseInputInteractor inputInteractor)
            {
                inputInteractor.SendHapticImpulse(hapticAmplitude, hapticDuration);
            }
        }

        private void SetHoverHighlight(bool highlighted)
        {
            if (laptopRenderers == null || propertyBlock == null) return;

            Color emission = highlighted ? hoverEmissionColor : Color.black;
            for (int i = 0; i < laptopRenderers.Length; i++)
            {
                Renderer r = laptopRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
