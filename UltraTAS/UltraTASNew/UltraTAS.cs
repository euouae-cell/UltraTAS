using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using System.Collections.Generic;
using System.IO;

namespace UltraTAS
{
    [BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.0.0")]
    public class UltraTAS : BaseUnityPlugin
    {
        private class TASFrame
        {
            public Vector2 Move;
            public Vector2 Look;
            public Vector2 WheelLook;
            public bool Punch, Hook, Fire1, Fire2, Jump, Slide, Dodge, ChangeFist;
            public bool NextVariation, PreviousVariation, NextWeapon, PrevWeapon, LastWeapon;
            public bool SelectVariant1, SelectVariant2, SelectVariant3;
            public bool Pause, Stats;
            public bool Slot1, Slot2, Slot3, Slot4, Slot5, Slot6;
        }

        // ULTRAKILL PlayerInput actions recovered from Assembly-CSharp.
        private static readonly string[] PlayerInputActions =
        {
            "Move", "Look", "WheelLook", "Punch", "Hook", "Fire1", "Fire2",
            "Jump", "Slide", "Dodge", "ChangeFist", "NextVariation",
            "PreviousVariation", "NextWeapon", "PrevWeapon", "LastWeapon",
            "SelectVariant1", "SelectVariant2", "SelectVariant3", "Pause", "Stats",
            "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6"
        };

        // InputActionState findings recovered from ULTRAKILL's Assembly-CSharp.
        // InputActionState owns the resolved Input System state and transfers ownership
        // of InputBindingResolver.memory during Initialize/ClaimDataFrom.
        //
        // Lifecycle recovered:
        //   Initialize(resolver) -> ClaimDataFrom(resolver) -> AddToGlobalList()
        //   ClaimDataFrom copies maps, controls, interactions, processors, composites,
        //   totalProcessorCount and unmanaged memory, then clears resolver.memory and
        //   calls ComputeControlGroupingIfNecessary().
        //   Clone() copies managed arrays and deep-clones UnmanagedMemory.
        //   Dispose()/Destroy() disables maps, clears action/map state references,
        //   removes the state from the global list, and frees Persistent unmanaged memory.
        //
        // UnmanagedMemory is one contiguous native allocation containing:
        //   TriggerState[], InteractionState[], BindingState[], ActionMapIndices[],
        //   controlMagnitudes[], compositeMagnitudes[], controlIndexToBindingIndex[],
        //   controlGroupingAndComplexity[], actionBindingIndicesAndCounts[],
        //   actionBindingIndices[], enabledControls bitset.
        //
        // ComputeControlGroupingIfNecessary() assigns grouping IDs and composite
        // complexity/count information used when registering state-change monitors.
        // enabledControls is a bitset: controlIndex / 32 selects the word and
        // 1 << (controlIndex % 32) selects the bit.
        //
        // Device/binding behavior recovered:
        //   IsUsingDevice(device) checks explicit map device restrictions first; if any
        //   map has unrestricted devices it falls back to the resolved controls' devices.
        //   CanUseDevice(device) similarly checks explicit restrictions, then searches
        //   every binding's effectivePath with InputControlPath.TryFindControl().
        //   HasEnabledActions() simply checks each map's enabled flag.
        //
        // Binding re-resolution:
        //   PrepareForBindingReResolution() disables enabled maps/actions and, for a
        //   partial resolve, preserves active controls/interactions where still valid.
        //   FinishBindingResolution() finishes composite setup and restores action,
        //   binding, control magnitude and interaction state as appropriate.
        //
        // Action reset/enable/disable behavior recovered:
        //   ResetActionState() cancels active interactions/actions, returns the action
        //   to Waiting/Disabled, clears active control/binding/interaction state and
        //   optionally clears per-update flags on hard reset.
        //   EnableAllActions()/EnableSingleAction() enable controls, set action phases,
        //   update enabled-action counts and notify listeners.
        //   DisableAllActions()/DisableSingleAction() disable controls, reset actions,
        //   update enabled-action counts and notify listeners.
        //   EnableControls()/DisableControls() call InputManager.AddStateChangeMonitor /
        //   RemoveStateChangeMonitor using the combined map/control/binding monitor ID.
        //   Initial-state-check flags are propagated to composite parents when needed.
        //
        // Interaction/action processing recovered:
        //   StartTimeout() schedules an InputManager state-change timeout at trigger.time
        //   + seconds. StopTimeout() removes it and updates timeout bookkeeping.
        //   ProcessTimeout() marks a timer expired and calls the interaction Process().
        //   ChangePhaseOfInteraction() is the bridge from an interaction to its action;
        //   it stops timers and propagates Started/Performed/Canceled to the action.
        //   ChangePhaseOfActionInternal() copies the trigger into action state, invokes
        //   listeners, records update-step performed/canceled flags and preserves
        //   pressed/released state and magnitude.
        //
        // Value-reading behavior recovered:
        //   ReadValue() evaluates composites through InputBindingCompositeContext, then
        //   applies binding processors. ReadValue<T>() does the same for typed values.
        //   ReadValueAsObject() uses the object processor path. IsActuated() checks
        //   trigger.magnitude against a threshold. ReadValueAsButton() uses the control's
        //   pressPointOrDefault or ButtonControl's global default.
        //   Composite part reads choose the strongest matching control by magnitude and
        //   can inspect pressTime, which matters for deterministic input recording.
        //
        // Global/device behavior recovered:
        //   InputActionState instances are tracked by weak GCHandles. OnDeviceChange()
        //   can remove devices, reset action state, and request binding re-resolution.
        //   DeferredResolutionOfBindings() resolves all live maps while resolution is
        //   deferred. SaveAndResetState()/ResetGlobals() destroys the tracked states and
        //   clears global callbacks.
        //
        // IMPORTANT TAS FINDING:
        //   InputState.Change(control, value) is the low-level state-write path exposed
        //   by UnityEngine.InputSystem.LowLevel.InputState. The recovered implementation
        //   validates the control's state block, calculates the device-relative byte
        //   offset, and calls InputSystem.s_Manager.UpdateState(...). This means TAS
        //   playback can write an already-resolved game's InputControl directly instead
        //   of faking OS keyboard/mouse events. The exact PlayerInput/InputActionState
        //   instance and controls still need to be resolved in ULTRAKILL at runtime.
        //
        // IMPORTANT:
        //   InputState.Change is NOT the same thing as simply changing InputActionState
        //   fields. The normal Input System state path must run so monitors, magnitudes,
        //   interactions, composites and action phases are updated normally.
        private static readonly string[] InputActionStateComponents =
        {
            "TriggerState.phase", "TriggerState.time", "TriggerState.startTime",
            "TriggerState.magnitude", "TriggerState.mapIndex", "TriggerState.controlIndex",
            "TriggerState.bindingIndex", "TriggerState.interactionIndex",
            "TriggerState.lastPerformedInUpdate", "TriggerState.lastCanceledInUpdate",
            "TriggerState.pressedInUpdate", "TriggerState.releasedInUpdate",
            "TriggerState.isPassThrough", "TriggerState.isButton", "TriggerState.isPressed",
            "BindingState.controlStartIndex", "BindingState.controlCount",
            "BindingState.interactionStartIndex", "BindingState.interactionCount",
            "BindingState.processorStartIndex", "BindingState.processorCount",
            "BindingState.actionIndex", "BindingState.mapIndex",
            "BindingState.compositeOrCompositeBindingIndex", "BindingState.pressTime",
            "BindingState.flags", "ActionMapIndices ranges", "UnmanagedMemory arrays",
            "enabledControls bitset", "controlGroupingAndComplexity"
        };

        private readonly List<TASFrame> frames = new List<TASFrame>();
        private bool recording;
        private bool playing;
        private int playbackFrame;
        private string tasPath;

        // Resolved at runtime. We intentionally do not construct a replacement
        // InputActionState; ULTRAKILL's existing state is what must receive TAS input.
        private PlayerInput playerInput;
        private readonly Dictionary<string, InputAction> resolvedActions = new Dictionary<string, InputAction>();

        private void Awake()
        {
            tasPath = Path.Combine(Paths.ConfigPath, "ultratas.tas");
            Logger.LogInfo("========================================");
            Logger.LogInfo("UltraTAS loaded.");
            Logger.LogInfo("PlayerInput/InputActionState TAS groundwork loaded.");
            Logger.LogInfo("Tracked actions: " + PlayerInputActions.Length);
            Logger.LogInfo("F6 = start/stop recording | F7 = playback | F8 = clear");
            Logger.LogInfo("========================================");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (recording) StopRecording();
                else StartRecording();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (playing) StopPlayback();
                else StartPlayback();
            }

            if (Input.GetKeyDown(KeyCode.F8)) ClearRecording();

            if (recording) RecordFrame();
            if (playing) PlayFrame();
        }

        private bool ResolvePlayerInput()
        {
            if (playerInput == null)
            {
                playerInput = FindObjectOfType<PlayerInput>();
                if (playerInput == null)
                {
                    Logger.LogWarning("UltraTAS: no PlayerInput component found yet.");
                    return false;
                }
            }

            resolvedActions.Clear();
            if (playerInput.actions == null)
            {
                Logger.LogWarning("UltraTAS: PlayerInput has no InputActionAsset.");
                return false;
            }

            foreach (string actionName in PlayerInputActions)
            {
                InputAction action = playerInput.actions.FindAction(actionName, false);
                if (action != null)
                    resolvedActions[actionName] = action;
            }

            Logger.LogInfo("UltraTAS: resolved " + resolvedActions.Count + "/" + PlayerInputActions.Length + " PlayerInput actions.");
            return resolvedActions.Count > 0;
        }

        private void StartRecording()
        {
            playing = false;
            frames.Clear();
            playbackFrame = 0;
            ResolvePlayerInput();
            recording = true;
            Logger.LogInfo("TAS recording started.");
        }

        private void StopRecording()
        {
            recording = false;
            SaveRecording();
            Logger.LogInfo("TAS recording stopped. Frames: " + frames.Count);
        }

        private void StartPlayback()
        {
            if (frames.Count == 0)
            {
                Logger.LogWarning("Cannot start playback: no recorded frames.");
                return;
            }

            recording = false;
            if (!ResolvePlayerInput())
            {
                Logger.LogWarning("Cannot start playback: PlayerInput actions could not be resolved.");
                return;
            }

            playing = true;
            playbackFrame = 0;
            Logger.LogInfo("TAS playback started. Frames: " + frames.Count);
        }

        private void StopPlayback()
        {
            playing = false;
            Logger.LogInfo("TAS playback stopped at frame " + playbackFrame + ".");
        }

        private void ClearRecording()
        {
            frames.Clear();
            recording = false;
            playing = false;
            playbackFrame = 0;
            Logger.LogInfo("TAS recording cleared.");
        }

        private void RecordFrame()
        {
            // Temporary capture only. The legacy UnityEngine.Input API is retained as a
            // fallback while the exact PlayerInput control mapping is being resolved.
            TASFrame frame = new TASFrame
            {
                Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
                Look = Vector2.zero,
                WheelLook = Vector2.zero,
                Punch = Input.GetMouseButton(0),
                Hook = Input.GetKey(KeyCode.E),
                Fire1 = Input.GetMouseButton(0),
                Fire2 = Input.GetMouseButton(1),
                Jump = Input.GetKey(KeyCode.Space),
                Slide = Input.GetKey(KeyCode.LeftControl),
                Dodge = Input.GetKey(KeyCode.LeftShift),
                ChangeFist = Input.GetKey(KeyCode.F),
                NextVariation = Input.GetKey(KeyCode.X),
                PreviousVariation = Input.GetKey(KeyCode.Z),
                NextWeapon = Input.GetKey(KeyCode.Q),
                PrevWeapon = false,
                LastWeapon = Input.GetKey(KeyCode.R),
                SelectVariant1 = false,
                SelectVariant2 = false,
                SelectVariant3 = false,
                Pause = Input.GetKey(KeyCode.Escape),
                Stats = Input.GetKey(KeyCode.Tab),
                Slot1 = Input.GetKey(KeyCode.Alpha1),
                Slot2 = Input.GetKey(KeyCode.Alpha2),
                Slot3 = Input.GetKey(KeyCode.Alpha3),
                Slot4 = Input.GetKey(KeyCode.Alpha4),
                Slot5 = Input.GetKey(KeyCode.Alpha5),
                Slot6 = Input.GetKey(KeyCode.Alpha6)
            };

            frames.Add(frame);
        }

        private void PlayFrame()
        {
            if (playbackFrame >= frames.Count)
            {
                StopPlayback();
                return;
            }

            TASFrame frame = frames[playbackFrame];

            // This is the first actual bridge toward native Input System playback.
            // We only write controls that have been resolved from ULTRAKILL's own
            // PlayerInput. No OS-level key/mouse injection is used.
            SetVector2Action("Move", frame.Move);
            SetVector2Action("Look", frame.Look);
            SetVector2Action("WheelLook", frame.WheelLook);
            SetButtonAction("Punch", frame.Punch);
            SetButtonAction("Hook", frame.Hook);
            SetButtonAction("Fire1", frame.Fire1);
            SetButtonAction("Fire2", frame.Fire2);
            SetButtonAction("Jump", frame.Jump);
            SetButtonAction("Slide", frame.Slide);
            SetButtonAction("Dodge", frame.Dodge);
            SetButtonAction("ChangeFist", frame.ChangeFist);
            SetButtonAction("NextVariation", frame.NextVariation);
            SetButtonAction("PreviousVariation", frame.PreviousVariation);
            SetButtonAction("NextWeapon", frame.NextWeapon);
            SetButtonAction("PrevWeapon", frame.PrevWeapon);
            SetButtonAction("LastWeapon", frame.LastWeapon);
            SetButtonAction("SelectVariant1", frame.SelectVariant1);
            SetButtonAction("SelectVariant2", frame.SelectVariant2);
            SetButtonAction("SelectVariant3", frame.SelectVariant3);
            SetButtonAction("Pause", frame.Pause);
            SetButtonAction("Stats", frame.Stats);
            SetButtonAction("Slot1", frame.Slot1);
            SetButtonAction("Slot2", frame.Slot2);
            SetButtonAction("Slot3", frame.Slot3);
            SetButtonAction("Slot4", frame.Slot4);
            SetButtonAction("Slot5", frame.Slot5);
            SetButtonAction("Slot6", frame.Slot6);

            playbackFrame++;
        }

        private void SetVector2Action(string actionName, Vector2 value)
        {
            InputAction action;
            if (!resolvedActions.TryGetValue(actionName, out action)) return;

            foreach (InputControl control in action.controls)
            {
                Vector2Control vector2 = control as Vector2Control;
                if (vector2 != null)
                {
                    InputState.Change(vector2, value);
                    return;
                }
            }
        }

        private void SetButtonAction(string actionName, bool pressed)
        {
            InputAction action;
            if (!resolvedActions.TryGetValue(actionName, out action)) return;

            foreach (InputControl control in action.controls)
            {
                ButtonControl button = control as ButtonControl;
                if (button != null)
                {
                    InputState.Change(button, pressed ? 1f : 0f);
                    return;
                }

                InputControl<float> floatControl = control as InputControl<float>;
                if (floatControl != null)
                {
                    InputState.Change(floatControl, pressed ? 1f : 0f);
                    return;
                }
            }
        }

        private void SaveRecording()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(tasPath, false))
                {
                    writer.WriteLine("UltraTAS v1");
                    writer.WriteLine("Frames=" + frames.Count);

                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            frame.Move.x + "," + frame.Move.y + "," +
                            frame.Look.x + "," + frame.Look.y + "," +
                            frame.WheelLook.x + "," + frame.WheelLook.y + "," +
                            Bool(frame.Punch) + "," + Bool(frame.Hook) + "," +
                            Bool(frame.Fire1) + "," + Bool(frame.Fire2) + "," +
                            Bool(frame.Jump) + "," + Bool(frame.Slide) + "," +
                            Bool(frame.Dodge) + "," + Bool(frame.ChangeFist) + "," +
                            Bool(frame.NextVariation) + "," + Bool(frame.PreviousVariation) + "," +
                            Bool(frame.NextWeapon) + "," + Bool(frame.PrevWeapon) + "," +
                            Bool(frame.LastWeapon) + "," + Bool(frame.SelectVariant1) + "," +
                            Bool(frame.SelectVariant2) + "," + Bool(frame.SelectVariant3) + "," +
                            Bool(frame.Pause) + "," + Bool(frame.Stats) + "," +
                            Bool(frame.Slot1) + "," + Bool(frame.Slot2) + "," +
                            Bool(frame.Slot3) + "," + Bool(frame.Slot4) + "," +
                            Bool(frame.Slot5) + "," + Bool(frame.Slot6));
                    }
                }

                Logger.LogInfo("TAS saved to: " + tasPath);
            }
            catch (System.Exception ex)
            {
                Logger.LogError("Failed to save TAS: " + ex);
            }
        }

        private static int Bool(bool value)
        {
            return value ? 1 : 0;
        }
    }
}
