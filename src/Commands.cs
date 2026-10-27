using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;

namespace CalamityShock
{
    /// <summary>/cs (alias /calamity) and all of its sub-commands.</summary>
    public class PluginCommands
    {
        public const string AdminPermission = "calamityshock.admin";
        public const string SummonPermission = "calamityshock.summon";
        public const string CraftPermission = "calamityshock.craft";

        private readonly CalamityShockPlugin _plugin;
        private Command _command;

        public PluginCommands(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        public void Register()
        {
            _command = new Command(Handle, "cs", "calamity")
            {
                HelpText = "CalamityShock: /cs help",
                AllowServer = true,
            };
            TShockAPI.Commands.ChatCommands.Add(_command);
        }

        public void Unregister()
        {
            if (_command != null) TShockAPI.Commands.ChatCommands.Remove(_command);
        }

        private bool IsAdmin(TSPlayer p) => p != null && (!p.RealPlayer || p.HasPermission(AdminPermission));

        private void Handle(CommandArgs args)
        {
            // In-game commands arrive on the game thread; console/REST commands do not and would race the world update.
            _plugin.RunSafely(() => Dispatch(args), args.Player != null && args.Player.RealPlayer);
        }

        private void Dispatch(CommandArgs args)
        {
            TSPlayer p = args.Player;
            string sub = args.Parameters.Count > 0 ? args.Parameters[0].ToLowerInvariant() : "help";
            try
            {
                switch (sub)
                {
                    case "help": Help(p); break;
                    case "mode": Mode(p, args.Parameters); break;
                    case "bosses": case "list": Bosses(p); break;
                    case "boss": case "info": BossInfo(p, args.Parameters); break;
                    case "summon": case "spawn": Summon(p, args.Parameters); break;
                    case "checklist": case "progress": Checklist(p); break;
                    case "status": case "active": Status(p); break;
                    case "recipes": Recipes(p, args.Parameters); break;
                    case "recipe": RecipeInfo(p, args.Parameters); break;
                    case "craft": Craft(p, args.Parameters); break;
                    case "reload": Reload(p); break;
                    case "setdowned": SetDowned(p, args.Parameters); break;
                    case "reset": Reset(p, args.Parameters); break;
                    case "despawn": Despawn(p); break;
                    case "test": Test(p, args.Parameters); break;
                    case "debug": Debug(p, args.Parameters); break;
                    case "class": ClassCmd(p, args.Parameters); break;
                    case "rage": Rage(p); break;
                    case "adren": case "adrenaline": Adrenaline(p); break;
                    case "hud": Hud(p, args.Parameters); break;
                    case "meters": Meters(p); break;
                    case "event": case "events": Event(p, args.Parameters); break;
                    case "arena": Arena(p, args.Parameters); break;
                    case "regions": Regions(p); break;
                    default:
                        p.SendErrorMessage($"Unknown sub-command '{sub}'. Try /cs help.");
                        break;
                }
            }
            catch (Exception ex)
            {
                p.SendErrorMessage("CalamityShock command failed: " + ex.Message);
                Log.Error($"/cs {sub} failed: {ex}");
            }
        }

        private void Help(TSPlayer p)
        {
            CalamityConfig cfg = _plugin.Config;
            p.SendInfoMessage($"CalamityShock - active mode: {cfg.CurrentMode.DisplayName}");
            p.SendMessage("/cs bosses | /cs boss <id> | /cs summon <id> | /cs checklist | /cs status", Color.LightGray);
            p.SendMessage("/cs recipes [page] | /cs recipe <name> | /cs craft <name> | /cs mode", Color.LightGray);
            p.SendMessage("/cs class rogue|none | /cs rage | /cs adren | /cs hud on|off | /cs meters | /cs event list|start|stop|status", Color.LightGray);
            if (IsAdmin(p))
                p.SendMessage("admin: /cs mode <name> | /cs summon <id> force | /cs setdowned <id> [true|false] | /cs reset all | /cs despawn | /cs reload | /cs arena build <name> [biome] | /cs arena undo <name> | /cs regions", Color.LightGray);
        }

        private void Mode(TSPlayer p, List<string> prm)
        {
            CalamityConfig cfg = _plugin.Config;
            if (prm.Count < 2)
            {
                ModeSettings m = cfg.CurrentMode;
                p.SendInfoMessage($"Active mode: {m.DisplayName} ({cfg.ActiveMode}). Enemies +{m.EnemyDifficultyBonus:0.##} difficulty, bosses +{m.BossDifficultyBonus:0.##}, boss enrage {(m.BossEnrageSeconds > 0 ? m.BossEnrageSeconds + "s" : "off")}.");
                p.SendMessage("Modes: " + string.Join(", ", cfg.Modes.OrderBy(kv => kv.Value.Rank).Select(kv => $"{kv.Key} ({kv.Value.DisplayName})")), Color.LightGray);
                p.SendMessage($"World difficulty is {Difficulty.World:0.##} (Classic 1, Expert 2, Master 3). An NPC's max life scales 1:1 with its difficulty.", Color.LightGray);
                return;
            }
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + " to change the mode."); return; }
            string name = prm[1];
            if (!cfg.Modes.ContainsKey(name)) { p.SendErrorMessage($"No mode named '{name}'. Modes: {string.Join(", ", cfg.Modes.Keys)}"); return; }
            cfg.ActiveMode = cfg.Modes.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            _plugin.SaveConfig();
            Net.Broadcast($"Difficulty mode changed to {cfg.CurrentMode.DisplayName}. Bosses already alive keep their old stats.", Net.EventColor);
        }

        private void Bosses(TSPlayer p)
        {
            var defs = _plugin.Bosses.Definitions;
            if (defs.Count == 0) { p.SendInfoMessage("No custom bosses are defined (tshock/CalamityShock/bosses.json)."); return; }
            p.SendInfoMessage("Custom bosses (/cs boss <id> for details):");
            foreach (BossDefinition d in defs)
            {
                if (d.Hidden) continue;
                bool downed = _plugin.Progression.IsCustomDowned(d.Id);
                var missing = _plugin.Progression.Unmet(d.RequiresDowned);
                string state = downed ? "[defeated]" : (missing.Count == 0 ? "[available]" : "[locked]");
                p.SendMessage($"{state} {d.Id} - {d.DisplayName} (built on {Ids.NpcName(d.BaseNpcId)}{(d.RequiredBiome != "Any" ? ", " + d.RequiredBiome : "")})", downed ? Color.LightGreen : (missing.Count == 0 ? Color.White : Color.Gray));
            }
        }

        private void BossInfo(TSPlayer p, List<string> prm)
        {
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs boss <id>"); return; }
            BossDefinition d = _plugin.Bosses.Find(prm[1]);
            if (d == null) { p.SendErrorMessage($"No boss '{prm[1]}'. /cs bosses lists them."); return; }
            var cat = _plugin.Bosses.Catalog;
            p.SendInfoMessage($"{d.DisplayName} ({d.Id}) - base NPC {Ids.NpcName(d.BaseNpcId)}, {d.Phases.Count} phases");
            string req = d.RequiresDowned.Count == 0 ? "none" : string.Join(", ", d.RequiresDowned.Select(r => ProgressionManager.Describe(r, cat) + (_plugin.Progression.IsMet(r) ? " (done)" : " (missing)")));
            p.SendMessage($"Requires: {req}; biome: {d.RequiredBiome}{(d.RequireNight ? ", night" : "")}{(d.RequireDay ? ", day" : "")}{(d.RequireHardmode ? ", hardmode" : "")}{(d.SummonItemId > 0 ? $", {Math.Max(1, d.SummonItemStack)}x {Ids.ItemName(d.SummonItemId)}" : "")}", Color.LightGray);
            p.SendMessage($"Difficulty: {Difficulty.ForBoss(_plugin.Config.CurrentMode, d):0.##} in the current mode; kills so far: {_plugin.Progression.KillCount(d.Id)}", Color.LightGray);
            if (d.Loot.Count > 0)
                p.SendMessage("Loot: " + string.Join(", ", d.Loot.Select(l => $"{Ids.ItemName(l.ItemId)}{(l.MaxStack > 1 ? $" x{l.MinStack}-{l.MaxStack}" : "")}{(l.Chance < 1f ? $" ({l.Chance * 100f:0}%)" : "")}{(!string.IsNullOrEmpty(l.MinMode) ? $" [{l.MinMode}+]" : "")}")), Color.LightGray);
        }

        private void Summon(TSPlayer p, List<string> prm)
        {
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs summon <id> [force]"); return; }
            if (!p.RealPlayer) { p.SendErrorMessage("Bosses can only be summoned by a player in the world."); return; }
            if (_plugin.Config.RequireSummonPermission && !p.HasPermission(SummonPermission) && !IsAdmin(p))
            {
                p.SendErrorMessage("You need " + SummonPermission + " to summon bosses.");
                return;
            }
            BossDefinition d = _plugin.Bosses.Find(prm[1]);
            if (d == null) { p.SendErrorMessage($"No boss '{prm[1]}'. /cs bosses lists them."); return; }
            bool force = prm.Count > 2 && prm[2].Equals("force", StringComparison.OrdinalIgnoreCase);
            if (force && !IsAdmin(p)) { p.SendErrorMessage("Only admins can force a summon."); force = false; }
            string error = _plugin.Bosses.TrySummon(p, d, force);
            if (error != null) p.SendErrorMessage(error);
        }

        private void Checklist(TSPlayer p)
        {
            p.SendInfoMessage("Vanilla progression:");
            var vanilla = ProgressionManager.ChecklistOrder.Select(k => (_plugin.Progression.IsMet(k) ? "[x] " : "[ ] ") + k);
            p.SendMessage(string.Join("  ", vanilla), Color.LightGray);
            if (_plugin.Bosses.Definitions.Count > 0)
            {
                p.SendInfoMessage("Custom bosses:");
                var custom = _plugin.Bosses.Definitions.Select(d => (_plugin.Progression.IsCustomDowned(d.Id) ? "[x] " : "[ ] ") + d.DisplayName + (_plugin.Progression.KillCount(d.Id) > 1 ? $" x{_plugin.Progression.KillCount(d.Id)}" : ""));
                p.SendMessage(string.Join("  ", custom), Color.LightGray);
            }
        }

        private void Status(TSPlayer p)
        {
            var lines = _plugin.Bosses.StatusLines();
            if (lines.Count == 0) { p.SendInfoMessage("No boss is active."); return; }
            foreach (string l in lines) p.SendMessage(l, Color.LightGray);
        }

        private void Recipes(TSPlayer p, List<string> prm)
        {
            var recipes = _plugin.Crafting.Recipes;
            if (recipes.Count == 0) { p.SendInfoMessage("No recipes are defined (tshock/CalamityShock/recipes.json)."); return; }
            const int perPage = 6;
            int pages = (recipes.Count + perPage - 1) / perPage;
            int page = 1;
            if (prm.Count > 1 && int.TryParse(prm[1], out int n)) page = Math.Clamp(n, 1, pages);
            p.SendInfoMessage($"Recipes (page {page}/{pages}) - /cs recipe <name> for details, /cs craft <name> to craft:");
            foreach (Recipe r in recipes.Skip((page - 1) * perPage).Take(perPage))
            {
                bool ok = _plugin.Crafting.CheckGates(p, r) == null;
                p.SendMessage($"{(ok ? "[available]" : "[locked]")} {r.Name} -> {r.ResultStack}x {Ids.ItemName(r.ResultItemId)}: {r.IngredientText()}{(r.StationTileId >= 0 ? " @ " + r.RequiresStation : "")}", ok ? Color.White : Color.Gray);
            }
        }

        private void RecipeInfo(TSPlayer p, List<string> prm)
        {
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs recipe <name>"); return; }
            Recipe r = _plugin.Crafting.Find(prm[1]);
            if (r == null) { p.SendErrorMessage($"No recipe '{prm[1]}'. /cs recipes lists them."); return; }
            p.SendInfoMessage($"{r.Name}: {r.Description}");
            p.SendMessage($"Makes {r.ResultStack}x {Ids.ItemName(r.ResultItemId)} from {r.IngredientText()}{(r.StationTileId >= 0 ? " near a " + r.RequiresStation : "")}", Color.LightGray);
            string gate = _plugin.Crafting.CheckGates(p, r);
            p.SendMessage(gate ?? "You meet the requirements.", gate == null ? Color.LightGreen : Color.Orange);
            if (!Main.ServerSideCharacter)
                p.SendMessage($"Server-side characters are off: drop the ingredients on the ground within {_plugin.Config.GroundCraftRadiusTiles} tiles of you, then /cs craft {r.Name}.", Color.LightGray);
        }

        private void Craft(TSPlayer p, List<string> prm)
        {
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs craft <name>"); return; }
            if (_plugin.Config.RequireCraftPermission && !p.HasPermission(CraftPermission) && !IsAdmin(p))
            {
                p.SendErrorMessage("You need " + CraftPermission + " to craft.");
                return;
            }
            Recipe r = _plugin.Crafting.Find(prm[1]);
            if (r == null) { p.SendErrorMessage($"No recipe '{prm[1]}'. /cs recipes lists them."); return; }
            string error = _plugin.Crafting.TryCraft(p, r);
            if (error != null) p.SendErrorMessage(error);
            else p.SendSuccessMessage($"Crafted {r.ResultStack}x {Ids.ItemName(r.ResultItemId)}.");
        }

        private void Reload(TSPlayer p)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            _plugin.ReloadAll();
            p.SendSuccessMessage("CalamityShock config, bosses, recipes and progress reloaded.");
        }

        private void SetDowned(TSPlayer p, List<string> prm)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs setdowned <bossid> [true|false]"); return; }
            BossDefinition d = _plugin.Bosses.Find(prm[1]);
            if (d == null) { p.SendErrorMessage($"No boss '{prm[1]}'."); return; }
            bool value = prm.Count < 3 || !prm[2].Equals("false", StringComparison.OrdinalIgnoreCase);
            _plugin.Progression.SetDowned(d.Id, value);
            p.SendSuccessMessage($"{d.DisplayName} is now marked as {(value ? "defeated" : "not defeated")}.");
        }

        private void Reset(TSPlayer p, List<string> prm)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            if (prm.Count < 2 || !prm[1].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                p.SendErrorMessage("Usage: /cs reset all   (clears all custom boss progress; vanilla flags are untouched)");
                return;
            }
            _plugin.Progression.ResetAll();
            p.SendSuccessMessage("Custom boss progression cleared.");
        }

        // ------------------------------------------------------------------ v2: class, meters, HUD

        private void ClassCmd(TSPlayer p, List<string> prm)
        {
            if (!p.RealPlayer) { p.SendErrorMessage("Only players have a class."); return; }
            PlayerState st = _plugin.Players.Get(p);
            if (prm.Count < 2)
            {
                p.SendInfoMessage($"Your class: {(st != null && st.IsRogue ? "Rogue" : "none")}. /cs class rogue | none");
                p.SendMessage("Rogue: stealth builds while you don't attack (faster standing still). Rogue weapons deal up to +" + (_plugin.Config.Players.StealthDamageBonusAtFull * 100f).ToString("0") + "% at full stealth, and a hit at full stealth is a stealth strike (x" + _plugin.Config.Players.StealthStrikeMultiplier.ToString("0.#") + ", spends all stealth). Rogue weapons: thrown consumables plus the RogueItems list in config.json.", Color.LightGray);
                return;
            }
            string c = prm[1].ToLowerInvariant();
            if (c == "rogue") { _plugin.Players.SetClass(p, true); p.SendSuccessMessage("You are now a Rogue. Stand still to build stealth; your HUD shows it."); }
            else if (c == "none" || c == "off") { _plugin.Players.SetClass(p, false); p.SendSuccessMessage("Class cleared."); }
            else p.SendErrorMessage("Usage: /cs class rogue | none");
        }

        private void Rage(TSPlayer p)
        {
            if (!p.RealPlayer) { p.SendErrorMessage("Only players have Rage."); return; }
            string err = _plugin.Players.TryActivateRage(p);
            if (err != null) p.SendErrorMessage(err); else p.SendSuccessMessage($"Rage activated for {_plugin.Config.Players.RageDurationSeconds}s.");
        }

        private void Adrenaline(TSPlayer p)
        {
            if (!p.RealPlayer) { p.SendErrorMessage("Only players have Adrenaline."); return; }
            string err = _plugin.Players.TryActivateAdrenaline(p);
            if (err != null) p.SendErrorMessage(err); else p.SendSuccessMessage($"Adrenaline activated for {_plugin.Config.Players.AdrenalineDurationSeconds}s.");
        }

        private void Hud(TSPlayer p, List<string> prm)
        {
            if (!p.RealPlayer) { p.SendErrorMessage("Only players have a HUD."); return; }
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs hud on | off"); return; }
            bool on = prm[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            _plugin.Players.SetHud(p, on);
            p.SendSuccessMessage(on ? "HUD on." : "HUD off.");
        }

        private void Meters(TSPlayer p)
        {
            var lines = _plugin.Players.StatusLines();
            if (lines.Count == 0) { p.SendInfoMessage("No players online."); return; }
            if (!_plugin.Players.MetersEnabled) p.SendInfoMessage("Rage/Adrenaline are off in this mode (RageAdrenalineEnabled).");
            foreach (string l in lines) p.SendMessage(l, Color.LightGray);
        }

        // ------------------------------------------------------------------ v2: events

        private void Event(TSPlayer p, List<string> prm)
        {
            string sub = prm.Count > 1 ? prm[1].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "list":
                {
                    var defs = _plugin.Events.Definitions;
                    if (defs.Count == 0) { p.SendInfoMessage("No events are defined (tshock/CalamityShock/events.json)."); return; }
                    p.SendInfoMessage("Events (/cs event start <id>):");
                    foreach (EventDefinition d in defs)
                    {
                        var missing = _plugin.Progression.Unmet(d.RequiresDowned);
                        bool done = _plugin.Progression.IsCustomDowned("event:" + d.Id);
                        string what = d.IsBossRush ? $"boss rush, {d.ResolvedBosses.Count} bosses" : $"{d.Waves.Count} waves{(d.RequiredBiome != "Any" ? ", " + d.RequiredBiome : "")}";
                        p.SendMessage($"{(done ? "[done]" : missing.Count == 0 ? "[available]" : "[locked]")} {d.Id} - {d.DisplayName} ({what})", done ? Color.LightGreen : missing.Count == 0 ? Color.White : Color.Gray);
                    }
                    p.SendMessage(_plugin.Events.StatusLine(), Color.LightGray);
                    break;
                }
                case "start":
                {
                    if (prm.Count < 3) { p.SendErrorMessage("Usage: /cs event start <id> [force]"); return; }
                    if (!_plugin.Config.AnyoneCanStartEvents && !IsAdmin(p) && !p.HasPermission("calamityshock.event")) { p.SendErrorMessage("You need calamityshock.event to start events."); return; }
                    EventDefinition d = _plugin.Events.Find(prm[2]);
                    if (d == null) { p.SendErrorMessage($"No event '{prm[2]}'. /cs event list shows them."); return; }
                    bool force = prm.Count > 3 && prm[3].Equals("force", StringComparison.OrdinalIgnoreCase) && IsAdmin(p);
                    string err = _plugin.Events.TryStart(d, p, force);
                    if (err != null) p.SendErrorMessage(err);
                    break;
                }
                case "stop":
                    // same rule as start, so whoever may start an event may also stop one
                    if (!_plugin.Config.AnyoneCanStartEvents && !IsAdmin(p) && !p.HasPermission("calamityshock.event")) { p.SendErrorMessage("You need calamityshock.event to stop events."); return; }
                    if (_plugin.Events.Active == null) { p.SendInfoMessage("No event is running."); return; }
                    _plugin.Events.Stop(true, "stopped by " + p.Name);
                    break;
                case "status":
                    p.SendInfoMessage(_plugin.Events.StatusLine());
                    break;
                default:
                    p.SendErrorMessage("Usage: /cs event list | start <id> | stop | status");
                    break;
            }
        }

        // ------------------------------------------------------------------ v2: arenas and regions

        private void Arena(TSPlayer p, List<string> prm)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + " to build arenas."); return; }
            string sub = prm.Count > 1 ? prm[1].ToLowerInvariant() : "list";
            switch (sub)
            {
                case "build":
                {
                    if (prm.Count < 3) { p.SendErrorMessage("Usage: /cs arena build <name> [" + string.Join("|", ArenaBuilder.PadNames) + "|none]"); return; }
                    string biome = prm.Count > 3 ? prm[3] : "none";
                    string err = _plugin.Arenas.Build(p, prm[2], biome);
                    if (err != null) p.SendErrorMessage(err);
                    else p.SendSuccessMessage($"Arena '{prm[2].ToLowerInvariant()}' built{(biome.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : " with a " + biome.ToLowerInvariant() + " pad under the floor")}. /cs arena undo {prm[2].ToLowerInvariant()} restores the old tiles.");
                    break;
                }
                case "undo":
                {
                    if (prm.Count < 3) { p.SendErrorMessage("Usage: /cs arena undo <name>"); return; }
                    string err = _plugin.Arenas.Undo(prm[2]);
                    if (err != null) p.SendErrorMessage(err); else p.SendSuccessMessage("Arena removed and the old tiles restored.");
                    break;
                }
                case "list":
                {
                    var names = _plugin.Arenas.ListSaved();
                    p.SendInfoMessage(names.Count == 0 ? "No arenas built." : "Arenas: " + string.Join(", ", names));
                    p.SendMessage("Biome pads: " + string.Join(", ", ArenaBuilder.PadNames), Color.LightGray);
                    break;
                }
                default:
                    p.SendErrorMessage("Usage: /cs arena build <name> [biome] | undo <name> | list");
                    break;
            }
        }

        private void Regions(TSPlayer p)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            var fx = _plugin.Regions.Effects;
            if (fx.Count == 0) { p.SendInfoMessage("No region effects configured (tshock/CalamityShock/regions.json)."); return; }
            p.SendInfoMessage("Region effects (define the regions themselves with /region):");
            foreach (var kv in fx)
            {
                bool exists = TShock.Regions.GetRegionByName(kv.Key) != null;
                p.SendMessage($"{(exists ? "[defined]" : "[no such region yet]")} {kv.Key}: +{kv.Value.DifficultyBonus:0.##} difficulty, {kv.Value.Buffs.Count} buff(s), {kv.Value.SpawnReplace.Count} replacement spawn(s) at {kv.Value.SpawnReplaceChance * 100f:0}%{(kv.Value.BlockBossSummons ? ", no boss summons" : "")}", exists ? Color.White : Color.Gray);
            }
        }

        /// <summary>Admin smoke test: spawns a boss with no requirement checks, at the player or (from the console) at the world spawn, and prints its stats.</summary>
        private void Test(TSPlayer p, List<string> prm)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            if (prm.Count < 2) { p.SendErrorMessage("Usage: /cs test <bossid>"); return; }
            BossDefinition d = _plugin.Bosses.Find(prm[1]);
            if (d == null) { p.SendErrorMessage($"No boss '{prm[1]}'."); return; }
            int x, y;
            if (p.RealPlayer) { x = (int)p.X; y = (int)p.Y - 300; }
            else { x = Main.spawnTileX * 16; y = Main.spawnTileY * 16 - 300; }
            BossController c = _plugin.Bosses.SpawnAt(d, x, y);
            if (c == null) { p.SendErrorMessage("Spawn failed (no free NPC slot?)."); return; }
            NPC npc = Main.npc[c.NpcIndex];
            p.SendSuccessMessage($"Spawned {d.DisplayName} as NPC #{c.NpcIndex} ({Ids.NpcName(npc.netID)}): {Difficulty.Describe(npc)}");
        }

        /// <summary>
        /// /cs debug dummy|undummy|projectiles - only with EnableDebugCommands. "dummy" marks player slot 0 as an active
        /// player standing at the world spawn so bosses have something to target on an empty test server.
        /// Never use it while real players are online.
        /// </summary>
        private void Debug(TSPlayer p, List<string> prm)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            if (!_plugin.Config.EnableDebugCommands) { p.SendErrorMessage("Set EnableDebugCommands to true in config.json to use /cs debug."); return; }
            string what = prm.Count > 1 ? prm[1].ToLowerInvariant() : "";
            switch (what)
            {
                case "dummy":
                {
                    if (Net.CountLivingPlayers() > 0) { p.SendErrorMessage("Players are online - refusing to create a dummy."); return; }
                    Player d = Main.player[0];
                    d.name = "Dummy";
                    d.active = true;
                    d.dead = false;
                    d.statLifeMax = 400;
                    d.statLifeMax2 = 400;
                    d.statLife = 400;
                    d.position = new Vector2(Main.spawnTileX * 16f, (Main.spawnTileY - 3) * 16f);
                    // Give it a TSPlayer too, so the player/event systems treat it like a (silent) connected player.
                    if (TShock.Players[0] == null) TShock.Players[0] = new TSPlayer(0) { Group = new SuperAdminGroup() };
                    _plugin.Players.ApplyStored(TShock.Players[0]);
                    p.SendSuccessMessage($"Dummy player active at {d.Center}. Bosses will now target and attack it. /cs debug undummy removes it.");
                    break;
                }
                case "undummy":
                    Main.player[0].active = false;
                    if (TShock.Players[0] != null && !TShock.Players[0].ConnectionAlive) TShock.Players[0] = null;
                    p.SendSuccessMessage("Dummy player removed.");
                    break;
                case "hit":
                {
                    // Simulates the dummy taking damage (rage up / adrenaline reset): /cs debug hit <damage>
                    int dmgTaken = prm.Count > 2 && int.TryParse(prm[2], out int dd) ? dd : 20;
                    PlayerState st = _plugin.Players.Get(0);
                    if (st == null || TShock.Players[0] == null) { p.SendErrorMessage("No dummy."); return; }
                    var fake = new GetDataHandlers.PlayerDamageEventArgs { Player = TShock.Players[0], ID = 0, Damage = (short)dmgTaken };
                    _plugin.Players.OnPlayerHurt(fake);
                    p.SendSuccessMessage($"Dummy hurt for {dmgTaken}: rage {st.Rage:0}%, adrenaline {st.Adrenaline:0}%");
                    break;
                }
                case "strike":
                {
                    // Simulates the dummy hitting NPC #<index> for <damage> through the strike hook: /cs debug strike <npc index> <damage>
                    if (prm.Count < 4 || !int.TryParse(prm[2], out int npcIndex) || !int.TryParse(prm[3], out int dmgDealt)) { p.SendErrorMessage("Usage: /cs debug strike <npc index> <damage>"); return; }
                    TSPlayer dummy = TShock.Players[0];
                    if (dummy == null) { p.SendErrorMessage("No dummy."); return; }
                    if (npcIndex < 0 || npcIndex >= Main.maxNPCs || !Main.npc[npcIndex].active) { p.SendErrorMessage("No such NPC."); return; }
                    int before = Main.npc[npcIndex].life;
                    var fake = new GetDataHandlers.NPCStrikeEventArgs { Player = dummy, ID = (short)npcIndex, Damage = (short)dmgDealt, Direction = 1, Critical = 0, Knockback = 0f };
                    _plugin.Players.MarkRogueThrow(0);   // as if the dummy had just thrown its weapon
                    _plugin.Players.OnNpcStrike(fake);
                    int extra = before - Main.npc[npcIndex].life;
                    p.SendSuccessMessage($"Strike hook ran for a {dmgDealt} hit: bonus strike took {extra} HP ({before} -> {Main.npc[npcIndex].life}).");
                    break;
                }
                case "as":
                {
                    // Runs a /cs sub-command as the dummy player: /cs debug as arena build test mushroom
                    if (TShock.Players[0] == null) { p.SendErrorMessage("No dummy."); return; }
                    var rest = prm.Skip(2).ToList();
                    if (rest.Count == 0) { p.SendErrorMessage("Usage: /cs debug as <sub-command...>"); return; }
                    Dispatch(new CommandArgs("/cs " + string.Join(" ", rest), TShock.Players[0], rest));
                    break;
                }
                case "zone":
                {
                    // Pretend the dummy stands in a biome (zones normally come from the client): /cs debug zone beach|desert|mushroom|corrupt|none
                    Player d = Main.player[0];
                    string z = prm.Count > 2 ? prm[2].ToLowerInvariant() : "none";
                    d.ZoneBeach = z == "beach" || z == "ocean"; d.ZoneDesert = z == "desert"; d.ZoneGlowshroom = z == "mushroom";
                    d.ZoneCorrupt = z == "corrupt" || z == "corruption"; d.ZoneCrimson = z == "crimson"; d.ZoneSnow = z == "snow"; d.ZoneJungle = z == "jungle";
                    p.SendSuccessMessage($"Dummy zone set to {z}.");
                    break;
                }
                case "tiles":
                {
                    // Counts tiles of a type in the biome scan area around the dummy: /cs debug tiles MushroomGrass
                    if (prm.Count < 3 || !Ids.TryTile(prm[2], out int tileType)) { p.SendErrorMessage("Usage: /cs debug tiles <tile name>"); return; }
                    int cx = (int)(Main.player[0].Center.X / 16f), cy = (int)(Main.player[0].Center.Y / 16f);
                    int halfW = Terraria.SceneMetrics.ZoneScanSize.X / 2, halfH = Terraria.SceneMetrics.ZoneScanSize.Y / 2, count = 0;
                    for (int x = Math.Max(0, cx - halfW); x < Math.Min(Main.maxTilesX, cx + halfW); x++)
                        for (int y = Math.Max(0, cy - halfH); y < Math.Min(Main.maxTilesY, cy + halfH); y++)
                        {
                            var t = Main.tile[x, y];
                            if (t != null && t.active() && t.type == tileType) count++;
                        }
                    p.SendInfoMessage($"{count} x {prm[2]} in the scan area around the dummy.");
                    break;
                }
                case "region":
                {
                    // Creates a TShock region centred on the dummy for testing region effects: /cs debug region <name> [w] [h]
                    if (prm.Count < 3) { p.SendErrorMessage("Usage: /cs debug region <name> [width] [height]"); return; }
                    int w = prm.Count > 3 && int.TryParse(prm[3], out int ww) ? ww : 200, h = prm.Count > 4 && int.TryParse(prm[4], out int hh) ? hh : 120;
                    int cx = (int)(Main.player[0].Center.X / 16f), cy = (int)(Main.player[0].Center.Y / 16f);
                    bool ok = TShock.Regions.AddRegion(cx - w / 2, cy - h / 2, w, h, prm[2], "Dummy", Main.worldID.ToString(), 0);
                    p.SendSuccessMessage(ok ? $"Region '{prm[2]}' created around the dummy ({w}x{h} tiles)." : "Region could not be created (exists already?).");
                    break;
                }
                case "setmeter":
                {
                    // /cs debug setmeter rage|adren|stealth <value>
                    PlayerState st = _plugin.Players.Get(0);
                    if (st == null || prm.Count < 4 || !float.TryParse(prm[3], out float v)) { p.SendErrorMessage("Usage: /cs debug setmeter rage|adren|stealth <0-100>"); return; }
                    switch (prm[2].ToLowerInvariant())
                    {
                        case "rage": st.Rage = v; break;
                        case "adren": st.Adrenaline = v; break;
                        case "stealth":
                            st.IsRogue = true; st.Stealth = v;
                            Main.player[0].inventory[0].SetDefaults(Terraria.ID.ItemID.Shuriken); // hold a rogue weapon
                            Main.player[0].inventory[0].stack = 50;
                            Main.player[0].selectedItemState.Select(0);
                            break;
                    }
                    p.SendSuccessMessage($"rage {st.Rage:0}%, adrenaline {st.Adrenaline:0}%, stealth {st.Stealth:0}% (rogue {st.IsRogue})");
                    break;
                }
                case "projectiles":
                {
                    int hostile = 0, total = 0;
                    var byType = new Dictionary<int, int>();
                    for (int i = 0; i < Main.maxProjectiles; i++)
                    {
                        Projectile pr = Main.projectile[i];
                        if (pr == null || !pr.active) continue;
                        total++;
                        if (pr.hostile) { hostile++; byType[pr.type] = byType.TryGetValue(pr.type, out int c) ? c + 1 : 1; }
                    }
                    p.SendInfoMessage($"{total} projectiles active, {hostile} hostile: " + string.Join(", ", byType.Select(kv => $"#{kv.Key} x{kv.Value}")));
                    break;
                }
                case "npcs":
                {
                    var byType = new Dictionary<int, int>();
                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        NPC n = Main.npc[i];
                        if (n == null || !n.active) continue;
                        byType[n.type] = byType.TryGetValue(n.type, out int c) ? c + 1 : 1;
                    }
                    p.SendInfoMessage("Active NPCs: " + string.Join(", ", byType.Select(kv => $"{Ids.NpcName(kv.Key)} x{kv.Value}")));
                    break;
                }
                case "items":
                {
                    var byType = new Dictionary<int, int>();
                    for (int i = 0; i < Main.maxItems; i++)
                    {
                        WorldItem it = Main.item[i];
                        if (it == null || !it.active || it.stack <= 0) continue;
                        byType[it.type] = byType.TryGetValue(it.type, out int c) ? c + it.stack : it.stack;
                    }
                    p.SendInfoMessage("Items on the ground: " + string.Join(", ", byType.Select(kv => $"{Ids.ItemName(kv.Key)} x{kv.Value}")));
                    break;
                }
                case "kill":
                {
                    // Kills every part of every active boss (custom and vanilla) the proper way (through StrikeNPC) so loot and progression fire.
                    int hits = 0;
                    foreach (BossController c in _plugin.Bosses.Controllers.ToList())
                    {
                        if (c.Finished) continue;
                        for (int i = 0; i < Main.maxNPCs; i++)
                        {
                            if (!c.Owns(i) || Main.npc[i].life <= 0) continue;
                            // Credit the hit to player 0 (the dummy) so instanced vanilla loot behaves like a real kill.
                            Main.npc[i].StrikeNPC(1000000, 0f, 0, false, false, 0, Main.player[0]);
                            hits++;
                        }
                    }
                    p.SendSuccessMessage($"Struck {hits} boss NPC(s).");
                    break;
                }
                case "spawn":
                {
                    // Spawns any vanilla NPC near the dummy/player through the normal NPC.NewNPC path (exercises the vanilla scaling hooks).
                    if (prm.Count < 3 || !Ids.TryNpc(prm[2], out int type)) { p.SendErrorMessage("Usage: /cs debug spawn <npc name or id>"); return; }
                    int x = p.RealPlayer ? (int)p.X : Main.spawnTileX * 16;
                    int y = p.RealPlayer ? (int)p.Y - 200 : Main.spawnTileY * 16 - 200;
                    int idx = NPC.NewNPC(new Terraria.DataStructures.EntitySource_DebugCommand(), x, y, type);
                    if (idx < 0 || idx >= Main.maxNPCs || !Main.npc[idx].active) { p.SendErrorMessage("Spawn failed or was cancelled."); return; }
                    Net.SyncNpc(idx);
                    p.SendSuccessMessage($"Spawned {Ids.NpcName(Main.npc[idx].netID)} as #{idx}: {Difficulty.Describe(Main.npc[idx])}");
                    break;
                }
                default:
                    p.SendErrorMessage("Usage: /cs debug dummy | undummy | projectiles | npcs | items | kill | spawn <npc> | hit <dmg> | strike <npc> <dmg> | setmeter <meter> <value>");
                    break;
            }
        }

        private void Despawn(TSPlayer p)
        {
            if (!IsAdmin(p)) { p.SendErrorMessage("You need " + AdminPermission + "."); return; }
            int removed = 0;
            foreach (BossController c in _plugin.Bosses.Controllers.ToList())
            {
                if (!c.IsCustom || c.Finished) continue;
                for (int i = 0; i < Main.maxNPCs; i++)
                {
                    if (c.Owns(i)) { Net.KillNpcSilently(i); removed++; }
                }
            }
            _plugin.Bosses.RemoveCustomControllers();
            p.SendSuccessMessage($"Removed {removed} boss NPC(s). Vanilla bosses are untouched (use /butcher for those).");
        }
    }
}
