using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRFixIt.XR
{
    /// <summary>Lifecycle state of an individual detachable laptop part or screw.</summary>
    public enum DisassemblyPartState
    {
        Installed,
        Removing,
        Removed,
        Installing
    }

    /// <summary>
    /// Represents a single screw, connector, cover, or hardware module on the laptop
    /// that can be removed to the side tray and reinstalled back into place.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class LaptopDisassemblyPart : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Identity & Rules")]
        [SerializeField] private string displayName;
        [SerializeField] private bool requiresScrewdriver;
        [SerializeField] private bool isScrew;
        [SerializeField] private LaptopDisassemblyPart[] removePrerequisites;

        [Header("Animation & Tray")]
        [SerializeField] private float liftDistance = 0.045f;
        [SerializeField] private float animDuration = 0.42f;
        [SerializeField] private float trayScale = 1f;

        [Header("Wiring")]
        [SerializeField] private LaptopDisassemblyManager manager;
        [SerializeField] private XRSimpleInteractable interactable;
        [SerializeField] private Renderer[] partRenderers;

        [Header("Hover Colors")]
        [SerializeField] private Color readyEmissionColor = new Color(0.12f, 0.52f, 0.65f, 1f);
        [SerializeField] private Color lockedEmissionColor = new Color(0.55f, 0.18f, 0.08f, 1f);

        private MaterialPropertyBlock propertyBlock;
        private Transform homeParent;
        private Vector3 homeLocalPosition;
        private Quaternion homeLocalRotation;
        private Vector3 homeLocalScale;
        private bool homeCaptured;
        private Coroutine transition;

        /// <summary>Current install/remove phase.</summary>
        public DisassemblyPartState State { get; private set; } = DisassemblyPartState.Installed;

        /// <summary>True when the part is fully seated in the laptop.</summary>
        public bool IsInstalled => State == DisassemblyPartState.Installed;

        /// <summary>True when the part is resting in the removed-parts tray.</summary>
        public bool IsRemoved => State == DisassemblyPartState.Removed;

        /// <summary>True while animating between laptop and tray.</summary>
        public bool IsAnimating => State == DisassemblyPartState.Removing || State == DisassemblyPartState.Installing;

        /// <summary>Human-readable part name for status prompts.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>Whether removing/installing this part requires holding the VR screwdriver.</summary>
        public bool RequiresScrewdriver => requiresScrewdriver;

        /// <summary>Whether this part spins like a threaded screw while animating.</summary>
        public bool IsScrew => isScrew;

        /// <summary>Parts that must already be removed before this part can be removed.</summary>
        public LaptopDisassemblyPart[] RemovePrerequisites => removePrerequisites;

        /// <summary>Uniform scale factor applied when parked in the removed-parts tray.</summary>
        public float TrayScale => trayScale;

        /// <summary>Collider belonging to this part.</summary>
        public Collider PartCollider { get; private set; }

        /// <summary>Index slot assigned inside the removed-parts tray (-1 when installed).</summary>
        public int TraySlotIndex { get; set; } = -1;

        /// <summary>World-space center of this part's collider (or transform).</summary>
        public Vector3 WorldCenter => PartCollider != null ? PartCollider.bounds.center : transform.position;

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            if (manager == null) manager = GetComponentInParent<LaptopDisassemblyManager>();
            PartCollider = GetComponent<Collider>();
            if (partRenderers == null || partRenderers.Length == 0)
            {
                partRenderers = GetComponentsInChildren<Renderer>(true);
            }
            CaptureHomePose();
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
            SetEmission(Color.black);
            if (interactable == null) return;
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        /// <summary>Updates collider availability (active only while laptop inspection is active).</summary>
        public void SetInteractiveEnabled(bool enabledState)
        {
            if (PartCollider != null)
            {
                PartCollider.enabled = enabledState;
            }
            if (!enabledState)
            {
                SetEmission(Color.black);
            }
        }

        /// <summary>Animates lifting this part out of the laptop and parking it on the side tray.</summary>
        public void AnimateRemove(Transform trayRoot, Vector3 trayLocalPos, Quaternion trayLocalRot)
        {
            if (State != DisassemblyPartState.Installed || trayRoot == null) return;

            CaptureHomePose();
            StopTransition();
            transition = StartCoroutine(RemoveRoutine(trayRoot, trayLocalPos, trayLocalRot));
        }

        /// <summary>Animates returning this part from the side tray back into its exact laptop seat.</summary>
        public void AnimateInstall()
        {
            if (State != DisassemblyPartState.Removed) return;

            StopTransition();
            transition = StartCoroutine(InstallRoutine());
        }

        private IEnumerator RemoveRoutine(Transform trayRoot, Vector3 trayLocalPos, Quaternion trayLocalRot)
        {
            State = DisassemblyPartState.Removing;
            SetEmission(Color.black);

            Transform laptopRoot = trayRoot.parent != null ? trayRoot.parent : homeParent;
            Vector3 liftOffsetWorld = laptopRoot.up * liftDistance;

            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 liftedPos = startPos + liftOffsetWorld;

            float liftPhaseDuration = Mathf.Max(0.08f, animDuration * 0.45f);
            for (float elapsed = 0f; elapsed < liftPhaseDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / liftPhaseDuration));
                transform.position = Vector3.Lerp(startPos, liftedPos, t);
                if (isScrew)
                {
                    transform.rotation = startRot * Quaternion.AngleAxis(-540f * t, Vector3.up);
                }
                yield return null;
            }

            transform.SetParent(trayRoot, true);
            Vector3 trayStartLocalPos = transform.localPosition;
            Quaternion trayStartLocalRot = transform.localRotation;
            Vector3 trayStartLocalScale = transform.localScale;
            Vector3 trayTargetLocalScale = Vector3.one * trayScale;

            float movePhaseDuration = Mathf.Max(0.1f, animDuration * 0.55f);
            for (float elapsed = 0f; elapsed < movePhaseDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / movePhaseDuration));
                transform.localPosition = Vector3.Lerp(trayStartLocalPos, trayLocalPos, t);
                transform.localRotation = Quaternion.Slerp(trayStartLocalRot, trayLocalRot, t);
                transform.localScale = Vector3.Lerp(trayStartLocalScale, trayTargetLocalScale, t);
                yield return null;
            }

            transform.localPosition = trayLocalPos;
            transform.localRotation = trayLocalRot;
            transform.localScale = trayTargetLocalScale;
            State = DisassemblyPartState.Removed;
            transition = null;
        }

        private IEnumerator InstallRoutine()
        {
            State = DisassemblyPartState.Installing;
            SetEmission(Color.black);

            transform.SetParent(homeParent, true);
            Vector3 startLocalPos = transform.localPosition;
            Quaternion startLocalRot = transform.localRotation;
            Vector3 startLocalScale = transform.localScale;
            Vector3 approachLocalPos = homeLocalPosition + Vector3.up * (liftDistance * 0.6f);

            float moveDuration = Mathf.Max(0.1f, animDuration * 0.55f);
            for (float elapsed = 0f; elapsed < moveDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
                transform.localPosition = Vector3.Lerp(startLocalPos, approachLocalPos, t);
                transform.localRotation = Quaternion.Slerp(startLocalRot, homeLocalRotation, t);
                transform.localScale = Vector3.Lerp(startLocalScale, homeLocalScale, t);
                yield return null;
            }

            float seatDuration = Mathf.Max(0.08f, animDuration * 0.45f);
            for (float elapsed = 0f; elapsed < seatDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seatDuration));
                transform.localPosition = Vector3.Lerp(approachLocalPos, homeLocalPosition, t);
                transform.localRotation = isScrew
                    ? homeLocalRotation * Quaternion.AngleAxis(540f * (1f - t), Vector3.up)
                    : homeLocalRotation;
                yield return null;
            }

            transform.localPosition = homeLocalPosition;
            transform.localRotation = homeLocalRotation;
            transform.localScale = homeLocalScale;
            State = DisassemblyPartState.Installed;
            transition = null;
        }

        private void CaptureHomePose()
        {
            if (homeCaptured) return;
            homeParent = transform.parent;
            homeLocalPosition = transform.localPosition;
            homeLocalRotation = transform.localRotation;
            homeLocalScale = transform.localScale;
            homeCaptured = true;
        }

        private void StopTransition()
        {
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (IsAnimating || manager == null) return;
            bool actionable = State == DisassemblyPartState.Installed ? manager.CanRemove(this) : manager.CanInstall(this);
            SetEmission(actionable ? readyEmissionColor : lockedEmissionColor);
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (interactable == null || !interactable.isHovered)
            {
                SetEmission(Color.black);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (manager != null)
            {
                manager.TryInteract(this, args.interactorObject);
            }
        }

        private void SetEmission(Color emission)
        {
            if (partRenderers == null || propertyBlock == null) return;
            for (int i = 0; i < partRenderers.Length; i++)
            {
                Renderer r = partRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
