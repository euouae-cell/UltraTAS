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
    [BepInPlugin("ti0z1.UltraTAS", "UltraTAS", "1.3.7")]
    public class UltraTAS : BaseUnityPlugin
    {
        private sealed class TASFrame
        {
            public Vector2 Move, Look, WheelLook;
            public Vector3 Position, Velocity;

            /*
             * This is the exact Unity RNG state immediately BEFORE the
             * physics tick represented by this TAS frame.
             *
             * Restoring this prevents enemy AI that uses UnityEngine.Random
             * from starting a tick from a different RNG state.
             */
            public UnityEngine.Random.State RandomState;

            public bool Punch, Hook, Fire1, Fire2;
            public bool Jump, Slide, Dodge, ChangeFist;

            public bool NextVariation, PreviousVariation;
            public bool NextWeapon, PrevWeapon, LastWeapon;

            public bool SelectVariant1, SelectVariant2, SelectVariant3;

            public bool Pause, Stats;

            public bool Slot1, Slot2, Slot3, Slot4, Slot5, Slot6;
        }

        /*
         * Enemy replay state.
         *
         * This deliberately uses only public runtime state exposed by
         * Enemy/EnemyIdentifier. Private AI internals are left alone until
         * we have the concrete enemy subclasses that own them.
         *
         * The state is kept in memory for this TAS session. The existing
         * plain-text TAS file format does not serialize UnityEngine.Random.State
         * or these runtime enemy snapshots yet.
         */
        private sealed class EnemyReplayState
        {
            public int EnemyType;
            public int EnemyClass;

            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;

            public bool RigidbodyIsKinematic;
            public bool RigidbodyUseGravity;

            public float Health;
            public float IdentifierHealth;

            public bool Dead;
            public bool Exploded;
            public bool Stationary;

            public bool BeingZapped;
            public bool HasBeenZapped;
            public bool PulledByMagnet;
            public bool Underwater;

            public bool CheckingSpawnStatus;
            public bool Flying;

            public bool DontCountAsKills;
            public bool SpecialOob;

            public bool Hooked;
            public bool Harpooned;
            public bool BeenGasolined;

            public bool HookIgnore;
            public bool Sandified;
            public bool Blessed;
            public bool Puppeted;

            public float RadianceTier;

            public bool HealthBuff;
            public bool SpeedBuff;
            public bool DamageBuff;

            public float TotalSpeedModifier;
            public float TotalDamageModifier;
            public float TotalHealthModifier;

            public bool IsBoss;

            public bool IgnorePlayer;
            public bool AttackEnemies;
            public bool PrioritizePlayerOverFallback;
            public bool PrioritizeEnemiesUnlessAttacked;
            public bool Madness;

            public bool Limp;
            public bool Grounded;
            public bool KnockedBack;
            public bool Falling;
            public float FallTime;
            public float Brakes;
            public float JuggleWeight;
            public int ParryFramesLeft;
            public bool Parryable;
            public bool PartiallyParryable;
            public bool IsMassDeath;
            public bool IsMassDieing;

            public bool Stopped;
            public bool IsOnOffNavmeshLink;
            public bool ChestExploding;

            public float KnockBackCharge;
            public float FallSpeed;
            public float ChestHP;

            public bool Healing;
            public bool NoHeal;

            public float LastTargetTick;
            public Vector3 LastPos;

            public bool HasNavMeshAgent;
            public bool NavEnabled;
            public bool NavUpdatePosition;
            public bool NavUpdateRotation;
            public bool NavAutoTraverseOffMeshLink;
            public bool NavIsStopped;
            public Vector3 NavDestination;

            public TargetKind Target;
            public int TargetEnemyId = -1;
            public Vector3 TargetPosition;

            public int FallbackEnemyId = -1;
            public Vector3 FallbackPosition;
            public bool HasFallbackTarget;

            public float TimeSinceSpawned;
        }

        private enum TargetKind
        {
            None = 0,
            Player = 1,
            Enemy = 2
        }

        private sealed class EnemyIdentity
        {
            public int Id;
            public int EnemyType;
            public int EnemyClass;
            public Vector3 SpawnPosition;
            public Quaternion SpawnRotation;
        }

        private sealed class EnemyTickRecord
        {
            public int EnemyId;
            public UnityEngine.Random.State RandomStateBefore;
            public UnityEngine.Random.State RandomStateAfter;
            public EnemyReplayState StateBefore = new EnemyReplayState();
            public EnemyReplayState StateAfter = new EnemyReplayState();
        }

        private sealed class PendingEnemyTick
        {
            public int EnemyId;
            public UnityEngine.Random.State RandomStateBefore;
            public EnemyReplayState StateBefore = new EnemyReplayState();
        }

        private readonly Dictionary<global::Enemy, int> enemyIds =
            new Dictionary<global::Enemy, int>();

        private readonly Dictionary<int, global::Enemy> playbackEnemies =
            new Dictionary<int, global::Enemy>();

        private readonly HashSet<int> claimedPlaybackEnemyIds =
            new HashSet<int>();

        private readonly List<EnemyIdentity> enemyIdentities =
            new List<EnemyIdentity>();

        private readonly Dictionary<int, List<EnemyTickRecord>> enemyUpdateRecords =
            new Dictionary<int, List<EnemyTickRecord>>();

        private readonly Dictionary<int, List<EnemyTickRecord>> enemyFixedUpdateRecords =
            new Dictionary<int, List<EnemyTickRecord>>();

        private readonly Dictionary<int, int> playbackEnemyUpdateIndices =
            new Dictionary<int, int>();

        private readonly Dictionary<int, int> playbackEnemyFixedUpdateIndices =
            new Dictionary<int, int>();

        private readonly Dictionary<global::Enemy, PendingEnemyTick> pendingEnemyUpdates =
            new Dictionary<global::Enemy, PendingEnemyTick>();

        private readonly Dictionary<global::Enemy, PendingEnemyTick> pendingEnemyFixedUpdates =
            new Dictionary<global::Enemy, PendingEnemyTick>();

        private int nextRecordingEnemyId;
        private bool enemyReplayMismatchLogged;

        private const float EnemyPositionTolerance = 0.035f;
        private const float EnemyVelocityTolerance = 0.50f;
        private const float EnemyHealthTolerance = 0.01f;
        private const float EnemySpawnMatchDistance = 6.0f;

        private static readonly string[] PlayerInputActions =
        {
            "Move", "Look", "WheelLook",
            "Punch", "Hook", "Fire1", "Fire2",
            "Jump", "Slide", "Dodge", "ChangeFist",
            "NextVariation", "PreviousVariation",
            "NextWeapon", "PrevWeapon", "LastWeapon",
            "SelectVariant1", "SelectVariant2", "SelectVariant3",
            "Pause", "Stats",
            "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6"
        };

        private readonly List<TASFrame> frames = new List<TASFrame>();

        private readonly Dictionary<string, InputAction> resolvedActions =
            new Dictionary<string, InputAction>();

        private Harmony? harmony;

        private global::PlayerInput? playerInput;
        private global::NewMovement? newMovement;

        private Transform? playerTransform;
        private Rigidbody? playerBody;

        private bool recording;
        private bool playing;

        private int playbackFrame;
        private int playbackPhysicsFrame;
        private int tasSeed;

        private int lastPlaybackUnityFrame = -1;
        private int lastRecordingUnityFrame = -1;

        private string tasPath = string.Empty;

        private int lastPlaybackSlot = -1;

        private bool playbackInputQueued;
        private int queuedPlaybackFrame = -1;

        /*
         * These variables are ONLY used while recording.
         *
         * The RNG state is captured in the FixedUpdate PREFIX, before
         * ULTRAKILL gets a chance to consume random numbers for that tick.
         */
        private UnityEngine.Random.State pendingRecordingRandomState;
        private bool pendingRecordingRandomStateValid;

        private GUIStyle? tasStyle;

        private static UltraTAS? Instance { get; set; }

        /*
         * Large divergence threshold used only as a safety net.
         *
         * This is NOT a continuous correction system anymore.
         */
        private const float HardResyncDistance = 0.20f;

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

            Logger.LogInfo(
                "UltraTAS 1.3.7 loaded."
            );

            Logger.LogInfo(
                "Per-physics-tick Unity RNG synchronization enabled."
            );

            Logger.LogInfo(
                "Enemy Update/FixedUpdate state replay synchronization enabled."
            );

            Logger.LogInfo(
                "F6 = start/stop recording | F7 = playback | F8 = clear"
            );
        }

        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;

            if (playing)
            {
                ReleaseInjectedInput();
            }

            harmony?.UnpatchSelf();
            harmony = null;

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
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

        /*
         * This is the important part of the deterministic RNG fix.
         *
         * Prefix:
         *   - recording -> save the RNG state BEFORE this physics tick
         *   - playback  -> restore the RNG state recorded for this tick
         *
         * Postfix:
         *   - recording -> save the resulting player state
         *   - playback  -> verify the resulting player position
         */
        [HarmonyPatch(typeof(global::Enemy), "Update")]
        private static class EnemyUpdatePatch
        {
            private static void Prefix(global::Enemy __instance)
            {
                Instance?.OnEnemyUpdatePrefix(__instance);
            }

            private static void Postfix(global::Enemy __instance)
            {
                Instance?.OnEnemyUpdatePostfix(__instance);
            }
        }

        [HarmonyPatch(typeof(global::Enemy), "FixedUpdate")]
        private static class EnemyFixedUpdatePatch
        {
            private static void Prefix(global::Enemy __instance)
            {
                Instance?.OnEnemyFixedUpdatePrefix(__instance);
            }

            private static void Postfix(global::Enemy __instance)
            {
                Instance?.OnEnemyFixedUpdatePostfix(__instance);
            }
        }

        [HarmonyPatch(typeof(global::NewMovement), "FixedUpdate")]
        private static class NewMovementFixedUpdatePatch
        {
            private static void Prefix(global::NewMovement __instance)
            {
                Instance?.OnPlayerPhysicsTickPrefix(__instance);
            }

            private static void Postfix(global::NewMovement __instance)
            {
                Instance?.OnPlayerPhysicsTickPostfix(__instance);
            }
        }

        private void SetPlayerInput(global::PlayerInput input)
        {
            playerInput = input;

            ResolvePlayerPhysics();

            Logger.LogInfo(
                "UltraTAS: captured ULTRAKILL PlayerInput instance."
            );
        }

        private void ResolvePlayerPhysics()
        {
            playerTransform = null;
            playerBody = null;
            newMovement = null;

            try
            {
                newMovement = MonoSingleton<NewMovement>.Instance;

                if (newMovement == null)
                {
                    return;
                }

                playerBody = newMovement.rb;
                playerTransform = newMovement.transform;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "UltraTAS: could not resolve NewMovement: "
                    + ex.Message
                );
            }
        }

        private void OnPlayerPhysicsTickPrefix(
            global::NewMovement movement
        )
        {
            if (!ReferenceEquals(movement, newMovement))
            {
                newMovement = movement;
                playerBody = movement.rb;
                playerTransform = movement.transform;
            }

            /*
             * RECORDING
             *
             * Capture RNG BEFORE FixedUpdate runs.
             */
            if (recording)
            {
                pendingRecordingRandomState =
                    UnityEngine.Random.state;

                pendingRecordingRandomStateValid = true;

                return;
            }

            /*
             * PLAYBACK
             *
             * Restore the exact RNG state that existed at the beginning
             * of this recorded physics tick.
             */
            if (!playing)
            {
                return;
            }

            if (
                playbackPhysicsFrame < 0
                || playbackPhysicsFrame >= frames.Count
            )
            {
                return;
            }

            TASFrame frame =
                frames[playbackPhysicsFrame];

            UnityEngine.Random.state =
                frame.RandomState;
        }

        private void OnPlayerPhysicsTickPostfix(
            global::NewMovement movement
        )
        {
            if (!ReferenceEquals(movement, newMovement))
            {
                newMovement = movement;
                playerBody = movement.rb;
                playerTransform = movement.transform;
            }

            /*
             * RECORDING
             *
             * Save the completed physics state.
             */
            if (recording)
            {
                if (!pendingRecordingRandomStateValid)
                {
                    /*
                     * This should never happen, but don't produce a broken
                     * frame if a weird execution path occurs.
                     */
                    pendingRecordingRandomState =
                        UnityEngine.Random.state;
                }

                RecordFrameFromPhysics(
                    pendingRecordingRandomState
                );

                pendingRecordingRandomStateValid = false;

                return;
            }

            if (!playing)
            {
                return;
            }

            if (
                playbackPhysicsFrame < 0
                || playbackPhysicsFrame >= frames.Count
            )
            {
                return;
            }

            TASFrame frame =
                frames[playbackPhysicsFrame];

            VerifyAndResyncPlayer(frame);

            /*
             * Advance ONLY after the physics tick has completed.
             *
             * This is an actual physics-tick counter instead of using
             * Time.frameCount, which is a render-frame counter.
             */
            playbackPhysicsFrame++;
        }

        private void ResetEnemyReplayState()
        {
            enemyIds.Clear();
            playbackEnemies.Clear();
            claimedPlaybackEnemyIds.Clear();
            enemyIdentities.Clear();

            enemyUpdateRecords.Clear();
            enemyFixedUpdateRecords.Clear();

            playbackEnemyUpdateIndices.Clear();
            playbackEnemyFixedUpdateIndices.Clear();

            pendingEnemyUpdates.Clear();
            pendingEnemyFixedUpdates.Clear();

            nextRecordingEnemyId = 0;
            enemyReplayMismatchLogged = false;
        }

        private void ResetEnemyPlaybackState()
        {
            enemyIds.Clear();
            playbackEnemies.Clear();
            claimedPlaybackEnemyIds.Clear();

            playbackEnemyUpdateIndices.Clear();
            playbackEnemyFixedUpdateIndices.Clear();

            pendingEnemyUpdates.Clear();
            pendingEnemyFixedUpdates.Clear();

            enemyReplayMismatchLogged = false;
        }

        private void OnEnemyUpdatePrefix(
            global::Enemy enemy
        )
        {
            if (enemy == null)
            {
                return;
            }

            if (recording)
            {
                int id = GetOrAssignRecordingEnemyId(enemy);

                pendingEnemyUpdates[enemy] =
                    new PendingEnemyTick
                    {
                        EnemyId = id,
                        RandomStateBefore = UnityEngine.Random.state,
                        StateBefore = CaptureEnemyState(enemy)
                    };

                return;
            }

            if (!playing)
            {
                return;
            }

            if (!TryGetPlaybackEnemyEvent(
                    enemy,
                    enemyUpdateRecords,
                    playbackEnemyUpdateIndices,
                    out EnemyTickRecord? tickRecord))
            {
                return;
            }

            UnityEngine.Random.state =
                tickRecord.RandomStateBefore;

            ApplyEnemyState(
                enemy,
                tickRecord.StateBefore
            );

            pendingEnemyUpdates[enemy] =
                new PendingEnemyTick
                {
                    EnemyId = tickRecord.EnemyId,
                    RandomStateBefore = tickRecord.RandomStateBefore,
                    StateBefore = tickRecord.StateBefore
                };
        }

        private void OnEnemyUpdatePostfix(
            global::Enemy enemy
        )
        {
            if (enemy == null)
            {
                return;
            }

            if (recording)
            {
                if (
                    !pendingEnemyUpdates.TryGetValue(
                        enemy,
                        out PendingEnemyTick? pending
                    )
                )
                {
                    return;
                }

                int id = pending.EnemyId;

                EnemyTickRecord recordedTick =
                    new EnemyTickRecord
                    {
                        EnemyId = id,
                        RandomStateBefore = pending.RandomStateBefore,
                        RandomStateAfter = UnityEngine.Random.state,
                        StateBefore = pending.StateBefore,
                        StateAfter = CaptureEnemyState(enemy)
                    };

                AddEnemyRecord(
                    enemyUpdateRecords,
                    id,
                    record
                );

                pendingEnemyUpdates.Remove(enemy);

                return;
            }

            if (!playing)
            {
                return;
            }

            if (
                !pendingEnemyUpdates.TryGetValue(
                    enemy,
                    out PendingEnemyTick? pendingPlayback
                )
            )
            {
                return;
            }

            if (
                !TryGetCurrentPlaybackRecord(
                    pendingPlayback.EnemyId,
                    enemyUpdateRecords,
                    playbackEnemyUpdateIndices,
                    out EnemyTickRecord? record
                )
            )
            {
                pendingEnemyUpdates.Remove(enemy);
                return;
            }

            VerifyAndResyncEnemy(
                enemy,
                record.StateAfter,
                "Update"
            );

            /*
             * Force the global RNG to exactly the state that the original
             * enemy Update left behind. This makes the next system that
             * consumes UnityEngine.Random start from the same point.
             */
            UnityEngine.Random.state =
                record.RandomStateAfter;

            pendingEnemyUpdates.Remove(enemy);
        }

        private void OnEnemyFixedUpdatePrefix(
            global::Enemy enemy
        )
        {
            if (enemy == null)
            {
                return;
            }

            if (recording)
            {
                int id = GetOrAssignRecordingEnemyId(enemy);

                pendingEnemyFixedUpdates[enemy] =
                    new PendingEnemyTick
                    {
                        EnemyId = id,
                        RandomStateBefore = UnityEngine.Random.state,
                        StateBefore = CaptureEnemyState(enemy)
                    };

                return;
            }

            if (!playing)
            {
                return;
            }

            if (!TryGetPlaybackEnemyEvent(
                    enemy,
                    enemyFixedUpdateRecords,
                    playbackEnemyFixedUpdateIndices,
                    out EnemyTickRecord? tickRecord))
            {
                return;
            }

            UnityEngine.Random.state =
                tickRecord.RandomStateBefore;

            ApplyEnemyState(
                enemy,
                tickRecord.StateBefore
            );

            pendingEnemyFixedUpdates[enemy] =
                new PendingEnemyTick
                {
                    EnemyId = tickRecord.EnemyId,
                    RandomStateBefore = tickRecord.RandomStateBefore,
                    StateBefore = tickRecord.StateBefore
                };
        }

        private void OnEnemyFixedUpdatePostfix(
            global::Enemy enemy
        )
        {
            if (enemy == null)
            {
                return;
            }

            if (recording)
            {
                if (
                    !pendingEnemyFixedUpdates.TryGetValue(
                        enemy,
                        out PendingEnemyTick? pending
                    )
                )
                {
                    return;
                }

                int id = pending.EnemyId;

                EnemyTickRecord recordedTick =
                    new EnemyTickRecord
                    {
                        EnemyId = id,
                        RandomStateBefore = pending.RandomStateBefore,
                        RandomStateAfter = UnityEngine.Random.state,
                        StateBefore = pending.StateBefore,
                        StateAfter = CaptureEnemyState(enemy)
                    };

                AddEnemyRecord(
                    enemyFixedUpdateRecords,
                    id,
                    record
                );

                pendingEnemyFixedUpdates.Remove(enemy);

                return;
            }

            if (!playing)
            {
                return;
            }

            if (
                !pendingEnemyFixedUpdates.TryGetValue(
                    enemy,
                    out PendingEnemyTick? pendingPlayback
                )
            )
            {
                return;
            }

            if (
                !TryGetCurrentPlaybackRecord(
                    pendingPlayback.EnemyId,
                    enemyFixedUpdateRecords,
                    playbackEnemyFixedUpdateIndices,
                    out EnemyTickRecord? record
                )
            )
            {
                pendingEnemyFixedUpdates.Remove(enemy);
                return;
            }

            VerifyAndResyncEnemy(
                enemy,
                record.StateAfter,
                "FixedUpdate"
            );

            UnityEngine.Random.state =
                record.RandomStateAfter;

            pendingEnemyFixedUpdates.Remove(enemy);
        }

        private void AddEnemyRecord(
            Dictionary<int, List<EnemyTickRecord>> records,
            int enemyId,
            EnemyTickRecord record
        )
        {
            if (!records.TryGetValue(
                    enemyId,
                    out List<EnemyTickRecord>? list))
            {
                list = new List<EnemyTickRecord>();
                records[enemyId] = list;
            }

            list.Add(record);
        }

        private bool TryGetPlaybackEnemyEvent(
            global::Enemy enemy,
            Dictionary<int, List<EnemyTickRecord>> records,
            Dictionary<int, int> indices,
            out EnemyTickRecord? record
        )
        {
            record = null;

            int enemyId;

            if (!TryGetPlaybackEnemyId(
                    enemy,
                    out enemyId
                ))
            {
                return false;
            }

            if (!records.TryGetValue(
                    enemyId,
                    out List<EnemyTickRecord>? list))
            {
                return false;
            }

            int index =
                indices.TryGetValue(
                    enemyId,
                    out int currentIndex
                )
                    ? currentIndex
                    : 0;

            if (index < 0 || index >= list.Count)
            {
                return false;
            }

            record = list[index];

            indices[enemyId] =
                index + 1;

            return true;
        }

        private bool TryGetCurrentPlaybackRecord(
            int enemyId,
            Dictionary<int, List<EnemyTickRecord>> records,
            Dictionary<int, int> indices,
            out EnemyTickRecord? record
        )
        {
            record = null;

            if (!records.TryGetValue(
                    enemyId,
                    out List<EnemyTickRecord>? list))
            {
                return false;
            }

            int index;

            if (
                !indices.TryGetValue(
                    enemyId,
                    out index
                )
            )
            {
                return false;
            }

            index--;

            if (index < 0 || index >= list.Count)
            {
                return false;
            }

            record = list[index];

            return true;
        }

        private int GetLogicalEnemyIdForSnapshot(
            global::Enemy enemy
        )
        {
            if (recording)
            {
                return GetOrAssignRecordingEnemyId(enemy);
            }

            if (playing && enemyIds.TryGetValue(enemy, out int existingId))
            {
                return existingId;
            }

            return -1;
        }

        private int GetOrAssignRecordingEnemyId(
            global::Enemy enemy
        )
        {
            if (
                enemyIds.TryGetValue(
                    enemy,
                    out int existingId
                )
            )
            {
                return existingId;
            }

            int id = nextRecordingEnemyId++;

            enemyIds[enemy] = id;

            EnemyIdentity identity =
                new EnemyIdentity
                {
                    Id = id,
                    EnemyType =
                        enemy.EID != null
                            ? (int)enemy.EID.enemyType
                            : -1,
                    EnemyClass =
                        enemy.EID != null
                            ? (int)enemy.EID.enemyClass
                            : -1,
                    SpawnPosition =
                        enemy.transform.position,
                    SpawnRotation =
                        enemy.transform.rotation
                };

            enemyIdentities.Add(identity);

            return id;
        }

        private bool TryGetPlaybackEnemyId(
            global::Enemy enemy,
            out int enemyId
        )
        {
            if (
                enemyIds.TryGetValue(
                    enemy,
                    out enemyId
                )
            )
            {
                return true;
            }

            int enemyType =
                enemy.EID != null
                    ? (int)enemy.EID.enemyType
                    : -1;

            int enemyClass =
                enemy.EID != null
                    ? (int)enemy.EID.enemyClass
                    : -1;

            float bestDistance =
                float.PositiveInfinity;

            EnemyIdentity? best =
                null;

            foreach (EnemyIdentity identity in enemyIdentities)
            {
                if (
                    claimedPlaybackEnemyIds.Contains(
                        identity.Id
                    )
                )
                {
                    continue;
                }

                if (
                    identity.EnemyType != enemyType
                    || identity.EnemyClass != enemyClass
                )
                {
                    continue;
                }

                float distance =
                    Vector3.Distance(
                        identity.SpawnPosition,
                        enemy.transform.position
                    );

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = identity;
                }
            }

            /*
             * Spawn positions can legitimately differ slightly because of
             * portal/navmesh placement. Prefer type/class and nearest spawn,
             * while still rejecting wildly different objects.
             */
            if (
                best == null
                || bestDistance > EnemySpawnMatchDistance
            )
            {
                if (!enemyReplayMismatchLogged)
                {
                    Logger.LogWarning(
                        "UltraTAS: could not match a playback enemy to "
                        + "a recorded enemy identity. Enemy state replay "
                        + "will be skipped for this enemy."
                    );

                    enemyReplayMismatchLogged = true;
                }

                enemyId = -1;
                return false;
            }

            enemyId = best.Id;

            enemyIds[enemy] =
                enemyId;

            playbackEnemies[enemyId] =
                enemy;

            claimedPlaybackEnemyIds.Add(
                enemyId
            );

            return true;
        }

        private EnemyReplayState CaptureEnemyState(
            global::Enemy enemy
        )
        {
            EnemyReplayState state =
                new EnemyReplayState();

            global::EnemyIdentifier eid =
                enemy.EID;

            state.EnemyType =
                eid != null
                    ? (int)eid.enemyType
                    : -1;

            state.EnemyClass =
                eid != null
                    ? (int)eid.enemyClass
                    : -1;

            state.Position =
                enemy.transform.position;

            state.Rotation =
                enemy.transform.rotation;

            Rigidbody? rb =
                enemy.Rigidbody;

            if (rb != null)
            {
                state.Velocity =
                    rb.velocity;

                state.AngularVelocity =
                    rb.angularVelocity;

                state.RigidbodyIsKinematic =
                    rb.isKinematic;

                state.RigidbodyUseGravity =
                    rb.useGravity;
            }

            state.Health =
                enemy.health;

            if (eid != null)
            {
                state.IdentifierHealth =
                    eid.health;

                state.Dead =
                    eid.dead;

                state.Exploded =
                    eid.exploded;

                state.Stationary =
                    eid.stationary;

                state.BeingZapped =
                    eid.beingZapped;

                state.HasBeenZapped =
                    eid.hasBeenZapped;

                state.PulledByMagnet =
                    eid.pulledByMagnet;

                state.Underwater =
                    eid.underwater;

                state.CheckingSpawnStatus =
                    eid.checkingSpawnStatus;

                state.Flying =
                    eid.flying;

                state.DontCountAsKills =
                    eid.dontCountAsKills;

                state.SpecialOob =
                    eid.specialOob;

                state.Hooked =
                    eid.hooked;

                state.Harpooned =
                    eid.harpooned;

                state.BeenGasolined =
                    eid.beenGasolined;

                state.HookIgnore =
                    eid.hookIgnore;

                state.Sandified =
                    eid.sandified;

                state.Blessed =
                    eid.blessed;

                state.Puppeted =
                    eid.puppet;

                state.RadianceTier =
                    eid.radianceTier;

                state.HealthBuff =
                    eid.healthBuff;

                state.SpeedBuff =
                    eid.speedBuff;

                state.DamageBuff =
                    eid.damageBuff;

                state.TotalSpeedModifier =
                    eid.totalSpeedModifier;

                state.TotalDamageModifier =
                    eid.totalDamageModifier;

                state.TotalHealthModifier =
                    eid.totalHealthModifier;

                state.IsBoss =
                    eid.isBoss;

                state.IgnorePlayer =
                    eid.IgnorePlayer;

                state.AttackEnemies =
                    eid.AttackEnemies;

                state.PrioritizePlayerOverFallback =
                    eid.prioritizePlayerOverFallback;

                state.PrioritizeEnemiesUnlessAttacked =
                    eid.prioritizeEnemiesUnlessAttacked;

                state.Madness =
                    eid.madness;

                state.TimeSinceSpawned =
                    (float)eid.timeSinceSpawned;

                if (eid.target != null)
                {
                    if (eid.target.isPlayer)
                    {
                        state.Target =
                            TargetKind.Player;

                        state.TargetPosition =
                            eid.target.position;
                    }
                    else if (eid.target.isEnemy)
                    {
                        state.Target =
                            TargetKind.Enemy;

                        state.TargetPosition =
                            eid.target.position;

                        Transform targetTransform =
                            eid.target.targetTransform;

                        if (targetTransform != null)
                        {
                            global::EnemyIdentifier targetEid =
                                targetTransform.GetComponentInParent<
                                    global::EnemyIdentifier
                                >();

                            if (targetEid == null)
                            {
                                targetEid =
                                    targetTransform.GetComponentInChildren<
                                        global::EnemyIdentifier
                                    >();
                            }

                            if (targetEid != null)
                            {
                                global::Enemy targetEnemy =
                                    FindEnemyForIdentifier(
                                        targetEid
                                    );

                                if (targetEnemy != null)
                                {
                                    state.TargetEnemyId =
                                        GetOrAssignRecordingEnemyId(
                                            targetEnemy
                                        );
                                }
                            }
                        }
                    }
                }

                if (eid.fallbackTarget != null)
                {
                    state.HasFallbackTarget =
                        true;

                    state.FallbackPosition =
                        eid.fallbackTarget.position;

                    global::Enemy fallbackEnemy =
                        eid.fallbackTarget.GetComponentInParent<
                            global::Enemy
                        >();

                    if (fallbackEnemy != null)
                    {
                        state.FallbackEnemyId =
                            GetLogicalEnemyIdForSnapshot(
                                fallbackEnemy
                            );
                    }
                }
            }

            state.Limp =
                enemy.limp;

            state.Grounded =
                enemy.grounded;

            state.KnockedBack =
                enemy.knockedBack;

            state.Falling =
                enemy.falling;

            state.FallTime =
                enemy.fallTime;

            state.Brakes =
                enemy.brakes;

            state.JuggleWeight =
                enemy.juggleWeight;

            state.ParryFramesLeft =
                enemy.parryFramesLeft;

            state.Parryable =
                enemy.parryable;

            state.PartiallyParryable =
                enemy.partiallyParryable;

            state.IsMassDeath =
                enemy.isMassDeath;

            state.IsMassDieing =
                enemy.isMassDieing;

            state.Stopped =
                enemy.stopped;

            state.IsOnOffNavmeshLink =
                enemy.isOnOffNavmeshLink;

            state.ChestExploding =
                enemy.chestExploding;

            state.LastTargetTick =
                (float)enemy.lastTargetTick;

            state.LastPos =
                enemy.lastPos;

            if (enemy.nma != null)
            {
                state.HasNavMeshAgent =
                    true;

                state.NavEnabled =
                    enemy.nma.enabled;

                state.NavUpdatePosition =
                    enemy.nma.updatePosition;

                state.NavUpdateRotation =
                    enemy.nma.updateRotation;

                state.NavAutoTraverseOffMeshLink =
                    enemy.nma.autoTraverseOffMeshLink;

                state.NavIsStopped =
                    enemy.nma.isStopped;

                state.NavDestination =
                    enemy.nma.destination;
            }

            return state;
        }

        private void ApplyEnemyState(
            global::Enemy enemy,
            EnemyReplayState state
        )
        {
            if (enemy == null || state == null)
            {
                return;
            }

            enemy.health =
                state.Health;

            enemy.limp =
                state.Limp;

            enemy.grounded =
                state.Grounded;

            enemy.knockedBack =
                state.KnockedBack;

            enemy.falling =
                state.Falling;

            enemy.fallTime =
                state.FallTime;

            enemy.brakes =
                state.Brakes;

            enemy.juggleWeight =
                state.JuggleWeight;

            enemy.parryFramesLeft =
                state.ParryFramesLeft;

            enemy.parryable =
                state.Parryable;

            enemy.partiallyParryable =
                state.PartiallyParryable;

            enemy.isMassDeath =
                state.IsMassDeath;

            enemy.isMassDieing =
                state.IsMassDieing;

            enemy.stopped =
                state.Stopped;

            enemy.isOnOffNavmeshLink =
                state.IsOnOffNavmeshLink;

            enemy.chestExploding =
                state.ChestExploding;

            enemy.lastTargetTick =
                state.LastTargetTick;

            enemy.lastPos =
                state.LastPos;

            global::EnemyIdentifier? eid =
                enemy.EID;

            if (eid != null)
            {
                eid.health =
                    state.IdentifierHealth;

                eid.dead =
                    state.Dead;

                eid.exploded =
                    state.Exploded;

                eid.stationary =
                    state.Stationary;

                eid.beingZapped =
                    state.BeingZapped;

                eid.hasBeenZapped =
                    state.HasBeenZapped;

                eid.pulledByMagnet =
                    state.PulledByMagnet;

                eid.underwater =
                    state.Underwater;

                eid.checkingSpawnStatus =
                    state.CheckingSpawnStatus;

                eid.flying =
                    state.Flying;

                eid.dontCountAsKills =
                    state.DontCountAsKills;

                eid.specialOob =
                    state.SpecialOob;

                eid.hooked =
                    state.Hooked;

                eid.harpooned =
                    state.Harpooned;

                eid.beenGasolined =
                    state.BeenGasolined;

                eid.hookIgnore =
                    state.HookIgnore;

                eid.sandified =
                    state.Sandified;

                eid.blessed =
                    state.Blessed;

                eid.puppet =
                    state.Puppeted;

                eid.radianceTier =
                    state.RadianceTier;

                eid.healthBuff =
                    state.HealthBuff;

                eid.speedBuff =
                    state.SpeedBuff;

                eid.damageBuff =
                    state.DamageBuff;

                eid.totalSpeedModifier =
                    state.TotalSpeedModifier;

                eid.totalDamageModifier =
                    state.TotalDamageModifier;

                eid.totalHealthModifier =
                    state.TotalHealthModifier;

                eid.isBoss =
                    state.IsBoss;

                eid.ignorePlayer =
                    state.IgnorePlayer;

                eid.attackEnemies =
                    state.AttackEnemies;

                eid.prioritizePlayerOverFallback =
                    state.PrioritizePlayerOverFallback;

                eid.prioritizeEnemiesUnlessAttacked =
                    state.PrioritizeEnemiesUnlessAttacked;

                eid.madness =
                    state.Madness;

                eid.timeSinceSpawned =
                    state.TimeSinceSpawned;

                RestoreEnemyTarget(
                    enemy,
                    state
                );
            }

            Rigidbody? rb =
                enemy.Rigidbody;

            if (rb != null)
            {
                rb.position =
                    state.Position;

                rb.rotation =
                    state.Rotation;

                rb.velocity =
                    state.Velocity;

                rb.angularVelocity =
                    state.AngularVelocity;

                rb.isKinematic =
                    state.RigidbodyIsKinematic;

                rb.useGravity =
                    state.RigidbodyUseGravity;
            }
            else
            {
                enemy.transform.SetPositionAndRotation(
                    state.Position,
                    state.Rotation
                );
            }

            if (enemy.nma != null && state.HasNavMeshAgent)
            {
                enemy.nma.updatePosition =
                    state.NavUpdatePosition;

                enemy.nma.updateRotation =
                    state.NavUpdateRotation;

                enemy.nma.autoTraverseOffMeshLink =
                    state.NavAutoTraverseOffMeshLink;

                enemy.nma.isStopped =
                    state.NavIsStopped;

                if (enemy.nma.enabled != state.NavEnabled)
                {
                    enemy.nma.enabled =
                        state.NavEnabled;
                }

                if (
                    enemy.nma.enabled
                    && enemy.nma.isOnNavMesh
                )
                {
                    if (
                        (enemy.nma.destination - state.NavDestination)
                            .sqrMagnitude
                        > 0.0001f
                    )
                    {
                        enemy.nma.SetDestination(
                            state.NavDestination
                        );
                    }
                }
            }

            Physics.SyncTransforms();
        }

        private void RestoreEnemyTarget(
            global::Enemy enemy,
            EnemyReplayState state
        )
        {
            global::EnemyIdentifier eid =
                enemy.EID;

            if (eid == null)
            {
                return;
            }

            switch (state.Target)
            {
                case TargetKind.Player:
                    eid.target =
                        EnemyTarget.TrackPlayer();
                    break;

                case TargetKind.Enemy:
                {
                    if (
                        state.TargetEnemyId >= 0
                        && playbackEnemies.TryGetValue(
                            state.TargetEnemyId,
                            out global::Enemy targetEnemy
                        )
                        && targetEnemy != null
                    )
                    {
                        eid.target =
                            new EnemyTarget(
                                targetEnemy.transform
                            );
                    }
                    else
                    {
                        eid.target =
                            null;
                    }

                    break;
                }

                default:
                    eid.target =
                        null;
                    break;
            }

            if (state.HasFallbackTarget)
            {
                if (
                    state.FallbackEnemyId >= 0
                    && playbackEnemies.TryGetValue(
                        state.FallbackEnemyId,
                        out global::Enemy fallbackEnemy
                    )
                    && fallbackEnemy != null
                )
                {
                    eid.fallbackTarget =
                        fallbackEnemy.transform;
                }
                else
                {
                    eid.fallbackTarget =
                        null;
                }
            }
            else
            {
                eid.fallbackTarget =
                    null;
            }
        }

        private global::Enemy? FindEnemyForIdentifier(
            global::EnemyIdentifier identifier
        )
        {
            if (identifier == null)
            {
                return null;
            }

            global::Enemy direct =
                identifier.GetComponent<
                    global::Enemy
                >();

            if (direct != null)
            {
                return direct;
            }

            return identifier.GetComponentInChildren<
                global::Enemy
            >();
        }

        private void VerifyAndResyncEnemy(
            global::Enemy enemy,
            EnemyReplayState recorded,
            string phase
        )
        {
            EnemyReplayState current =
                CaptureEnemyState(enemy);

            bool mismatch =
                Vector3.Distance(
                    current.Position,
                    recorded.Position
                ) > EnemyPositionTolerance
                || Vector3.Distance(
                    current.Velocity,
                    recorded.Velocity
                ) > EnemyVelocityTolerance
                || Mathf.Abs(
                    current.Health
                    - recorded.Health
                ) > EnemyHealthTolerance
                || current.Dead != recorded.Dead
                || current.Exploded != recorded.Exploded
                || current.Limp != recorded.Limp
                || current.Falling != recorded.Falling
                || current.KnockedBack != recorded.KnockedBack
                || current.Grounded != recorded.Grounded
                || current.Stopped != recorded.Stopped
                || current.IsOnOffNavmeshLink
                    != recorded.IsOnOffNavmeshLink
                || current.ChestExploding
                    != recorded.ChestExploding
                || current.Target
                    != recorded.Target
                || current.TargetEnemyId
                    != recorded.TargetEnemyId;

            if (!mismatch)
            {
                return;
            }

            int enemyId =
                enemyIds.TryGetValue(
                    enemy,
                    out int id
                )
                    ? id
                    : -1;

            Logger.LogWarning(
                "UltraTAS: enemy state desync at "
                + phase
                + ". EnemyId="
                + enemyId
                + ", PosError="
                + Vector3.Distance(
                    current.Position,
                    recorded.Position
                ).ToString(
                    "F3",
                    CultureInfo.InvariantCulture
                )
                + "m, VelError="
                + Vector3.Distance(
                    current.Velocity,
                    recorded.Velocity
                ).ToString(
                    "F3",
                    CultureInfo.InvariantCulture
                )
                + ", Health="
                + current.Health.ToString(
                    "F3",
                    CultureInfo.InvariantCulture
                )
                + "->"
                + recorded.Health.ToString(
                    "F3",
                    CultureInfo.InvariantCulture
                )
            );

            ApplyEnemyState(
                enemy,
                recorded
            );
        }

        private void OnBeforeInputUpdate()
        {
            if (!playing)
            {
                return;
            }

            /*
             * Input still has to be injected through Unity's Input System
             * update boundary.
             *
             * This keeps the exact recorded button/mouse states without
             * inventing artificial minimum press durations.
             */
            int unityFrame = Time.frameCount;

            if (unityFrame == lastPlaybackUnityFrame)
            {
                return;
            }

            lastPlaybackUnityFrame = unityFrame;

            PlayFrame();
        }

        private void OnAfterInputUpdate()
        {
            if (recording)
            {
                int unityFrame = Time.frameCount;

                if (unityFrame != lastRecordingUnityFrame)
                {
                    lastRecordingUnityFrame = unityFrame;
                }
            }

            if (playing && playbackInputQueued)
            {
                playbackInputQueued = false;
                queuedPlaybackFrame = -1;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (recording)
                {
                    StopRecording();
                }
                else
                {
                    StartRecording();
                }
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (playing)
                {
                    StopPlayback();
                }
                else
                {
                    StartPlayback();
                }
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                ClearRecording();
            }
        }

        private void OnGUI()
        {
            if (!recording && !playing)
            {
                return;
            }

            if (tasStyle == null)
            {
                tasStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft
                };

                tasStyle.normal.textColor =
                    new Color(1f, 1f, 1f, 0.75f);
            }

            GUI.Label(
                new Rect(15f, 15f, 100f, 40f),
                "TAS",
                tasStyle
            );
        }

        private bool ResolvePlayerInput()
        {
            if (playerInput == null)
            {
                Logger.LogWarning(
                    "UltraTAS: PlayerInput has not been captured yet."
                );

                return false;
            }

            resolvedActions.Clear();

            AddAction(
                "Move",
                playerInput.Actions.Movement.Move
            );

            AddAction(
                "Look",
                playerInput.Actions.Movement.Look
            );

            AddAction(
                "WheelLook",
                playerInput.Actions.Weapon.WheelLook
            );

            AddAction(
                "Fire1",
                playerInput.Actions.Weapon.PrimaryFire
            );

            AddAction(
                "Fire2",
                playerInput.Actions.Weapon.SecondaryFire
            );

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
                "Slot1",
                playerInput.Actions.Weapon.Revolver
            );

            AddAction(
                "Slot2",
                playerInput.Actions.Weapon.Shotgun
            );

            AddAction(
                "Slot3",
                playerInput.Actions.Weapon.Nailgun
            );

            AddAction(
                "Slot4",
                playerInput.Actions.Weapon.Railcannon
            );

            AddAction(
                "Slot5",
                playerInput.Actions.Weapon.RocketLauncher
            );

            AddAction(
                "Slot6",
                playerInput.Actions.Weapon.SpawnerArm
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

            AddAction(
                "Punch",
                playerInput.Actions.Fist.Punch
            );

            AddAction(
                "Hook",
                playerInput.Actions.Fist.Hook
            );

            AddAction(
                "ChangeFist",
                playerInput.Actions.Fist.ChangeFist
            );

            AddAction(
                "Jump",
                playerInput.Actions.Movement.Jump
            );

            AddAction(
                "Slide",
                playerInput.Actions.Movement.Slide
            );

            AddAction(
                "Dodge",
                playerInput.Actions.Movement.Dodge
            );

            AddAction(
                "Pause",
                playerInput.Actions.UI.Pause
            );

            AddAction(
                "Stats",
                playerInput.Actions.HUD.Stats
            );

            ResolvePlayerPhysics();

            Logger.LogInfo(
                "UltraTAS: resolved "
                + resolvedActions.Count
                + "/"
                + PlayerInputActions.Length
                + " native actions."
            );

            return resolvedActions.Count > 0;
        }

        private void AddAction(
            string name,
            InputAction? action
        )
        {
            if (action == null)
            {
                Logger.LogWarning(
                    "UltraTAS: native action is null: "
                    + name
                );

                return;
            }

            resolvedActions[name] = action;
        }

        private void StartRecording()
        {
            if (!ResolvePlayerInput())
            {
                return;
            }

            playing = false;

            frames.Clear();

            ResetEnemyReplayState();

            playbackFrame = 0;
            playbackPhysicsFrame = 0;

            lastRecordingUnityFrame =
                Time.frameCount;

            lastPlaybackUnityFrame = -1;

            lastPlaybackSlot = -1;

            playbackInputQueued = false;
            queuedPlaybackFrame = -1;

            pendingRecordingRandomStateValid =
                false;

            tasSeed = Environment.TickCount;

            UnityEngine.Random.InitState(
                tasSeed
            );

            recording = true;

            Logger.LogInfo(
                "TAS recording started. Seed: "
                + tasSeed
            );
        }

        private void StopRecording()
        {
            recording = false;

            pendingRecordingRandomStateValid =
                false;

            SaveRecording();

            Logger.LogInfo(
                "TAS recording stopped. Frames: "
                + frames.Count
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
            {
                return;
            }

            recording = false;

            ResetEnemyPlaybackState();

            UnityEngine.Random.InitState(
                tasSeed
            );

            playbackFrame = 0;
            playbackPhysicsFrame = 0;

            lastPlaybackUnityFrame =
                Time.frameCount;

            lastRecordingUnityFrame = -1;

            lastPlaybackSlot = -1;

            playbackInputQueued = false;
            queuedPlaybackFrame = -1;

            playing = true;

            /*
             * The first FixedUpdate will restore frames[0].RandomState
             * immediately before ULTRAKILL performs that physics tick.
             */
            Logger.LogInfo(
                "TAS playback started. Frames: "
                + frames.Count
                + ", Seed: "
                + tasSeed
            );
        }

        private void StopPlayback()
        {
            if (!playing)
            {
                return;
            }

            playing = false;

            playbackInputQueued = false;
            queuedPlaybackFrame = -1;

            ReleaseInjectedInput();

            lastPlaybackSlot = -1;

            playbackPhysicsFrame = 0;

            Logger.LogInfo(
                "TAS playback stopped at frame "
                + playbackFrame
                + "."
            );
        }

        private void ClearRecording()
        {
            if (playing)
            {
                ReleaseInjectedInput();
            }

            frames.Clear();

            ResetEnemyReplayState();

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

            pendingRecordingRandomStateValid =
                false;

            Logger.LogInfo(
                "TAS recording cleared."
            );
        }

        private void RecordFrameFromPhysics(
            UnityEngine.Random.State randomState
        )
        {
            if (!recording)
            {
                return;
            }

            if (resolvedActions.Count == 0)
            {
                return;
            }

            frames.Add(
                new TASFrame
                {
                    Move = ReadVector2("Move"),
                    Look = ReadVector2("Look"),
                    WheelLook = ReadVector2("WheelLook"),

                    Position = GetPlayerPosition(),
                    Velocity = GetPlayerVelocity(),

                    RandomState = randomState,

                    Punch = ReadButton("Punch"),
                    Hook = ReadButton("Hook"),

                    Fire1 = ReadButton("Fire1"),
                    Fire2 = ReadButton("Fire2"),

                    Jump = ReadButton("Jump"),
                    Slide = ReadButton("Slide"),
                    Dodge = ReadButton("Dodge"),

                    ChangeFist = ReadButton(
                        "ChangeFist"
                    ),

                    NextVariation = ReadButton(
                        "NextVariation"
                    ),

                    PreviousVariation = ReadButton(
                        "PreviousVariation"
                    ),

                    NextWeapon = ReadButton(
                        "NextWeapon"
                    ),

                    PrevWeapon = ReadButton(
                        "PrevWeapon"
                    ),

                    LastWeapon = ReadButton(
                        "LastWeapon"
                    ),

                    SelectVariant1 = ReadButton(
                        "SelectVariant1"
                    ),

                    SelectVariant2 = ReadButton(
                        "SelectVariant2"
                    ),

                    SelectVariant3 = ReadButton(
                        "SelectVariant3"
                    ),

                    Pause = ReadButton("Pause"),
                    Stats = ReadButton("Stats"),

                    Slot1 = ReadButton("Slot1"),
                    Slot2 = ReadButton("Slot2"),
                    Slot3 = ReadButton("Slot3"),
                    Slot4 = ReadButton("Slot4"),
                    Slot5 = ReadButton("Slot5"),
                    Slot6 = ReadButton("Slot6")
                }
            );
        }

        private Vector3 GetPlayerPosition()
        {
            if (playerBody != null)
            {
                return playerBody.position;
            }

            if (playerTransform != null)
            {
                return playerTransform.position;
            }

            return Vector3.zero;
        }

        private Vector3 GetPlayerVelocity()
        {
            if (playerBody != null)
            {
                return playerBody.velocity;
            }

            return Vector3.zero;
        }

        /*
         * This remains a safety net rather than the normal synchronization
         * mechanism.
         *
         * We do NOT constantly move the player toward the recorded position.
         */
        private void VerifyAndResyncPlayer(
            TASFrame frame
        )
        {
            if (
                playerBody == null
                || playerTransform == null
            )
            {
                ResolvePlayerPhysics();
            }

            if (
                playerBody == null
                || playerTransform == null
            )
            {
                return;
            }

            Vector3 error =
                frame.Position
                - playerBody.position;

            float distance =
                error.magnitude;

            if (distance <= HardResyncDistance)
            {
                return;
            }

            Logger.LogWarning(
                "UltraTAS: player desync detected at physics frame "
                + playbackPhysicsFrame
                + ". Error: "
                + distance.ToString(
                    "F3",
                    CultureInfo.InvariantCulture
                )
                + "m. Resynchronizing."
            );

            playerBody.position =
                frame.Position;

            playerBody.velocity =
                frame.Velocity;

            Physics.SyncTransforms();
        }

        private Vector2 ReadVector2(
            string actionName
        )
        {
            if (
                !resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action
                )
                || action == null
            )
            {
                return Vector2.zero;
            }

            return action.ReadValue<Vector2>();
        }

        private bool ReadButton(
            string actionName
        )
        {
            if (
                !resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action
                )
                || action == null
            )
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

            TASFrame frame =
                frames[playbackFrame];

            QueueKeyboardFrame(frame);
            QueueMouseFrame(frame);

            playbackInputQueued = true;
            queuedPlaybackFrame =
                playbackFrame;

            ProcessWeaponTransition(
                frame
            );

            playbackFrame++;
        }

        private void ProcessWeaponTransition(
            TASFrame frame
        )
        {
            int requestedSlot =
                GetRequestedSlot(frame);

            if (requestedSlot < 0)
            {
                return;
            }

            if (
                requestedSlot
                == lastPlaybackSlot
            )
            {
                return;
            }

            lastPlaybackSlot =
                requestedSlot;
        }

        private static int GetRequestedSlot(
            TASFrame frame
        )
        {
            if (frame.Slot1) return 1;
            if (frame.Slot2) return 2;
            if (frame.Slot3) return 3;
            if (frame.Slot4) return 4;
            if (frame.Slot5) return 5;
            if (frame.Slot6) return 6;

            return -1;
        }

        private void QueueKeyboardFrame(
            TASFrame frame
        )
        {
            Keyboard? keyboard =
                Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

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

                InputSystem.QueueEvent(
                    eventPtr
                );
            }
        }

        private void QueueMouseFrame(
            TASFrame frame
        )
        {
            Mouse? mouse =
                Mouse.current;

            if (mouse == null)
            {
                return;
            }

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

                WriteButtonActionToEvent(
                    "Fire1",
                    frame.Fire1,
                    eventPtr,
                    mouse
                );

                WriteButtonActionToEvent(
                    "Fire2",
                    frame.Fire2,
                    eventPtr,
                    mouse
                );

                InputSystem.QueueEvent(
                    eventPtr
                );
            }
        }

        private void ReleaseInjectedInput()
        {
            Keyboard? keyboard =
                Keyboard.current;

            if (keyboard != null)
            {
                using (
                    StateEvent.From(
                        keyboard,
                        out InputEventPtr eventPtr
                    )
                )
                {
                    foreach (
                        string actionName
                        in PlayerInputActions
                    )
                    {
                        if (
                            actionName == "Move"
                            || actionName == "Look"
                            || actionName == "WheelLook"
                            || actionName == "Fire1"
                            || actionName == "Fire2"
                        )
                        {
                            continue;
                        }

                        WriteButtonActionToEvent(
                            actionName,
                            false,
                            eventPtr,
                            keyboard
                        );
                    }

                    WriteActionToEvent(
                        "Move",
                        Vector2.zero,
                        eventPtr,
                        keyboard
                    );

                    InputSystem.QueueEvent(
                        eventPtr
                    );
                }
            }

            Mouse? mouse =
                Mouse.current;

            if (mouse != null)
            {
                using (
                    StateEvent.From(
                        mouse,
                        out InputEventPtr eventPtr
                    )
                )
                {
                    WriteActionToEvent(
                        "Look",
                        Vector2.zero,
                        eventPtr,
                        mouse
                    );

                    WriteActionToEvent(
                        "WheelLook",
                        Vector2.zero,
                        eventPtr,
                        mouse
                    );

                    WriteButtonActionToEvent(
                        "Fire1",
                        false,
                        eventPtr,
                        mouse
                    );

                    WriteButtonActionToEvent(
                        "Fire2",
                        false,
                        eventPtr,
                        mouse
                    );

                    InputSystem.QueueEvent(
                        eventPtr
                    );
                }
            }
        }

        private void WriteActionToEvent(
            string actionName,
            Vector2 value,
            InputEventPtr eventPtr,
            InputDevice device
        )
        {
            if (
                !resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action
                )
                || action == null
            )
            {
                return;
            }

            foreach (InputControl control in action.controls)
            {
                if (
                    !BelongsToDevice(
                        control,
                        device
                    )
                )
                {
                    continue;
                }

                if (
                    control
                    is Vector2Control vector2
                )
                {
                    vector2.WriteValueIntoEvent(
                        value,
                        eventPtr
                    );

                    continue;
                }

                string path =
                    control.path.ToLowerInvariant();

                float amount;

                if (
                    path.EndsWith("/w")
                    || path.EndsWith("/up")
                )
                {
                    amount =
                        Mathf.Max(
                            0f,
                            value.y
                        );
                }
                else if (
                    path.EndsWith("/s")
                    || path.EndsWith("/down")
                )
                {
                    amount =
                        Mathf.Max(
                            0f,
                            -value.y
                        );
                }
                else if (
                    path.EndsWith("/a")
                    || path.EndsWith("/left")
                )
                {
                    amount =
                        Mathf.Max(
                            0f,
                            -value.x
                        );
                }
                else if (
                    path.EndsWith("/d")
                    || path.EndsWith("/right")
                )
                {
                    amount =
                        Mathf.Max(
                            0f,
                            value.x
                        );
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
            InputDevice device
        )
        {
            if (
                !resolvedActions.TryGetValue(
                    actionName,
                    out InputAction? action
                )
                || action == null
            )
            {
                return;
            }

            float value =
                pressed
                    ? 1f
                    : 0f;

            foreach (InputControl control in action.controls)
            {
                if (
                    !BelongsToDevice(
                        control,
                        device
                    )
                )
                {
                    continue;
                }

                WriteControlValue(
                    control,
                    value,
                    eventPtr
                );
            }
        }

        private static bool BelongsToDevice(
            InputControl control,
            InputDevice device
        )
        {
            return ReferenceEquals(
                control.device,
                device
            );
        }

        private static void WriteControlValue(
            InputControl control,
            float value,
            InputEventPtr eventPtr
        )
        {
            if (
                control
                is InputControl<float>
                floatControl
            )
            {
                floatControl.WriteValueIntoEvent(
                    value,
                    eventPtr
                );

                return;
            }

            if (
                control
                is ButtonControl button
            )
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
                        new StreamWriter(
                            tasPath,
                            false
                        )
                )
                {
                    /*
                     * The file is still v7-compatible for the ordinary
                     * input/trajectory data.
                     *
                     * Random.State itself is kept in memory because Unity's
                     * internal RNG state is not safely representable using
                     * the existing plain-text format.
                     */
                    writer.WriteLine(
                        "UltraTAS v8"
                    );

                    writer.WriteLine(
                        "Seed=" + tasSeed
                    );

                    writer.WriteLine(
                        "Frames=" + frames.Count
                    );

                    writer.WriteLine(
                        "Trajectory=PositionVelocity"
                    );

                    writer.WriteLine(
                        "EnemyReplay=InMemoryOnly"
                    );

                    foreach (TASFrame frame in frames)
                    {
                        writer.WriteLine(
                            F(frame.Move.x)
                            + ","
                            + F(frame.Move.y)
                            + ","
                            + F(frame.Look.x)
                            + ","
                            + F(frame.Look.y)
                            + ","
                            + F(frame.WheelLook.x)
                            + ","
                            + F(frame.WheelLook.y)
                            + ","
                            + F(frame.Position.x)
                            + ","
                            + F(frame.Position.y)
                            + ","
                            + F(frame.Position.z)
                            + ","
                            + F(frame.Velocity.x)
                            + ","
                            + F(frame.Velocity.y)
                            + ","
                            + F(frame.Velocity.z)
                            + ","
                            + Bits(frame)
                        );
                    }
                }

                Logger.LogInfo(
                    "UltraTAS: saved TAS v7 with "
                    + "movement trajectory and "
                    + "per-tick RNG synchronization. Frames: "
                    + frames.Count
                );
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "UltraTAS: failed to save TAS: "
                    + ex
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

        private static string Bits(
            TASFrame f
        )
        {
            return
                (f.Punch ? "1" : "0")
                + (f.Hook ? "1" : "0")
                + (f.Fire1 ? "1" : "0")
                + (f.Fire2 ? "1" : "0")
                + (f.Jump ? "1" : "0")
                + (f.Slide ? "1" : "0")
                + (f.Dodge ? "1" : "0")
                + (f.ChangeFist ? "1" : "0")
                + (f.NextVariation ? "1" : "0")
                + (f.PreviousVariation ? "1" : "0")
                + (f.NextWeapon ? "1" : "0")
                + (f.PrevWeapon ? "1" : "0")
                + (f.LastWeapon ? "1" : "0")
                + (f.SelectVariant1 ? "1" : "0")
                + (f.SelectVariant2 ? "1" : "0")
                + (f.SelectVariant3 ? "1" : "0")
                + (f.Pause ? "1" : "0")
                + (f.Stats ? "1" : "0")
                + (f.Slot1 ? "1" : "0")
                + (f.Slot2 ? "1" : "0")
                + (f.Slot3 ? "1" : "0")
                + (f.Slot4 ? "1" : "0")
                + (f.Slot5 ? "1" : "0")
                + (f.Slot6 ? "1" : "0");
        }
    }
}
