using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace UltraTAS
{
    [BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.1.0")]
    public class UltraTAS : BaseUnityPlugin
    {
        private sealed class TASFrame
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

        private static readonly string[] PlayerInputActions =
        {
            "Move", "Look", "WheelLook", "Punch", "Hook", "Fire1", "Fire2",
            "Jump", "Slide", "Dodge", "ChangeFist", "NextVariation",
            "PreviousVariation", "NextWeapon", "PrevWeapon", "LastWeapon",
            "SelectVariant1", "SelectVariant2", "SelectVariant3", "Pause", "Stats",
            "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6"
        };

        // Recovered Unity Input System findings:
        // InputActionState is the live resolved state for maps, controls, composites,
        // interactions, processors and action phases. TAS should feed that existing state.
        // InputState.Change(control, value) reaches InputSystem.s_Manager.UpdateState()
        // using the control's device-relative state offset, avoiding OS-level injection.
        // ReadValue<T>() evaluates composites and applies binding processors.
        // Composite part evaluation chooses the strongest matching control by magnitude.
        // InputActionState registers state-change monitors for enabled controls.
        // Interaction timers and phase changes belong to the normal action pipeline.
        // Do not directly edit TriggerState to fake Started/Performed/Canceled.
        //
        // Recovered InputManager.UpdateState findings:
        // InputState.Change() ultimately calls InputManager.UpdateState(). That method:
        //  1. Gets the device's front state buffer and sorts state-change monitors.
        //  2. Runs ProcessStateChangeMonitors() against the changed device-state region.
        //  3. Compares the new region with the front buffer (respecting noisy-device masks).
        //  4. Flips/updates the device state buffers for the requested InputUpdateType.
        //  5. Invokes onDeviceStateChange callbacks.
        //  6. Fires state-change notifications when monitors were signalled.
        // Therefore TAS input written with InputState.Change is not merely changing a
        // value returned by ReadValue(); it enters the Input System's actual device-state
        // update path. This is important for action callbacks, button transitions,
        // interactions and controls monitored by the existing PlayerInput pipeline.
        // Repeated Change() calls on controls belonging to the same device are still
        // separate state updates, so deterministic frame injection will eventually need
        // careful batching/timing rather than assuming one Change() equals one game frame.
        //
        // Recovered GroundCheck findings:
        // UpdateState() is frame-driven. onGround follows touchingGround unless forced off.
        // superJumpChance, extraJumpChance and bounceChance are Time.deltaTime windows.
        // Bounce checks InputSource.Jump.IsPressed, so Jump must reach the real input path.
        // These fields are useful later for TAS state verification; TAS must not write them.

        private readonly List<TASFrame> frames = new List<TASFrame>();
        private readonly Dictionary<string, InputAction> resolvedActions = new Dictionary<string, InputAction>();
        private bool recording;
        private bool playing;
        private int playbackFrame;
        private string tasPath;
        private PlayerInput playerInput;

        private void Awake()
        {
            tasPath = Path.Combine(Paths.ConfigPath, "ultratas.tas");
            InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
            InputSystem.onAfterUpdate += OnAfterInputUpdate;

            Logger.LogInfo("========================================");
            Logger.LogInfo("UltraTAS 1.1.0 loaded.");
            Logger.LogInfo("Native Unity Input System TAS bridge enabled.");
            Logger.LogInfo("F6 = start/stop recording | F7 = playback | F8 = clear");
            Logger.LogInfo("========================================");
        }

        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
        }

        // Playback is injected before the Input System processes the update. This is much
        // closer to the game's real InputActionState timing than writing from MonoBehaviour.Update.
        private void OnBeforeInputUpdate(InputUpdateType updateType)
        {
            if (!playing || updateType == InputUpdateType.None)
                return;

            PlayFrame();
        }

        // Recording happens after the Input System update, so ReadValue() sees the same
        // resolved composite/processor state that gameplay sees.
        private void OnAfterInputUpdate(InputUpdateType updateType)
        {
            if (!recording || updateType == InputUpdateType.None)
                return;

            RecordFrame();
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

            if (Input.GetKeyDown(KeyCode.F8))
                ClearRecording();
        }

        private bool ResolvePlayerInput()
        {
            if (playerInput == null || playerInput.gameObject == null)
                playerInput = FindObjectOfType<PlayerInput>();

            if (playerInput == null)
            {
                Logger.LogWarning("UltraTAS: no PlayerInput component found.");
                return false;
            }

            if (playerInput.actions == null)
            {
                Logger.LogWarning("UltraTAS: PlayerInput has no InputActionAsset.");
                return false;
            }

            resolvedActions.Clear();
            foreach (string actionName in PlayerInputActions)
            {
                InputAction action = playerInput.actions.FindAction(actionName, false);
                if (action != null)
                    resolvedActions[actionName] = action;
                else
                    Logger.LogWarning("UltraTAS: action not found: " + actionName);
            }

            Logger.LogInfo("UltraTAS: resolved " + resolvedActions.Count + "/" + PlayerInputActions.Length + " actions.");
            return resolvedActions.Count > 0;
        }

        private void StartRecording()
        {
            if (!ResolvePlayerInput())
                return;

            playing = false;
            frames.Clear();
            playbackFrame = 0;
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

            if (!ResolvePlayerInput())
                return;

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
            if (resolvedActions.Count == 0)
                return;

            TASFrame frame = new TASFrame
            {
                Move = ReadVector2("Move"),
                Look = ReadVector2("Look"),
                WheelLook = ReadVector2("WheelLook"),
                Punch = ReadButton("Punch"),
                Hook = ReadButton("Hook"),
                Fire1 = ReadButton("Fire1"),
                Fire2 = ReadButton("Fire2"),
                Jump = ReadButton("Jump"),
                Slide = ReadButton("Slide"),
                Dodge = ReadButton("Dodge"),
                ChangeFist = ReadButton("ChangeFist"),
                NextVariation = ReadButton("NextVariation"),
                PreviousVariation = ReadButton("PreviousVariation"),
                NextWeapon = ReadButton("NextWeapon"),
                PrevWeapon = ReadButton("PrevWeapon"),
                LastWeapon = ReadButton("LastWeapon"),
                SelectVariant1 = ReadButton("SelectVariant1"),
                SelectVariant2 = ReadButton("SelectVariant2"),
                SelectVariant3 = ReadButton("SelectVariant3"),
                Pause = ReadButton("Pause"),
                Stats = ReadButton("Stats"),
                Slot1 = ReadButton("Slot1"),
                Slot2 = ReadButton("Slot2"),
                Slot3 = ReadButton("Slot3"),
                Slot4 = ReadButton("Slot4"),
                Slot5 = ReadButton("Slot5"),
                Slot6 = ReadButton("Slot6")
            };

            frames.Add(frame);
        }

        private Vector2 ReadVector2(string actionName)
        {
            InputAction action;
            if (!resolvedActions.TryGetValue(actionName, out action) || action.valueType != typeof(Vector2))
                return Vector2.zero;

            return action.ReadValue<Vector2>();
        }

        private bool ReadButton(string actionName)
        {
            InputAction action;
            if (!resolvedActions.TryGetValue(actionName, out action))
                return false;

            return action.IsPressed();
        }

        private void PlayFrame()
        {
            if (playbackFrame >= frames.Count)
            {
                StopPlayback();
                return;
            }

            TASFrame frame = frames[playbackFrame];
            WriteVector2("Move", frame.Move);
            WriteVector2("Look", frame.Look);
            WriteVector2("WheelLook", frame.WheelLook);
            WriteButton("Punch", frame.Punch);
            WriteButton("Hook", frame.Hook);
            WriteButton("Fire1", frame.Fire1);
            WriteButton("Fire2", frame.Fire2);
            WriteButton("Jump", frame.Jump);
            WriteButton("Slide", frame.Slide);
            WriteButton("Dodge", frame.Dodge);
            WriteButton("ChangeFist", frame.ChangeFist);
            WriteButton("NextVariation", frame.NextVariation);
            WriteButton("PreviousVariation", frame.PreviousVariation);
            WriteButton("NextWeapon", frame.NextWeapon);
            WriteButton("PrevWeapon", frame.PrevWeapon);
            WriteButton("LastWeapon", frame.LastWeapon);
            WriteButton("SelectVariant1", frame.SelectVariant1);
            WriteButton("SelectVariant2", frame.SelectVariant2);
            WriteButton("SelectVariant3", frame.SelectVariant3);
            WriteButton("Pause", frame.Pause);
            WriteButton("Stats", frame.Stats);
            WriteButton("Slot1", frame.Slot1);
            WriteButton("Slot2", frame.Slot2);
            WriteButton("Slot3", frame.Slot3);
            WriteButton("Slot4", frame.Slot4);
            WriteButton("Slot5", frame.Slot5);
            WriteButton("Slot6", frame.Slot6);

            playbackFrame++;
        }

        private void WriteVector2(string actionName, Vector2 value)
        {
            InputAction action;
            if (!resolvedActions.TryGetValue(actionName, out action))
                return;

            foreach (InputControl control in action.controls)
            {
                Vector2Control vector2 = control as Vector2Control;
                if (vector2 != null)
                {
                    InputState.Change(vector2, value);
                    return;
                }
            }

            // A 2D Vector2 composite exposes button/axis parts instead of a Vector2Control.
            // Write those parts through InputState.Change so InputActionState evaluates the
            // composite normally on the input update.
            bool wroteComposite = false;
            foreach (InputControl control in action.controls)
            {
                string path = control.path.ToLowerInvariant();
                float amount;

                if (path.EndsWith("/w") || path.EndsWith("/up")) amount = Mathf.Max(0f, value.y);
                else if (path.EndsWith("/s") || path.EndsWith("/down")) amount = Mathf.Max(0f, -value.y);
                else if (path.EndsWith("/a") || path.EndsWith("/left")) amount = Mathf.Max(0f, -value.x);
                else if (path.EndsWith("/d") || path.EndsWith("/right")) amount = Mathf.Max(0f, value.x);
                else continue;

                InputControl<float> floatControl = control as InputControl<float>;
                if (floatControl != null)
                {
                    InputState.Change(floatControl, amount);
                    wroteComposite = true;
                }
            }

            if (!wroteComposite)
                Logger.LogWarning("UltraTAS: could not find a writable Vector2 control/composite for " + actionName + ".");
        }

        private void WriteButton(string actionName, bool pressed)
        {
            InputAction action;
            if (!resolvedActions.TryGetValue(actionName, out action))
                return;

            float value = pressed ? 1f : 0f;
            foreach (InputControl control in action.controls)
            {
                ButtonControl button = control as ButtonControl;
                if (button != null)
                {
                    InputState.Change(button, value);
                    return;
                }

                InputControl<float> floatControl = control as InputControl<float>;
                if (floatControl != null)
                {
                    InputState.Change(floatControl, value);
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
                    writer.WriteLine("UltraTAS v2");
                    writer.WriteLine("Frames=" + frames.Count);
                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            F(frame.Move.x) + "," + F(frame.Move.y) + "," +
                            F(frame.Look.x) + "," + F(frame.Look.y) + "," +
                            F(frame.WheelLook.x) + "," + F(frame.WheelLook.y) + "," +
                            Bits(frame));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("UltraTAS: failed to save TAS: " + ex);
            }
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Bits(TASFrame f)
        {
            return (f.Punch ? "1" : "0") + (f.Hook ? "1" : "0") + (f.Fire1 ? "1" : "0") +
                   (f.Fire2 ? "1" : "0") + (f.Jump ? "1" : "0") + (f.Slide ? "1" : "0") +
                   (f.Dodge ? "1" : "0") + (f.ChangeFist ? "1" : "0") +
                   (f.NextVariation ? "1" : "0") + (f.PreviousVariation ? "1" : "0") +
                   (f.NextWeapon ? "1" : "0") + (f.PrevWeapon ? "1" : "0") +
                   (f.LastWeapon ? "1" : "0") + (f.SelectVariant1 ? "1" : "0") +
                   (f.SelectVariant2 ? "1" : "0") + (f.SelectVariant3 ? "1" : "0") +
                   (f.Pause ? "1" : "0") + (f.Stats ? "1" : "0") +
                   (f.Slot1 ? "1" : "0") + (f.Slot2 ? "1" : "0") + (f.Slot3 ? "1" : "0") +
                   (f.Slot4 ? "1" : "0") + (f.Slot5 ? "1" : "0") + (f.Slot6 ? "1" : "0");
        }
    }
}
