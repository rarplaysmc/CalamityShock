using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace CalamityShock
{
    /// <summary>
    /// Runtime state of one active boss (custom or vanilla). Created when the boss spawns, ticked every game
    /// update, and removed when the boss dies or despawns. The vanilla AI keeps moving the boss; this class layers
    /// phases, projectile volleys, minion waves, enrage and the movement/AI-speed tweaks on top.
    /// </summary>
    public class BossController
    {
        private class AttackState
        {
            public AttackPattern Pattern;
            public long NextTick;
            public int BurstsLeft;
            public long BurstTick;
        }

        private class MinionState
        {
            public MinionSpawn Spawn;
            public long NextTick;
            public bool DoneOnce;
        }

        public BossDefinition Def { get; }
        public ModeSettings Mode { get; }
        public int NpcIndex { get; private set; }
        public byte Generation { get; private set; }
        public int BaseType => Def.BaseNpcId;
        public float AppliedDifficulty { get; }
        public int AppliedPlayerCount { get; }
        public float AppliedDefenseMultiplier { get; }
        public int SummonerIndex { get; set; } = -1;
        public bool IsCustom => !Def.IsVanillaAugment;

        public long TicksAlive { get; private set; }
        public int PhaseIndex { get; private set; } = -1;
        public bool Enraged { get; private set; }
        public bool Finished { get; private set; }
        public bool Killed { get; set; }

        /// <summary>Vanilla "downed" flags as they were when this (custom) boss spawned; null for vanilla bosses.</summary>
        public ProgressionManager.VanillaFlagSnapshot FlagSnapshot { get; set; }

        /// <summary>AI patches added for this fight only (boss rush keeps bosses from fleeing daylight / the wrong biome).</summary>
        public List<string> ExtraAiPatches { get; set; }

        /// <summary>Def.AiPatches plus ExtraAiPatches, or null when there are none.</summary>
        public List<string> EffectiveAiPatches
        {
            get
            {
                bool a = Def.AiPatches != null && Def.AiPatches.Count > 0, b = ExtraAiPatches != null && ExtraAiPatches.Count > 0;
                if (!a && !b) return null;
                if (a && !b) return Def.AiPatches;
                if (!a) return ExtraAiPatches;
                var all = new List<string>(Def.AiPatches);
                all.AddRange(ExtraAiPatches);
                return all;
            }
        }

        private readonly List<AttackState> _attacks = new List<AttackState>();
        private readonly List<MinionState> _minions = new List<MinionState>();
        private float _aiAccumulator;
        private float _phaseMoveMult;
        private float _phaseAiSpeed;
        private long _peakLifeMax;

        /// <summary>
        /// The NPC instances that make up this boss, as (slot, generation) pairs. Ownership is by instance, not by
        /// type, so a vanilla King Slime that spawns while Crabulon is alive is a different boss.
        /// </summary>
        private readonly Dictionary<int, byte> _owned = new Dictionary<int, byte>();

        public BossController(BossDefinition def, ModeSettings mode, int npcIndex, float difficulty, int playerCount, float defenseMultiplier)
        {
            Def = def;
            Mode = mode;
            NpcIndex = npcIndex;
            Generation = Main.npc[npcIndex].generation;
            AppliedDifficulty = difficulty;
            AppliedPlayerCount = playerCount;
            AppliedDefenseMultiplier = defenseMultiplier;
            Adopt(npcIndex);
        }

        /// <summary>Registers an NPC slot as a part of this boss.</summary>
        public void Adopt(int npcIndex)
        {
            if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return;
            _owned[npcIndex] = Main.npc[npcIndex].generation;
        }

        /// <summary>
        /// True when a new NPC of a part type spawned close enough to this boss to belong to it. The base type itself is
        /// never adopted: the vanilla AI spawns segments, creepers and limbs, not a second head, so a nearby NPC of
        /// the base type is a separate (vanilla) boss.
        /// </summary>
        public bool ShouldAdopt(NPC spawned, float maxDistance = 1600f)
        {
            if (spawned == null || spawned.type == BaseType || !Def.SegmentIds.Contains(spawned.type)) return false;
            foreach (var kv in _owned)
            {
                NPC n = Main.npc[kv.Key];
                if (!n.active || n.generation != kv.Value) continue;
                if (Vector2.Distance(n.Center, spawned.Center) <= maxDistance) return true;
            }
            return false;
        }

        /// <summary>Enumerates the slots of parts that are still active (including ones at 0 life awaiting checkDead).</summary>
        public IEnumerable<int> OwnedActive()
        {
            foreach (var kv in _owned)
            {
                NPC n = Main.npc[kv.Key];
                if (n.active && n.generation == kv.Value && Def.SegmentIds.Contains(n.type)) yield return kv.Key;
            }
        }

        public NPC Anchor => NpcIndex >= 0 && NpcIndex < Main.maxNPCs ? Main.npc[NpcIndex] : null;

        /// <summary>Seconds until enrage, resolved from the definition and the mode. 0 = never.</summary>
        public int EnrageSeconds
        {
            get
            {
                if (Def.EnrageSeconds < 0) return 0;
                if (Def.EnrageSeconds > 0) return Def.EnrageSeconds;
                return Math.Max(0, Mode?.BossEnrageSeconds ?? 0);
            }
        }

        public float MovementMultiplier
        {
            get
            {
                float m = _phaseMoveMult > 0 ? _phaseMoveMult : (Def.MovementSpeedMultiplier > 0 ? Def.MovementSpeedMultiplier : (Mode?.BossMovementSpeedMultiplier ?? 1f));
                if (Enraged) m *= Mode?.EnrageMovementMultiplier ?? 1f;
                return Math.Max(0.1f, m);
            }
        }

        public float AiSpeed
        {
            get
            {
                float s = _phaseAiSpeed > 0 ? _phaseAiSpeed : (Def.AiSpeed > 0 ? Def.AiSpeed : (Mode?.BossAiSpeed ?? 1f));
                return Math.Clamp(s, 1f, 3f);
            }
        }

        /// <summary>True when the NPC at index is one of this boss's parts (segments, creepers, ...).</summary>
        public bool Owns(int npcIndex)
        {
            if (npcIndex < 0 || npcIndex >= Main.maxNPCs) return false;
            if (!_owned.TryGetValue(npcIndex, out byte gen)) return false;
            NPC n = Main.npc[npcIndex];
            return n.active && n.generation == gen;
        }

        public bool IsPartType(int npcType) => Def.SegmentIds.Contains(npcType);

        /// <summary>
        /// Health fraction 0..1 over all parts. The denominator is the highest total max life ever seen, so parts that
        /// died (worm segments, creepers) keep counting as lost health instead of vanishing from the average.
        /// </summary>
        public float HealthFraction()
        {
            long life = 0, max = 0;
            foreach (int i in OwnedActive())
            {
                NPC n = Main.npc[i];
                if (!HasOwnLife(n)) continue;          // shared-life segments: only the master carries the pool
                max += Math.Max(1, n.lifeMax);
                if (CountsAsLiving(n)) life += Math.Max(0, n.life);
            }
            if (max > _peakLifeMax) _peakLifeMax = max;
            if (_peakLifeMax <= 0) return 0f;
            return Math.Clamp((float)life / _peakLifeMax, 0f, 1f);
        }

        /// <summary>Number of living parts (1 for single-NPC bosses).</summary>
        public int PartsAlive()
        {
            int n = 0;
            foreach (int i in OwnedActive())
                if (CountsAsLiving(Main.npc[i])) n++;
            return n;
        }

        /// <summary>
        /// False for a part whose life is a mirror of another NPC's (The Destroyer's segments all point at the head
        /// through realLife): their own "life" is stale and they are deactivated silently, never killed.
        /// </summary>
        private static bool HasOwnLife(NPC n) => n.realLife < 0 || n.realLife == n.whoAmI;

        /// <summary>
        /// Whether a part still counts as alive. Besides shared-life segments, Moon Lord's head and hands are
        /// special: when they are "killed" the game resets their life to full for the death animation (ai[0] = -2)
        /// and only deactivates them after the core dies, without a kill event.
        /// </summary>
        private static bool CountsAsLiving(NPC n)
        {
            if (!HasOwnLife(n)) return false;
            if ((n.type == NPCID.MoonLordHead || n.type == NPCID.MoonLordHand) && n.ai[0] <= -2f) return false;
            return n.life > 0;
        }

        /// <summary>
        /// Makes sure NpcIndex points at an active part of this boss. Returns false when nothing is left.
        /// A part with life &lt;= 0 is still "active" until Terraria runs checkDead on the next NPC update, and the
        /// NpcKilled hook fires from inside checkDead - so a dying part must keep the controller alive.
        /// </summary>
        private bool ValidateAnchor()
        {
            if (Owns(NpcIndex) && Main.npc[NpcIndex].generation == Generation) return true;

            // Worms split / parts die: re-anchor to any active owned part, preferring a living base (head) type.
            int fallback = -1;
            foreach (int i in OwnedActive())
            {
                NPC n = Main.npc[i];
                if (n.type == BaseType && CountsAsLiving(n)) { NpcIndex = i; Generation = n.generation; return true; }
                if (fallback < 0 || (CountsAsLiving(n) && !CountsAsLiving(Main.npc[fallback]))) fallback = i;
            }
            if (fallback >= 0)
            {
                NpcIndex = fallback;
                Generation = Main.npc[fallback].generation;
                return true;
            }
            return false;
        }

        /// <summary>Called once per game tick (before vanilla updates).</summary>
        public void Tick(long tick, bool announce)
        {
            if (Finished) return;
            if (!ValidateAnchor())
            {
                Finished = true;
                return;
            }
            TicksAlive++;
            DecideExtraAiSteps();

            float hpPercent = HealthFraction() * 100f;
            if (PartsAlive() == 0) return; // dying: no phase changes or attacks while checkDead is pending
            int newPhase = PhaseIndex;
            for (int i = PhaseIndex + 1; i < Def.Phases.Count; i++)
            {
                if (hpPercent <= Def.Phases[i].HealthBelowPercent) newPhase = i;
            }
            if (newPhase != PhaseIndex) EnterPhase(newPhase, tick, announce);

            int enrage = EnrageSeconds;
            if (!Enraged && enrage > 0 && TicksAlive >= (long)enrage * 60)
            {
                Enraged = true;
                if (announce) Net.Broadcast(Def.Format(Def.EnrageMessage), Net.WarnColor);
                NPC a = Anchor;
                if (a != null) Net.CombatText(a.Top, "ENRAGED", Net.WarnColor);
            }

            float rate = Enraged ? Math.Max(0.1f, Mode?.EnrageAttackRateMultiplier ?? 1f) : 1f;
            foreach (AttackState s in _attacks)
            {
                if (tick >= s.NextTick)
                {
                    Fire(s.Pattern);
                    s.NextTick = tick + Math.Max(5, (long)(s.Pattern.IntervalTicks / rate));
                    s.BurstsLeft = s.Pattern.BurstCount - 1;
                    s.BurstTick = tick + Math.Max(1, s.Pattern.BurstGapTicks);
                }
                else if (s.BurstsLeft > 0 && tick >= s.BurstTick)
                {
                    Fire(s.Pattern);
                    s.BurstsLeft--;
                    s.BurstTick = tick + Math.Max(1, s.Pattern.BurstGapTicks);
                }
            }

            foreach (MinionState m in _minions)
            {
                if (m.Spawn.IntervalTicks <= 0)
                {
                    if (m.DoneOnce) continue;
                    m.DoneOnce = true;
                    SpawnMinions(m.Spawn);
                }
                else if (tick >= m.NextTick)
                {
                    m.NextTick = tick + m.Spawn.IntervalTicks;
                    SpawnMinions(m.Spawn);
                }
            }
        }

        private void EnterPhase(int index, long tick, bool announce)
        {
            PhaseIndex = index;
            BossPhase phase = Def.Phases[index];
            _attacks.Clear();
            _minions.Clear();
            _phaseMoveMult = phase.MovementSpeedMultiplier;
            _phaseAiSpeed = phase.AiSpeed;
            foreach (AttackPattern p in phase.Attacks)
                _attacks.Add(new AttackState { Pattern = p, NextTick = tick + Math.Max(0, p.StartDelayTicks) });
            foreach (MinionSpawn m in phase.Minions)
                _minions.Add(new MinionState { Spawn = m, NextTick = tick + Math.Max(0, m.IntervalTicks) });
            if (announce && !string.IsNullOrEmpty(phase.Message))
                Net.Broadcast(Def.Format(phase.Message), Net.BossColor);
        }

        /// <summary>Called after the vanilla update: extra displacement for flying bosses.</summary>
        public void PostTick(HashSet<int> movementBoostTypes)
        {
            if (Finished) return;
            float mult = MovementMultiplier;
            if (Math.Abs(mult - 1f) < 0.001f) return;
            bool explicitBoost = Def.MovementSpeedMultiplier > 0 || _phaseMoveMult > 0;
            foreach (int i in OwnedActive())
            {
                NPC n = Main.npc[i];
                if (n.life <= 0) continue;
                // Only no-tile-collision NPCs can safely be nudged, and vanilla ones only when whitelisted.
                if (!n.noTileCollide) continue;
                if (!explicitBoost && !movementBoostTypes.Contains(n.type)) continue;
                n.position += n.velocity * (mult - 1f);
            }
        }

        /// <summary>Extra AI steps every part of this boss takes during the current tick (decided once per tick in Tick()).</summary>
        public int ExtraAiStepsThisTick { get; private set; }

        private void DecideExtraAiSteps()
        {
            float speed = AiSpeed;
            if (speed <= 1.001f) { ExtraAiStepsThisTick = 0; return; }
            _aiAccumulator += speed - 1f;
            int extra = (int)_aiAccumulator;
            _aiAccumulator -= extra;
            ExtraAiStepsThisTick = extra;
        }

        private void Fire(AttackPattern p)
        {
            NPC a = Anchor;
            if (a == null || !a.active) return;
            Vector2 origin = a.Center + new Vector2(p.OffsetX, p.OffsetY);
            Player target = Net.NearestPlayer(origin, p.MaxRange > 0 ? p.MaxRange : 4000f);
            if (target == null) return;
            var source = a.GetSpawnSource_ForProjectile();
            int count = Math.Max(1, p.Count);
            string pattern = (p.Pattern ?? "aimed").Trim().ToLowerInvariant();

            switch (pattern)
            {
                case "ring":
                {
                    float start = (float)(Main.rand.NextDouble() * Math.PI * 2);
                    for (int i = 0; i < count; i++)
                    {
                        float angle = start + (float)(Math.PI * 2 * i / count);
                        Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * p.Speed;
                        Net.SpawnProjectile(source, origin, vel, p.ProjectileId, p.Damage, p.Knockback, p.Ai0, p.Ai1);
                    }
                    break;
                }
                case "rain":
                {
                    for (int i = 0; i < count; i++)
                    {
                        float x = target.Center.X + (float)(Main.rand.NextDouble() - 0.5) * p.RainWidth;
                        float y = target.Center.Y - p.RainHeight - (float)Main.rand.NextDouble() * 120f;
                        Vector2 vel = new Vector2((float)(Main.rand.NextDouble() - 0.5) * 2f, p.Speed);
                        Net.SpawnProjectile(source, new Vector2(x, y), vel, p.ProjectileId, p.Damage, p.Knockback, p.Ai0, p.Ai1);
                    }
                    break;
                }
                case "random":
                {
                    for (int i = 0; i < count; i++)
                        Net.SpawnProjectile(source, origin, Net.RandomUnit() * p.Speed, p.ProjectileId, p.Damage, p.Knockback, p.Ai0, p.Ai1);
                    break;
                }
                case "ontarget":
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 pos = target.Center + new Vector2(p.OffsetX, p.OffsetY) + (count > 1 ? Net.RandomUnit() * 120f : Vector2.Zero);
                        Net.SpawnProjectile(source, pos, new Vector2(0f, p.Speed), p.ProjectileId, p.Damage, p.Knockback, p.Ai0, p.Ai1);
                    }
                    break;
                }
                default: // aimed
                {
                    Vector2 dir = target.Center - origin;
                    if (dir.LengthSquared() < 1f) dir = Net.RandomUnit();
                    dir.Normalize();
                    float spread = MathHelper.ToRadians(Math.Max(0f, p.SpreadDegrees));
                    for (int i = 0; i < count; i++)
                    {
                        float offset = count == 1 ? 0f : -spread / 2f + spread * i / (count - 1);
                        Vector2 vel = Net.Rotate(dir, offset) * p.Speed;
                        Net.SpawnProjectile(source, origin, vel, p.ProjectileId, p.Damage, p.Knockback, p.Ai0, p.Ai1);
                    }
                    break;
                }
            }
        }

        private void SpawnMinions(MinionSpawn m)
        {
            NPC a = Anchor;
            if (a == null || !a.active) return;
            if (Net.NearestPlayer(a.Center, 3000f) == null) return;
            int alive = NPC.CountNPCS(m.NpcId);
            int toSpawn = Math.Min(m.Count, Math.Max(0, m.MaxAlive - alive));
            if (toSpawn <= 0) return;

            for (int i = 0; i < toSpawn; i++)
            {
                Vector2 pos = a.Center + Net.RandomUnit() * Math.Max(16, m.SpreadPixels);
                BossManager.PendingMinion = m.InheritBossDifficulty ? this : null;
                try
                {
                    int idx = NPC.NewNPC(a.GetSpawnSourceForNPCFromNPCAI(), (int)pos.X, (int)pos.Y, m.NpcId);
                    if (idx >= 0 && idx < Main.maxNPCs) Net.SyncNpc(idx);
                }
                finally
                {
                    BossManager.PendingMinion = null;
                }
            }
            if (!string.IsNullOrEmpty(m.Message)) Net.Broadcast(Def.Format(m.Message), Net.BossColor);
        }
    }
}
