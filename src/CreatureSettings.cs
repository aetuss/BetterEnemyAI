using System;
using System.Collections.Generic;
using System.Globalization;

namespace BetterEnemyAI
{
    // Effective settings for one creature type: a per-creature override where one is set, the global
    // setting otherwise. Defaults (no overrides) is used for creatures without an entry.
    class CreatureSettings
    {
        internal static readonly CreatureSettings Defaults = new CreatureSettings();

        public bool? enabled;
        public bool? noCircleAfterAttack;
        public FlyingMode? flyingAfterAttack;
        public float? retreatDistance;
        public float? flyingWander;
        public float? flyingFrontArc;
        public bool? noPeriodicCircling;
        public float? giveUpWhenNotSensed;
        public float? giveUpWhenNoCombat;
        public float? maxChaseDistance;
        public float? giveUpWhenUnreachable;
        public float? ignoreAfterGiveUp;
        public bool? returnAfterGiveUp;

        public bool Enabled { get { return enabled ?? true; } }
        public bool NoCircleAfterAttack { get { return noCircleAfterAttack ?? Plugin.NoCircleAfterAttack.Value; } }
        public FlyingMode FlyingAfterAttack { get { return flyingAfterAttack ?? Plugin.FlyingAfterAttack.Value; } }
        public float RetreatDistance { get { return retreatDistance ?? Plugin.RetreatDistance.Value; } }
        public float FlyingWander { get { return flyingWander ?? Plugin.FlyingWander.Value; } }
        public float FlyingFrontArc { get { return flyingFrontArc ?? Plugin.FlyingFrontArc.Value; } }
        public bool NoPeriodicCircling { get { return noPeriodicCircling ?? Plugin.NoPeriodicCircling.Value; } }
        public float GiveUpWhenNotSensed { get { return giveUpWhenNotSensed ?? Plugin.GiveUpWhenNotSensed.Value; } }
        public float GiveUpWhenNoCombat { get { return giveUpWhenNoCombat ?? Plugin.GiveUpWhenNoCombat.Value; } }
        public float MaxChaseDistance { get { return maxChaseDistance ?? Plugin.MaxChaseDistance.Value; } }
        public float GiveUpWhenUnreachable { get { return giveUpWhenUnreachable ?? Plugin.GiveUpWhenUnreachable.Value; } }
        public float IgnoreAfterGiveUp { get { return ignoreAfterGiveUp ?? Plugin.IgnoreAfterGiveUp.Value; } }
        public bool ReturnAfterGiveUp { get { return returnAfterGiveUp ?? Plugin.ReturnAfterGiveUp.Value; } }
    }

    // Parses the CreatureOverrides setting:
    //   Troll: GiveUpWhenNoCombatSeconds=30, MaxChaseDistance=80; Deathsquito: Chase=Vanilla
    // Entries are separated by ';', the creature's prefab name comes before ':', and settings are
    // key=value pairs separated by ','. Keys are the normal setting names (case-insensitive), plus
    // Enabled, Chase=Vanilla and Circling=Vanilla. Bad entries are logged and skipped.
    static class CreatureOverrides
    {
        static Dictionary<string, CreatureSettings> byName =
            new Dictionary<string, CreatureSettings>(StringComparer.OrdinalIgnoreCase);

        // Bumped on every parse so cached lookups in AiState refresh.
        internal static int Version;

        internal static CreatureSettings Find(string prefabName)
        {
            CreatureSettings settings;
            return byName.TryGetValue(prefabName, out settings) ? settings : CreatureSettings.Defaults;
        }

        internal static void Parse(string text)
        {
            Dictionary<string, CreatureSettings> result =
                new Dictionary<string, CreatureSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawEntry in text.Split(';'))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0) continue;
                int colon = entry.IndexOf(':');
                if (colon <= 0)
                {
                    Warn(entry, "expected 'CreatureName: Setting=Value'");
                    continue;
                }
                string name = entry.Substring(0, colon).Trim();
                CreatureSettings settings;
                if (!result.TryGetValue(name, out settings))
                {
                    settings = new CreatureSettings();
                    result[name] = settings;
                }
                foreach (string rawPair in entry.Substring(colon + 1).Split(','))
                {
                    string pair = rawPair.Trim();
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    string error = eq <= 0
                        ? "expected Setting=Value"
                        : Apply(settings, pair.Substring(0, eq).Trim().ToLowerInvariant(), pair.Substring(eq + 1).Trim());
                    if (error != null) Warn(name + ": " + pair, error);
                }
            }
            byName = result;
            Version++;
        }

        static string Apply(CreatureSettings s, string key, string value)
        {
            switch (key)
            {
                case "enabled": return ParseBool(value, out s.enabled);
                case "nocircleafterattack": return ParseBool(value, out s.noCircleAfterAttack);
                case "noperiodiccircling": return ParseBool(value, out s.noPeriodicCircling);
                case "flyingafterattack": return ParseFlying(value, out s.flyingAfterAttack);
                case "retreatdistance": return ParseFloat(value, out s.retreatDistance);
                case "flyingwander": return ParseFloat(value, out s.flyingWander);
                case "flyingfrontarc": return ParseFloat(value, out s.flyingFrontArc);
                case "giveupwhennotsensedseconds": return ParseFloat(value, out s.giveUpWhenNotSensed);
                case "giveupwhennocombatseconds": return ParseFloat(value, out s.giveUpWhenNoCombat);
                case "maxchasedistance": return ParseFloat(value, out s.maxChaseDistance);
                case "giveupwhenunreachableseconds": return ParseFloat(value, out s.giveUpWhenUnreachable);
                case "ignoreaftergiveupseconds": return ParseFloat(value, out s.ignoreAfterGiveUp);
                case "returnaftergiveup": return ParseBool(value, out s.returnAfterGiveUp);
                case "chase":
                    if (!IsVanilla(value)) return "only Chase=Vanilla is supported";
                    s.giveUpWhenNotSensed = 0f;
                    s.giveUpWhenNoCombat = 0f;
                    s.maxChaseDistance = 0f;
                    s.giveUpWhenUnreachable = 0f;
                    return null;
                case "circling":
                    if (!IsVanilla(value)) return "only Circling=Vanilla is supported";
                    s.noCircleAfterAttack = false;
                    s.noPeriodicCircling = false;
                    s.flyingAfterAttack = FlyingMode.Vanilla;
                    return null;
                default:
                    return "unknown setting '" + key + "'";
            }
        }

        static bool IsVanilla(string value)
        {
            return string.Equals(value, "vanilla", StringComparison.OrdinalIgnoreCase);
        }

        static string ParseBool(string value, out bool? result)
        {
            result = null;
            switch (value.ToLowerInvariant())
            {
                case "true": case "on": case "yes": case "1": result = true; return null;
                case "false": case "off": case "no": case "0": result = false; return null;
                default: return "'" + value + "' is not true/false";
            }
        }

        static string ParseFloat(string value, out float? result)
        {
            result = null;
            float parsed;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return "'" + value + "' is not a number (use a dot for decimals, e.g. 1.5)";
            result = parsed;
            return null;
        }

        static string ParseFlying(string value, out FlyingMode? result)
        {
            result = null;
            foreach (FlyingMode mode in Enum.GetValues(typeof(FlyingMode)))
            {
                if (string.Equals(mode.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    result = mode;
                    return null;
                }
            }
            return "'" + value + "' is not Vanilla, HitAndRetreat or Hover";
        }

        static void Warn(string entry, string problem)
        {
            Plugin.Log.LogWarning("CreatureOverrides: skipped '" + entry + "': " + problem);
        }
    }
}
