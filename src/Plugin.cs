using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace CalamityShock
{
    /// <summary>
    /// CalamityShock - Calamity-flavoured difficulty modes, re-tuned bosses, progression gating, loot and
    /// server-side crafting for TShock 6.x (Terraria 1.4.5). Everything runs on the server; players use the
    /// unmodified Terraria client.
    /// </summary>
    [ApiVersion(2, 1)]
    public class CalamityShockPlugin : TerrariaPlugin
    {
        private const string ConfigFile = "config.json";

        public override string Name => "CalamityShock";
        public override string Author => "you";
        public override string Description => "Calamity-style difficulty modes, bosses, progression, loot and crafting";
        public override Version Version => new Version(0, 2, 0);

        public CalamityConfig Config { get; private set; } = CalamityConfig.CreateDefault();
        public BossManager Bosses { get; }
        public ProgressionManager Progression { get; } = new ProgressionManager();
        public Crafting Crafting { get; }
        public PlayerSystem Players { get; }
        public EventManager Events { get; }
        public ArenaBuilder Arenas { get; }
        public RegionEffects Regions { get; }
        private readonly PluginCommands _commands;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _gameThreadQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        private bool _loaded;

        /// <summary>
        /// Runs an action on the game thread at the start of the next update. Console commands run on their own
        /// thread, and spawning or striking NPCs from there races with the world update.
        /// </summary>
        public void RunOnGameThread(Action action)
        {
            if (action != null) _gameThreadQueue.Enqueue(action);
        }

        /// <summary>
        /// Runs an action on the game thread if the world is ticking (players online or -forceupdate), otherwise
        /// right away - on an idle server nothing races with us and the queue would not be drained.
        /// In-game commands already arrive on the game thread, so they always run immediately.
        /// </summary>
        public void RunSafely(Action action, bool alreadyOnGameThread)
        {
            if (action == null) return;
            if (alreadyOnGameThread || !WorldIsTicking())
            {
                try { action(); }
                catch (Exception ex) { Log.Error("Action failed: " + ex); }
                return;
            }
            RunOnGameThread(action);
        }

        public static bool WorldIsTicking()
        {
            if (ServerApi.ForceUpdate) return true;
            for (int i = 0; i < Main.maxPlayers; i++)
                if (Main.player[i] != null && Main.player[i].active) return true;
            return false;
        }

        public CalamityShockPlugin(Main game) : base(game)
        {
            Order = 10; // after TShock itself
            Bosses = new BossManager(this);
            Crafting = new Crafting(this);
            Players = new PlayerSystem(this);
            Events = new EventManager(this);
            Arenas = new ArenaBuilder(this);
            Regions = new RegionEffects(this);
            Players.ExtraHudLine = p => Events.HudLine(p);
            _commands = new PluginCommands(this);
        }

        public override void Initialize()
        {
            ServerApi.Hooks.GamePostInitialize.Register(this, OnGamePostInitialize);
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            ServerApi.Hooks.GamePostUpdate.Register(this, OnGamePostUpdate);
            ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
            ServerApi.Hooks.NpcAIUpdate.Register(this, OnNpcAiUpdate);
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
            ServerApi.Hooks.NpcLootDrop.Register(this, OnNpcLootDrop);
            ServerApi.Hooks.DropBossBag.Register(this, OnDropBossBag);
            ServerApi.Hooks.ServerBroadcast.Register(this, OnServerBroadcast);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
            GeneralHooks.ReloadEvent += OnReload;
            PlayerHooks.PlayerPostLogin += OnPostLogin;
            GetDataHandlers.NPCStrike.Register(OnNpcStrikePacket);
            GetDataHandlers.PlayerDamage.Register(OnPlayerDamagePacket);
            GetDataHandlers.NewProjectile.Register(OnNewProjectilePacket);
            GetDataHandlers.PlayerSpawn.Register(OnPlayerSpawnPacket);

            // OTAPI-level hooks TSAPI does not expose with enough detail: keep our difficulty through NPC.Transform.
            HookEvents.Terraria.NPC.Transform += OnNpcTransform;
            HookEvents.Terraria.NPC.SetDefaults += OnNpcSetDefaults;
            OTAPI.Hooks.NPC.Create += OnNpcCreate;

            _commands.Register();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnGamePostInitialize);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.GamePostUpdate.Deregister(this, OnGamePostUpdate);
                ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
                ServerApi.Hooks.NpcAIUpdate.Deregister(this, OnNpcAiUpdate);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
                ServerApi.Hooks.NpcLootDrop.Deregister(this, OnNpcLootDrop);
                ServerApi.Hooks.DropBossBag.Deregister(this, OnDropBossBag);
                ServerApi.Hooks.ServerBroadcast.Deregister(this, OnServerBroadcast);
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                ServerApi.Hooks.WorldSave.Deregister(this, OnWorldSave);
                GeneralHooks.ReloadEvent -= OnReload;
                PlayerHooks.PlayerPostLogin -= OnPostLogin;
                GetDataHandlers.NPCStrike.UnRegister(OnNpcStrikePacket);
                GetDataHandlers.PlayerDamage.UnRegister(OnPlayerDamagePacket);
                GetDataHandlers.NewProjectile.UnRegister(OnNewProjectilePacket);
                GetDataHandlers.PlayerSpawn.UnRegister(OnPlayerSpawnPacket);
                HookEvents.Terraria.NPC.Transform -= OnNpcTransform;
                HookEvents.Terraria.NPC.SetDefaults -= OnNpcSetDefaults;
                OTAPI.Hooks.NPC.Create -= OnNpcCreate;
                _commands.Unregister();
                if (_loaded) Progression.Save();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ loading

        public void ReloadAll()
        {
            Config = JsonStore.LoadOrCreate(ConfigFile, CalamityConfig.CreateDefault, out bool fromFile);
            Config.AfterLoad();
            // Re-save a config that loaded fine so keys added by newer plugin versions show up with their defaults.
            if (fromFile) SaveConfig();
            Progression.Load();
            Bosses.Load();
            Crafting.Load();
            Players.Load();
            Events.Load();
            Regions.Load();
            _loaded = true;
            Log.Info($"Active mode: {Config.CurrentMode.DisplayName}. World difficulty {Difficulty.World:0.##}. SSC {(Main.ServerSideCharacter ? "on" : "off")}.");
        }

        public void SaveConfig() => JsonStore.Save(ConfigFile, Config);

        private void OnGamePostInitialize(EventArgs args)
        {
            try { ReloadAll(); }
            catch (Exception ex) { Log.Error("Failed to load: " + ex); }
        }

        private void OnReload(ReloadEventArgs e)
        {
            bool onGameThread = e?.Player != null && e.Player.RealPlayer;
            RunSafely(() =>
            {
                try
                {
                    ReloadAll();
                    e?.Player?.SendSuccessMessage("CalamityShock reloaded.");
                }
                catch (Exception ex)
                {
                    Log.Error("Reload failed: " + ex);
                    e?.Player?.SendErrorMessage("CalamityShock reload failed: " + ex.Message);
                }
            }, onGameThread);
        }

        // ------------------------------------------------------------------ hook forwarding

        private void OnGameUpdate(EventArgs args)
        {
            while (_gameThreadQueue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception ex) { Log.Error("Queued action failed: " + ex); }
            }
            if (!_loaded) return;
            Bosses.OnGameUpdate();
            try { Players.Tick(); } catch (Exception ex) { Log.Error("Player tick failed: " + ex.Message); }
            try { Events.Tick(); } catch (Exception ex) { Log.Error("Event tick failed: " + ex.Message); }
            try { Regions.Tick(); } catch (Exception ex) { Log.Error("Region tick failed: " + ex.Message); }
        }

        private void OnGamePostUpdate(EventArgs args)
        {
            if (!_loaded) return;
            Bosses.OnGamePostUpdate();
        }

        private void OnNpcSpawn(NpcSpawnEventArgs args)
        {
            if (!_loaded) return;
            try { Bosses.OnNpcSpawn(args); }
            catch (Exception ex) { Log.Error("NpcSpawn handler failed: " + ex); }
        }

        private void OnNpcAiUpdate(NpcAiUpdateEventArgs args)
        {
            if (!_loaded) return;
            Bosses.OnNpcAiUpdate(args);
        }

        private void OnNpcKilled(NpcKilledEventArgs args)
        {
            if (!_loaded) return;
            try { Bosses.OnNpcKilled(args); }
            catch (Exception ex) { Log.Error("NpcKilled handler failed: " + ex); }
            try { Events.OnNpcKilled(args.npc); }
            catch (Exception ex) { Log.Error("Event kill handler failed: " + ex.Message); }
        }

        private void OnNpcLootDrop(NpcLootDropEventArgs args)
        {
            if (!_loaded) return;
            Bosses.OnLootDrop(args);
        }

        private void OnDropBossBag(DropBossBagEventArgs args)
        {
            if (!_loaded) return;
            Bosses.OnBossBag(args);
        }

        private void OnServerBroadcast(ServerBroadcastEventArgs args)
        {
            if (!_loaded) return;
            try { Bosses.OnBroadcast(args); }
            catch (Exception ex) { Log.Error("Broadcast handler failed: " + ex.Message); }
        }

        private void OnNpcCreate(object sender, OTAPI.Hooks.NPC.CreateEventArgs e)
        {
            if (!_loaded) return;
            Bosses.OnNpcCreate(sender, e);
        }

        private void OnNpcTransform(NPC npc, HookEvents.Terraria.NPC.TransformEventArgs args)
        {
            if (!_loaded) return;
            Bosses.OnTransform(npc, args);
        }

        private void OnNpcSetDefaults(NPC npc, HookEvents.Terraria.NPC.SetDefaultsEventArgs args)
        {
            if (!_loaded) return;
            Bosses.OnSetDefaults(npc, args);
        }

        private void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            if (!_loaded) return;
            if (args.Who < 0 || args.Who >= TShock.Players.Length) return;
            TSPlayer p = TShock.Players[args.Who];
            if (p == null) return;
            Players.ApplyStored(p);
            if (Config.AnnounceModeOnJoin)
                p.SendMessage($"CalamityShock: {Config.CurrentMode.DisplayName} mode is active. Type /cs help for bosses, recipes and progression.", Net.EventColor);
        }

        private void OnLeave(LeaveEventArgs args)
        {
            if (!_loaded) return;
            Players.OnLeave(args.Who);
        }

        private void OnPostLogin(PlayerPostLoginEventArgs e)
        {
            if (!_loaded || e?.Player == null) return;
            Players.ApplyStored(e.Player);
        }

        private void OnNpcStrikePacket(object sender, GetDataHandlers.NPCStrikeEventArgs args)
        {
            if (!_loaded) return;
            try { Players.OnNpcStrike(args); }
            catch (Exception ex) { Log.Error("NPCStrike handler failed: " + ex.Message); }
        }

        private void OnPlayerDamagePacket(object sender, GetDataHandlers.PlayerDamageEventArgs args)
        {
            if (!_loaded) return;
            try { Players.OnPlayerHurt(args); }
            catch (Exception ex) { Log.Error("PlayerDamage handler failed: " + ex.Message); }
        }

        private void OnNewProjectilePacket(object sender, GetDataHandlers.NewProjectileEventArgs args)
        {
            if (!_loaded) return;
            try { Players.OnNewProjectile(args); }
            catch (Exception ex) { Log.Error("NewProjectile handler failed: " + ex.Message); }
        }

        private void OnPlayerSpawnPacket(object sender, GetDataHandlers.SpawnEventArgs args)
        {
            if (!_loaded || args.Handled || args.Player == null) return;
            Players.OnRespawn(args.Player.Index);   // the packet's own player byte is client-controlled
        }

        private void OnWorldSave(WorldSaveEventArgs args)
        {
            if (!_loaded) return;
            // World saves run on TShock's save thread; progress.json is already written on every change, so this is
            // only a safety net - run it on the game thread so it cannot race a kill being recorded.
            RunSafely(() => Progression.Save(), false);
        }
    }
}
