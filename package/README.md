# BetterEnemyAI

Stops enemies from circling around you after they attack, and makes them give up long chases sooner.

## What changes?

- **No circling after attacks:** After an attack, enemies no longer run circles around you while waiting for their next attack. If they are in range they stand still and face you; otherwise they move toward you.
- **Softer circling for flyers:** Flying enemies (deathsquitos, bats, drakes, etc.) still circle you while their attack recharges, like in vanilla, but slower and smoother. They back off about 6 meters, drift around you with changing speed and direction, stay in front of you or at your sides instead of flanking behind you, and dive again once their attack is ready. Walking toward them closes the distance instead of pushing them away. `FlyingWander` sets how much they move and `FlyingFrontArc` how far around you they may go. You can switch them back to vanilla circling or make them hover in place instead.
- **No periodic circling:** Some enemies stop attacking every now and then to circle around their target. This is turned off.
- **Shorter chases:**
  - An enemy gives up if it can't see or hear you for 10 seconds (vanilla: 30 seconds).
  - An enemy gives up if 20 seconds pass without it hitting you or taking damage from you (vanilla: 60 seconds).
  - An enemy gives up once it gets 60 meters away from where the chase started.
  - A ground enemy gives up if it has no path to you for 6 seconds (say, you're up on a rock) and you aren't hitting it. Near your buildings, enemies that attack buildings keep their vanilla behavior, so your base can still be raided.
  - An enemy that gave up won't look for a new target for 8 seconds. If you hit it, it fights back right away.
  - An enemy that gave up walks straight back to where the chase started, instead of slowly wandering around where it stopped (vanilla).

Tamed animals, friendly creatures and bosses are not affected (bosses can be enabled in the config). Chases by raid monsters stay vanilla by default. Creatures added by other mods are affected too.

## Configuration

All values can be changed in game with F1 (Configuration Manager) or in your mod manager's config editor. To exclude specific creatures, list their prefab names in `ExcludedCreatures`, separated by commas. Example: `Wolf,Deathsquito`

### Per-creature settings

`CreatureOverrides` gives specific creatures their own values. Write the creature's name, a colon, then `Setting=Value` pairs separated by commas; separate creatures with semicolons:

```
Troll: GiveUpWhenNoCombatSeconds=30, MaxChaseDistance=80; Greydwarf: GiveUpWhenNoCombatSeconds=15
```

Any setting name from the config works. There are also three shortcuts:

- `Chase=Vanilla` keeps that creature's normal chasing (e.g. `Deathsquito: Chase=Vanilla`)
- `Circling=Vanilla` keeps its normal circling
- `Enabled=false` turns the mod off for it

Use a dot for decimals (`1.5`). Mistakes are skipped and reported in the BepInEx log.

To find creature names, stand near them, open the console (F5) and type `bea_creatures`. It lists every creature within 40 m (or `bea_creatures 100` for 100 m) and whether the mod affects it.

## Multiplayer

Enemy AI in an area is run by the player who "owns" that area, so every player should install the mod. If you run a dedicated server, install it there too.

- **Settings sync:** When you join a server that has the mod, the server's settings are used for everyone, so enemies behave the same no matter who is nearby. Your own config file isn't changed; your settings come back when you leave. If the host changes a setting in game, it's sent to everyone right away. On a dedicated server, edit the server's config file and restart it.
- **Missing mod warning:** If someone joins without the mod, the server log (and the host, in a player-hosted game) gets a message about it. A version mismatch between server and client is reported the same way.
