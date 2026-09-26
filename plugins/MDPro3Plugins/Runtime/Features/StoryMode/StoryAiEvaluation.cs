using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Text features are ranking hints, never a substitute for the core's legal action list.
    // Keep this per executor: concurrent duels must not share mutable decision state.
    internal sealed partial class StoryAiEvaluation
    {
        [Flags]
        internal enum Role { None = 0, Search = 1, Draw = 2, Extend = 4, Grave = 8, Interrupt = 16, Starter = 32, Removal = 64 }
        private readonly Dictionary<int, Role> roles = new Dictionary<int, Role>();
        private readonly WindBot.Game.Duel duel;
        private ClientField Bot => duel.Fields[0];
        private ClientField Enemy => duel.Fields[1];

        internal StoryAiEvaluation(WindBot.Game.Duel duel, string ownDeckFile = null) { this.duel = duel; developmentDeckFile = ownDeckFile; }

        internal static readonly HashSet<int> HandTraps = new HashSet<int>
        {
            14558127, 23434538, 94145021, 59438930, 73642296, 97268402, 52038441,
            34267821, 91800273, 27204311, 84192580, 42141493, 87126721
        };
        internal static bool Exodia(ClientCard c) => c != null &&
            (c.Id == 7902349 || c.Id == 8124921 || c.Id == 44519536 || c.Id == 70903634 || c.Id == 33396948);
        internal static bool Hidden(ClientCard c) => c.Controller == 1 &&
            (c.Location == CardLocation.Hand || c.Location == CardLocation.Deck || c.Location == CardLocation.Extra || c.IsFacedown());
        internal static bool Has(ClientCard c, CardType type) =>
            ((c.Type != 0 ? c.Type : c.Data?.Type ?? 0) & (int)type) != 0;
        internal static int Attack(ClientCard c) => Math.Max(0, c.Location == CardLocation.MonsterZone ? c.Attack : c.Data?.Attack ?? c.Attack);
        internal static int Defense(ClientCard c) => Math.Max(0, c.Location == CardLocation.MonsterZone ? c.Defense : c.Data?.Defense ?? c.Defense);
        internal static int Level(ClientCard c) => c.Level > 0 ? c.Level : c.Data?.Level ?? 0;
        internal static bool Contains(string text, params string[] parts) => parts.Any(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);

        internal Role Roles(ClientCard c)
        {
            if (c == null || Hidden(c) || c.Data == null) return Role.None;
            if (roles.TryGetValue(c.Id, out var cached)) return cached;
            string text = c.Data.Description ?? "";
            Role role = Role.None;
            if (Contains(text, "加入手卡", "加入手牌", "add", "加える") &&
                Contains(text, "卡组", "牌组", "deck", "デッキ")) role |= Role.Search;
            if (Contains(text, "抽", "draw", "ドロー")) role |= Role.Draw;
            if (Contains(text, "特殊召唤", "特殊召喚", "special summon")) role |= Role.Extend;
            if (Contains(text, "送去墓地的场合", "送入墓地的場合", "送去墓地的場合", "sent to the gy", "sent to the graveyard",
                "把墓地的这张卡除外", "将墓地的这张卡除外", "banish this card from your gy", "墓地へ送られた場合")) role |= Role.Grave;
            if (Contains(text, "无效", "無效", "無効", "negate")) role |= Role.Interrupt;
            if (Contains(text, "破坏", "破壞", "destroy", "banish", "除外", "return it to the hand")) role |= Role.Removal;
            if (Contains(text, "召唤成功", "召喚成功", "通常召喚", "normal summoned", "normal or special summoned")) role |= Role.Starter;
            // Bound the cache even for an unusually long duel involving generated cards.
            if (roles.Count < 4096) roles[c.Id] = role;
            return role;
        }

        internal float Threat(ClientCard c)
        {
            if (c == null) return 0;
            if (Hidden(c)) return c.Location == CardLocation.MonsterZone ? 1500 : 1600;
            float value = 700 + Math.Max(Attack(c), Defense(c)) * .65f;
            if (Has(c, CardType.Monster))
            {
                value += 200 * c.Overlays.Count + 220 * (Has(c, CardType.Link) ? Math.Max(c.LinkCount, Level(c)) : 0);
                if (Has(c, CardType.Effect)) value += c.IsDisabled() ? 80 : 650;
            }
            else value = Has(c, CardType.Continuous | CardType.Field | CardType.Equip) ? 1700 : 1000;
            if (!c.IsDisabled())
            {
                if (c.IsFloodgate()) value += 4500;
                if (c.IsMonsterDangerous() || c.IsMonsterInvincible()) value += 1600;
                if ((Roles(c) & Role.Interrupt) != 0) value += 1300;
                if ((Roles(c) & (Role.Search | Role.Draw)) != 0) value += 450;
            }
            return value;
        }

        internal float Keep(ClientCard c)
        {
            if (c == null) return 0;
            if (c.Controller == 1) return -Threat(c);
            if (Exodia(c)) return 25000;
            float value = Threat(c);
            if (Has(c, CardType.Token)) return 180 + Attack(c) * .18f;
            var role = Roles(c);
            if (c.Location == CardLocation.Hand)
            {
                if (HandTraps.Contains(c.Id)) value += 2100;
                if ((role & (Role.Search | Role.Starter)) != 0) value += 1600;
                if ((role & Role.Extend) != 0) value += 650;
                if (Has(c, CardType.Monster) && Level(c) > 4 && (role & Role.Extend) == 0) value -= 800;
                value -= 650 * Bot.Hand.Count(x => x != c && x.Id == c.Id);
            }
            if (c.Location == CardLocation.MonsterZone && c.IsDisabled()) value *= .55f;
            if (c.Location == CardLocation.MonsterZone && c.Attacked) value -= 300;
            return Math.Max(100, value);
        }

        internal float MaterialCost(ClientCard c, int hint)
        {
            if (c.Controller == 1) return -Threat(c); // e.g. Super Polymerization / Kaiju.
            float value = Keep(c);
            bool sendsToGrave = hint != HintMsg.Remove && hint != HintMsg.ToDeck && hint != HintMsg.XyzMaterial;
            if (sendsToGrave && (Roles(c) & Role.Grave) != 0) value -= 1200;
            if (c.Location == CardLocation.Overlay) value = 200;
            if (c.Location == CardLocation.Grave) value *= .45f;
            return value;
        }

        internal float Acquire(ClientCard c)
        {
            if (Hidden(c)) return 0;
            float value = Keep(c);
            var role = Roles(c);
            if ((role & (Role.Search | Role.Starter)) != 0 && Bot.GetMonsterCount() < 2) value += 1800;
            if ((role & Role.Extend) != 0) value += Bot.GetMonsterCount() > 0 ? 1400 : 700;
            if ((role & Role.Interrupt) != 0 && Bot.GetMonsterCount() > 1) value += 900;
            value -= 1100 * Bot.Hand.Count(x => x.Id == c.Id && x != c);
            // Once the normal summon is spent, favor live extenders over another normal summon.
            if (duel.Player == 0 && duel.MainPhase != null && duel.MainPhase.SummonableCards.Count == 0 &&
                Has(c, CardType.Monster) && (role & Role.Extend) == 0) value -= 1100;
            return value;
        }

        internal float SummonValue(ClientCard c)
        {
            float value = 900 + Attack(c) * .65f;
            var role = Roles(c);
            if ((role & Role.Starter) != 0) value += 2000;
            if ((role & Role.Search) != 0) value += 1600;
            if ((role & Role.Extend) != 0) value += 1100;
            if ((role & Role.Interrupt) != 0) value += 800;
            if ((role & Role.Draw) != 0) value += 900;
            if ((role & Role.Removal) != 0 && Enemy.GetFieldCount() > 0) value += 1200;
            if (HandTraps.Contains(c.Id)) value -= 2400;
            if (Has(c, CardType.Tuner) && Bot.GetMonsterCount() > 0 && Bot.ExtraDeck.Any(x => Has(x, CardType.Synchro))) value += 800;
            if (!Has(c, CardType.Tuner) && Bot.GetMonsters().Any(x => Has(x, CardType.Tuner)) &&
                Bot.ExtraDeck.Any(x => Has(x, CardType.Synchro) && Bot.GetMonsters().Any(t => Has(t, CardType.Tuner) && Level(t) + Level(c) == Level(x)))) value += 1000;
            if (Bot.ExtraDeck.Any(x => Has(x, CardType.Xyz) && Level(x) == Level(c)) &&
                Bot.GetMonsters().Any(x => !Has(x, CardType.Xyz | CardType.Link) && Level(x) == Level(c))) value += 850;
            if (Enemy.GetMonsters().Any(x => !Hidden(x) && Attack(c) > x.GetDefensePower())) value += 500;
            if (Exodia(c)) return -25000;
            return value;
        }

        internal bool CanTarget(ClientCard target, ClientCard source)
        {
            if (Hidden(target)) return true;
            if (target.IsShouldNotBeTarget()) return false;
            if (!target.IsDisabled() && Contains(target.Data?.Description ?? "", "cannot target this card with card effects",
                "cannot be targeted by card effects", "对方不能把这张卡作为效果的对象", "不能成为对方的效果的对象",
                "對方不能把這張卡作為效果的對象", "相手の効果の対象にならない")) return false;
            return Has(source, CardType.Monster) ? !target.IsShouldNotBeMonsterTarget() : !target.IsShouldNotBeSpellTrapTarget();
        }
    }
}
