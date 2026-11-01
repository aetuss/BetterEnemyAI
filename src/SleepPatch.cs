using HarmonyLib;

namespace BetterEnemyAI
{
    // Beds refuse sleep while the player IsSensed, which any hostile creature targeting them sets through
    // Player.OnTargeted, from MonsterAI.UpdateTarget. That includes a fed animal penned up for taming.
    // While a creature being tamed updates its target, its OnTargeted calls are dropped, so it no longer
    // counts as an enemy nearby. Runs where the creature's AI runs (the area's owner), like the rest of the mod.
    static class SleepPatch
    {
        static MonsterAI updating;

        // Private in Tameable: taming progress, 0-100.
        static readonly System.Func<Tameable, int> getTameness =
            AccessTools.MethodDelegate<System.Func<Tameable, int>>(AccessTools.Method(typeof(Tameable), "GetTameness"));

        [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
        static class TrackUpdatingCreature
        {
            static void Prefix(MonsterAI __instance)
            {
                updating = __instance;
            }

            static void Postfix()
            {
                updating = null;
            }
        }

        [HarmonyPatch(typeof(Player), "OnTargeted")]
        static class IgnoreTamingCreatures
        {
            static bool Prefix()
            {
                return updating == null || !Plugin.TamingDoesntBlockSleep.Value || !IsBeingTamed(updating);
            }
        }

        // Not tamed yet, but taming has started and it's fed, i.e. an animal the player is actually taming.
        static bool IsBeingTamed(MonsterAI ai)
        {
            Tameable tameable = ai.GetComponent<Tameable>();
            return tameable != null && !tameable.IsTamed() && !tameable.IsHungry() && getTameness(tameable) > 0;
        }
    }
}
