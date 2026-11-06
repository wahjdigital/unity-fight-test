# Unity fight test

A fixed camera siege battle in Unity 6.3 (URP): a walled town with a log palisade, archer towers and painted buildings, attacked by motion captured knights, archers and a hero. You pick troops on the cards and tap or hold outside the walls to send them; they breach the palisade, fight, burn the town down, and the result gives one to three stars.

Everything is built in code. The scene is empty; `BattleBootstrap` lays out the camera, light, ground, town, palisade, trees and interface when it starts.

## Build

Unity 6000.3 with the iOS (or Mac) module. From the project folder:

```bash
Unity -batchmode -quit -projectPath . -executeMethod Build.Setup
Unity -batchmode -quit -projectPath . -executeMethod Build.Mac
Unity -batchmode -quit -projectPath . -executeMethod Build.IOS
```

`Build.Setup` makes the render pipeline (soft shadows, ambient occlusion, HDR), draws its own textures, creates the materials, imports the soldiers with their moves and writes the scene and player settings. Set your own Apple team in `Build.Player` before building for iOS.

Self test: start the Mac player with `-autotest <folder>` (on a phone, set the environment variable `FIGHT_AUTOTEST=1`); it deploys troops by itself, saves screenshots and logs the frame rate.

## Soldiers (not included)

Adobe's terms do not allow sharing Mixamo files, so get them from [mixamo.com](https://www.mixamo.com) with a free account. Download as FBX for Unity, 30 frames per second: the idle move with skin, every other move without skin, and walk and run with In Place ticked. Put them here:

| Folder | Character | Files |
| --- | --- | --- |
| `Assets/Resources/Units/paladin` | Paladin W/Prop J Nordstrom | `paladin_idle` (Sword And Shield Idle), `paladin_walk`, `paladin_run`, `paladin_attack1` (High Attack), `paladin_attack2` (Downward Slash), `paladin_attack3` (Cross Slash), `paladin_hit` (Unblocked Impact), `paladin_death1` (Falling Back Death), `paladin_death2` (Falling Forward Death), `paladin_cheer` (Male Cheering With Two Fists Pump) |
| `Assets/Resources/Units/knight` | Knight D Pelegrini (the hero) | `knight_idle`, `knight_walk`, `knight_run` (Sword And Shield), `knight_attack1` (Power Slash), `knight_attack2` (Slash Combo), `knight_attack3` (Jump Attack), `knight_power` (Powering Up), `knight_hit` (Head Impact), `knight_death1` (Falling Back Death), `knight_cheer` (Celebrating After A Win) |
| `Assets/Resources/Units/archer` | Erika Archer With Bow/Arrow | `archer_idle` (Standing Idle With Bow), `archer_walk`, `archer_run` (Walking and Running Forward With Bow), `archer_draw` (Reloading Bow), `archer_shoot` (Standing Aim Fire Arrow), `archer_aim` (Aiming Idle With Bow), `archer_hit` (Hit Reaction From The Front With Bow), `archer_death1` (Falling Backwards With Bow), `archer_death2` (Falling Forwards With Bow), `archer_cheer` (Male Cheering With Two Fists Pump) |

Then run `Build.Setup` again: it pulls the textures out of each character, gives them proper materials and names every move after its file. The hero borrows the paladin's sword and shield at runtime.
