using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace XRFixIt.XR
{
    /// <summary>
    /// Provides non-VR desktop walking (WASD/Arrows + right-mouse look) when standing
    /// and cleanly coordinates input with the Editor's XR Interaction Simulator.
    /// </summary>
    [DisallowMultipleComponent]
    public class DesktopFallbackLocomotion : MonoBehaviour
    {
        private static readonly List<XRDisplaySubsystem> DisplaySubsystems = new List<XRDisplaySubsystem>();

        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private float moveSpeed = 1.6f;
        [SerializeField] private float lookSensitivity = 0.18f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float maxPitchAngle = 75f;
        [SerializeField] private float desktopEyeHeight = 1.62f;
        [SerializeField] private Key toggleSimulatorModeKey = Key.F1;

        private XRInteractionSimulator simulator;
        private TrackedPoseDriver cameraPoseDriver;
        private bool isSeated = true;
        private bool useSimulatorWhileStanding;
        private float verticalVelocity;
        private float pitchDegrees;

        /// <summary>
        /// True when desktop WASD/mouse-look mode is actively driving the camera and CharacterController.
        /// </summary>
        public bool IsDesktopWalkActive =>
            !IsXRDisplayActive() && !isSeated && (simulator == null || !useSimulatorWhileStanding);

        /// <summary>
        /// Returns true when any XR display subsystem is actively running (headset connected/active).
        /// </summary>
        public static bool IsXRDisplayActive()
        {
            DisplaySubsystems.Clear();
            SubsystemManager.GetSubsystems(DisplaySubsystems);
            for (int i = 0; i < DisplaySubsystems.Count; i++)
            {
                if (DisplaySubsystems[i].running)
                {
                    return true;
                }
            }
            return false;
        }

        private void Awake()
        {
            if (xrOrigin == null)
            {
                xrOrigin = GetComponent<XROrigin>();
            }
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                cameraPoseDriver = xrOrigin.Camera.GetComponent<TrackedPoseDriver>();
            }
        }

        private void Start()
        {
            simulator = FindAnyObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include);
            ApplyModeState();
        }

        /// <summary>
        /// Updates whether the user is seated and switches Simulator/Desktop input accordingly.
        /// </summary>
        public void SetSeated(bool seated)
        {
            isSeated = seated;
            verticalVelocity = 0f;
            if (simulator == null)
            {
                simulator = FindAnyObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include);
            }
            ApplyModeState();
        }

        private void Update()
        {
            if (IsXRDisplayActive())
            {
                EnsureXRTrackingActive();
                return;
            }

            if (!isSeated && simulator != null && Keyboard.current != null &&
                Keyboard.current[toggleSimulatorModeKey].wasPressedThisFrame)
            {
                useSimulatorWhileStanding = !useSimulatorWhileStanding;
                ApplyModeState();
            }

            if (!IsDesktopWalkActive)
            {
                return;
            }

            HandleMouseLook();
            HandleMovement();
        }

        private void ApplyModeState()
        {
            if (IsXRDisplayActive())
            {
                EnsureXRTrackingActive();
                return;
            }

            bool enableSimulator = isSeated || useSimulatorWhileStanding;
            if (simulator != null)
            {
                simulator.enabled = enableSimulator;
                if (simulator.interactionSimulatorUI != null)
                {
                    simulator.interactionSimulatorUI.SetActive(enableSimulator);
                }
            }

            if (cameraPoseDriver != null)
            {
                cameraPoseDriver.enabled = enableSimulator;
            }

            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                if (xrOrigin.CameraFloorOffsetObject != null)
                {
                    float targetOffsetY = xrOrigin.CameraYOffset > 0.1f ? xrOrigin.CameraYOffset : desktopEyeHeight;
                    if (!enableSimulator || targetOffsetY > 0.1f)
                    {
                        xrOrigin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, targetOffsetY, 0f);
                    }
                }

                Transform camTransform = xrOrigin.Camera.transform;
                camTransform.localPosition = Vector3.zero;

                if (!enableSimulator)
                {
                    Vector3 euler = camTransform.localEulerAngles;
                    pitchDegrees = NormalizeSignedAngle(euler.x);
                    pitchDegrees = Mathf.Clamp(pitchDegrees, -maxPitchAngle, maxPitchAngle);
                    camTransform.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
                }
            }
        }

        private void EnsureXRTrackingActive()
        {
            if (cameraPoseDriver != null && !cameraPoseDriver.enabled)
            {
                cameraPoseDriver.enabled = true;
            }
        }

        private void HandleMouseLook()
        {
            if (Mouse.current == null || !Mouse.current.rightButton.isPressed || xrOrigin == null || xrOrigin.Camera == null)
            {
                return;
            }

            Vector2 delta = Mouse.current.delta.ReadValue();
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            xrOrigin.transform.Rotate(0f, delta.x * lookSensitivity, 0f, UnityEngine.Space.World);
            pitchDegrees = Mathf.Clamp(pitchDegrees - delta.y * lookSensitivity, -maxPitchAngle, maxPitchAngle);
            xrOrigin.Camera.transform.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
        }

        private void HandleMovement()
        {
            if (characterController == null || !characterController.enabled || xrOrigin == null || xrOrigin.Camera == null)
            {
                return;
            }

            Vector2 moveInput = ReadKeyboardMoveInput();
            Transform camTransform = xrOrigin.Camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(camTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(camTransform.right, Vector3.up).normalized;
            Vector3 horizontalMove = (forward * moveInput.y + right * moveInput.x) * moveSpeed;

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -0.5f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            Vector3 velocity = horizontalMove + Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }

        private static Vector2 ReadKeyboardMoveInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return Vector2.zero;
            }

            float x = 0f;
            float y = 0f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;

            Vector2 input = new Vector2(x, y);
            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
