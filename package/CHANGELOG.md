# Changelog

## 1.5.4

- Leaves IslandsOfValheim's Valhalla duel fighters (`IoV_Einherjar`, `IoV_EinherjarChampion`) alone. IslandsOfValheim runs their AI itself, and BetterEnemyAI's changes could make them run in place or turn away mid-duel. `bea_creatures` lists them as "IslandsOfValheim duel fighter, not affected". If you added `IoV_Einherjar` to `ExcludedCreatures` as a workaround, you can remove it.

## 1.5.3

- README: says that the mod was made with AI (Claude by Anthropic), as Thunderstore asks. The DLL carries the same note in its metadata. No gameplay changes.

## 1.5.2

- README: notes that multiplayer and dedicated servers haven't been tested yet. No code changes.

## 1.5.1

- Added a link for feedback and bug reports: [GitHub Issues](https://github.com/aetuss/BetterEnemyAI/issues).

## 1.5.0

- An animal you're taming (fed, taming started) no longer counts as an enemy nearby when you try to sleep (`TamingDoesntBlockSleep`, on by default).

## 1.4.4

- New icon.

## 1.4.3

- Sea creatures (serpents) keep their vanilla circling, and land creatures keep it while swimming, instead of trying to walk to your front or back off for a run-up in the water. Their chases are still shortened.

## 1.4.2

- Fixed enemies giving up mid-fight (they then re-alerted moments later, which made wolves howl again): the distance and no-path rules now wait until neither side has attacked for 5 seconds.
- Boars and other chargers no longer walk around you after backing off for a run-up, which looked like circling; they wait at run-up distance facing you and charge as soon as their attack is usable again.
- New `DebugLog` setting writes enemy decisions to the BepInEx log.

## 1.4.1

- Fixed boars (and other enemies whose attack needs a run-up) taking a long time to attack again: they now wait outside their attack's minimum range, backing off first if they're too close.

## 1.4.0

- Ground enemies that end up beside or behind you after attacking (e.g. boars charging past) walk back around to your front before waiting, instead of standing behind you (`GroundFrontArc`, also per creature).

## 1.3.0

- Ground enemies give up after 6 seconds without a path to you, unless you're hitting them or they're at your base and can attack buildings (`GiveUpWhenUnreachableSeconds`, also per creature; `Chase=Vanilla` turns it off too).

## 1.2.0

- Enemies that give up a chase now walk straight back to where the chase started (`ReturnAfterGiveUp`, on by default, also per creature).

## 1.1.0

- Per-creature settings: `CreatureOverrides` gives specific creatures their own values, with `Chase=Vanilla`, `Circling=Vanilla` and `Enabled=false` shortcuts.
- New console command `bea_creatures` lists nearby creatures by the names the settings use.

## 1.0.0

- First release: no circling after attacks, softer circling for flyers, shorter chases, server settings sync.
