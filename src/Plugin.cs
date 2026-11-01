using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BetterEnemyAI
{
    public enum FlyingMode
    {
        Vanilla,
        HitAndRetreat,
        Hover
    }

    [BepInPlugin(Guid, ModName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "BetterEnemyAI";
        public const string ModName = "BetterEnemyAI";
        public const string Version = "1.1.0";

        // Vanilla MonsterAI gives up after 30 s unsensed / 60 s without attacking.
        internal static Synced<bool> Enabled;
        internal static Synced<string> ExcludedCreatures;
        internal static Synced<bool> AffectBosses;
        internal static Synced<bool> AffectRaidMonsters;

        internal static Synced<bool> NoCircleAfterAttack;
        internal static Synced<FlyingMode> FlyingAfterAttack;
        internal static Synced<float> RetreatDistance;
        internal static Synced<float> FlyingWander;
        internal static Synced<float> FlyingFrontArc;
        internal static Synced<bool> NoPeriodicCircling;

        internal static Synced<float> GiveUpWhenNotSensed;
        internal static Synced<float> GiveUpWhenNoCombat;
        internal static Synced<float> MaxChaseDistance;
        internal static Synced<float> IgnoreAfterGiveUp;

        internal static Synced<string> CreatureOverridesText;

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        static HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // All settings are server-synced; see ConfigSync.
        Synced<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            return new Synced<T>(Config.Bind(section, key, defaultValue,
                new ConfigDescription(description.Description + " In multiplayer, the server's value is used.",
                    description.AcceptableValues)));
        }

        Synced<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Bind("1 - General", "Enabled", true,
                "Turns the mod on or off.");
            ExcludedCreatures = Bind("1 - General", "ExcludedCreatures", "",
                "Prefab names of creatures the mod should not affect, separated by commas. Example: Wolf,Deathsquito,Troll");
            AffectBosses = Bind("1 - General", "AffectBosses", false,
                "Should bosses be affected too?");
            AffectRaidMonsters = Bind("1 - General", "AffectRaidMonsters", false,
                "Also shorten chases by creatures hunting the player during a raid? (Circling settings always apply.)");

            NoCircleAfterAttack = Bind("2 - Circling", "NoCircleAfterAttack", true,
                "Ground enemies stop circling around you after attacking while their next attack recharges; they stand still and face you instead.");
            FlyingAfterAttack = Bind("2 - Circling", "FlyingAfterAttack", FlyingMode.HitAndRetreat,
                "What flying enemies (deathsquitos, bats, drakes, etc.) do while their next attack recharges. " +
                "Vanilla = circle around you as usual. HitAndRetreat = back off a short distance, hover facing you, then dive when ready. " +
                "Hover = stay where they are and face you.");
            RetreatDistance = Bind("2 - Circling", "RetreatDistance", 6f,
                "How many meters flying enemies back off from you in HitAndRetreat mode.");
            FlyingWander = Bind("2 - Circling", "FlyingWander", 1f,
                new ConfigDescription("How much flying enemies move around you while waiting in HitAndRetreat mode. " +
                    "0 = hover in place, 1 = slow, loose circling (default), 2 = faster and wider.",
                    new AcceptableValueRange<float>(0f, 2f)));
            FlyingFrontArc = Bind("2 - Circling", "FlyingFrontArc", 90f,
                new ConfigDescription("In HitAndRetreat mode, how far (in degrees) flying enemies may circle to either side of where you are facing. " +
                    "90 = they stay in front of you or at your sides, 180 = anywhere, including behind you (like vanilla).",
                    new AcceptableValueRange<float>(30f, 180f)));
            NoPeriodicCircling = Bind("2 - Circling", "NoPeriodicCircling", true,
                "Stops some enemies from periodically breaking off their attack to circle around their target.");

            GiveUpWhenNotSensed = Bind("3 - Chase", "GiveUpWhenNotSensedSeconds", 10f,
                "An enemy gives up the chase if it can't see or hear you for this many seconds. Vanilla is 30. 0 = vanilla behavior.");
            GiveUpWhenNoCombat = Bind("3 - Chase", "GiveUpWhenNoCombatSeconds", 20f,
                "An enemy gives up the chase if for this many seconds it neither hits you nor takes damage from you (even if it can see you). Vanilla is 60. 0 = off.");
            MaxChaseDistance = Bind("3 - Chase", "MaxChaseDistance", 60f,
                "An enemy gives up the chase once it is this many meters away from where the chase started (and it hasn't taken damage in the last 5 seconds). 0 = off.");
            IgnoreAfterGiveUp = Bind("3 - Chase", "IgnoreAfterGiveUpSeconds", 8f,
                "An enemy that gave up won't look for a new target for this many seconds. If you hit it, it fights back right away.");

            CreatureOverridesText = Bind("4 - Per creature", "CreatureOverrides", "",
                "Different settings for specific creatures. Format: 'Name: Setting=Value, Setting=Value; Name: ...'. " +
                "Use the setting names above, e.g. 'Troll: GiveUpWhenNoCombatSeconds=30, MaxChaseDistance=80; Greydwarf: GiveUpWhenNoCombatSeconds=15'. " +
                "Shortcuts: Enabled=false (mod off for that creature), Chase=Vanilla (normal chasing), Circling=Vanilla (normal circling), " +
                "e.g. 'Deathsquito: Chase=Vanilla'. Use a dot for decimals. " +
                "To see creature names, open the console (F5) near them and type: bea_creatures");

            ExcludedCreatures.Changed += ParseExcluded;
            ParseExcluded();
            CreatureOverridesText.Changed += () => CreatureOverrides.Parse(CreatureOverridesText.Value);
            CreatureOverrides.Parse(CreatureOverridesText.Value);

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        }

        static void ParseExcluded()
        {
            excluded.Clear();
            foreach (string part in ExcludedCreatures.Value.Split(','))
            {
                string name = part.Trim();
                if (name.Length > 0) excluded.Add(name);
            }
        }

        // Hostile, untamed, non-excluded creatures only; never player-side summons.
        internal static bool Affects(Character character, AiState state)
        {
            if (!Enabled.Value || character == null) return false;
            if (character.IsTamed() || character.GetFaction() == Character.Faction.Players) return false;
            if (character.IsBoss() && !AffectBosses.Value) return false;
            return !excluded.Contains(state.PrefabName) && state.Settings.Enabled;
        }

        internal static bool IsExcluded(string prefabName)
        {
            return excluded.Contains(prefabName);
        }
    }

    class AiState
    {
        static readonly ConditionalWeakTable<MonsterAI, AiState> table = new ConditionalWeakTable<MonsterAI, AiState>();

        public string PrefabName;

        CreatureSettings settings;
        int settingsVersion = -1;

        // This creature type's effective settings, re-resolved whenever the overrides change.
        public CreatureSettings Settings
        {
            get
            {
                if (settingsVersion != CreatureOverrides.Version)
                {
                    settings = CreatureOverrides.Find(PrefabName);
                    settingsVersion = CreatureOverrides.Version;
                }
                return settings;
            }
        }

        // Values the creature had before we touched them, so they can be restored.
        public bool Modified;
        public bool Circulate;
        public bool CirculateFlying;
        public float CircleInterval;

        public Character ChaseTarget;
        public Vector3 ChaseStart;

        // Hit-and-retreat orbit; the noise seed keeps flyers from all moving alike.
        public float NoiseSeed = UnityEngine.Random.Range(0f, 1000f);
        public float LastRetreatTime = -1f;
        public Vector3 OrbitCenter;
        public float OrbitAngle;
        public float OrbitRadius;
        public Vector3 Move;
        public bool Running;

        public static AiState Get(MonsterAI ai)
        {
            AiState state;
            if (!table.TryGetValue(ai, out state))
            {
                state = new AiState();
                string name = ai.gameObject.name;
                int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
                state.PrefabName = clone >= 0 ? name.Substring(0, clone).Trim() : name;
                table.Add(ai, state);
            }
            return state;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
    static class CirclingPatch
    {
        // The flying creature whose "circle while charging" call should become a retreat this frame.
        internal static MonsterAI Retreater;

        static void Prefix(MonsterAI __instance, Character ___m_character)
        {
            Retreater = null;
            AiState state = AiState.Get(__instance);
            if (!Plugin.Affects(___m_character, state))
            {
                if (state.Modified)
                {
                    __instance.m_circulateWhileCharging = state.Circulate;
                    __instance.m_circulateWhileChargingFlying = state.CirculateFlying;
                    __instance.m_circleTargetInterval = state.CircleInterval;
                    state.Modified = false;
                }
                return;
            }

            if (!state.Modified)
            {
                state.Circulate = __instance.m_circulateWhileCharging;
                state.CirculateFlying = __instance.m_circulateWhileChargingFlying;
                state.CircleInterval = __instance.m_circleTargetInterval;
                state.Modified = true;
            }
            CreatureSettings settings = state.Settings;
            __instance.m_circulateWhileCharging = state.Circulate && !settings.NoCircleAfterAttack;
            FlyingMode mode = settings.FlyingAfterAttack;
            __instance.m_circulateWhileChargingFlying = state.CirculateFlying && mode != FlyingMode.Hover;
            __instance.m_circleTargetInterval = settings.NoPeriodicCircling ? 0f : state.CircleInterval;
            if (mode == FlyingMode.HitAndRetreat && state.CirculateFlying && ___m_character.IsFlying())
                Retreater = __instance;
        }

        static void Postfix()
        {
            Retreater = null;
        }
    }

    // Vanilla flyers circle via RandomMovementArroundPoint while their attack recharges: a new random point
    // around the target every couple of seconds at full speed, often behind the player. HitAndRetreat keeps
    // the circling but softens it: a slow orbit whose speed and direction drift on noise, kept within
    // FlyingFrontArc of where the player faces, around a lagging copy of the player's position (so walking
    // at the creature closes the distance instead of pushing it away), with all steering smoothed over time.
    [HarmonyPatch(typeof(BaseAI), "RandomMovementArroundPoint")]
    static class RetreatPatch
    {
        const float NewRetreatAfter = 0.5f;   // seconds without a retreat call before the next one counts as new
        const float OrbitSpeed = 30f;         // peak degrees per second at FlyingWander = 1
        const float CenterLag = 1.5f;         // seconds for the orbit centre to catch up with the target
        const float SteerSmoothing = 0.45f;   // seconds to blend into a new movement direction
        const float HoverHeight = 2.5f;
        const float RadiusWobble = 1.5f;      // meters at FlyingWander = 1
        const float HeightWobble = 0.8f;

        static readonly Action<BaseAI, Vector3> lookTowards =
            AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookTowards", new[] { typeof(Vector3) }));
        static readonly Action<BaseAI> chargeStop =
            AccessTools.MethodDelegate<Action<BaseAI>>(AccessTools.Method(typeof(BaseAI), "ChargeStop"));

        static bool Prefix(BaseAI __instance, float __0, Vector3 __1, Character ___m_character)
        {
            MonsterAI ai = CirclingPatch.Retreater;
            if (ai == null || ai != __instance) return true;
            Character target = ai.GetTargetCreature();
            if (target == null || (target.transform.position - __1).sqrMagnitude > 0.01f) return true;

            AiState state = AiState.Get(ai);
            CreatureSettings settings = state.Settings;
            float dt = __0;
            float wander = Mathf.Max(settings.FlyingWander, 0f);
            Vector3 self = ai.transform.position;

            if (Time.time - state.LastRetreatTime > NewRetreatAfter)
            {
                // Start the orbit on the side the creature is already on, so the first move is outward.
                Vector3 away = self - __1;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -ai.transform.forward;
                state.OrbitCenter = __1;
                state.OrbitAngle = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
                state.OrbitRadius = Mathf.Max(settings.RetreatDistance, 1f) * UnityEngine.Random.Range(0.9f, 1.2f);
                state.Move = Vector3.zero;
                state.Running = true;
            }
            state.LastRetreatTime = Time.time;
            state.OrbitCenter = Vector3.Lerp(state.OrbitCenter, __1, 1f - Mathf.Exp(-dt / CenterLag));

            // Orbit speed and direction drift slowly with noise, so it sometimes pauses or turns back.
            float t = Time.time;
            float s = state.NoiseSeed;
            float orbit = (Mathf.PerlinNoise(s, t * 0.25f) - 0.5f) * 2.5f;
            state.OrbitAngle += orbit * OrbitSpeed * wander * dt;

            float frontArc = Mathf.Clamp(settings.FlyingFrontArc, 0f, 180f);
            if (frontArc < 180f)
            {
                Vector3 forward = target.transform.forward;
                float facing = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                float offset = Mathf.DeltaAngle(facing, state.OrbitAngle);
                if (Mathf.Abs(offset) > frontArc)
                    state.OrbitAngle = Mathf.MoveTowardsAngle(state.OrbitAngle, facing + Mathf.Sign(offset) * frontArc, 60f * dt);
            }

            float radius = state.OrbitRadius + (Mathf.PerlinNoise(s + 13.1f, t * 0.3f) - 0.5f) * 2f * RadiusWobble * wander;
            float angle = state.OrbitAngle * Mathf.Deg2Rad;
            Vector3 desired = state.OrbitCenter + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            desired.y = state.OrbitCenter.y + HoverHeight +
                (Mathf.PerlinNoise(s + 47.9f, t * 0.35f) - 0.5f) * 2f * HeightWobble * Mathf.Min(wander, 1f);
            desired = KeepAboveGround(ai, desired);

            Vector3 toDesired = desired - self;
            float length = toDesired.magnitude;
            Vector3 wanted = length > 0.1f ? toDesired / length * Mathf.Clamp01(length / 2f) : Vector3.zero;
            state.Move = Vector3.Lerp(state.Move, wanted, 1f - Mathf.Exp(-dt / SteerSmoothing));

            // Hysteresis so it doesn't flicker between flying speeds.
            if (state.Running && length < 3f) state.Running = false;
            else if (!state.Running && length > 6f) state.Running = true;

            // Face where it's flying when moving fast, and the target when nearly still, blending in between.
            Vector3 toTarget = target.GetTopPoint() - self;
            toTarget.y = 0f;
            Vector3 flatMove = new Vector3(state.Move.x, 0f, state.Move.z);
            Vector3 look = toTarget;
            if (flatMove.sqrMagnitude > 0.0001f && toTarget.sqrMagnitude > 0.0001f)
                look = Vector3.Slerp(toTarget.normalized, flatMove.normalized, Mathf.Clamp01(flatMove.magnitude * 1.5f));

            chargeStop(ai);
            lookTowards(ai, look);
            ___m_character.SetRun(state.Running);
            ___m_character.SetMoveDir(state.Move);
            return false;
        }

        static Vector3 KeepAboveGround(MonsterAI ai, Vector3 point)
        {
            float minY = ZoneSystem.instance.GetSolidHeight(point) + ai.m_flyAltitudeMin + 0.5f;
            if (point.y < minY) point.y = minY;
            return point;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
    static class ChasePatch
    {
        const float HurtGrace = 5f;

        // Protected in BaseAI; Invoke dispatches to MonsterAI's override. Only runs on give-up.
        static readonly System.Reflection.MethodInfo setAlerted = AccessTools.Method(typeof(BaseAI), "SetAlerted");

        static void Postfix(MonsterAI __instance, Character ___m_character,
            ref Character ___m_targetCreature, ref StaticTarget ___m_targetStatic,
            float ___m_timeSinceSensedTargetCreature, ref float ___m_timeSinceAttacking,
            ref float ___m_updateTargetTimer, float ___m_timeSinceHurt)
        {
            AiState state = AiState.Get(__instance);
            Character target = ___m_targetCreature;
            if (target == null || !target.IsPlayer())
            {
                state.ChaseTarget = null;
                return;
            }
            if (state.ChaseTarget != target)
            {
                state.ChaseTarget = target;
                state.ChaseStart = __instance.transform.position;
            }

            if (!Plugin.Affects(___m_character, state)) return;
            if (__instance.HuntPlayer() && !Plugin.AffectRaidMonsters.Value) return;

            CreatureSettings settings = state.Settings;
            float notSensed = settings.GiveUpWhenNotSensed;
            float noCombat = settings.GiveUpWhenNoCombat;
            float maxDistance = settings.MaxChaseDistance;

            bool giveUp = notSensed > 0f && ___m_timeSinceSensedTargetCreature > notSensed;
            if (!giveUp && noCombat > 0f && ___m_timeSinceAttacking > noCombat && ___m_timeSinceHurt > noCombat)
                giveUp = true;
            if (!giveUp && maxDistance > 0f && ___m_timeSinceHurt > HurtGrace &&
                Vector3.Distance(__instance.transform.position, state.ChaseStart) > maxDistance)
                giveUp = true;
            if (!giveUp) return;

            // Same reset vanilla does when it gives up, with a configurable re-target delay.
            setAlerted.Invoke(__instance, new object[] { false });
            ___m_targetCreature = null;
            ___m_targetStatic = null;
            ___m_timeSinceAttacking = 0f;
            ___m_updateTargetTimer = Mathf.Max(settings.IgnoreAfterGiveUp, 0f);
            state.ChaseTarget = null;
        }
    }
}
