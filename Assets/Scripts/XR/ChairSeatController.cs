using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRFixIt.XR
{
    /// <summary>
    /// Manages seated/standing state for the XR Origin via VR pointer selection on the chair,
    /// desktop mouse click, or a configurable keyboard shortcut while keeping 6DoF head/hand tracking active.
    /// </summary>
    [DisallowMultipleComponent]
    public class ChairSeatController : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private XRSimpleInteractable chairInteractable;
        [SerializeField] private Transform seatEyeAnchor;
        [SerializeField] private Transform standAnchor;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Behaviour[] locomotionBehaviours;
        [SerializeField] private DesktopFallbackLocomotion desktopFallback;
        [SerializeField] private Renderer[] chairRenderers;

        [SerializeField] private bool startSeated = true;
        [SerializeField] private Key toggleSeatKey = Key.F;
        [SerializeField] private float toggleCooldown = 0.35f;
        [SerializeField] private bool allowDesktopMouseClickOnChair = true;
        [SerializeField] private Color hoverEmissionColor = new Color(0.15f, 0.45f, 0.65f, 1f);

        private MaterialPropertyBlock hoverPropertyBlock;
        private float lastToggleTime = -10f;

        /// <summary>
        /// True when the user is currently seated at the desk chair.
        /// </summary>
        public bool IsSeated { get; private set; }

        private void Awake()
        {
            hoverPropertyBlock = new MaterialPropertyBlock();
            if (chairInteractable == null)
            {
                chairInteractable = GetComponent<XRSimpleInteractable>();
            }
            if (xrOrigin == null)
            {
                xrOrigin = FindAnyObjectByType<XROrigin>();
            }
            if (characterController == null && xrOrigin != null)
            {
                characterController = xrOrigin.GetComponent<CharacterController>();
            }
            if (desktopFallback == null && xrOrigin != null)
            {
                desktopFallback = xrOrigin.GetComponent<DesktopFallbackLocomotion>();
            }
        }

        private void OnEnable()
        {
            if (chairInteractable == null)
            {
                return;
            }
            chairInteractable.selectEntered.AddListener(OnChairSelectEntered);
            chairInteractable.hoverEntered.AddListener(OnChairHoverEntered);
            chairInteractable.hoverExited.AddListener(OnChairHoverExited);
        }

        private void OnDisable()
        {
            if (chairInteractable == null)
            {
                return;
            }
            chairInteractable.selectEntered.RemoveListener(OnChairSelectEntered);
            chairInteractable.hoverEntered.RemoveListener(OnChairHoverEntered);
            chairInteractable.hoverExited.RemoveListener(OnChairHoverExited);
            SetHoverVisual(false);
        }

        private void Start()
        {
            if (startSeated)
            {
                SitDown();
            }
            else
            {
                StandUp();
            }
        }

        private IEnumerator RealignSeatAfterFirstFrame()
        {
            yield return null;
            if (IsSeated)
            {
                AlignCameraToSeat();
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[toggleSeatKey].wasPressedThisFrame)
            {
                ToggleSeat();
                return;
            }

            if (allowDesktopMouseClickOnChair && !DesktopFallbackLocomotion.IsXRDisplayActive() &&
                (desktopFallback == null || desktopFallback.IsDesktopWalkActive))
            {
                CheckDesktopMouseChairClick();
            }
        }

        /// <summary>
        /// Toggles between seated and standing states, respecting the toggle cooldown.
        /// </summary>
        public void ToggleSeat()
        {
            if (Time.unscaledTime - lastToggleTime < toggleCooldown)
            {
                return;
            }
            lastToggleTime = Time.unscaledTime;

            if (IsSeated)
            {
                StandUp();
            }
            else
            {
                SitDown();
            }
        }

        /// <summary>
        /// Places the user in the seated viewpoint while keeping 6DoF head and hand tracking active.
        /// </summary>
        public void SitDown()
        {
            IsSeated = true;
            SetLocomotionEnabled(false);
            if (desktopFallback != null)
            {
                desktopFallback.SetSeated(true);
            }
            AlignCameraToSeat();
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(RealignSeatAfterFirstFrame());
            }
        }

        /// <summary>
        /// Moves the user to the clear floor spot beside the chair and enables locomotion.
        /// </summary>
        public void StandUp()
        {
            IsSeated = false;
            SetLocomotionEnabled(false);

            if (desktopFallback != null)
            {
                desktopFallback.SetSeated(false);
            }

            if (xrOrigin != null && standAnchor != null)
            {
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

            SetLocomotionEnabled(true);
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

        private void CheckDesktopMouseChairClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            if (xrOrigin == null || xrOrigin.Camera == null || chairInteractable == null)
            {
                return;
            }

            Ray ray = xrOrigin.Camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                IsChairCollider(hit.collider))
            {
                ToggleSeat();
            }
        }

        private bool IsChairCollider(Collider col)
        {
            if (col == null || chairInteractable == null)
            {
                return false;
            }
            var cols = chairInteractable.colliders;
            for (int i = 0; i < cols.Count; i++)
            {
                if (cols[i] == col)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnChairSelectEntered(SelectEnterEventArgs _)
        {
            ToggleSeat();
        }

        private void OnChairHoverEntered(HoverEnterEventArgs _)
        {
            SetHoverVisual(true);
        }

        private void OnChairHoverExited(HoverExitEventArgs _)
        {
            SetHoverVisual(false);
        }

        private void SetHoverVisual(bool highlighted)
        {
            if (chairRenderers == null || hoverPropertyBlock == null)
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
                r.GetPropertyBlock(hoverPropertyBlock);
                hoverPropertyBlock.SetColor(EmissionColorId, color);
                r.SetPropertyBlock(hoverPropertyBlock);
            }
        }
    }
}
