using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRFixIt.XR
{
    /// <summary>
    /// Controls a VR-grabbable screwdriver: hover highlighting, controller haptics,
    /// trigger-driven bit spinning while held, and automatic desk return if dropped.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class ScrewdriverTool : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("References")]
        [SerializeField] private XRGrabInteractable grabInteractable;
        [SerializeField] private Rigidbody toolRigidbody;
        [SerializeField] private Transform attachTransform;
        [SerializeField] private Transform tipTransform;
        [SerializeField] private Transform spinPivot;
        [SerializeField] private Renderer[] toolRenderers;

        [Header("Feedback & Behaviour")]
        [SerializeField] private Color hoverEmissionColor = new Color(0.18f, 0.45f, 0.70f, 1f);
        [SerializeField] private float hapticAmplitude = 0.25f;
        [SerializeField] private float hapticDuration = 0.05f;
        [SerializeField] private float spinDegreesPerSecond = 540f;

        [Header("Desk Return Safety")]
        [SerializeField] private float minWorldY = 0.25f;
        [SerializeField] private float maxDistanceFromHome = 2.5f;

        private MaterialPropertyBlock propertyBlock;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool hasHomePose;

        /// <summary>True while a VR controller or hand is holding the screwdriver.</summary>
        public bool IsHeld => grabInteractable != null && grabInteractable.isSelected;

        /// <summary>True while the player holds Activate (trigger) while holding the screwdriver.</summary>
        public bool IsSpinning { get; private set; }

        /// <summary>World-space transform at the very tip of the screwdriver bit (+Z points forward).</summary>
        public Transform TipTransform => tipTransform;

        /// <summary>Ergonomic grip point aligned to the screwdriver shaft.</summary>
        public Transform AttachTransform => attachTransform;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (grabInteractable == null) grabInteractable = GetComponent<XRGrabInteractable>();
            if (toolRigidbody == null) toolRigidbody = GetComponent<Rigidbody>();
            if (toolRenderers == null || toolRenderers.Length == 0)
            {
                toolRenderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private void Start()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            hasHomePose = true;
        }

        private void OnEnable()
        {
            if (grabInteractable == null) return;
            grabInteractable.hoverEntered.AddListener(OnHoverEntered);
            grabInteractable.hoverExited.AddListener(OnHoverExited);
            grabInteractable.selectEntered.AddListener(OnSelectEntered);
            grabInteractable.selectExited.AddListener(OnSelectExited);
            grabInteractable.activated.AddListener(OnActivated);
            grabInteractable.deactivated.AddListener(OnDeactivated);
        }

        private void OnDisable()
        {
            IsSpinning = false;
            SetHoverHighlight(false);

            if (grabInteractable == null) return;
            grabInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            grabInteractable.hoverExited.RemoveListener(OnHoverExited);
            grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            grabInteractable.selectExited.RemoveListener(OnSelectExited);
            grabInteractable.activated.RemoveListener(OnActivated);
            grabInteractable.deactivated.RemoveListener(OnDeactivated);
        }

        private void Update()
        {
            if (IsSpinning && IsHeld && spinPivot != null)
            {
                spinPivot.Rotate(Vector3.forward, spinDegreesPerSecond * Time.deltaTime, Space.Self);
                return;
            }

            if (hasHomePose && !IsHeld)
            {
                bool fellBelowDesk = transform.position.y < minWorldY;
                bool driftedTooFar = (transform.position - homePosition).sqrMagnitude > maxDistanceFromHome * maxDistanceFromHome;
                if (fellBelowDesk || driftedTooFar)
                {
                    ReturnToHome();
                }
            }
        }

        /// <summary>Returns the screwdriver cleanly to its starting spot on the desk.</summary>
        public void ReturnToHome()
        {
            if (!hasHomePose) return;

            IsSpinning = false;
            if (toolRigidbody != null)
            {
                toolRigidbody.linearVelocity = Vector3.zero;
                toolRigidbody.angularVelocity = Vector3.zero;
                toolRigidbody.position = homePosition;
                toolRigidbody.rotation = homeRotation;
            }
            transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!IsHeld)
            {
                SetHoverHighlight(true);
            }
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (grabInteractable == null || !grabInteractable.isHovered)
            {
                SetHoverHighlight(false);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            SetHoverHighlight(false);
            SendHaptic(args.interactorObject);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            IsSpinning = false;
            SendHaptic(args.interactorObject);
        }

        private void OnActivated(ActivateEventArgs args)
        {
            if (IsHeld)
            {
                IsSpinning = true;
            }
        }

        private void OnDeactivated(DeactivateEventArgs args)
        {
            IsSpinning = false;
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
            if (toolRenderers == null || propertyBlock == null) return;

            Color emission = highlighted ? hoverEmissionColor : Color.black;
            for (int i = 0; i < toolRenderers.Length; i++)
            {
                Renderer r = toolRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
