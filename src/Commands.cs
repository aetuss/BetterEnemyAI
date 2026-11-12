using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace BetterEnemyAI
{
    // Console command that lists nearby creatures by prefab name, so players know what to write in
    // ExcludedCreatures and CreatureOverrides.
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    static class Commands
    {
        const float DefaultRange = 40f;

        static void Postfix()
        {
            new Terminal.ConsoleCommand("bea_creatures",
                "[range] - BetterEnemyAI: list creatures near you by the names used in its settings",
                args =>
                {
                    Player player = Player.m_localPlayer;
                    if (player == null)
                    {
                        args.Context.AddString("Join a world first.");
                        return;
                    }
                    float range;
                    if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out range))
                        range = DefaultRange;

                    SortedDictionary<string, int> counts = new SortedDictionary<string, int>();
                    Dictionary<string, string> notes = new Dictionary<string, string>();
                    foreach (Character character in Character.GetAllCharacters())
                    {
                        if (character == null || character.IsPlayer()) continue;
                        if (Vector3.Distance(character.transform.position, player.transform.position) > range) continue;
                        MonsterAI ai = character.GetComponent<MonsterAI>();
                        if (ai == null) continue;
                        AiState state = AiState.Get(ai);
                        int count;
                        counts.TryGetValue(state.PrefabName, out count);
                        counts[state.PrefabName] = count + 1;
                        notes[state.PrefabName] = Describe(character, state);
                    }

                    if (counts.Count == 0)
                    {
                        args.Context.AddString("No creatures within " + range.ToString("0", CultureInfo.InvariantCulture) + " m.");
                        return;
                    }
                    args.Context.AddString("Creatures within " + range.ToString("0", CultureInfo.InvariantCulture) + " m:");
                    foreach (KeyValuePair<string, int> entry in counts)
                        args.Context.AddString("  " + entry.Key + " x" + entry.Value + " - " + notes[entry.Key]);
                });
        }

        static string Describe(Character character, AiState state)
        {
            if (!Plugin.Enabled.Value) return "mod is off";
            if (character.IsTamed() || character.GetFaction() == Character.Faction.Players) return "friendly, not affected";
            if (character.IsBoss() && !Plugin.AffectBosses.Value) return "boss, not affected";
            if (Plugin.IsScriptedDuelFighter(state.PrefabName)) return "driven by another mod, not affected";
            if (Plugin.IsExcluded(state.PrefabName)) return "excluded";
            if (!state.Settings.Enabled) return "turned off in CreatureOverrides";
            return state.Settings == CreatureSettings.Defaults ? "affected" : "affected, has CreatureOverrides";
        }
    }
}
