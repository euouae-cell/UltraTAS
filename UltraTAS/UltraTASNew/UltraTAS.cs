using BepInEx;
using HarmonyLib;
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
    [BepInPlugin("OWATAMSATE.UltraTAS", "UltraTAS", "1.2.2")]
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

        private readonly List<TASFrame> frames = new List<TASFrame>();
        private readonly Dictionary<string, InputAction> resolvedActions =
            new Dictionary<string, InputAction>();

        private Harmony? harmony;

        private bool recording;
        private bool playing;
        private int playbackFrame;

        private string tasPath = string.Empty;

        private global::PlayerInput? playerInput;

        private static UltraTAS? Instance { get; set; }

        private void Awake()
        {
            Instance = this;

            tasPath = Path.Combine(
                Paths.ConfigPath,
                "ultratas.tas"
            );

            harmony = new Harmony("OWATAMSATE.UltraTAS");
            harmony.PatchAll();

            InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
            InputSystem.onAfterUpdate += OnAfterInputUpdate;

            Logger.LogInfo("========================================");
            Logger.LogInfo("UltraTAS 1.2.2 loaded.");
            Logger.LogInfo("Using ULTRAKILL's native PlayerInput instance.");
            Logger.LogInfo("F6 = start/stop recording | F7 = playback | F8 = clear");
            Logger.LogInfo("Playback uses Unity Input System state events.");
            Logger.LogInfo("========================================");
        }

        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;

            harmony?.UnpatchSelf();
            harmony = null;

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        [HarmonyPatch(typeof(global::PlayerInput))]
        [HarmonyPatch(MethodType.Constructor)]
        private static class PlayerInputConstructorPatch
        {
            private static void Postfix(global::PlayerInput __instance)
            {
                Instance?.SetPlayerInput(__instance);
            }
        }

        private void SetPlayerInput(global::PlayerInput input)
        {
            playerInput = input;

            Logger.LogInfo(
                "UltraTAS: captured ULTRAKILL PlayerInput instance."
            );
        }

        private void OnBeforeInputUpdate()
        {
            if (!playing)
                return;

            PlayFrame();
        }

        private void OnAfterInputUpdate()
        {
            if (!recording)
                return;

            RecordFrame();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (recording)
                    StopRecording();
                else
                    StartRecording();
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (playing)
                    StopPlayback();
                else
                    StartPlayback();
            }

            if (Input.GetKeyDown(KeyCode.F8))
                ClearRecording();
        }

        private bool ResolvePlayerInput()
        {
            if (playerInput == null)
            {
                Logger.LogWarning(
                    "UltraTAS: ULTRAKILL PlayerInput instance has not been captured yet."
                );

                return false;
            }

            resolvedActions.Clear();

            AddAction("Move", playerInput.Actions.Movement.Move);
            AddAction("Look", playerInput.Actions.Movement.Look);
            AddAction("WheelLook", playerInput.Actions.Weapon.WheelLook);

            AddAction("Punch", playerInput.Actions.Fist.Punch);
            AddAction("Hook", playerInput.Actions.Fist.Hook);

            AddAction("Fire1", playerInput.Actions.Weapon.PrimaryFire);
            AddAction("Fire2", playerInput.Actions.Weapon.SecondaryFire);

            AddAction("Jump", playerInput.Actions.Movement.Jump);
            AddAction("Slide", playerInput.Actions.Movement.Slide);
            AddAction("Dodge", playerInput.Actions.Movement.Dodge);

            AddAction("ChangeFist", playerInput.Actions.Fist.ChangeFist);

            AddAction(
                "NextVariation",
                playerInput.Actions.Weapon.NextVariation
            );

            AddAction(
                "PreviousVariation",
                playerInput.Actions.Weapon.PreviousVariation
            );

            AddAction(
                "NextWeapon",
                playerInput.Actions.Weapon.NextWeapon
            );

            AddAction(
                "PrevWeapon",
                playerInput.Actions.Weapon.PreviousWeapon
            );

            AddAction(
                "LastWeapon",
                playerInput.Actions.Weapon.LastUsedWeapon
            );

            AddAction(
                "SelectVariant1",
                playerInput.Actions.Weapon.VariationSlot1
            );

            AddAction(
                "SelectVariant2",
                playerInput.Actions.Weapon.VariationSlot2
            );

            AddAction(
                "SelectVariant3",
                playerInput.Actions.Weapon.VariationSlot3
            );

            AddAction("Pause", playerInput.Actions.UI.Pause);
            AddAction("Stats", playerInput.Actions.HUD.Stats);

            AddAction("Slot1", playerInput.Actions.Weapon.Revolver);
            AddAction("Slot2", playerInput.Actions.Weapon.Shotgun);
            AddAction("Slot3", playerInput.Actions.Weapon.Nailgun);
            AddAction("Slot4", playerInput.Actions.Weapon.Railcannon);
            AddAction("Slot5", playerInput.Actions.Weapon.RocketLauncher);
            AddAction("Slot6", playerInput.Actions.Weapon.SpawnerArm);

            Logger.LogInfo(
                "UltraTAS: resolved " +
                resolvedActions.Count +
                "/" +
                PlayerInputActions.Length +
                " native PlayerInput actions."
            );

            return resolvedActions.Count > 0;
        }

        private void AddAction(string name, InputAction? action)
        {
            if (action != null)
            {
                resolvedActions[name] = action;
            }
            else
            {
                Logger.LogWarning(
                    "UltraTAS: native action is null: " + name
                );
            }
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

            Logger.LogInfo(
                "TAS recording stopped. Frames: " + frames.Count
            );
        }

        private void StartPlayback()
        {
            if (frames.Count == 0)
            {
                Logger.LogWarning(
                    "Cannot start playback: no recorded frames."
                );

                return;
            }

            if (!ResolvePlayerInput())
                return;

            recording = false;
            playing = true;
            playbackFrame = 0;

            Logger.LogInfo(
                "TAS playback started. Frames: " + frames.Count
            );
        }

        private void StopPlayback()
        {
            playing = false;

            Logger.LogInfo(
                "TAS playback stopped at frame " +
                playbackFrame +
                "."
            );
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
            if (!resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action) ||
                action == null)
            {
                return Vector2.zero;
            }

            return action.ReadValue<Vector2>();
        }

        private bool ReadButton(string actionName)
        {
            if (!resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action) ||
                action == null)
            {
                return false;
            }

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

            /*
             * IMPORTANT:
             *
             * Do NOT use InputState.Change() here.
             *
             * Keyboard keys such as W/A/S/D are bitfield controls.
             * InputState.Change() cannot write those individual controls,
             * which was the source of:
             *
             * "Cannot change state of bitfield control
             *  'Key:/Keyboard/w'"
             *
             * Instead we create StateEvents for the actual devices and
             * write the controls into those events.
             */

            QueueKeyboardFrame(frame);
            QueueMouseFrame(frame);

            playbackFrame++;
        }

        private void QueueKeyboardFrame(TASFrame frame)
        {
            Keyboard? keyboard = Keyboard.current;

            if (keyboard == null)
                return;

            using (
                StateEvent.From(
                    keyboard,
                    out InputEventPtr eventPtr
                )
            )
            {
                WriteActionToEvent(
                    "Move",
                    frame.Move,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Punch",
                    frame.Punch,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Hook",
                    frame.Hook,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Fire1",
                    frame.Fire1,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Fire2",
                    frame.Fire2,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Jump",
                    frame.Jump,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slide",
                    frame.Slide,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Dodge",
                    frame.Dodge,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "ChangeFist",
                    frame.ChangeFist,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "NextVariation",
                    frame.NextVariation,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "PreviousVariation",
                    frame.PreviousVariation,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "NextWeapon",
                    frame.NextWeapon,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "PrevWeapon",
                    frame.PrevWeapon,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "LastWeapon",
                    frame.LastWeapon,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "SelectVariant1",
                    frame.SelectVariant1,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "SelectVariant2",
                    frame.SelectVariant2,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "SelectVariant3",
                    frame.SelectVariant3,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Pause",
                    frame.Pause,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Stats",
                    frame.Stats,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slot1",
                    frame.Slot1,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slot2",
                    frame.Slot2,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slot3",
                    frame.Slot3,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slot4",
                    frame.Slot4,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slot5",
                    frame.Slot5,
                    eventPtr,
                    keyboard
                );

                WriteButtonActionToEvent(
                    "Slot6",
                    frame.Slot6,
                    eventPtr,
                    keyboard
                );

                InputSystem.QueueEvent(eventPtr);
            }
        }

        private void QueueMouseFrame(TASFrame frame)
        {
            Mouse? mouse = Mouse.current;

            if (mouse == null)
                return;

            using (
                StateEvent.From(
                    mouse,
                    out InputEventPtr eventPtr
                )
            )
            {
                WriteActionToEvent(
                    "Look",
                    frame.Look,
                    eventPtr,
                    mouse
                );

                WriteActionToEvent(
                    "WheelLook",
                    frame.WheelLook,
                    eventPtr,
                    mouse
                );

                InputSystem.QueueEvent(eventPtr);
            }
        }

        private void WriteActionToEvent(
            string actionName,
            Vector2 value,
            InputEventPtr eventPtr,
            InputDevice device)
        {
            if (!resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action) ||
                action == null)
            {
                return;
            }

            foreach (InputControl control in action.controls)
            {
                if (!BelongsToDevice(control, device))
                    continue;

                string path = control.path.ToLowerInvariant();

                if (control is Vector2Control vector2)
                {
                    vector2.WriteValueIntoEvent(
                        value,
                        eventPtr
                    );

                    return;
                }

                /*
                 * Handle WASD-style Vector2 composites.
                 *
                 * These are KeyControls, so we write them into the
                 * Keyboard StateEvent instead of using InputState.Change().
                 */

                float amount;

                if (
                    path.EndsWith("/w") ||
                    path.EndsWith("/up")
                )
                {
                    amount = Mathf.Max(0f, value.y);
                }
                else if (
                    path.EndsWith("/s") ||
                    path.EndsWith("/down")
                )
                {
                    amount = Mathf.Max(0f, -value.y);
                }
                else if (
                    path.EndsWith("/a") ||
                    path.EndsWith("/left")
                )
                {
                    amount = Mathf.Max(0f, -value.x);
                }
                else if (
                    path.EndsWith("/d") ||
                    path.EndsWith("/right")
                )
                {
                    amount = Mathf.Max(0f, value.x);
                }
                else
                {
                    continue;
                }

                WriteControlValue(
                    control,
                    amount,
                    eventPtr
                );
            }
        }

        private void WriteButtonActionToEvent(
            string actionName,
            bool pressed,
            InputEventPtr eventPtr,
            InputDevice device)
        {
            if (!resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action) ||
                action == null)
            {
                return;
            }

            float value = pressed ? 1f : 0f;

            foreach (InputControl control in action.controls)
            {
                if (!BelongsToDevice(control, device))
                    continue;

                WriteControlValue(
                    control,
                    value,
                    eventPtr
                );
            }
        }

        private static bool BelongsToDevice(
            InputControl control,
            InputDevice device)
        {
            return ReferenceEquals(
                control.device,
                device
            );
        }

        private static void WriteControlValue(
            InputControl control,
            float value,
            InputEventPtr eventPtr)
        {
            /*
             * This is the important difference from the old version.
             *
             * WriteValueIntoEvent() modifies the temporary StateEvent.
             * It does NOT try to directly mutate a bitfield KeyControl
             * through InputState.Change().
             */

            if (control is InputControl<float> floatControl)
            {
                floatControl.WriteValueIntoEvent(
                    value,
                    eventPtr
                );

                return;
            }

            if (control is ButtonControl button)
            {
                button.WriteValueIntoEvent(
                    value,
                    eventPtr
                );
            }
        }

        private void SaveRecording()
        {
            try
            {
                using (
                    StreamWriter writer =
                        new StreamWriter(tasPath, false)
                )
                {
                    writer.WriteLine("UltraTAS v2");
                    writer.WriteLine(
                        "Frames=" + frames.Count
                    );

                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            F(frame.Move.x) + "," +
                            F(frame.Move.y) + "," +

                            F(frame.Look.x) + "," +
                            F(frame.Look.y) + "," +

                            F(frame.WheelLook.x) + "," +
                            F(frame.WheelLook.y) + "," +

                            Bits(frame)
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "UltraTAS: failed to save TAS: " + ex
                );
            }
        }

        private static string F(float value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture
            );
        }

        private static string Bits(TASFrame f)
        {
            return
                (f.Punch ? "1" : "0") +
                (f.Hook ? "1" : "0") +
                (f.Fire1 ? "1" : "0") +
                (f.Fire2 ? "1" : "0") +
                (f.Jump ? "1" : "0") +
                (f.Slide ? "1" : "0") +
                (f.Dodge ? "1" : "0") +
                (f.ChangeFist ? "1" : "0") +

                (f.NextVariation ? "1" : "0") +
                (f.PreviousVariation ? "1" : "0") +

                (f.NextWeapon ? "1" : "0") +
                (f.PrevWeapon ? "1" : "0") +
                (f.LastWeapon ? "1" : "0") +

                (f.SelectVariant1 ? "1" : "0") +
                (f.SelectVariant2 ? "1" : "0") +
                (f.SelectVariant3 ? "1" : "0") +

                (f.Pause ? "1" : "0") +
                (f.Stats ? "1" : "0") +

                (f.Slot1 ? "1" : "0") +
                (f.Slot2 ? "1" : "0") +
                (f.Slot3 ? "1" : "0") +
                (f.Slot4 ? "1" : "0") +
                (f.Slot5 ? "1" : "0") +
                (f.Slot6 ? "1" : "0");
        }
    }
}
