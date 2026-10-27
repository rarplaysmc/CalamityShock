using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace CalamityShock
{
    /// <summary>config.json "Players": Rage, Adrenaline, the Rogue class and the HUD.</summary>
    public class PlayerSettings
    {
        // ---- Rage: fills while a boss is near and when you take hits; when full it fires (or you /cs rage) ----
        public bool AutoActivateRage { get; set; } = true;
        /// <summary>Rage gained per second while a boss is within BossRangePixels.</summary>
        public float RageGainPerSecondNearBoss { get; set; } = 3f;
        /// <summary>Rage gained per point of damage taken.</summary>
        public float RagePerDamageTaken { get; set; } = 0.4f;
        /// <summary>Rage lost per second when no boss is near.</summary>
        public float RageDecayPerSecond { get; set; } = 10f;
        public int RageDurationSeconds { get; set; } = 9;
        public float RageDamageMultiplier { get; set; } = 1.35f;

        // ---- Adrenaline: fills while a boss is near and you are NOT getting hit; any hit resets it ----
        public bool AutoActivateAdrenaline { get; set; } = true;
        public int AdrenalineChargeSeconds { get; set; } = 45;
        public int AdrenalineDurationSeconds { get; set; } = 5;
        public float AdrenalineDamageMultiplier { get; set; } = 2f;
        /// <summary>Adrenaline lost per second when no boss is near.</summary>
        public float AdrenalineDecayPerSecond { get; set; } = 20f;

        /// <summary>A boss within this many pixels counts as "in a boss fight".</summary>
        public float BossRangePixels { get; set; } = 3200f;

        // ---- Rogue: stealth fills while you are not attacking (faster standing still); a hit at full stealth is a stealth strike ----
        public int StealthFillSecondsStill { get; set; } = 5;
        public int StealthFillSecondsMoving { get; set; } = 12;
        /// <summary>Ticks after any attack during which stealth does not regenerate.</summary>
        public int StealthPauseTicksAfterAttack { get; set; } = 45;
        /// <summary>Rogue hits deal up to this much extra damage at full stealth (0.25 = +25%).</summary>
        public float StealthDamageBonusAtFull { get; set; } = 0.25f;
        /// <summary>A stealth strike multiplies the hit by this and spends all stealth.</summary>
        public float StealthStrikeMultiplier { get; set; } = 2f;
        /// <summary>Friendly projectile burst released by a stealth strike (name or id, "" = none).</summary>
        public string StealthStrikeProjectile { get; set; } = "Shuriken";
        public int StealthStrikeShrapnelCount { get; set; } = 4;
        /// <summary>Damage of each shrapnel projectile as a fraction of the hit (0.5 = half).</summary>
        public float StealthStrikeShrapnelDamageFraction { get; set; } = 0.5f;
        /// <summary>Any thrown consumable ranged item (shuriken, knives, javelins, grenades...) counts as a Rogue weapon.</summary>
        public bool RogueIncludesThrownConsumables { get; set; } = true;
        /// <summary>A hit only gets the rogue bonus if the player launched a rogue projectile within this many ticks (so minions and other weapons do not borrow it).</summary>
        public int RogueHitWindowTicks { get; set; } = 180;
        /// <summary>Extra items that count as Rogue weapons.</summary>
        public List<string> RogueItems { get; set; } = new List<string> { "FlyingKnife", "ShadowFlameKnife", "VampireKnives", "BloodyMachete", "Flairon", "PaperAirplaneA", "PaperAirplaneB" };

        // ---- HUD (server status text, top centre of the screen) ----
        public bool HudEnabledByDefault { get; set; } = true;
        public int HudUpdateTicks { get; set; } = 15;
        /// <summary>Show the HUD even when no meter is doing anything (otherwise it is cleared when idle).</summary>
        public bool HudAlwaysVisible { get; set; } = false;
    }

    /// <summary>Runtime state of one connected player.</summary>
    public class PlayerState
    {
        public int Index;
        public string Key = "";          // persistence key (account name when logged in, else character name)
        public bool IsRogue;
        public bool HudEnabled = true;

        public float Rage;               // 0..100
        public int RageActiveTicks;
        public float Adrenaline;         // 0..100
        public int AdrenalineActiveTicks;
        public float Stealth;            // 0..100
        public long LastHitTakenTick = -100000;
        public long LastAttackTick = -100000;
        public long LastRogueThrowTick = -100000;
        public bool BossNear;

        public string LastHud = "";
        public long LastHudTick;

        public bool RageActive => RageActiveTicks > 0;
        public bool AdrenalineActive => AdrenalineActiveTicks > 0;

        public void ResetMeters()
        {
            Rage = 0; RageActiveTicks = 0;
            Adrenaline = 0; AdrenalineActiveTicks = 0;
            Stealth = 0;
        }
    }

    /// <summary>players.json</summary>
    public class PlayerStore
    {
        public class Entry
        {
            public string Class { get; set; } = "";
            public bool Hud { get; set; } = true;
        }
        public Dictionary<string, Entry> Players { get; set; } = new Dictionary<string, Entry>();
    }

    /// <summary>
    /// Rage, Adrenaline, the Rogue class and the HUD. All of it lives on the server: meters are tracked per player,
    /// bonus damage is applied as an extra server-side strike on top of the player's own hit, and the HUD is the
    /// "server status text" the client draws at the top of the screen (supports colour tags).
    /// </summary>
    public class PlayerSystem
    {
        private const string FileName = "players.json";
        private readonly CalamityShockPlugin _plugin;
        private readonly PlayerState[] _states = new PlayerState[Main.maxPlayers];
        private PlayerStore _store = new PlayerStore();
        private HashSet<int> _rogueItems = new HashSet<int>();
        private HashSet<int> _rogueProjectiles = new HashSet<int>();
        private int _shrapnelType;
        private long _tick;
        private readonly List<Vector2> _bossPositions = new List<Vector2>();

        /// <summary>Other systems can add a HUD line (the event system shows wave progress here).</summary>
        public Func<TSPlayer, string> ExtraHudLine { get; set; }

        private PlayerSettings S => _plugin.Config.Players;

        public PlayerSystem(CalamityShockPlugin plugin)
        {
            _plugin = plugin;
        }

        // ------------------------------------------------------------------ loading

        public void Load()
        {
            _store = JsonStore.LoadOrCreate(FileName, () => new PlayerStore());
            _store.Players ??= new Dictionary<string, PlayerStore.Entry>();
            _store.Players = new Dictionary<string, PlayerStore.Entry>(_store.Players, StringComparer.OrdinalIgnoreCase);

            _rogueItems = new HashSet<int>();
            PlayerSettings s = S;
            if (s.RogueIncludesThrownConsumables)
            {
                var probe = new Item();
                for (int type = 1; type < ItemID.Count; type++)
                {
                    try { probe.SetDefaults(type); } catch { continue; }
                    // thrown weapons: ranged, consumable, fire a projectile, use no ammo - and are not ammo themselves
                    if (probe.ranged && probe.consumable && probe.shoot > 0 && probe.useAmmo == 0 && probe.ammo == 0 && probe.damage > 0)
                        _rogueItems.Add(type);
                }
            }
            foreach (string name in s.RogueItems ?? new List<string>())
            {
                if (Ids.TryItem(name, out int id)) _rogueItems.Add(id);
                else Log.Warn($"config Players.RogueItems: unknown item '{name}'");
            }
            // The projectiles those items fire: a hit only counts as a rogue hit shortly after one of these was launched.
            _rogueProjectiles = new HashSet<int>();
            {
                var probe = new Item();
                foreach (int type in _rogueItems)
                {
                    try { probe.SetDefaults(type); } catch { continue; }
                    if (probe.shoot > 0) _rogueProjectiles.Add(probe.shoot);
                }
            }
            _shrapnelType = 0;
            if (!string.IsNullOrWhiteSpace(s.StealthStrikeProjectile) && !Ids.TryProjectile(s.StealthStrikeProjectile, out _shrapnelType))
                Log.Warn($"config Players.StealthStrikeProjectile: unknown projectile '{s.StealthStrikeProjectile}'");

            // Re-apply persisted settings to players already online (reload).
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p != null && p.Active) ApplyStored(p);
            }
            Log.Info($"Rogue weapon set: {_rogueItems.Count} items, {_rogueProjectiles.Count} projectile types.");
        }

        public void Save() => JsonStore.Save(FileName, _store);

        public bool IsRogueItem(int itemType) => _rogueItems.Contains(itemType);

        public PlayerState Get(int index)
        {
            if (index < 0 || index >= Main.maxPlayers) return null;
            return _states[index] ??= new PlayerState { Index = index, HudEnabled = S.HudEnabledByDefault };
        }

        public PlayerState Get(TSPlayer p) => p == null || !p.RealPlayer ? null : Get(p.Index);

        private static string KeyFor(TSPlayer p) => p.IsLoggedIn && p.Account != null ? "acct:" + p.Account.Name : "name:" + p.Name;

        /// <summary>Loads class/HUD choices for a player who just joined or logged in.</summary>
        public void ApplyStored(TSPlayer p)
        {
            PlayerState st = Get(p);
            if (st == null) return;
            st.Key = KeyFor(p);
            if (_store.Players.TryGetValue(st.Key, out var e) && e != null)
            {
                st.IsRogue = string.Equals(e.Class, "rogue", StringComparison.OrdinalIgnoreCase);
                st.HudEnabled = e.Hud;
            }
            else
            {
                st.IsRogue = false;
                st.HudEnabled = S.HudEnabledByDefault;
            }
        }

        private void Persist(PlayerState st)
        {
            if (string.IsNullOrEmpty(st.Key)) return;
            _store.Players[st.Key] = new PlayerStore.Entry { Class = st.IsRogue ? "rogue" : "", Hud = st.HudEnabled };
            Save();
        }

        public void SetClass(TSPlayer p, bool rogue)
        {
            PlayerState st = Get(p);
            if (st == null) return;
            if (string.IsNullOrEmpty(st.Key)) st.Key = KeyFor(p);
            st.IsRogue = rogue;
            st.Stealth = 0;
            Persist(st);
        }

        public void SetHud(TSPlayer p, bool enabled)
        {
            PlayerState st = Get(p);
            if (st == null) return;
            if (string.IsNullOrEmpty(st.Key)) st.Key = KeyFor(p);
            st.HudEnabled = enabled;
            if (!enabled) SendStatus(p, "");
            st.LastHud = "";
            Persist(st);
        }

        public void OnLeave(int index)
        {
            if (index >= 0 && index < Main.maxPlayers) _states[index] = null;
        }

        public void OnRespawn(int index)
        {
            PlayerState st = Get(index);
            st?.ResetMeters();
        }

        public bool MetersEnabled => _plugin.Config.CurrentMode.MetersEnabled;

        // ------------------------------------------------------------------ activation

        public string TryActivateRage(TSPlayer p)
        {
            PlayerState st = Get(p);
            if (st == null) return "Not a player.";
            if (!MetersEnabled) return "Rage is only available in Revengeance mode or above.";
            if (st.RageActive) return "Rage is already active.";
            if (st.Rage < 100f) return $"Rage is at {st.Rage:0}% - it must be full.";
            st.RageActiveTicks = Math.Max(1, S.RageDurationSeconds) * 60;
            st.Rage = 100f;
            Net.CombatText(p.TPlayer.Top, "RAGE!", new Color(255, 70, 70));
            return null;
        }

        public string TryActivateAdrenaline(TSPlayer p)
        {
            PlayerState st = Get(p);
            if (st == null) return "Not a player.";
            if (!MetersEnabled) return "Adrenaline is only available in Revengeance mode or above.";
            if (st.AdrenalineActive) return "Adrenaline is already active.";
            if (st.Adrenaline < 100f) return $"Adrenaline is at {st.Adrenaline:0}% - it must be full.";
            st.AdrenalineActiveTicks = Math.Max(1, S.AdrenalineDurationSeconds) * 60;
            st.Adrenaline = 100f;
            Net.CombatText(p.TPlayer.Top, "ADRENALINE!", new Color(255, 215, 0));
            return null;
        }

        // ------------------------------------------------------------------ per tick

        public void Tick()
        {
            _tick++;
            PlayerSettings s = S;
            bool meters = MetersEnabled;

            _bossPositions.Clear();
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC n = Main.npc[i];
                if (n.active && n.life > 0 && BossManager.IsBossLike(n)) _bossPositions.Add(n.Center);
            }

            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p == null || !p.Active || p.TPlayer == null) continue;
                PlayerState st = Get(i);
                Player tp = p.TPlayer;
                bool dead = tp.dead;
                float rangeSq = s.BossRangePixels * s.BossRangePixels;
                st.BossNear = false;
                foreach (Vector2 b in _bossPositions)
                {
                    if (Vector2.DistanceSquared(b, tp.Center) <= rangeSq) { st.BossNear = true; break; }
                }

                if (meters && !dead)
                {
                    // Rage
                    if (st.RageActiveTicks > 0)
                    {
                        st.RageActiveTicks--;
                        if (st.RageActiveTicks == 0) st.Rage = 0f;
                    }
                    else if (st.BossNear) st.Rage = Math.Min(100f, st.Rage + s.RageGainPerSecondNearBoss / 60f);
                    else st.Rage = Math.Max(0f, st.Rage - s.RageDecayPerSecond / 60f);
                    if (!st.RageActive && st.Rage >= 100f && s.AutoActivateRage) TryActivateRage(p);

                    // Adrenaline
                    if (st.AdrenalineActiveTicks > 0)
                    {
                        st.AdrenalineActiveTicks--;
                        if (st.AdrenalineActiveTicks == 0) st.Adrenaline = 0f;
                    }
                    else if (st.BossNear) st.Adrenaline = Math.Min(100f, st.Adrenaline + 100f / (Math.Max(1, s.AdrenalineChargeSeconds) * 60f));
                    else st.Adrenaline = Math.Max(0f, st.Adrenaline - s.AdrenalineDecayPerSecond / 60f);
                    if (!st.AdrenalineActive && st.Adrenaline >= 100f && s.AutoActivateAdrenaline) TryActivateAdrenaline(p);
                }
                else if (!meters)
                {
                    st.Rage = 0; st.RageActiveTicks = 0; st.Adrenaline = 0; st.AdrenalineActiveTicks = 0;
                }

                // Stealth (rogue only)
                if (st.IsRogue && !dead)
                {
                    bool attackedRecently = _tick - st.LastAttackTick < Math.Max(0, s.StealthPauseTicksAfterAttack);
                    if (!attackedRecently && st.Stealth < 100f)
                    {
                        bool moving = tp.velocity.LengthSquared() > 0.25f;
                        int seconds = moving ? Math.Max(1, s.StealthFillSecondsMoving) : Math.Max(1, s.StealthFillSecondsStill);
                        st.Stealth = Math.Min(100f, st.Stealth + 100f / (seconds * 60f));
                    }
                }

                if (st.HudEnabled && _tick - st.LastHudTick >= Math.Max(5, s.HudUpdateTicks))
                {
                    st.LastHudTick = _tick;
                    string hud = BuildHud(p, st, meters);
                    if (hud != st.LastHud)
                    {
                        st.LastHud = hud;
                        SendStatus(p, hud);
                    }
                }
            }
        }

        private string BuildHud(TSPlayer p, PlayerState st, bool meters)
        {
            var sb = new StringBuilder();
            bool anything = false;
            if (meters)
            {
                if (st.RageActive) sb.Append($"[c/FF4646:RAGE {Math.Ceiling(st.RageActiveTicks / 60f):0}s]");
                else sb.Append($"[c/FF4646:Rage] {Bar(st.Rage)} {st.Rage:0}%");
                sb.Append("   ");
                if (st.AdrenalineActive) sb.Append($"[c/FFD700:ADRENALINE {Math.Ceiling(st.AdrenalineActiveTicks / 60f):0}s]");
                else sb.Append($"[c/FFD700:Adrenaline] {Bar(st.Adrenaline)} {st.Adrenaline:0}%");
                anything = st.Rage > 0 || st.Adrenaline > 0 || st.RageActive || st.AdrenalineActive || st.BossNear;
            }
            if (st.IsRogue)
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"[c/9ACD32:Stealth] {Bar(st.Stealth)} {st.Stealth:0}%{(st.Stealth >= 100f ? " [c/9ACD32:READY]" : "")}");
                anything = true;
            }
            string extra = null;
            try { extra = ExtraHudLine?.Invoke(p); } catch { }
            if (!string.IsNullOrEmpty(extra))
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(extra);
                anything = true;
            }
            if (!anything && !S.HudAlwaysVisible) return "";
            return sb.ToString();
        }

        private static string Bar(float value)
        {
            int filled = (int)Math.Round(Math.Clamp(value, 0f, 100f) / 10f);
            return "[" + new string('|', filled) + new string('.', 10 - filled) + "]";
        }

        /// <summary>Packet 9: StatusMax delta 0, flags 1 = hide the percent suffix, 2 = draw with shadows.</summary>
        private static void SendStatus(TSPlayer p, string text)
        {
            try { p.SendData(PacketTypes.Status, text ?? "", 0, 3); } catch { }
        }

        // ------------------------------------------------------------------ combat hooks

        /// <summary>GetDataHandlers.PlayerDamage (the hurt packet): rage up, adrenaline reset.</summary>
        public void OnPlayerHurt(GetDataHandlers.PlayerDamageEventArgs args)
        {
            if (args.Handled || args.Player == null) return;
            int victim = args.ID;
            if (victim < 0 || victim >= Main.maxPlayers) return;
            // The packet names its victim; only trust it from the victim itself or from a PvP opponent (vanilla drops anything else).
            if (victim != args.Player.Index)
            {
                Player attacker = args.Player.TPlayer;
                Player target = Main.player[victim];
                if (attacker == null || target == null || !attacker.hostile || !target.hostile || !target.active) return;
            }
            PlayerState st = Get(victim);
            if (st == null) return;
            st.LastHitTakenTick = _tick;
            if (!MetersEnabled) return;
            if (!st.RageActive) st.Rage = Math.Min(100f, st.Rage + Math.Max(0, (int)args.Damage) * S.RagePerDamageTaken);
            if (!st.AdrenalineActive) st.Adrenaline = 0f;
        }

        /// <summary>GetDataHandlers.NewProjectile: a projectile fired by the player counts as an attack (pauses stealth).</summary>
        public void OnNewProjectile(GetDataHandlers.NewProjectileEventArgs args)
        {
            if (args.Handled || args.Player == null || !args.Player.RealPlayer) return;
            if (args.Owner != args.Player.Index) return;
            PlayerState st = Get(args.Player);
            if (st == null || !st.IsRogue) return;
            // Packet 27 is also re-sent for projectiles that already exist (minions, yoyos...); only a new launch is an attack.
            bool isNew = args.Index < 0 || args.Index >= Main.maxProjectiles || !Main.projectile[args.Index].active;
            if (!isNew) return;
            // Hooks, pets and the like carry no damage; minions and sentries are not the player's own attack.
            if (args.Damage <= 0 || IsMinionOrSentry(args.Type)) return;
            st.LastAttackTick = _tick;
            if (_rogueProjectiles.Contains(args.Type)) st.LastRogueThrowTick = _tick;
        }

        /// <summary>Lets the debug strike command pretend a rogue weapon was just thrown.</summary>
        public void MarkRogueThrow(int index)
        {
            PlayerState st = Get(index);
            if (st != null) st.LastRogueThrowTick = _tick;
        }

        private readonly Dictionary<int, bool> _minionCache = new Dictionary<int, bool>();

        private bool IsMinionOrSentry(int type)
        {
            if (_minionCache.TryGetValue(type, out bool v)) return v;
            bool minion = false;
            try
            {
                var probe = new Projectile();
                probe.SetDefaults(type);
                minion = probe.minion || probe.sentry;
            }
            catch { }
            _minionCache[type] = minion;
            return minion;
        }

        /// <summary>
        /// GetDataHandlers.NPCStrike: the player's own hit goes through vanilla untouched (TShock's anti-cheat checks
        /// stay in place). If Rage / Adrenaline / stealth multiply the damage, the extra part is applied as a second,
        /// server-side strike that every client also sees (its own damage number).
        /// </summary>
        public void OnNpcStrike(GetDataHandlers.NPCStrikeEventArgs args)
        {
            if (args.Handled) return;
            TSPlayer p = args.Player;
            if (p == null || !p.RealPlayer || p.TPlayer == null) return;
            int id = args.ID;
            if (id < 0 || id >= Main.maxNPCs) return;
            NPC npc = Main.npc[id];
            if (!npc.active || npc.friendly || npc.townNPC || npc.life <= 0 || npc.dontTakeDamage) return;
            // Hitting these is what summons the Empress / starts the Cultist ritual, and TShock checks the summonboss
            // permission for them only after this handler ran - so never add a server-side hit to them.
            if (npc.type == NPCID.EmpressButterfly || npc.type == NPCID.CultistDevote || npc.type == NPCID.CultistArcherBlue || npc.type == NPCID.CultistTablet) return;
            int dmg = args.Damage;
            if (dmg <= 0) return;
            // Mirror TShock's own rejections so a packet it is about to drop does not get amplified.
            if (dmg > TShock.Config.Settings.MaxDamage && !p.HasPermission(Permissions.ignoredamagecap)) return;
            if (p.IsBeingDisabled()) return;

            PlayerState st = Get(p);
            if (st == null) return;
            // A hit while the use button is down is the player attacking (melee); minion hits land without it and do not pause stealth.
            if (p.TPlayer.controlUseItem) st.LastAttackTick = _tick;

            PlayerSettings s = S;
            float mult = 1f;
            bool stealthStrike = false;
            bool meters = MetersEnabled;
            if (meters && st.RageActive) mult *= Math.Max(1f, s.RageDamageMultiplier);
            if (meters && st.AdrenalineActive) mult *= Math.Max(1f, s.AdrenalineDamageMultiplier);

            Player tp = p.TPlayer;
            Item held = tp.selectedItem >= 0 && tp.selectedItem < tp.inventory.Length ? tp.inventory[tp.selectedItem] : null;
            // A rogue hit = a rogue weapon in hand and a rogue projectile launched a moment ago (the hit packet does not
            // say what dealt the damage, so this keeps minions and other weapons from borrowing the bonus).
            bool rogueHit = st.IsRogue && held != null && IsRogueItem(held.type) && _tick - st.LastRogueThrowTick <= Math.Max(0, s.RogueHitWindowTicks);
            if (rogueHit)
            {
                mult *= 1f + Math.Max(0f, s.StealthDamageBonusAtFull) * (Math.Clamp(st.Stealth, 0f, 100f) / 100f);
                if (st.Stealth >= 100f && s.StealthStrikeMultiplier > 1f)
                {
                    mult *= s.StealthStrikeMultiplier;
                    stealthStrike = true;
                }
            }
            if (mult <= 1.001f) return;

            bool crit = args.Critical != 0;
            int hitDirection = (sbyte)args.Direction;
            double afterDefense = Main.CalculateDamageNPCsTake(dmg, npc.defense);
            double wantedExtra = afterDefense * (mult - 1f);
            // Packet 28 carries a 16-bit damage value, so the extra hit must fit in one (a bigger value wraps negative on clients).
            int extraPreDefense = (int)Math.Min(short.MaxValue, Math.Ceiling(wantedExtra + npc.defense * 0.5));
            if (extraPreDefense < 1) return;
            if (stealthStrike) st.Stealth = 0f;

            try
            {
                npc.PlayerInteraction(p.Index);
                // fromNet: false makes StrikeNPC broadcast packet 28 itself, so every client applies and shows the extra hit.
                npc.StrikeNPC(extraPreDefense, 0f, hitDirection, crit, false, p.Index, tp);
                if (npc.life <= 0) Net.SyncNpc(id);
            }
            catch (Exception ex)
            {
                Log.Error("Bonus strike failed: " + ex.Message);
                return;
            }

            if (stealthStrike)
            {
                Net.CombatText(npc.Top, "Stealth strike!", new Color(154, 205, 50));
                if (_shrapnelType > 0 && s.StealthStrikeShrapnelCount > 0)
                {
                    int shrapnelDamage = Math.Max(1, (int)(dmg * Math.Max(0f, s.StealthStrikeShrapnelDamageFraction)));
                    var source = new EntitySource_DebugCommand();
                    float radius = Math.Max(npc.width, npc.height) / 2f + 20f;
                    for (int i = 0; i < s.StealthStrikeShrapnelCount; i++)
                    {
                        // Start just outside the target's hitbox and fly outward, so the burst hits what is around the target.
                        Vector2 dir = Net.RandomUnit();
                        int idx = Projectile.NewProjectile(source, npc.Center + dir * radius, dir * 9f, _shrapnelType, shrapnelDamage, 3f, Main.myPlayer);
                        if (idx >= 0 && idx < Main.maxProjectiles)
                        {
                            Projectile pr = Main.projectile[idx];
                            pr.friendly = true;
                            pr.hostile = false;
                            pr.noDropItem = true;
                        }
                    }
                }
            }
        }

        public List<string> StatusLines()
        {
            var lines = new List<string>();
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer p = TShock.Players[i];
                if (p == null || !p.Active) continue;
                PlayerState st = Get(i);
                lines.Add($"{p.Name}: {(st.IsRogue ? "rogue, " : "")}rage {st.Rage:0}%{(st.RageActive ? " ACTIVE" : "")}, adrenaline {st.Adrenaline:0}%{(st.AdrenalineActive ? " ACTIVE" : "")}{(st.IsRogue ? $", stealth {st.Stealth:0}%" : "")}, boss near: {st.BossNear}");
            }
            return lines;
        }
    }
}
