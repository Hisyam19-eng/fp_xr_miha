using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace XRFixIt.XR
{
    /// <summary>
    /// Coordinates laptop disassembly and reassembly: enforces prerequisite dependencies,
    /// manages the side tray layout, supports physical VR screwdriver touch + XR/desktop clicks,
    /// and updates the floating inspection HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public class LaptopDisassemblyManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LaptopInspectController inspectController;
        [SerializeField] private ScrewdriverTool screwdriver;
        [SerializeField] private Transform removedPartsTray;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private LaptopDisassemblyPart[] parts;

        [Header("Physical Screwdriver Detection")]
        [SerializeField] private float screwTouchRadius = 0.032f;
        [SerializeField] private float screwTouchCooldown = 0.6f;

        [Header("Tray Grid Layout")]
        [SerializeField] private int trayColumns = 3;
        [SerializeField] private float traySpacingX = 0.095f;
        [SerializeField] private float traySpacingZ = 0.085f;
        [SerializeField] private float hapticAmplitude = 0.30f;
        [SerializeField] private float hapticDuration = 0.06f;

        private readonly List<LaptopDisassemblyPart> traySlots = new List<LaptopDisassemblyPart>();
        private float lastScrewTouchTime = -10f;
        private bool wasInspecting;
        private bool desktopClickStartedOnPart;
        private float desktopDragDistance;
        private string lastFeedbackMessage = "Klik komponen atau pakai obeng pada sekrup";

        private void Awake()
        {
            if (inspectController == null) inspectController = GetComponent<LaptopInspectController>();
            if (screwdriver == null) screwdriver = FindAnyObjectByType<ScrewdriverTool>();
            if (parts == null || parts.Length == 0)
            {
                parts = GetComponentsInChildren<LaptopDisassemblyPart>(true);
            }
        }

        private void Start()
        {
            SetPartsInteractive(false);
            RefreshHud();
        }

        private void Update()
        {
            bool isInspecting = inspectController != null && inspectController.IsInspecting;
            if (isInspecting != wasInspecting)
            {
                wasInspecting = isInspecting;
                SetPartsInteractive(isInspecting);
                RefreshHud();
            }

            if (!isInspecting) return;

            CheckPhysicalScrewdriverTouch();

            if (!DesktopFallbackLocomotion.IsXRDisplayActive())
            {
                HandleDesktopPartClick();
            }
        }

        // ---------------------------------------------------------------- public validation & actions

        /// <summary>True when <paramref name="part"/> has no blocking upper parts and meets VR screwdriver rules.</summary>
        public bool CanRemove(LaptopDisassemblyPart part, out string reason)
        {
            reason = null;
            if (part == null || !part.IsInstalled) return false;

            if (inspectController != null && !inspectController.IsInspecting)
            {
                reason = "Klik laptop terlebih dahulu untuk membongkar";
                return false;
            }

            LaptopDisassemblyPart[] prereqs = part.RemovePrerequisites;
            if (prereqs != null)
            {
                for (int i = 0; i < prereqs.Length; i++)
                {
                    LaptopDisassemblyPart req = prereqs[i];
                    if (req != null && !req.IsRemoved)
                    {
                        reason = $"Lepas {req.DisplayName} dulu";
                        return false;
                    }
                }
            }

            if (part.RequiresScrewdriver && DesktopFallbackLocomotion.IsXRDisplayActive())
            {
                if (screwdriver == null) screwdriver = FindAnyObjectByType<ScrewdriverTool>();
                if (screwdriver == null || !screwdriver.IsHeld)
                {
                    reason = "Pegang obeng dulu untuk melepas sekrup";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Convenience overload when caller only needs the boolean check.</summary>
        public bool CanRemove(LaptopDisassemblyPart part) => CanRemove(part, out _);

        /// <summary>True when all underlayers beneath <paramref name="part"/> are installed and nothing covers it.</summary>
        public bool CanInstall(LaptopDisassemblyPart part, out string reason)
        {
            reason = null;
            if (part == null || !part.IsRemoved) return false;

            // Upper parts listed as remove prerequisites must stay removed while seating this inner part.
            LaptopDisassemblyPart[] prereqs = part.RemovePrerequisites;
            if (prereqs != null)
            {
                for (int i = 0; i < prereqs.Length; i++)
                {
                    LaptopDisassemblyPart req = prereqs[i];
                    if (req != null && !req.IsRemoved)
                    {
                        reason = $"Lepas {req.DisplayName} dulu";
                        return false;
                    }
                }
            }

            // Inner parts that depended on this part must be installed before this part goes back over them.
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    LaptopDisassemblyPart candidate = parts[i];
                    if (candidate == null || candidate == part || candidate.IsInstalled) continue;
                    LaptopDisassemblyPart[] candidateReqs = candidate.RemovePrerequisites;
                    if (candidateReqs == null) continue;

                    for (int j = 0; j < candidateReqs.Length; j++)
                    {
                        if (candidateReqs[j] == part)
                        {
                            reason = $"Pasang {candidate.DisplayName} dulu";
                            return false;
                        }
                    }
                }
            }

            if (part.RequiresScrewdriver && DesktopFallbackLocomotion.IsXRDisplayActive())
            {
                if (screwdriver == null) screwdriver = FindAnyObjectByType<ScrewdriverTool>();
                if (screwdriver == null || !screwdriver.IsHeld)
                {
                    reason = "Pegang obeng dulu untuk mengencangkan sekrup";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Convenience overload when caller only needs the boolean check.</summary>
        public bool CanInstall(LaptopDisassemblyPart part) => CanInstall(part, out _);

        /// <summary>Attempts to remove or reinstall <paramref name="part"/> and updates HUD/haptics.</summary>
        public bool TryInteract(LaptopDisassemblyPart part, IXRInteractor interactor = null)
        {
            if (part == null || part.IsAnimating || (inspectController != null && !inspectController.IsInspecting))
            {
                return false;
            }

            if (part.IsInstalled)
            {
                if (!CanRemove(part, out string reason))
                {
                    SetFeedback(reason);
                    return false;
                }

                int slot = AllocateTraySlot(part);
                ComputeTraySlotLocalPose(slot, out Vector3 localPos, out Quaternion localRot);
                part.AnimateRemove(removedPartsTray, localPos, localRot);
                SendHaptic(interactor);
                SetFeedback($"Dilepas: {part.DisplayName}");
                return true;
            }

            if (part.IsRemoved)
            {
                if (!CanInstall(part, out string reason))
                {
                    SetFeedback(reason);
                    return false;
                }

                ReleaseTraySlot(part);
                part.AnimateInstall();
                SendHaptic(interactor);
                SetFeedback($"Dipasang: {part.DisplayName}");
                return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- input & screwdriver proximity

        private void CheckPhysicalScrewdriverTouch()
        {
            if (Time.unscaledTime - lastScrewTouchTime < screwTouchCooldown) return;
            if (screwdriver == null) screwdriver = FindAnyObjectByType<ScrewdriverTool>();
            if (screwdriver == null || !screwdriver.IsHeld || screwdriver.TipTransform == null) return;
            if (parts == null) return;

            Vector3 tipPos = screwdriver.TipTransform.position;
            float bestDistSq = screwTouchRadius * screwTouchRadius;
            LaptopDisassemblyPart closestScrew = null;

            for (int i = 0; i < parts.Length; i++)
            {
                LaptopDisassemblyPart part = parts[i];
                if (part == null || !part.IsScrew || part.IsAnimating) continue;
                bool actionable = part.IsInstalled ? CanRemove(part) : CanInstall(part);
                if (!actionable) continue;

                float distSq = (part.WorldCenter - tipPos).sqrMagnitude;
                if (distSq <= bestDistSq)
                {
                    bestDistSq = distSq;
                    closestScrew = part;
                }
            }

            if (closestScrew != null && TryInteract(closestScrew))
            {
                lastScrewTouchTime = Time.unscaledTime;
            }
        }

        private void HandleDesktopPartClick()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                desktopClickStartedOnPart = !IsPointerBlockedByUi() && TryRaycastPart(mouse.position.ReadValue(), out _);
                desktopDragDistance = 0f;
            }
            else if (mouse.leftButton.isPressed && desktopClickStartedOnPart)
            {
                desktopDragDistance += mouse.delta.ReadValue().magnitude;
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                bool wasClick = desktopClickStartedOnPart && desktopDragDistance < 6f;
                desktopClickStartedOnPart = false;
                if (wasClick && TryRaycastPart(mouse.position.ReadValue(), out LaptopDisassemblyPart hitPart))
                {
                    TryInteract(hitPart);
                }
            }
        }

        private bool TryRaycastPart(Vector2 screenPos, out LaptopDisassemblyPart part)
        {
            part = null;
            Camera cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            part = hit.collider.GetComponentInParent<LaptopDisassemblyPart>();
            return part != null && hit.transform.IsChildOf(transform);
        }

        // ---------------------------------------------------------------- tray & helpers

        private int AllocateTraySlot(LaptopDisassemblyPart part)
        {
            if (part.TraySlotIndex >= 0) return part.TraySlotIndex;

            for (int i = 0; i < traySlots.Count; i++)
            {
                if (traySlots[i] == null)
                {
                    traySlots[i] = part;
                    part.TraySlotIndex = i;
                    return i;
                }
            }

            int index = traySlots.Count;
            traySlots.Add(part);
            part.TraySlotIndex = index;
            return index;
        }

        private void ReleaseTraySlot(LaptopDisassemblyPart part)
        {
            int idx = part.TraySlotIndex;
            if (idx >= 0 && idx < traySlots.Count && traySlots[idx] == part)
            {
                traySlots[idx] = null;
            }
            part.TraySlotIndex = -1;
        }

        private void ComputeTraySlotLocalPose(int slotIndex, out Vector3 localPos, out Quaternion localRot)
        {
            int cols = Mathf.Max(1, trayColumns);
            int col = slotIndex % cols;
            int row = slotIndex / cols;
            localPos = new Vector3(col * traySpacingX, 0.01f, 0.14f - row * traySpacingZ);
            localRot = Quaternion.identity;
        }

        private void SetPartsInteractive(bool interactive)
        {
            if (parts == null) return;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null)
                {
                    parts[i].SetInteractiveEnabled(interactive);
                }
            }
        }

        private void SetFeedback(string message)
        {
            lastFeedbackMessage = message;
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (statusLabel == null) return;
            bool visible = inspectController != null && inspectController.IsInspecting;
            statusLabel.gameObject.SetActive(visible);
            if (!visible) return;

            int removedCount = 0;
            int total = parts != null ? parts.Length : 0;
            for (int i = 0; i < total; i++)
            {
                if (parts[i] != null && (parts[i].IsRemoved || parts[i].State == DisassemblyPartState.Removing))
                {
                    removedCount++;
                }
            }

            string returnHint = DesktopFallbackLocomotion.IsXRDisplayActive() ? "[B/Y] Kembali" : "[Esc] Kembali";
            statusLabel.text = $"Bongkar: {removedCount}/{total}  |  {returnHint}\n<size=75%>{lastFeedbackMessage}</size>";
        }

        private static bool IsPointerBlockedByUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void SendHaptic(IXRInteractor interactor)
        {
            if (interactor is XRBaseInputInteractor inputInteractor)
            {
                inputInteractor.SendHapticImpulse(hapticAmplitude, hapticDuration);
            }
        }
    }
}
