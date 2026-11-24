using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.XR;
using UnityEngine.Scripting;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Input;

#if UNITY_EDITOR
using UnityEditor;
#endif
#if USE_INPUT_SYSTEM_POSE_CONTROL
using PoseControl = UnityEngine.InputSystem.XR.PoseControl;
#else
using PoseControl = UnityEngine.XR.OpenXR.Input.PoseControl;
#endif

#if USE_STICK_CONTROL_THUMBSTICKS
using ThumbstickControl = UnityEngine.InputSystem.Controls.StickControl;
#else
using ThumbstickControl = UnityEngine.InputSystem.Controls.Vector2Control;
#endif

namespace XRFixIt.XR
{
    /// <summary>
    /// Enables native OpenXR controller bindings for PICO 4 controllers using XR_BD_controller_interaction
    /// (/interaction_profiles/bytedance/pico4_controller) so X/Y, A/B, thumbsticks, grip, and trigger map cleanly.
    /// </summary>
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(
        UiName = "PICO 4 Controller Profile",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "ByteDance",
        Desc = "Maps PICO 4 controllers via XR_BD_controller_interaction (/interaction_profiles/bytedance/pico4_controller).",
        OpenxrExtensionStrings = "XR_BD_controller_interaction",
        Version = "1.0.0",
        Category = UnityEditor.XR.OpenXR.Features.FeatureCategory.Interaction,
        FeatureId = featureId)]
#endif
    public class Pico4ControllerProfile : OpenXRInteractionFeature
    {
        public const string featureId = "com.xrfixit.openxr.feature.input.pico4";
        public const string profile = "/interaction_profiles/bytedance/pico4_controller";

        private const string kDeviceLocalizedName = "PICO 4 Controller OpenXR";

        [Preserve, InputControlLayout(displayName = "PICO 4 Controller (OpenXR)", commonUsages = new[] { "LeftHand", "RightHand" })]
        public class Pico4Controller : XRControllerWithRumble
        {
            [Preserve, InputControl(aliases = new[] { "Primary2DAxis", "Joystick" }, usage = "Primary2DAxis")]
            public ThumbstickControl thumbstick { get; private set; }

            [Preserve, InputControl(aliases = new[] { "GripAxis", "squeeze" }, usage = "Grip")]
            public AxisControl grip { get; private set; }

            [Preserve, InputControl(aliases = new[] { "GripButton", "squeezeClicked" }, usage = "GripButton")]
            public ButtonControl gripPressed { get; private set; }

            [Preserve, InputControl(aliases = new[] { "Primary", "menuButton", "systemButton" }, usage = "MenuButton")]
            public ButtonControl menu { get; private set; }

            [Preserve, InputControl(aliases = new[] { "A", "X", "buttonA", "buttonX" }, usage = "PrimaryButton")]
            public ButtonControl primaryButton { get; private set; }

            [Preserve, InputControl(aliases = new[] { "ATouched", "XTouched", "ATouch", "XTouch", "buttonATouched", "buttonXTouched" }, usage = "PrimaryTouch")]
            public ButtonControl primaryTouched { get; private set; }

            [Preserve, InputControl(aliases = new[] { "B", "Y", "buttonB", "buttonY" }, usage = "SecondaryButton")]
            public ButtonControl secondaryButton { get; private set; }

            [Preserve, InputControl(aliases = new[] { "BTouched", "YTouched", "BTouch", "YTouch", "buttonBTouched", "buttonYTouched" }, usage = "SecondaryTouch")]
            public ButtonControl secondaryTouched { get; private set; }

            [Preserve, InputControl(usage = "Trigger")]
            public AxisControl trigger { get; private set; }

            [Preserve, InputControl(aliases = new[] { "indexButton", "indexTouched", "triggerbutton" }, usage = "TriggerButton")]
            public ButtonControl triggerPressed { get; private set; }

            [Preserve, InputControl(aliases = new[] { "indexTouch", "indexNearTouched" }, usage = "TriggerTouch")]
            public ButtonControl triggerTouched { get; private set; }

            [Preserve, InputControl(aliases = new[] { "JoystickOrPadPressed", "thumbstickClick", "joystickClicked" }, usage = "Primary2DAxisClick")]
            public ButtonControl thumbstickClicked { get; private set; }

            [Preserve, InputControl(aliases = new[] { "JoystickOrPadTouched", "thumbstickTouch", "joystickTouched" }, usage = "Primary2DAxisTouch")]
            public ButtonControl thumbstickTouched { get; private set; }

            [Preserve, InputControl(offset = 0, aliases = new[] { "device", "gripPose" }, usage = "Device")]
            public PoseControl devicePose { get; private set; }

            [Preserve, InputControl(offset = 0, alias = "aimPose", usage = "Pointer")]
            public PoseControl pointer { get; private set; }

            [Preserve, InputControl(offset = 28, usage = "IsTracked")]
            new public ButtonControl isTracked { get; private set; }

            [Preserve, InputControl(offset = 32, usage = "TrackingState")]
            new public IntegerControl trackingState { get; private set; }

            [Preserve, InputControl(offset = 36, noisy = true, alias = "gripPosition")]
            new public Vector3Control devicePosition { get; private set; }

            [Preserve, InputControl(offset = 48, noisy = true, alias = "gripOrientation")]
            new public QuaternionControl deviceRotation { get; private set; }

            [Preserve, InputControl(offset = 96)]
            public Vector3Control pointerPosition { get; private set; }

            [Preserve, InputControl(offset = 108, alias = "pointerOrientation")]
            public QuaternionControl pointerRotation { get; private set; }

            [Preserve, InputControl(usage = "Haptic")]
            public HapticControl haptic { get; private set; }

            protected override void FinishSetup()
            {
                base.FinishSetup();
                thumbstick = GetChildControl<ThumbstickControl>("thumbstick");
                trigger = GetChildControl<AxisControl>("trigger");
                triggerPressed = GetChildControl<ButtonControl>("triggerPressed");
                triggerTouched = GetChildControl<ButtonControl>("triggerTouched");
                grip = GetChildControl<AxisControl>("grip");
                gripPressed = GetChildControl<ButtonControl>("gripPressed");
                menu = GetChildControl<ButtonControl>("menu");
                primaryButton = GetChildControl<ButtonControl>("primaryButton");
                primaryTouched = GetChildControl<ButtonControl>("primaryTouched");
                secondaryButton = GetChildControl<ButtonControl>("secondaryButton");
                secondaryTouched = GetChildControl<ButtonControl>("secondaryTouched");
                thumbstickClicked = GetChildControl<ButtonControl>("thumbstickClicked");
                thumbstickTouched = GetChildControl<ButtonControl>("thumbstickTouched");

                devicePose = GetChildControl<PoseControl>("devicePose");
                pointer = GetChildControl<PoseControl>("pointer");

                isTracked = GetChildControl<ButtonControl>("isTracked");
                trackingState = GetChildControl<IntegerControl>("trackingState");
                devicePosition = GetChildControl<Vector3Control>("devicePosition");
                deviceRotation = GetChildControl<QuaternionControl>("deviceRotation");
                pointerPosition = GetChildControl<Vector3Control>("pointerPosition");
                pointerRotation = GetChildControl<QuaternionControl>("pointerRotation");

                haptic = GetChildControl<HapticControl>("haptic");
            }
        }

        protected override void RegisterDeviceLayout()
        {
            UnityEngine.InputSystem.InputSystem.RegisterLayout(typeof(Pico4Controller),
                matches: new InputDeviceMatcher()
                    .WithInterface(XRUtilities.InterfaceMatchAnyVersion)
                    .WithProduct(kDeviceLocalizedName));
        }

        protected override void UnregisterDeviceLayout()
        {
            UnityEngine.InputSystem.InputSystem.RemoveLayout(nameof(Pico4Controller));
        }

        protected override string GetDeviceLayoutName()
        {
            return nameof(Pico4Controller);
        }

        protected override void RegisterActionMapsWithRuntime()
        {
            ActionMapConfig actionMap = new ActionMapConfig()
            {
                name = "pico4controller",
                localizedName = kDeviceLocalizedName,
                desiredInteractionProfile = profile,
                manufacturer = "ByteDance",
                serialNumber = "",
                deviceInfos = new List<DeviceConfig>()
                {
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left),
                        userPath = UserPaths.leftHand
                    },
                    new DeviceConfig()
                    {
                        characteristics = (InputDeviceCharacteristics)(InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right),
                        userPath = UserPaths.rightHand
                    }
                },
                actions = new List<ActionConfig>()
                {
                    new ActionConfig()
                    {
                        name = "thumbstick",
                        localizedName = "Thumbstick",
                        type = ActionType.Axis2D,
                        usages = new List<string>() { "Primary2DAxis" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/thumbstick", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "grip",
                        localizedName = "Grip",
                        type = ActionType.Axis1D,
                        usages = new List<string>() { "Grip" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/squeeze/value", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "gripPressed",
                        localizedName = "Grip Pressed",
                        type = ActionType.Binary,
                        usages = new List<string>() { "GripButton" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/squeeze/value", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "menu",
                        localizedName = "Menu",
                        type = ActionType.Binary,
                        usages = new List<string>() { "MenuButton" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = "/input/menu/click",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = "/input/system/click",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "primaryButton",
                        localizedName = "Primary Button",
                        type = ActionType.Binary,
                        usages = new List<string>() { "PrimaryButton" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = "/input/x/click",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = "/input/a/click",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "primaryTouched",
                        localizedName = "Primary Touched",
                        type = ActionType.Binary,
                        usages = new List<string>() { "PrimaryTouch" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = "/input/x/touch",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = "/input/a/touch",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "secondaryButton",
                        localizedName = "Secondary Button",
                        type = ActionType.Binary,
                        usages = new List<string>() { "SecondaryButton" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = "/input/y/click",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = "/input/b/click",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "secondaryTouched",
                        localizedName = "Secondary Touched",
                        type = ActionType.Binary,
                        usages = new List<string>() { "SecondaryTouch" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding()
                            {
                                interactionPath = "/input/y/touch",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.leftHand }
                            },
                            new ActionBinding()
                            {
                                interactionPath = "/input/b/touch",
                                interactionProfileName = profile,
                                userPaths = new List<string>() { UserPaths.rightHand }
                            }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "trigger",
                        localizedName = "Trigger",
                        type = ActionType.Axis1D,
                        usages = new List<string>() { "Trigger" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/trigger/value", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "triggerPressed",
                        localizedName = "Trigger Pressed",
                        type = ActionType.Binary,
                        usages = new List<string>() { "TriggerButton" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/trigger/value", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "triggerTouched",
                        localizedName = "Trigger Touched",
                        type = ActionType.Binary,
                        usages = new List<string>() { "TriggerTouch" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/trigger/touch", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "thumbstickClicked",
                        localizedName = "Thumbstick Clicked",
                        type = ActionType.Binary,
                        usages = new List<string>() { "Primary2DAxisClick" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/thumbstick/click", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "thumbstickTouched",
                        localizedName = "Thumbstick Touched",
                        type = ActionType.Binary,
                        usages = new List<string>() { "Primary2DAxisTouch" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/thumbstick/touch", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "devicePose",
                        localizedName = "Device Pose",
                        type = ActionType.Pose,
                        usages = new List<string>() { "Device" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/grip/pose", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "pointer",
                        localizedName = "Pointer Pose",
                        type = ActionType.Pose,
                        usages = new List<string>() { "Pointer" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/input/aim/pose", interactionProfileName = profile }
                        }
                    },
                    new ActionConfig()
                    {
                        name = "haptic",
                        localizedName = "Haptic Output",
                        type = ActionType.Vibrate,
                        usages = new List<string>() { "Haptic" },
                        bindings = new List<ActionBinding>()
                        {
                            new ActionBinding() { interactionPath = "/output/haptic", interactionProfileName = profile }
                        }
                    }
                }
            };

            AddActionMap(actionMap);
        }
    }
}
