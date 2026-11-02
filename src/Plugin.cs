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
        public const string Version = "1.5.1";

        // Vanilla MonsterAI gives up after 30 s unsensed / 60 s without attacking.
        internal static Synced<bool> Enabled;
        internal static Synced<string> ExcludedCreatures;
        internal static Synced<bool> AffectBosses;
        internal static Synced<bool> AffectRaidMonsters;
        internal static Synced<bool> TamingDoesntBlockSleep;

        internal static Synced<bool> NoCircleAfterAttack;
        internal static Synced<float> GroundFrontArc;
        internal static Synced<FlyingMode> FlyingAfterAttack;
        internal static Synced<float> RetreatDistance;
        internal static Synced<float> FlyingWander;
        internal static Synced<float> FlyingFrontArc;
        internal static Synced<bool> NoPeriodicCircling;

        internal static Synced<float> GiveUpWhenNotSensed;
        internal static Synced<float> GiveUpWhenNoCombat;
        internal static Synced<float> MaxChaseDistance;
        internal static Synced<float> GiveUpWhenUnreachable;
        internal static Synced<float> IgnoreAfterGiveUp;
        internal static Synced<bool> ReturnAfterGiveUp;

        internal static Synced<string> CreatureOverridesText;

        // Local only (not synced): each player's log is their own.
        internal static ConfigEntry<bool> DebugLog;

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
            TamingDoesntBlockSleep = Bind("1 - General", "TamingDoesntBlockSleep", true,
                "An animal you're taming (fed, taming started) no longer counts as an enemy nearby when you try to sleep. Wild creatures still do.");

            NoCircleAfterAttack = Bind("2 - Circling", "NoCircleAfterAttack", true,
                "Ground enemies stop circling around you after attacking while their next attack recharges; they stand still and face you instead.");
            GroundFrontArc = Bind("2 - Circling", "GroundFrontArc", 90f,
                new ConfigDescription("With NoCircleAfterAttack, a ground enemy that ends up beside or behind you after attacking (e.g. a boar charging past) " +
                    "walks back around to your front before waiting, if it is more than this many degrees from where you face. " +
                    "90 = it waits only in front of you, 180 = it waits wherever it is.",
                    new AcceptableValueRange<float>(30f, 180f)));
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
                "An enemy gives up the chase once it is this many meters away from where the chase started (unless you're fighting: it attacked or got hit in the last 5 seconds). 0 = off.");
            GiveUpWhenUnreachable = Bind("3 - Chase", "GiveUpWhenUnreachableSeconds", 6f,
                "A ground enemy gives up the chase if it can't find a path to you (e.g. you're on a rock) for this many seconds, unless you're fighting (it attacked or got hit in the last 5 seconds). " +
                "Near your buildings, enemies that attack buildings keep vanilla behavior. 0 = off.");
            IgnoreAfterGiveUp = Bind("3 - Chase", "IgnoreAfterGiveUpSeconds", 8f,
                "An enemy that gave up won't look for a new target for this many seconds. If you hit it, it fights back right away.");
            ReturnAfterGiveUp = Bind("3 - Chase", "ReturnAfterGiveUp", true,
                "An enemy that gave up walks straight back to where the chase started, instead of slowly wandering around where it stopped.");

            CreatureOverridesText = Bind("4 - Per creature", "CreatureOverrides", "",
                "Different settings for specific creatures. Format: 'Name: Setting=Value, Setting=Value; Name: ...'. " +
                "Use the setting names above, e.g. 'Troll: GiveUpWhenNoCombatSeconds=30, MaxChaseDistance=80; Greydwarf: GiveUpWhenNoCombatSeconds=15'. " +
                "Shortcuts: Enabled=false (mod off for that creature), Chase=Vanilla (normal chasing), Circling=Vanilla (normal circling), " +
                "e.g. 'Deathsquito: Chase=Vanilla'. Use a dot for decimals. " +
                "To see creature names, open the console (F5) near them and type: bea_creatures");

            DebugLog = Config.Bind("1 - General", "DebugLog", false,
                "Writes what enemies decide (e.g. why one gave up a chase) to the BepInEx log. Useful when reporting a problem. Not synced in multiplayer.");

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

        internal static void Debug(string message)
        {
            if (DebugLog != null && DebugLog.Value) Log.LogInfo(message);
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

        // When the target first became unreachable by path (-1 = reachable), and when to check again.
        public float UnreachableSince = -1f;
        public float NextPathCheck;

        // Walking around to the target's front after an attack (RepositionPatch).
        public bool Repositioning;

        // Backing off to get a run-up for a minimum-range attack (RunUpPatch).
        public bool RunUpPending;
        bool? isCharger;

        // Whether all of its attacks need a run-up (a boar's charge). Its weapons don't change, so cache it.
        public bool IsCharger(Character character)
        {
            if (!isCharger.HasValue)
            {
                Humanoid humanoid = character as Humanoid;
                isCharger = humanoid != null && RunUpPatch.ShortestMinRange(humanoid) > 0f;
            }
            return isCharger.Value;
        }

        // Walking back to where the chase started after giving up.
        public bool Returning;
        public Vector3 ReturnTarget;
        public float ReturnBestDistance;
        public float ReturnProgressTime;

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
        // The creature whose "circle while charging" call should become a flying retreat (Retreater) or a
        // walk to the target's front (Repositioner) this frame.
        internal static MonsterAI Retreater;
        internal static MonsterAI Repositioner;

        static void Prefix(MonsterAI __instance, Character ___m_character)
        {
            Retreater = null;
            Repositioner = null;
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
            // Water-only creatures (serpents) keep all their vanilla circling: circling a boat is how they
            // fight, and "walk to the front" makes no sense there. Swimming land creatures (a neck in a lake)
            // also keep it while in the water.
            bool waterOnly = __instance.m_avoidLand;
            bool swimming = ___m_character.IsSwimming();

            // With a front arc below 180 the vanilla circling call is kept, and RepositionPatch replaces it.
            bool reposition = settings.NoCircleAfterAttack && settings.GroundFrontArc < 180f;
            __instance.m_circulateWhileCharging = state.Circulate && (waterOnly || !settings.NoCircleAfterAttack || reposition);
            FlyingMode mode = settings.FlyingAfterAttack;
            __instance.m_circulateWhileChargingFlying = state.CirculateFlying && mode != FlyingMode.Hover;
            __instance.m_circleTargetInterval = settings.NoPeriodicCircling && !waterOnly ? 0f : state.CircleInterval;
            if (___m_character.IsFlying())
            {
                if (mode == FlyingMode.HitAndRetreat && state.CirculateFlying) Retreater = __instance;
            }
            else if (reposition && state.Circulate && !waterOnly && !swimming)
            {
                Repositioner = __instance;
            }
        }

        static void Postfix()
        {
            Retreater = null;
            Repositioner = null;
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

    // Ground creatures that circle in vanilla (boars, wolves, ...) often charge past the player and end up
    // behind them; just stopping there looks odd. While their attack recharges: in front of the target
    // (within GroundFrontArc of where it faces) they close to a short gap and stand facing it, like with
    // NoCircleAfterAttack alone; anywhere else they walk around to the target's front first.
    [HarmonyPatch(typeof(BaseAI), "RandomMovementArroundPoint")]
    static class RepositionPatch
    {
        const float Gap = 1.5f;               // meters between the two bodies when standing
        const float BackInFront = 0.7f;       // fraction of the arc to reach before repositioning stops
        const float MinRangeMargin = 1f;      // meters beyond a weapon's minimum range to wait at
        const float TooCloseMargin = 0.75f;   // how far inside the waiting distance before backing off

        static readonly Func<BaseAI, float, Vector3, float, bool, bool> moveTo =
            AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(
                AccessTools.Method(typeof(BaseAI), "MoveTo", new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) }));
        static readonly Action<BaseAI, Vector3> lookAt =
            AccessTools.MethodDelegate<Action<BaseAI, Vector3>>(AccessTools.Method(typeof(BaseAI), "LookAt", new[] { typeof(Vector3) }));
        static readonly Action<BaseAI> stopMoving =
            AccessTools.MethodDelegate<Action<BaseAI>>(AccessTools.Method(typeof(BaseAI), "StopMoving"));
        static readonly Action<BaseAI> chargeStop =
            AccessTools.MethodDelegate<Action<BaseAI>>(AccessTools.Method(typeof(BaseAI), "ChargeStop"));

        static bool Prefix(BaseAI __instance, float __0, Vector3 __1, Character ___m_character)
        {
            MonsterAI ai = CirclingPatch.Repositioner;
            if (ai == null || ai != __instance) return true;
            Character target = ai.GetTargetCreature();
            if (target == null || (target.transform.position - __1).sqrMagnitude > 0.01f) return true;

            AiState state = AiState.Get(ai);
            float arc = Mathf.Clamp(state.Settings.GroundFrontArc, 0f, 180f);
            Vector3 self = ai.transform.position;
            Vector3 fromTarget = self - __1;
            fromTarget.y = 0f;
            Vector3 forward = target.transform.forward;
            float facing = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float bearing = Mathf.Atan2(fromTarget.x, fromTarget.z) * Mathf.Rad2Deg;
            float offset = Mathf.DeltaAngle(facing, bearing);
            float standDistance = StandDistance(___m_character, target);

            // Chargers don't walk around: backing off for their run-up, then walking around, reads as circling.
            // They just wait at run-up distance facing the target. Others walk around with hysteresis: once
            // going, keep going until well inside the arc.
            if (state.IsCharger(___m_character)) state.Repositioning = false;
            else if (Mathf.Abs(offset) > arc) state.Repositioning = true;
            else if (Mathf.Abs(offset) < arc * BackInFront) state.Repositioning = false;

            chargeStop(ai);
            if (state.Repositioning)
            {
                // Aim for the edge of the front area on the side the creature is already on.
                float side = offset < 0f ? -1f : 1f;
                float spotBearing = (facing + side * arc * 0.5f) * Mathf.Deg2Rad;
                Vector3 spot = __1 + new Vector3(Mathf.Sin(spotBearing), 0f, Mathf.Cos(spotBearing)) * (standDistance + 0.5f);
                moveTo(ai, __0, spot, 0.5f, true);
                return false;
            }

            float distance = fromTarget.magnitude;
            if (distance > standDistance + 0.5f)
            {
                moveTo(ai, __0, __1, standDistance, true);
            }
            else if (distance < standDistance - TooCloseMargin)
            {
                // Back off to get a run-up; MoveTo turns it away, it faces the target again once there.
                Vector3 away = distance > 0.01f ? fromTarget / distance : -ai.transform.forward;
                moveTo(ai, __0, __1 + away * standDistance, 0.5f, true);
            }
            else
            {
                stopMoving(ai);
                lookAt(ai, target.GetTopPoint());
            }
            return false;
        }

        // Where to wait, center to center. Some attacks (a boar's charge) have a minimum range and can't be
        // used closer than that, so a creature parked too close just loiters until it happens to drift out;
        // wait outside the current weapon's minimum range instead, but still within its reach.
        static float StandDistance(Character character, Character target)
        {
            float distance = character.GetRadius() + target.GetRadius() + Gap;
            Humanoid humanoid = character as Humanoid;
            ItemDrop.ItemData weapon = humanoid != null ? humanoid.GetCurrentWeapon() : null;
            if (weapon == null) return distance;
            float minRange = weapon.m_shared.m_aiAttackRangeMin;
            if (minRange <= 0f) return distance;
            distance = Mathf.Max(distance, minRange + MinRangeMargin);
            float reach = weapon.m_shared.m_aiAttackRange + target.GetRadius();
            if (reach > minRange + MinRangeMargin) distance = Mathf.Min(distance, reach - 0.3f);
            return distance;
        }
    }

    // Attacks with a minimum range (a boar's charge) are unequipped entirely while the target is closer
    // than that, and with no weapon MonsterAI falls back to idle wandering around its spawn point until it
    // happens to drift out of range. Vanilla mostly avoids this by circling away; with NoCircleAfterAttack
    // the creature would loiter instead, so back it off to its run-up distance.
    [HarmonyPatch(typeof(BaseAI), "IdleMovement")]
    static class RunUpPatch
    {
        const float Margin = 1f;

        static readonly System.Reflection.FieldInfo updateWeaponTimer = AccessTools.Field(typeof(MonsterAI), "m_updateWeaponTimer");

        static readonly Func<BaseAI, float, Vector3, float, bool, bool> moveTo =
            AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(
                AccessTools.Method(typeof(BaseAI), "MoveTo", new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) }));

        static bool Prefix(BaseAI __instance, float __0, Character ___m_character)
        {
            MonsterAI ai = __instance as MonsterAI;
            if (ai == null) return true;
            Humanoid humanoid = ___m_character as Humanoid;
            if (humanoid == null || humanoid.GetCurrentWeapon() != null || ___m_character.IsFlying() ||
                ___m_character.IsSwimming() || ai.m_avoidLand) return true;
            Character target = ai.GetTargetCreature();
            if (target == null) return true;
            AiState state = AiState.Get(ai);
            if (state.Returning || !Plugin.Affects(___m_character, state) || !state.Settings.NoCircleAfterAttack) return true;

            float runUp = ShortestMinRange(humanoid);
            if (runUp <= 0f) return true;
            Vector3 fromTarget = ai.transform.position - target.transform.position;
            fromTarget.y = 0f;
            float distance = fromTarget.magnitude;
            if (distance >= runUp)
            {
                // Out of minimum range: re-pick the weapon now rather than on vanilla's next weapon check.
                if (state.RunUpPending)
                {
                    updateWeaponTimer.SetValue(ai, 0f);
                    state.RunUpPending = false;
                }
                return true;
            }

            Vector3 away = distance > 0.01f ? fromTarget / distance : -ai.transform.forward;
            moveTo(ai, __0, target.transform.position + away * (runUp + Margin), 0.5f, true);
            if (!state.RunUpPending) Plugin.Debug(state.PrefabName + " backs off " + runUp + " m for a run-up");
            state.RunUpPending = true;
            return false;
        }

        // The distance at which the first of its weapons becomes usable again; 0 if one has no minimum.
        internal static float ShortestMinRange(Humanoid humanoid)
        {
            float shortest = float.MaxValue;
            foreach (ItemDrop.ItemData item in humanoid.GetInventory().GetAllItems())
            {
                if (!item.IsWeapon()) continue;
                shortest = Mathf.Min(shortest, item.m_shared.m_aiAttackRangeMin);
            }
            return shortest == float.MaxValue ? 0f : Mathf.Max(shortest, 0f);
        }
    }

    [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
    static class ChasePatch
    {
        const float CombatGrace = 5f;          // seconds since last attack or hit that still count as fighting
        const float PathCheckInterval = 1f;
        const float BaseRadius = 12f;         // player pieces this close to the target count as their base

        // Protected in BaseAI; Invoke dispatches to MonsterAI's override. Only runs on give-up.
        static readonly System.Reflection.MethodInfo setAlerted = AccessTools.Method(typeof(BaseAI), "SetAlerted");
        static readonly Func<BaseAI, Vector3, bool> havePath =
            AccessTools.MethodDelegate<Func<BaseAI, Vector3, bool>>(AccessTools.Method(typeof(BaseAI), "HavePath", new[] { typeof(Vector3) }));
        static readonly List<Piece> nearbyPieces = new List<Piece>();

        // True once the target has had no walkable path for `seconds` in a row. Flyers can always reach.
        // Near the target's base, creatures that attack buildings are left to vanilla, which switches them
        // to the buildings when it can't reach the player.
        static bool IsUnreachable(MonsterAI ai, Character character, AiState state, Character target, float seconds)
        {
            if (seconds <= 0f || character.IsFlying()) return false;
            if (Time.time >= state.NextPathCheck)
            {
                state.NextPathCheck = Time.time + PathCheckInterval;
                if (havePath(ai, target.transform.position)) state.UnreachableSince = -1f;
                else if (state.UnreachableSince < 0f) state.UnreachableSince = Time.time;
            }
            if (state.UnreachableSince < 0f || Time.time - state.UnreachableSince <= seconds) return false;
            return !(ai.m_attackPlayerObjects && NearPlayerBuildings(target.transform.position));
        }

        static bool NearPlayerBuildings(Vector3 position)
        {
            nearbyPieces.Clear();
            Piece.GetAllPiecesInRadius(position, BaseRadius, nearbyPieces);
            foreach (Piece piece in nearbyPieces)
                if (piece != null && piece.IsPlacedByPlayer()) return true;
            return false;
        }

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
                state.Returning = false;
                state.UnreachableSince = -1f;
            }

            if (!Plugin.Affects(___m_character, state)) return;
            if (__instance.HuntPlayer() && !Plugin.AffectRaidMonsters.Value) return;

            CreatureSettings settings = state.Settings;
            float notSensed = settings.GiveUpWhenNotSensed;
            float noCombat = settings.GiveUpWhenNoCombat;
            float maxDistance = settings.MaxChaseDistance;

            // Distance and reachability only count outside a fight: not while it's attacking or being hit,
            // or it could give up mid-fight and re-alert moments later (wolves howl on every alert).
            bool inFight = ___m_timeSinceHurt <= CombatGrace || ___m_timeSinceAttacking <= CombatGrace;
            string reason = null;
            if (notSensed > 0f && ___m_timeSinceSensedTargetCreature > notSensed)
                reason = "couldn't see or hear the target";
            else if (noCombat > 0f && ___m_timeSinceAttacking > noCombat && ___m_timeSinceHurt > noCombat)
                reason = "no combat for " + noCombat + " s";
            else if (!inFight && maxDistance > 0f &&
                Vector3.Distance(__instance.transform.position, state.ChaseStart) > maxDistance)
                reason = "chased farther than " + maxDistance + " m";
            else if (IsUnreachable(__instance, ___m_character, state, target, settings.GiveUpWhenUnreachable) && !inFight)
                reason = "no path to the target";
            if (reason == null) return;
            Plugin.Debug(state.PrefabName + " gave up the chase: " + reason);

            // Same reset vanilla does when it gives up, with a configurable re-target delay.
            setAlerted.Invoke(__instance, new object[] { false });
            ___m_targetCreature = null;
            ___m_targetStatic = null;
            ___m_timeSinceAttacking = 0f;
            ___m_updateTargetTimer = Mathf.Max(settings.IgnoreAfterGiveUp, 0f);
            state.ChaseTarget = null;

            float homeDistance = Utils.DistanceXZ(__instance.transform.position, state.ChaseStart);
            if (settings.ReturnAfterGiveUp && homeDistance > ReturnPatch.ArriveDistance * 2f)
            {
                state.Returning = true;
                state.ReturnTarget = state.ChaseStart;
                state.ReturnBestDistance = homeDistance;
                state.ReturnProgressTime = Time.time;
            }
        }
    }

    // Vanilla idles by wandering in short random hops around the spawn point, so a creature that gave up
    // drifts home slowly, if at all. While Returning, idle movement is replaced by walking straight back
    // to where the chase started. Any new target stops this (UpdateAI only idles without one), and so
    // does arriving or failing to get closer for a few seconds (blocked by terrain).
    [HarmonyPatch(typeof(BaseAI), "IdleMovement")]
    static class ReturnPatch
    {
        internal const float ArriveDistance = 2f;
        const float StuckTime = 4f;
        const float Progress = 1f;            // meters closer that counts as progress

        static readonly Func<BaseAI, float, Vector3, float, bool, bool> moveTo =
            AccessTools.MethodDelegate<Func<BaseAI, float, Vector3, float, bool, bool>>(
                AccessTools.Method(typeof(BaseAI), "MoveTo", new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) }));

        static bool Prefix(BaseAI __instance, float __0)
        {
            MonsterAI ai = __instance as MonsterAI;
            if (ai == null) return true;
            AiState state = AiState.Get(ai);
            if (!state.Returning) return true;

            float distance = Utils.DistanceXZ(ai.transform.position, state.ReturnTarget);
            if (distance < state.ReturnBestDistance - Progress)
            {
                state.ReturnBestDistance = distance;
                state.ReturnProgressTime = Time.time;
            }
            if (distance <= ArriveDistance || Time.time - state.ReturnProgressTime > StuckTime)
            {
                state.Returning = false;
                return true;
            }
            moveTo(ai, __0, state.ReturnTarget, ArriveDistance, false);
            return false;
        }
    }
}
