using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace CalamityShock
{
    /// <summary>Thin logging wrapper so every message is prefixed consistently.</summary>
    public static class Log
    {
        public static void Info(string msg) => TShock.Log?.ConsoleInfo("[CalamityShock] " + msg);
        public static void Warn(string msg) => TShock.Log?.ConsoleError("[CalamityShock] WARNING: " + msg);
        public static void Error(string msg) => TShock.Log?.ConsoleError("[CalamityShock] ERROR: " + msg);
    }

    /// <summary>
    /// Network helpers. Everything the plugin does to the world has to be told to the clients;
    /// these wrap the handful of packets we rely on.
    /// </summary>
    public static class Net
    {
        public static readonly Color BossColor = new Color(175, 75, 255);
        public static readonly Color EventColor = new Color(50, 255, 130);
        public static readonly Color WarnColor = new Color(255, 120, 60);

        /// <summary>Full NPC sync (packet 23). After a fresh spawn this carries the NPC's difficulty to clients.</summary>
        public static void SyncNpc(int index)
        {
            if (index < 0 || index >= Main.maxNPCs) return;
            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, index);
        }

        /// <summary>Sends the NPC's GivenName to all clients (packet 56). Clients show it when hovering the NPC.</summary>
        public static void SyncNpcName(int index)
        {
            if (index < 0 || index >= Main.maxNPCs) return;
            NetMessage.SendData((int)PacketTypes.UpdateNPCName, -1, -1, null, index);
        }

        /// <summary>Removes an NPC from the world on the server and on every client.</summary>
        public static void KillNpcSilently(int index)
        {
            if (index < 0 || index >= Main.maxNPCs) return;
            NPC npc = Main.npc[index];
            npc.active = false;
            npc.life = 0;
            NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, index);
        }

        /// <summary>
        /// Spawns a projectile owned by the server. Only projectile types that are hostile by default can hurt
        /// players (the hostile flag is decided client-side from the type, it is not sent over the network),
        /// so the boss catalog is validated with <see cref="IsHostileProjectile"/> when it loads.
        /// Projectile.NewProjectile already broadcasts packet 27 on the server.
        /// </summary>
        public static int SpawnProjectile(IEntitySource source, Vector2 position, Vector2 velocity, int type, int damage, float knockback, float ai0 = 0f, float ai1 = 0f)
        {
            if (type <= 0 || type >= ProjectileID.Count) return -1;
            int index = Projectile.NewProjectile(source ?? new EntitySource_DebugCommand(), position, velocity, type, damage, knockback, Main.myPlayer, ai0, ai1);
            if (index >= 0 && index < Main.maxProjectiles)
            {
                Projectile p = Main.projectile[index];
                // Projectile-vs-NPC hits are computed by the owner, which for these is the server. Some hostile types
                // (falling sand, mud balls...) are also "friendly" and would damage the boss that fired them.
                p.friendly = false;
                // Mark it as an NPC's projectile and stop falling-block types from turning into tiles / block items
                // when they land (the server runs their Kill code because it owns them).
                p.npcProj = true;
                p.noDropItem = true;
            }
            return index;
        }

        /// <summary>True when the projectile type damages players (its defaults set hostile = true).</summary>
        public static bool IsHostileProjectile(int type)
        {
            if (type <= 0 || type >= ProjectileID.Count) return false;
            try
            {
                var probe = new Projectile();
                probe.SetDefaults(type);
                return probe.hostile;
            }
            catch { return false; }
        }

        /// <summary>Drops an item on the ground at a position and broadcasts it.</summary>
        public static int DropItem(Vector2 center, int type, int stack, int prefix = 0)
        {
            if (type <= 0 || type >= ItemID.Count || stack <= 0) return -1;
            return Item.NewItem(new EntitySource_DebugCommand(), center, type, stack, prefix);
        }

        /// <summary>Removes a world item everywhere.</summary>
        public static void RemoveWorldItem(int index)
        {
            if (index < 0 || index >= Main.maxItems) return;
            Main.item[index].TurnToAirAndSync();
        }

        public static void Broadcast(string message, Color color)
        {
            if (string.IsNullOrEmpty(message)) return;
            TSPlayer.All.SendMessage(message, color);
            Log.Info(message);
        }

        /// <summary>Floating combat text at a world position (packet 119).</summary>
        public static void CombatText(Vector2 position, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            NetMessage.SendData((int)PacketTypes.CreateCombatTextExtended, -1, -1, NetworkText.FromLiteral(text), (int)color.PackedValue, position.X, position.Y);
        }

        /// <summary>Nearest living player to a position, or null if nobody is within maxDistance pixels.</summary>
        public static Player NearestPlayer(Vector2 position, float maxDistance = 4000f)
        {
            Player best = null;
            float bestDist = maxDistance * maxDistance;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player p = Main.player[i];
                if (p == null || !p.active || p.dead) continue;
                float d = Vector2.DistanceSquared(p.Center, position);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        public static int CountLivingPlayers()
        {
            int n = 0;
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player p = Main.player[i];
                if (p != null && p.active && !p.dead) n++;
            }
            return n;
        }

        public static Vector2 RandomUnit()
        {
            double a = Main.rand.NextDouble() * Math.PI * 2;
            return new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
        }

        public static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }
    }
}
