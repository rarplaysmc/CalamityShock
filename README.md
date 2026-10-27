# CalamityShock (v0.2.0)

Calamity-flavoured difficulty modes, re-tuned bosses, progression gating, loot tables, server-side crafting,
Rage/Adrenaline meters, a Rogue class, wave events, a Boss Rush, built arenas and region effects
for **TShock 6.x / Terraria 1.4.5** servers. Everything runs on the server; your players use the normal,
unmodified Terraria client (PC, and in principle mobile/console builds that can join TShock servers).

Built and tested against **TShock 6.2.1 for Terraria 1.4.5.8** (.NET 9).

## What it can and cannot do

A TShock plugin only runs on the server, and the vanilla client only knows vanilla content. So this plugin
**cannot** add new sprites, items, tiles, biomes, sounds, music or UI. What it **can** do is bend the vanilla
content Calamity-style:

| Feature | How it works |
|---|---|
| **Difficulty modes** (Normal / Revengeance / Death, add your own) | Terraria 1.4.5 gives every NPC a `difficulty` value (Classic 1, Expert 2, Master 3, Legendary 4, and it extrapolates above that) which is **synced to clients**. The plugin re-runs `SetDefaults` with a higher value when an NPC spawns, so max life, contact damage and the boss health bar are all correct on every client. Server-side defense multipliers, boss movement/AI speed, enrage timers and extra loot come on top. |
| **Custom bosses** | Built on a vanilla boss NPC (so the boss bar and player-count scaling work) with a custom name, custom difficulty, phases by health %, projectile volleys (aimed / ring / rain / random / on-target), minion waves, enrage, custom loot, biome/time/progression requirements, and "AI patches" that stop the base boss from fleeing the wrong biome or daytime. They look like the vanilla sprite, they fight differently. Six come built in: Desert Scourge, Crabulon, The Hive Mind, The Perforator Hive, The Slime God, Cryogen. |
| **Vanilla boss augments** | The same phase/attack/minion system layered on vanilla bosses (17 come configured, from King Slime to Moon Lord), enabled per mode. |
| **Progression** | A persisted checklist of custom bosses, requirement gates for summoning (custom bosses *and* vanilla flags such as `EyeOfCthulhu` or `WallOfFlesh`), and optional gates on vanilla bosses ("no Skeletron until the Desert Scourge is down"). |
| **Loot** | Custom drop tables (vanilla items only), mode-specific extra boss drops, per-entry chance/stack/prefix/minimum mode, optional suppression of the base boss's own loot. |
| **Server-side crafting** | `/cs craft <recipe>`: recipes with ingredients, result, required crafting station tile and progression gates. With server-side characters on, ingredients come from the inventory; otherwise from items dropped on the ground near the player. |
| **Rage & Adrenaline** (v2) | Server-tracked meters per player (Revengeance and up): Rage fills near bosses and when you take hits, Adrenaline fills while you avoid damage and resets when hit. When full they fire automatically (or `/cs rage`, `/cs adren`) and multiply your damage for a few seconds. The bonus is applied as an extra server-side hit every client sees. |
| **Rogue class** (v2) | `/cs class rogue`: stealth builds while you don't attack (faster standing still). Rogue weapons - thrown consumables plus a configurable list - deal up to +25% at full stealth, and a hit at full stealth is a **stealth strike** (x2, spends all stealth, releases a burst of shrapnel). |
| **HUD** (v2) | The meters are drawn through the game's own server-status text (colour coded, no client mod needed): the client puts that line near the top right of the screen, just above the minimap. `/cs hud off` hides it. |
| **Events** (v2) | `events.json`: wave events (Acid Rain: five waves of scaled ocean enemies ending in The Mauler) and a **Boss Rush** (every boss back to back, no loot until the end). Events gate on progression, reward every participant, and count as progression themselves (`event:acidrain`). |
| **Arenas** (v2) | `/cs arena build <name> [biome]` builds a flat platform arena at your feet and, optionally, a "biome pad" of enough biome blocks under the floor to make the game treat it as that biome - so a mushroom arena anywhere lets you summon Crabulon there. `/cs arena undo <name>` puts the old tiles back. |
| **Region effects** (v2) | `regions.json`: any TShock region can get buffs while inside (Brimstone Crag burns, the Abyss blinds and slows), tougher natural spawns, replacement spawn tables, entry/exit messages and a boss-summon ban. |

Known limits (inherent to server-side plugins):

* Contact damage is computed by the client from the same `difficulty` value as max life, so **HP and damage scale together**. To give a boss more HP *without* more damage use `PlayerCountOverride` (bosses get +HP per player in Expert+; the override pretends there are more players).
* Projectile attacks must use projectile types that are hostile by default (the hostile flag is not networked). The catalog validates this on load and warns in the console.
* `ReplaceVanillaLoot` suppresses the base boss's item table and treasure bag, but coins and the generic boss healing potions still drop.
* Boss movement speed-up only applies to no-tile-collision (flying) bosses and is cosmetic-ish: clients predict vanilla speed between syncs, so set `BossMovementSpeedMultiplier` to `1.0` if a boss looks jittery on your connection.
* Rage/Adrenaline/stealth bonuses cannot change the number your own hit shows; they land as a second, server-applied hit with its own damage number right after yours (the enemy's health bar is correct either way). Stealth-strike shrapnel is spawned and resolved by the server: it hurts enemies, never players, and uses a projectile type that is friendly by default (`StealthStrikeProjectile`), so keep it one.
* The HUD uses the game's server-status text line. The client draws it at a fixed spot near the top right (the same place the "connecting..." status appears), which is just above the minimap frame, so with the minimap open the two sit close together - shrink the minimap or use `/cs hud off` if it bothers you. Any other plugin that writes status text will fight with it. During development the HUD was checked against the game's drawing and network code, not on a real client, so report anything that looks off.
* Arena biome pads work because the client counts biome blocks around you; the pad must stay within the ~170x120 tile scan window around the player, which the default 120x50 arena satisfies. Desert pads do not count in the ocean strip at the world's edges.

## Getting the DLL

### From GitHub (recommended)

The repository builds itself: every push to `main` runs `.github/workflows/build.yml`, which compiles the plugin on
GitHub's servers and publishes `CalamityShock.dll`

* on the **`release` branch** - that branch holds only the DLL, so a server can `git pull` it (next section);
* as a **workflow artifact** (Actions tab, open the run, download "CalamityShock");
* attached to a **GitHub Release** whenever you push a tag such as `v0.2.0`.

First-time setup of the repository, from this folder (needs git and the [GitHub CLI](https://cli.github.com/)):

```bash
git init -b main
git add -A
git commit -m "CalamityShock"
gh repo create CalamityShock --private --source=. --push
```

(Without the GitHub CLI: create an empty repository on github.com, then `git remote add origin <its url>` and
`git push -u origin main`.) The first build takes a minute or two; afterwards the `release` branch exists.

### Building it yourself

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
cd CalamityShock
dotnet build -c Release
```

The DLL is `bin/Release/CalamityShock.dll`. The project pulls `TShock 6.2.1` (and TSAPI/OTAPI) from NuGet; nothing
else is required. Offline build against an unpacked TShock release folder (the one with
`ServerPlugins/TShockAPI.dll` and `bin/OTAPI.dll`):

```bash
dotnet build -c Release -p:TShockRefDir=/path/to/unpacked/tshock
```

## Install

1. Get `CalamityShock.dll` into your server's `ServerPlugins/` folder (next to `TShockAPI.dll`). Only that one file.
   Either copy it there, or let git keep it up to date - run this **inside** `ServerPlugins/` (git needs to be
   installed on the server; the folder is not empty, which is why this is an init + checkout rather than a clone):

   ```bash
   git init
   git remote add origin https://github.com/<you>/CalamityShock.git
   git fetch origin release
   git checkout release
   ```

   `CalamityShock.dll` now sits next to `TShockAPI.dll`, and the branch's `.gitignore` hides everything else in the
   folder from git. From then on, updating the plugin is `git pull` inside `ServerPlugins/` followed by a server
   restart (plugins are only loaded at start-up).
2. Start the server. On first start the plugin writes its files to `tshock/CalamityShock/`:
   * `config.json` - modes, global switches, vanilla boss gates, `Players` (meters/rogue/HUD) and `Arena` settings
   * `bosses.json` - custom bosses and vanilla augments
   * `recipes.json` - server-side recipes
   * `events.json` - wave events and boss rushes
   * `regions.json` - region effects (keyed by TShock region name)
   * `players.json` - class and HUD choices per player
   * `progress.json` - which custom bosses and events are done (saved with the world)
   * `arenas/` - undo records for built arenas
3. Edit the JSON files and run `/cs reload` (or TShock's `/reload`). `config.json` is rewritten after every successful
   load so that keys added by newer versions appear with their defaults (so comments in it do not survive and a misspelled key is silently dropped; check the
   file after a reload if a setting does not seem to take).

Plugins built for TShock 5.x (.NET 6) do not load on 6.x and vice versa; this one targets 6.x.

## Commands

Everyone (progression permitting):

| Command | What it does |
|---|---|
| `/cs help` | Overview |
| `/cs mode` | Show the active mode and the world's difficulty |
| `/cs bosses` | List custom bosses and whether they are locked / available / defeated |
| `/cs boss <id>` | Requirements, difficulty in the current mode and loot of a boss |
| `/cs summon <id>` | Summon a custom boss (requirements, biome, time, summon item are checked) |
| `/cs checklist` | Vanilla and custom boss checklist |
| `/cs status` | Active boss fights: HP %, phase, enrage timer, applied stats |
| `/cs recipes [page]`, `/cs recipe <name>` | Browse recipes |
| `/cs craft <name>` | Craft |
| `/cs class rogue` / `/cs class none` | Pick or drop the Rogue class (saved per account) |
| `/cs rage`, `/cs adren` | Fire a full Rage / Adrenaline meter yourself (they also fire automatically by default) |
| `/cs hud on` / `off` | Show or hide the meter HUD |
| `/cs meters` | Everyone's meters, for checking what the server thinks |
| `/cs event list` / `start <id>` / `stop` / `status` | Events (anyone by default; set `AnyoneCanStartEvents` false to require `calamityshock.event`) |

Admin (`calamityshock.admin`, superadmins have it through `*`):

| Command | What it does |
|---|---|
| `/cs mode <name>` | Switch mode (saved to config.json). Bosses already alive keep their stats. |
| `/cs summon <id> force` | Skip the requirement checks |
| `/cs test <id>` | Spawn a boss with no checks (works from the console; spawns at the world spawn point) |
| `/cs setdowned <id> [true/false]` | Edit progression |
| `/cs reset all` | Clear custom boss progression (vanilla flags untouched) |
| `/cs despawn` | Remove all active custom bosses silently |
| `/cs reload` | Reload all JSON files |
| `/cs arena build <name> [desert/snow/corruption/crimson/hallow/jungle/mushroom/none]` | Build an arena at your feet (120x50 by default, see `Arena` in config.json) |
| `/cs arena undo <name>`, `/cs arena list` | Restore the old tiles / list built arenas |
| `/cs regions` | Show configured region effects and whether the TShock region exists yet |
| `/cs debug ...` | Only with `EnableDebugCommands: true` - `dummy` creates a fake player at spawn so bosses can be tested on an empty server, `kill`, `spawn <npc>`, `npcs`, `items`, `projectiles` |

Alias: `/calamity`. Optional permissions: `calamityshock.summon` and `calamityshock.craft` are only required when
`RequireSummonPermission` / `RequireCraftPermission` are set to true in config.json.

## config.json

```jsonc
{
  "ActiveMode": "normal",            // normal | revengeance | death (or any key you add to Modes)
  "Modes": {
    "revengeance": {
      "DisplayName": "Revengeance",
      "Rank": 1,                     // ordering, used by loot "MinMode"
      "EnemyDifficultyBonus": 0.25,  // added to the world difficulty for hostile non-boss NPCs
      "BossDifficultyBonus": 0.5,    // added for bosses
      "EnemyDefenseMultiplier": 1.1, // server-side only
      "BossDefenseMultiplier": 1.15,
      "BossMovementSpeedMultiplier": 1.15, // flying bosses listed in MovementBoostNpcs
      "BossAiSpeed": 1.2,            // 1.0 vanilla; 1.5 = an extra AI step every other tick (faster attacks)
      "BossEnrageSeconds": 600,      // 0 = never
      "EnrageAttackRateMultiplier": 1.5,
      "EnrageMovementMultiplier": 1.2,
      "VanillaBossAugments": true,   // use the "VanillaAugments" phases from bosses.json
      "ExtraBossLoot": [ { "Item": "LifeCrystal", "MinStack": 1, "MaxStack": 1, "Chance": 0.5 } ]
    }
  },
  "ScaleEnemies": true,
  "ScaleVanillaBosses": true,
  "AnnounceModeOnJoin": true,
  "AnnouncePhases": true,
  "RequireSummonPermission": false,
  "RequireCraftPermission": false,
  "GroundCraftRadiusTiles": 8,       // crafting without SSC takes ingredients lying within this radius
  "StationRadiusTiles": 6,
  "CustomBossesSetVanillaFlags": false, // false: killing Crabulon does not count as killing King Slime
  "EnableDebugCommands": false,
  "MovementBoostNpcs": [ "EyeofCthulhu", "BrainofCthulhu", "..." ],
  "VanillaBossGates": { "SkeletronHead": [ "desertscourge" ] } // NPC name -> requirements
}
```

How much is "+0.5 difficulty"? Max life scales 1:1 with the difficulty value (Expert = 2, so +0.5 is +25% life
relative to Expert); contact damage follows a slightly steeper curve, and the curve gets steeper again above
Master (3), so keep bonuses modest. The shipped Revengeance/Death numbers are a starting point, not a balance pass.

## bosses.json

Each custom boss:

```jsonc
{
  "Id": "crabulon",
  "DisplayName": "Crabulon",
  "BaseNpc": "KingSlime",             // a vanilla *boss* type, by name or id
  "SegmentNpcs": [],                  // other NPC types that are part of this boss (worm segments, Creepers, Moon Lord parts)
  "Difficulty": null,                 // absolute value (0.5 is the lowest the game accepts), or null to use world + mode bonus + DifficultyBonus
  "DifficultyBonus": 0.25,
  "PlayerCountOverride": 0,           // pretend N players for the Expert+ boss HP bonus (0 = real count)
  "DefenseMultiplier": 1.0,
  "MovementSpeedMultiplier": 0,       // 0 = mode value; only matters for flying bases
  "AiSpeed": 1.25,                    // 0 = mode value
  "EnrageSeconds": 0,                 // 0 = mode default, -1 = never
  "AiPatches": [ "InEvil" ],          // see below
  "RequiresDowned": [ "desertscourge", "EyeOfCthulhu" ],
  "RequiredBiome": "Mushroom",        // Desert, Jungle, Corruption, Crimson, Mushroom, Snow, Dungeon, Hallow, Underworld, Ocean, Sky, Underground, Any
  "RequireNight": false, "RequireDay": false, "RequireHardmode": false,
  "SummonItem": "", "SummonItemStack": 1,   // required in inventory; consumed only with SSC
  "SpawnMessage": "{name} crawls out of the glowing mushrooms!",
  "DefeatMessage": "{name} has been defeated!",
  "EnrageMessage": "{name} has become enraged!",
  "Phases": [
    {
      "HealthBelowPercent": 100,
      "Message": "",
      "MovementSpeedMultiplier": 0, "AiSpeed": 0,
      "Attacks": [
        { "Projectile": "SpikedSlimeSpike", "Pattern": "ring", "Count": 8, "Speed": 7, "Damage": 20,
          "IntervalTicks": 180, "StartDelayTicks": 60, "BurstCount": 1, "BurstGapTicks": 8,
          "SpreadDegrees": 30, "RainWidth": 900, "RainHeight": 700, "OffsetX": 0, "OffsetY": 0, "MaxRange": 2400 }
      ],
      "Minions": [
        { "Npc": "AnomuraFungus", "Count": 2, "IntervalTicks": 600, "MaxAlive": 4, "SpreadPixels": 160, "InheritBossDifficulty": false, "Message": "" }
      ]
    }
  ],
  "Loot": [ { "Item": "GlowingMushroom", "MinStack": 40, "MaxStack": 60, "Chance": 1.0, "Prefix": 0, "MinMode": "" } ],
  "ReplaceVanillaLoot": true
}
```

Attack patterns: `aimed` (fan around the nearest player), `ring`, `rain` (falls from above the target),
`random`, `ontarget` (spawns at the target's position - use for Sandnado-style stationary attacks).
`Damage` is the base; clients multiply hostile projectile damage by the world difficulty (x2 Expert, x3 Master).

AI patches (needed because the base boss keeps its vanilla brain):

* `InEvil`, `InJungle`, `InSnow`, `InHallow`, `InDesert`, `InUndergroundDesert`, `InBeach`, `InDungeon`, `InMushroom`, `InSky` -
  while the boss AI runs, every player counts as being in that biome. Eater of Worlds and Brain of Cthulhu leave when
  the player is not in Corruption/Crimson, so the Desert Scourge and Hive Mind use `InEvil`.
* `Night` / `Day` - the AI believes it is night/day (Ice Queen, Pumpking, Mourning Wood, the Eye, Skeletron, the Twins flee at dawn).
* `FrostMoon` / `PumpkinMoon` - the AI believes the event is running.

`VanillaAugments` is a dictionary keyed by vanilla NPC name with the same shape (no `DisplayName`, no requirements).
Worm bosses need `SegmentNpcs` so the whole worm counts as one boss.

Names are matched case-insensitively ignoring spaces: `KingSlime`, `king slime` and `50` are all accepted.
NPC/item/projectile/tile names are the field names of `NPCID`, `ItemID`, `ProjectileID`, `TileID`
(e.g. `EaterofWorldsHead`, `LifeCrystal`, `CursedFlameHostile`, `Anvils`).

Hostile projectiles that work well as attacks: `SandBallFalling`, `SandnadoHostile`, `Stinger`, `QueenBeeStinger`,
`CursedFlameHostile`, `Skull`, `DemonSickle`, `EyeLaser`, `DeathLaser`, `PinkLaser`, `FrostBlastHostile`,
`FrostWave`, `FrostShard`, `IceSpike`, `IcewaterSpit`, `SpikedSlimeSpike`, `PoisonDartShotFromSlimes`,
`GoldenShowerHostile`, `ShadowBeamHostile`, `Spike`, `BloodShot`, `BloodNautilusShot`, `Fireball`, `RockGolemRock`,
`SeedPlantera`, `PoisonSeedPlantera`, `ThornBall`, `SharknadoBolt`, `Cthulunado`, `BombSkeletronPrime`,
`RocketSkeleton`, `QueenSlimeGelAttack`, `QueenSlimeMinionBlueSpike`, `QueenSlimeMinionPinkBall`,
`CultistBossFireBall`, `CultistBossIceMist`, `PhantasmalBolt`, `PhantasmalEye`, `PhantasmalSphere`, `NebulaBolt`,
`VortexLaser`, `StardustSoldierLaser`, `MartianTurretBolt`, `SaucerLaser`, `WebSpit`, `HarpyFeather`, `MudBall`, `AshBallFalling`.

## recipes.json

```jsonc
{
  "Recipes": [
    {
      "Name": "reaver-shark",
      "Description": "A pickaxe from the deep.",
      "Ingredients": [ { "Item": "SharkFin", "Stack": 2 }, { "Item": "Coral", "Stack": 8 } ],
      "ResultItem": "ReaverShark", "ResultStack": 1, "ResultPrefix": 0,   // -1 = random prefix
      "RequiresStation": "Anvils",     // Anvils, WorkBenches, Furnaces, MythrilAnvil, DemonAltar, Hellforge, AlchemyTable, TinkerersWorkbench...
      "RequiresDowned": [ "desertscourge" ],
      "RequiresHardmode": false,
      "MinMode": ""
    }
  ]
}
```

## Rage, Adrenaline and the Rogue class (config.json "Players")

```jsonc
"Players": {
  "AutoActivateRage": true,           // fire when full (there are no key binds server-side; /cs rage also works)
  "RageGainPerSecondNearBoss": 3,     // a boss within BossRangePixels
  "RagePerDamageTaken": 0.4,          // +40 rage for a 100 damage hit
  "RageDecayPerSecond": 10,           // when no boss is near
  "RageDurationSeconds": 9, "RageDamageMultiplier": 1.35,
  "AutoActivateAdrenaline": true,
  "AdrenalineChargeSeconds": 45,      // any hit taken resets it to 0
  "AdrenalineDurationSeconds": 5, "AdrenalineDamageMultiplier": 2.0,
  "AdrenalineDecayPerSecond": 20,
  "BossRangePixels": 3200,
  "StealthFillSecondsStill": 5, "StealthFillSecondsMoving": 12, "StealthPauseTicksAfterAttack": 45,
  "StealthDamageBonusAtFull": 0.25, "StealthStrikeMultiplier": 2.0,
  "StealthStrikeProjectile": "Shuriken", "StealthStrikeShrapnelCount": 4, "StealthStrikeShrapnelDamageFraction": 0.5,
  "RogueIncludesThrownConsumables": true,   // shuriken, knives, javelins, grenades, bones...
  "RogueItems": [ "FlyingKnife", "ShadowFlameKnife", "VampireKnives", "BloodyMachete", "Flairon", "PaperAirplaneA", "PaperAirplaneB" ],
  "RogueHitWindowTicks": 180,         // a hit is a rogue hit if a rogue projectile was thrown within the last 3 s
  "HudEnabledByDefault": true, "HudUpdateTicks": 15, "HudAlwaysVisible": false
}
```

Each mode has `RageAdrenalineEnabled` (true for revengeance and death by default, false for normal; when the key is
missing, as in a config.json written by v0.1, it is on for every mode with `Rank` 1 or higher). How the bonus
works: your own hit goes through untouched (TShock's anti-cheat checks stay in place); the plugin then applies a
second hit for the extra damage, credited to you, which shows as its own damage number on every client. The hit
packet does not say which weapon dealt the damage, so a rogue hit is "a rogue weapon in hand and one of its
projectiles thrown within `RogueHitWindowTicks`" - minions and other weapons do not get the stealth bonus.
Stealth builds while you are not attacking: launching a projectile or hitting something with the use button held
pauses it for `StealthPauseTicksAfterAttack`; minion hits do not.

## events.json

```jsonc
{
  "Events": [
    {
      "Id": "acidrain", "DisplayName": "Acid Rain", "Type": "waves",
      "RequiredBiome": "Ocean",           // waves spawn near players standing in this biome; nobody there = the wave waits
      "RequiresDowned": [],
      "Waves": [
        { "Name": "Wave 1", "Message": "...", "MaxAlive": 8, "SpawnIntervalTicks": 60,
          "Spawns": [ { "Npc": "Crab", "Count": 6, "DifficultyBonus": 0 }, { "Boss": "mauler" } ] }
      ],
      "TimeLimitSeconds": 0,
      "Loot": [ { "Item": "SharkFin", "MinStack": 3, "MaxStack": 6 } ],   // dropped at every participant
      "StartMessage": "{name} has begun!", "CompleteMessage": "...", "FailMessage": "..."
    },
    {
      "Id": "bossrush", "DisplayName": "Boss Rush", "Type": "bossrush",
      "RequiresDowned": [ "MoonLord" ],
      "Bosses": [ "KingSlime", "desertscourge", "Retinazer+Spazmatism", "MoonLordCore" ],   // custom ids or vanilla names; "+" = fought together
      "BossRushDelayTicks": 240, "SuppressBossLoot": true, "FailOnDespawn": true,
      "BossAiPatches": [ "Night", "InEvil" ],   // so the Twins don't leave at dawn and the Eater doesn't leave the corruption
      "Loot": [ { "Item": "CelestialSigil" } ]
    }
  ]
}
```

A `"Boss": "<id>"` wave entry spawns a custom boss from bosses.json (The Mauler ships `Hidden: true`, so it only
appears in the event). Completing an event records `event:<id>`, usable in any `RequiresDowned` list.

## regions.json

```jsonc
{
  "Regions": {
    "brimstone-crag": {                   // the name of a TShock region (/region define brimstone-crag, then set its corners)
      "Buffs": [ { "Buff": "OnFire", "Seconds": 2 } ],
      "DifficultyBonus": 1.0,             // natural spawns inside get this on top of the mode
      "SpawnReplace": [ { "Npc": "RedDevil", "Weight": 2 }, { "Npc": "FireImp", "Weight": 3 } ],
      "SpawnReplaceChance": 0.6,
      "EnterMessage": "The air burns.", "ExitMessage": "You leave the Brimstone Crag.",
      "BlockBossSummons": false
    }
  }
}
```

## Arenas

`/cs arena build <name> [biome]` clears a `Width` x `Height` box above your feet, lays a stone floor, rows of wooden
platforms every `PlatformSpacing` tiles with torches, and - with a biome - enough biome blocks under the floor
(1500 sand, 1500 ice, 300 ebonstone/crimstone, 125 pearlstone, 140 jungle grass, 100 mushroom grass) for the
game's biome scan to flag the area. Desert pads don't count within the ocean strip at the world's edges. The tiles you
overwrote are saved under `tshock/CalamityShock/arenas/<name>.json`; `/cs arena undo <name>` restores them.

## Progression requirement names

Custom boss ids (`desertscourge`, ...), completed events (`event:acidrain`), or these vanilla flags: `KingSlime`, `EyeOfCthulhu`, `EvilBoss`
(`EaterOfWorlds` / `BrainOfCthulhu`), `QueenBee`, `Skeletron`, `Deerclops`, `WallOfFlesh` (`Hardmode`), `QueenSlime`,
`TheDestroyer`, `TheTwins`, `SkeletronPrime`, `AnyMechBoss`, `AllMechBosses`, `Plantera`, `Golem`, `DukeFishron`,
`EmpressOfLight`, `LunaticCultist`, `MoonLord`, `GoblinArmy`, `PirateInvasion`, `FrostLegion`, `MartianMadness`,
`Pumpking`, `MourningWood`, `IceQueen`, `SantaNK1`, `Everscream`.

## Project layout

```
CalamityShock.csproj     net9.0, PackageReference TShock 6.2.1 (or -p:TShockRefDir for offline builds)
src/Plugin.cs            entry point, hook registration, reload, game-thread queue
src/Config.cs            config.json model + JSON helpers
src/Difficulty.cs        the SetDefaults re-run that makes clients agree with the server's numbers
src/BossData.cs          bosses.json model, default catalog, AI patches
src/BossController.cs    per-fight state: phases, attacks, minions, enrage, AI speed
src/BossManager.cs       catalog, NPC hooks (spawn scaling, adoption, gating, AI, kills, loot, messages), summoning
src/Progression.cs       progress.json, vanilla flag lookup, vanilla flag snapshot/restore
src/Loot.cs              loot table rolling
src/Crafting.cs          recipes.json model and crafting (inventory with SSC, ground items without)
src/Players.cs           Rage/Adrenaline/stealth meters, Rogue class, bonus strikes, HUD
src/Events.cs            events.json model, wave events, Boss Rush
src/Arena.cs             /cs arena build / undo, biome pads
src/Regions.cs           regions.json model, region buffs and spawn replacement
src/Commands.cs          /cs
src/Ids.cs, src/Net.cs   name<->id resolution, networking helpers
.github/                 the GitHub Actions build (workflows/build.yml), the script that publishes the DLL to the
                         release branch, and the README/.gitignore that branch carries
```

## Testing without players

Set `EnableDebugCommands: true` and use `/cs debug dummy` from the console to create a fake player at the world
spawn (bosses target it, events spawn around it, meters tick for it). Then `/cs test <boss>`, `/cs debug kill`,
`/cs debug strike <npc> <damage>`, `/cs debug setmeter stealth 100`, `/cs debug zone beach`,
`/cs debug region <name>`, `/cs debug as <sub-command...>` (run a command as the dummy, e.g. `arena build x mushroom`),
`/cs debug tiles <tile>`, `/cs debug npcs|items|projectiles`. Run the server with `-forceupdate` so the world ticks
with nobody connected. Never use the dummy while real players are online.

## Ideas for the next step

* More custom bosses in `bosses.json` (every Calamity boss can be mapped onto a vanilla base with phases and patches).
* Class accessories: react to what the player wears (the server knows the armor/accessory slots) to grant stealth or rage bonuses.
* Scheduled events: start Acid Rain automatically after a random delay once its requirements are met.
