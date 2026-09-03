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
    [BepInPlugin("ti0z1.UltraTAS", "UltraTAS", "1.3.6")]
    public class UltraTAS : BaseUnityPlugin
    {
        private sealed class TASFrame
        {
            public Vector2 Move, Look, WheelLook;
            public Vector3 Position, Velocity;
            public UnityEngine.Random.State RandomState;
            public bool Punch, Hook, Fire1, Fire2, Jump, Slide, Dodge, ChangeFist;
            public bool NextVariation, PreviousVariation, NextWeapon, PrevWeapon, LastWeapon;
            public bool SelectVariant1, SelectVariant2, SelectVariant3, Pause, Stats;
            public bool Slot1, Slot2, Slot3, Slot4, Slot5, Slot6;
        }

        private static readonly string[] PlayerInputActions =
        {
            "Move","Look","WheelLook","Punch","Hook","Fire1","Fire2","Jump","Slide","Dodge","ChangeFist",
            "NextVariation","PreviousVariation","NextWeapon","PrevWeapon","LastWeapon","SelectVariant1","SelectVariant2","SelectVariant3",
            "Pause","Stats","Slot1","Slot2","Slot3","Slot4","Slot5","Slot6"
        };

        private readonly List<TASFrame> frames = new List<TASFrame>();
        private readonly Dictionary<string, InputAction> resolvedActions = new Dictionary<string, InputAction>();
        private Harmony? harmony;
        private global::PlayerInput? playerInput;
        private global::NewMovement? newMovement;
        private Transform? playerTransform;
        private Rigidbody? playerBody;
        private bool recording, playing;
        private int playbackFrame, playbackPhysicsFrame, tasSeed;
        private int lastPlaybackUnityFrame = -1, lastRecordingUnityFrame = -1;
        private string tasPath = string.Empty;
        private int lastPlaybackSlot = -1;
        private bool playbackInputQueued;
        private int queuedPlaybackFrame = -1;
        private UnityEngine.Random.State pendingRecordingRandomState;
        private bool pendingRecordingRandomStateValid;
        private GUIStyle? tasStyle;
        private static UltraTAS? Instance { get; set; }
        private const float HardResyncDistance = 0.20f;

        private void Awake()
        {
            Instance = this;
            tasPath = Path.Combine(Paths.ConfigPath, "ultratas.tas");
            harmony = new Harmony("OWATAMSATE.UltraTAS");
            harmony.PatchAll();
            InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
            InputSystem.onAfterUpdate += OnAfterInputUpdate;
            Logger.LogInfo("UltraTAS 1.3.6 loaded. Per-physics-tick Unity RNG synchronization enabled.");
            Logger.LogInfo("F6 = start/stop recording | F7 = playback | F8 = clear");
        }

        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
            if (playing) ReleaseInjectedInput();
            harmony?.UnpatchSelf();
            harmony = null;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        [HarmonyPatch(typeof(global::PlayerInput))]
        [HarmonyPatch(MethodType.Constructor)]
        private static class PlayerInputConstructorPatch
        {
            private static void Postfix(global::PlayerInput __instance) => Instance?.SetPlayerInput(__instance);
        }

        [HarmonyPatch(typeof(global::NewMovement), "FixedUpdate")]
        private static class NewMovementFixedUpdatePatch
        {
            private static void Prefix(global::NewMovement __instance) => Instance?.OnPlayerPhysicsTickPrefix(__instance);
            private static void Postfix(global::NewMovement __instance) => Instance?.OnPlayerPhysicsTickPostfix(__instance);
        }

        private void SetPlayerInput(global::PlayerInput input)
        {
            playerInput = input;
            ResolvePlayerPhysics();
            Logger.LogInfo("UltraTAS: captured ULTRAKILL PlayerInput instance.");
        }

        private void ResolvePlayerPhysics()
        {
            playerTransform = null;
            playerBody = null;
            newMovement = null;
            try
            {
                newMovement = MonoSingleton<NewMovement>.Instance;
                if (newMovement == null) return;
                playerBody = newMovement.rb;
                playerTransform = newMovement.transform;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("UltraTAS: could not resolve NewMovement: " + ex.Message);
            }
        }

        private void OnPlayerPhysicsTickPrefix(global::NewMovement movement)
        {
            if (!ReferenceEquals(movement, newMovement))
            {
                newMovement = movement;
                playerBody = movement.rb;
                playerTransform = movement.transform;
            }

            if (recording)
            {
                pendingRecordingRandomState = UnityEngine.Random.state;
                pendingRecordingRandomStateValid = true;
                return;
            }

            if (!playing) return;
            if (playbackPhysicsFrame < 0 || playbackPhysicsFrame >= frames.Count) return;
            UnityEngine.Random.state = frames[playbackPhysicsFrame].RandomState;
        }

        private void OnPlayerPhysicsTickPostfix(global::NewMovement movement)
        {
            if (!ReferenceEquals(movement, newMovement))
            {
                newMovement = movement;
                playerBody = movement.rb;
                playerTransform = movement.transform;
            }

            if (recording)
            {
                UnityEngine.Random.State state = pendingRecordingRandomStateValid
                    ? pendingRecordingRandomState : UnityEngine.Random.state;
                RecordFrameFromPhysics(state);
                pendingRecordingRandomStateValid = false;
                return;
            }

            if (!playing) return;
            if (playbackPhysicsFrame < 0 || playbackPhysicsFrame >= frames.Count) return;
            VerifyAndResyncPlayer(frames[playbackPhysicsFrame]);
            playbackPhysicsFrame++;
        }

        private void OnBeforeInputUpdate()
        {
            if (!playing) return;
            int unityFrame = Time.frameCount;
            if (unityFrame == lastPlaybackUnityFrame) return;
            lastPlaybackUnityFrame = unityFrame;
            PlayFrame();
        }

        private void OnAfterInputUpdate()
        {
            if (recording)
            {
                int unityFrame = Time.frameCount;
                if (unityFrame != lastRecordingUnityFrame) lastRecordingUnityFrame = unityFrame;
            }
            if (playing && playbackInputQueued)
            {
                playbackInputQueued = false;
                queuedPlaybackFrame = -1;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6)) { if (recording) StopRecording(); else StartRecording(); }
            if (Input.GetKeyDown(KeyCode.F7)) { if (playing) StopPlayback(); else StartPlayback(); }
            if (Input.GetKeyDown(KeyCode.F8)) ClearRecording();
        }

        private void OnGUI()
        {
            if (!recording && !playing) return;
            if (tasStyle == null)
            {
                tasStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft
                };
                tasStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            }
            GUI.Label(new Rect(15f, 15f, 100f, 40f), "TAS", tasStyle);
        }

        private bool ResolvePlayerInput()
        {
            if (playerInput == null)
            {
                Logger.LogWarning("UltraTAS: PlayerInput has not been captured yet.");
                return false;
            }
            resolvedActions.Clear();
            AddAction("Move", playerInput.Actions.Movement.Move);
            AddAction("Look", playerInput.Actions.Movement.Look);
            AddAction("WheelLook", playerInput.Actions.Weapon.WheelLook);
            AddAction("Fire1", playerInput.Actions.Weapon.PrimaryFire);
            AddAction("Fire2", playerInput.Actions.Weapon.SecondaryFire);
            AddAction("NextVariation", playerInput.Actions.Weapon.NextVariation);
            AddAction("PreviousVariation", playerInput.Actions.Weapon.PreviousVariation);
            AddAction("NextWeapon", playerInput.Actions.Weapon.NextWeapon);
            AddAction("PrevWeapon", playerInput.Actions.Weapon.PreviousWeapon);
            AddAction("LastWeapon", playerInput.Actions.Weapon.LastUsedWeapon);
            AddAction("Slot1", playerInput.Actions.Weapon.Revolver);
            AddAction("Slot2", playerInput.Actions.Weapon.Shotgun);
            AddAction("Slot3", playerInput.Actions.Weapon.Nailgun);
            AddAction("Slot4", playerInput.Actions.Weapon.Railcannon);
            AddAction("Slot5", playerInput.Actions.Weapon.RocketLauncher);
            AddAction("Slot6", playerInput.Actions.Weapon.SpawnerArm);
            AddAction("SelectVariant1", playerInput.Actions.Weapon.VariationSlot1);
            AddAction("SelectVariant2", playerInput.Actions.Weapon.VariationSlot2);
            AddAction("SelectVariant3", playerInput.Actions.Weapon.VariationSlot3);
            AddAction("Punch", playerInput.Actions.Fist.Punch);
            AddAction("Hook", playerInput.Actions.Fist.Hook);
            AddAction("ChangeFist", playerInput.Actions.Fist.ChangeFist);
            AddAction("Jump", playerInput.Actions.Movement.Jump);
            AddAction("Slide", playerInput.Actions.Movement.Slide);
            AddAction("Dodge", playerInput.Actions.Movement.Dodge);
            AddAction("Pause", playerInput.Actions.UI.Pause);
            AddAction("Stats", playerInput.Actions.HUD.Stats);
            ResolvePlayerPhysics();
            Logger.LogInfo("UltraTAS: resolved " + resolvedActions.Count + "/" + PlayerInputActions.Length + " native actions.");
            return resolvedActions.Count > 0;
        }

        private void AddAction(string name, InputAction? action)
        {
            if (action == null)
            {
                Logger.LogWarning("UltraTAS: native action is null: " + name);
                return;
            }
            resolvedActions[name] = action;
        }

        private void StartRecording()
        {
            if (!ResolvePlayerInput()) return;
            playing = false;
            frames.Clear();
            playbackFrame = 0;
            playbackPhysicsFrame = 0;
            lastRecordingUnityFrame = Time.frameCount;
            lastPlaybackUnityFrame = -1;
            lastPlaybackSlot = -1;
            playbackInputQueued = false;
            queuedPlaybackFrame = -1;
            pendingRecordingRandomStateValid = false;
            tasSeed = Environment.TickCount;
            UnityEngine.Random.InitState(tasSeed);
            recording = true;
            Logger.LogInfo("TAS recording started. Seed: " + tasSeed);
        }

        private void StopRecording()
        {
            recording = false;
            pendingRecordingRandomStateValid = false;
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
            if (!ResolvePlayerInput()) return;
            recording = false;
            UnityEngine.Random.InitState(tasSeed);
            playbackFrame = 0;
            playbackPhysicsFrame = 0;
            lastPlaybackUnityFrame = Time.frameCount;
            lastRecordingUnityFrame = -1;
            lastPlaybackSlot = -1;
            playbackInputQueued = false;
            queuedPlaybackFrame = -1;
            playing = true;
            Logger.LogInfo("TAS playback started. Frames: " + frames.Count + ", Seed: " + tasSeed);
        }

        private void StopPlayback()
        {
            if (!playing) return;
            playing = false;
            playbackInputQueued = false;
            queuedPlaybackFrame = -1;
            ReleaseInjectedInput();
            lastPlaybackSlot = -1;
            playbackPhysicsFrame = 0;
            Logger.LogInfo("TAS playback stopped at frame " + playbackFrame + ".");
        }

        private void ClearRecording()
        {
            if (playing) ReleaseInjectedInput();
            frames.Clear();
            recording = false;
            playing = false;
            playbackFrame = 0;
            playbackPhysicsFrame = 0;
            tasSeed = 0;
            lastPlaybackUnityFrame = -1;
            lastRecordingUnityFrame = -1;
            lastPlaybackSlot = -1;
            playbackInputQueued = false;
            queuedPlaybackFrame = -1;
            pendingRecordingRandomStateValid = false;
            Logger.LogInfo("TAS recording cleared.");
        }

        private void RecordFrameFromPhysics(UnityEngine.Random.State randomState)
        {
            if (!recording || resolvedActions.Count == 0) return;
            frames.Add(new TASFrame
            {
                Move = ReadVector2("Move"), Look = ReadVector2("Look"), WheelLook = ReadVector2("WheelLook"),
                Position = GetPlayerPosition(), Velocity = GetPlayerVelocity(), RandomState = randomState,
                Punch = ReadButton("Punch"), Hook = ReadButton("Hook"), Fire1 = ReadButton("Fire1"), Fire2 = ReadButton("Fire2"),
                Jump = ReadButton("Jump"), Slide = ReadButton("Slide"), Dodge = ReadButton("Dodge"), ChangeFist = ReadButton("ChangeFist"),
                NextVariation = ReadButton("NextVariation"), PreviousVariation = ReadButton("PreviousVariation"),
                NextWeapon = ReadButton("NextWeapon"), PrevWeapon = ReadButton("PrevWeapon"), LastWeapon = ReadButton("LastWeapon"),
                SelectVariant1 = ReadButton("SelectVariant1"), SelectVariant2 = ReadButton("SelectVariant2"), SelectVariant3 = ReadButton("SelectVariant3"),
                Pause = ReadButton("Pause"), Stats = ReadButton("Stats"),
                Slot1 = ReadButton("Slot1"), Slot2 = ReadButton("Slot2"), Slot3 = ReadButton("Slot3"),
                Slot4 = ReadButton("Slot4"), Slot5 = ReadButton("Slot5"), Slot6 = ReadButton("Slot6")
            });
        }

        private Vector3 GetPlayerPosition()
        {
            if (playerBody != null) return playerBody.position;
            if (playerTransform != null) return playerTransform.position;
            return Vector3.zero;
        }

        private Vector3 GetPlayerVelocity() => playerBody != null ? playerBody.velocity : Vector3.zero;

        private void VerifyAndResyncPlayer(TASFrame frame)
        {
            if (playerBody == null || playerTransform == null) ResolvePlayerPhysics();
            if (playerBody == null || playerTransform == null) return;
            float distance = (frame.Position - playerBody.position).magnitude;
            if (distance <= HardResyncDistance) return;
            Logger.LogWarning("UltraTAS: player desync detected at physics frame " + playbackPhysicsFrame + ". Error: " + distance.ToString("F3", CultureInfo.InvariantCulture) + "m. Resynchronizing.");
            playerBody.position = frame.Position;
            playerBody.velocity = frame.Velocity;
            Physics.SyncTransforms();
        }

        private Vector2 ReadVector2(string actionName)
        {
            if (!resolvedActions.TryGetValue(actionName, out InputAction? action) || action == null) return Vector2.zero;
            return action.ReadValue<Vector2>();
        }

        private bool ReadButton(string actionName)
        {
            if (!resolvedActions.TryGetValue(actionName, out InputAction? action) || action == null) return false;
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
            QueueKeyboardFrame(frame);
            QueueMouseFrame(frame);
            playbackInputQueued = true;
            queuedPlaybackFrame = playbackFrame;
            ProcessWeaponTransition(frame);
            playbackFrame++;
        }

        private void ProcessWeaponTransition(TASFrame frame)
        {
            int requestedSlot = GetRequestedSlot(frame);
            if (requestedSlot < 0 || requestedSlot == lastPlaybackSlot) return;
            lastPlaybackSlot = requestedSlot;
        }

        private static int GetRequestedSlot(TASFrame frame)
        {
            if (frame.Slot1) return 1;
            if (frame.Slot2) return 2;
            if (frame.Slot3) return 3;
            if (frame.Slot4) return 4;
            if (frame.Slot5) return 5;
            if (frame.Slot6) return 6;
            return -1;
        }

        private void QueueKeyboardFrame(TASFrame frame)
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null) return;
            using (StateEvent.From(keyboard, out InputEventPtr eventPtr))
            {
                WriteActionToEvent("Move", frame.Move, eventPtr, keyboard);
                WriteButtonActionToEvent("Punch", frame.Punch, eventPtr, keyboard);
                WriteButtonActionToEvent("Hook", frame.Hook, eventPtr, keyboard);
                WriteButtonActionToEvent("Jump", frame.Jump, eventPtr, keyboard);
                WriteButtonActionToEvent("Slide", frame.Slide, eventPtr, keyboard);
                WriteButtonActionToEvent("Dodge", frame.Dodge, eventPtr, keyboard);
                WriteButtonActionToEvent("ChangeFist", frame.ChangeFist, eventPtr, keyboard);
                WriteButtonActionToEvent("NextVariation", frame.NextVariation, eventPtr, keyboard);
                WriteButtonActionToEvent("PreviousVariation", frame.PreviousVariation, eventPtr, keyboard);
                WriteButtonActionToEvent("NextWeapon", frame.NextWeapon, eventPtr, keyboard);
                WriteButtonActionToEvent("PrevWeapon", frame.PrevWeapon, eventPtr, keyboard);
                WriteButtonActionToEvent("LastWeapon", frame.LastWeapon, eventPtr, keyboard);
                WriteButtonActionToEvent("SelectVariant1", frame.SelectVariant1, eventPtr, keyboard);
                WriteButtonActionToEvent("SelectVariant2", frame.SelectVariant2, eventPtr, keyboard);
                WriteButtonActionToEvent("SelectVariant3", frame.SelectVariant3, eventPtr, keyboard);
                WriteButtonActionToEvent("Pause", frame.Pause, eventPtr, keyboard);
                WriteButtonActionToEvent("Stats", frame.Stats, eventPtr, keyboard);
                WriteButtonActionToEvent("Slot1", frame.Slot1, eventPtr, keyboard);
                WriteButtonActionToEvent("Slot2", frame.Slot2, eventPtr, keyboard);
                WriteButtonActionToEvent("Slot3", frame.Slot3, eventPtr, keyboard);
                WriteButtonActionToEvent("Slot4", frame.Slot4, eventPtr, keyboard);
                WriteButtonActionToEvent("Slot5", frame.Slot5, eventPtr, keyboard);
                WriteButtonActionToEvent("Slot6", frame.Slot6, eventPtr, keyboard);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private void QueueMouseFrame(TASFrame frame)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null) return;
            using (StateEvent.From(mouse, out InputEventPtr eventPtr))
            {
                WriteActionToEvent("Look", frame.Look, eventPtr, mouse);
                WriteActionToEvent("WheelLook", frame.WheelLook, eventPtr, mouse);
                WriteButtonActionToEvent("Fire1", frame.Fire1, eventPtr, mouse);
                WriteButtonActionToEvent("Fire2", frame.Fire2, eventPtr, mouse);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private void ReleaseInjectedInput()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null)
            {
                using (StateEvent.From(keyboard, out InputEventPtr eventPtr))
                {
                    foreach (string actionName in PlayerInputActions)
                    {
                        if (actionName == "Move" || actionName == "Look" || actionName == "WheelLook" || actionName == "Fire1" || actionName == "Fire2") continue;
                        WriteButtonActionToEvent(actionName, false, eventPtr, keyboard);
                    }
                    WriteActionToEvent("Move", Vector2.zero, eventPtr, keyboard);
                    InputSystem.QueueEvent(eventPtr);
                }
            }
            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                using (StateEvent.From(mouse, out InputEventPtr eventPtr))
                {
                    WriteActionToEvent("Look", Vector2.zero, eventPtr, mouse);
                    WriteActionToEvent("WheelLook", Vector2.zero, eventPtr, mouse);
                    WriteButtonActionToEvent("Fire1", false, eventPtr, mouse);
                    WriteButtonActionToEvent("Fire2", false, eventPtr, mouse);
                    InputSystem.QueueEvent(eventPtr);
                }
            }
        }

        private void WriteActionToEvent(string actionName, Vector2 value, InputEventPtr eventPtr, InputDevice device)
        {
            if (!resolvedActions.TryGetValue(actionName, out InputAction? action) || action == null) return;
            foreach (InputControl control in action.controls)
            {
                if (!BelongsToDevice(control, device)) continue;
                if (control is Vector2Control vector2)
                {
                    vector2.WriteValueIntoEvent(value, eventPtr);
                    continue;
                }
                string path = control.path.ToLowerInvariant();
                float amount;
                if (path.EndsWith("/w") || path.EndsWith("/up")) amount = Mathf.Max(0f, value.y);
                else if (path.EndsWith("/s") || path.EndsWith("/down")) amount = Mathf.Max(0f, -value.y);
                else if (path.EndsWith("/a") || path.EndsWith("/left")) amount = Mathf.Max(0f, -value.x);
                else if (path.EndsWith("/d") || path.EndsWith("/right")) amount = Mathf.Max(0f, value.x);
                else continue;
                WriteControlValue(control, amount, eventPtr);
            }
        }

        private void WriteButtonActionToEvent(string actionName, bool pressed, InputEventPtr eventPtr, InputDevice device)
        {
            if (!resolvedActions.TryGetValue(actionName, out InputAction? action) || action == null) return;
            float value = pressed ? 1f : 0f;
            foreach (InputControl control in action.controls)
            {
                if (!BelongsToDevice(control, device)) continue;
                WriteControlValue(control, value, eventPtr);
            }
        }

        private static bool BelongsToDevice(InputControl control, InputDevice device) => ReferenceEquals(control.device, device);

        private static void WriteControlValue(InputControl control, float value, InputEventPtr eventPtr)
        {
            if (control is InputControl<float> floatControl)
            {
                floatControl.WriteValueIntoEvent(value, eventPtr);
                return;
            }
            if (control is ButtonControl button) button.WriteValueIntoEvent(value, eventPtr);
        }

        private void SaveRecording()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(tasPath, false))
                {
                    writer.WriteLine("UltraTAS v7");
                    writer.WriteLine("Seed=" + tasSeed);
                    writer.WriteLine("Frames=" + frames.Count);
                    writer.WriteLine("Trajectory=PositionVelocity");
                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            F(frame.Move.x)+","+F(frame.Move.y)+","+F(frame.Look.x)+","+F(frame.Look.y)+","+
                            F(frame.WheelLook.x)+","+F(frame.WheelLook.y)+","+
                            F(frame.Position.x)+","+F(frame.Position.y)+","+F(frame.Position.z)+","+
                            F(frame.Velocity.x)+","+F(frame.Velocity.y)+","+F(frame.Velocity.z)+","+Bits(frame));
                    }
                }
                Logger.LogInfo("UltraTAS: saved TAS v7 with movement trajectory and per-tick RNG synchronization. Frames: " + frames.Count);
            }
            catch (Exception ex)
            {
                Logger.LogError("UltraTAS: failed to save TAS: " + ex);
            }
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string Bits(TASFrame f)
        {
            return (f.Punch?"1":"0")+(f.Hook?"1":"0")+(f.Fire1?"1":"0")+(f.Fire2?"1":"0")+
                   (f.Jump?"1":"0")+(f.Slide?"1":"0")+(f.Dodge?"1":"0")+(f.ChangeFist?"1":"0")+
                   (f.NextVariation?"1":"0")+(f.PreviousVariation?"1":"0")+(f.NextWeapon?"1":"0")+(f.PrevWeapon?"1":"0")+
                   (f.LastWeapon?"1":"0")+(f.SelectVariant1?"1":"0")+(f.SelectVariant2?"1":"0")+(f.SelectVariant3?"1":"0")+
                   (f.Pause?"1":"0")+(f.Stats?"1":"0")+(f.Slot1?"1":"0")+(f.Slot2?"1":"0")+(f.Slot3?"1":"0")+
                   (f.Slot4?"1":"0")+(f.Slot5?"1":"0")+(f.Slot6?"1":"0");
        }
    }
}
