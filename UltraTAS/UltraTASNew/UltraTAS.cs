using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
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
        // ADDITIONAL INPUTACTIONSTATE FINDINGS FROM THE NEXT ASSEMBLY-CSharp CHUNK:
        //   StartTimeout(seconds, trigger) schedules an InputManager state-change timeout
        //   at trigger.time + seconds for the trigger's control/binding/interaction.
        //   StopTimeout() removes that monitor and updates the interaction's accumulated
        //   timeout bookkeeping. ProcessTimeout() marks the timer expired and calls the
        //   interaction's Process(ref context) with timerHasExpired = true.
        //   ChangePhaseOfInteraction() is the bridge from an interaction to its action:
        //   it updates interaction state, stops active timers, then propagates Started /
        //   Performed / Canceled to ChangePhaseOfAction(). On Performed it can reset the
        //   other interactions on the same binding; on Canceled it can advance to the
        //   next active interaction. This is important for reproducing the game's exact
        //   action semantics instead of merely setting booleans.
        //   ChangePhaseOfActionInternal() copies the trigger into action state and invokes
        //   the actual InputAction listeners for Started/Performed/Canceled. It also
        //   records the InputUpdate step in lastPerformedInUpdate/lastCanceledInUpdate
        //   and preserves pressed/released flags and magnitude.
        //   ReadValue() does NOT just read a raw control: composite bindings are evaluated
        //   through InputBindingCompositeContext, then binding processors are applied.
        //   ReadValue<T>() performs the same process for typed values, while
        //   ReadValueAsObject() does it through the object processor path.
        //   IsActuated() uses trigger.magnitude (or treats negative magnitude as actuated)
        //   and compares it against the supplied threshold. ReadValueAsButton() uses the
        //   control's pressPointOrDefault when available, otherwise the global default.
        //   Composite part evaluation chooses the strongest matching control by magnitude
        //   and can inspect pressTime, which matters for deterministic composite inputs.
        //   Global InputActionState instances are tracked through weak GCHandles in the
        //   global list. OnDeviceChange() can reset states, remove devices and trigger
        //   binding resolution depending on the device-change type. DeferredResolutionOfBindings()
        //   resolves every live action map while binding resolution is deferred.
        //
        // IMPORTANT FOR TAS IMPLEMENTATION:
        // Playback should ultimately feed the game's existing Unity Input System state/
        // event path rather than OS-level keyboard injection or a replacement manager.
        // The recovered state layout gives us the internal structures needed to do that.
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

        private void StartRecording()
        {
            playing = false;
            frames.Clear();
            recording = true;
            playbackFrame = 0;
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
            // Temporary capture only. We are deliberately retaining this until the
            // remaining InputActionState/InputEvent path has been mapped. It is NOT the
            // intended final recorder source.
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

            // Deliberately no fake keyboard/mouse injection yet.
            // Next stage: resolve the game's PlayerInput/InputActionState and feed the
            // native Input System event/state path at deterministic frame boundaries.
            playbackFrame++;
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
