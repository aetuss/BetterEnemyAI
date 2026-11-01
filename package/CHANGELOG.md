# Changelog

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
