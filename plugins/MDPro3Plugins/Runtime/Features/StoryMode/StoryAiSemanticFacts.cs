using System;
using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        // Facts describe script operations, never a preferred card or combo.
        internal sealed class TacticalFact
        {
            internal StoryLuckyExecutor.EffectPurpose Purpose;
            internal int Offset, Origin = 4, Locations = 12, Hint, CountKey, AttackCost, MaterialAttack, LinkAttack;
            internal int CostLocations, CostHint, DiscardCount, CustomHint, Option, MaxLevel = 99, Life, DrawCount, OverlayCost;
            internal int DrawOverlayType, RemovalOverlayType, ExitOverlayType;
            internal bool DefaultDescription, Trigger, Quick, MonsterOnly, SpellTrapOnly, NonTargeting;
            internal bool Once, InstanceOnce, FaceupOnce, PreventDirect, ExcludeSelf, SpecialOnly, BattleOnly;
            internal bool AttributeCost, LinkCounters, SharedLimit, AllTargets, Narrow;
            internal bool SelfCost;
            internal TacticalFact[] Modes;
            internal Func<ClientCard, bool> CostFilter, TargetFilter;
        }
        internal sealed class CardFacts
        {
            internal EffectLimit[] Limits = new EffectLimit[0];
            internal TacticalFact[] Effects = new TacticalFact[0];
            internal int MaterialAttack, LinkAttack, ImmunityBonus, ProtectionBonus;
            internal bool FlexibleTuner, GraveRedirect, OpponentGraveRedirect, OpponentDamageShield, BothDamageShield, UniversalRevival;
            internal bool BattleEndBalance;
            internal int[] SummonSets = new int[0];
            internal int[] ListedCodes = new int[0];
        }
        internal sealed class EffectLimit
        {
            internal int CountKey, Origin, Offset;
            internal bool DefaultDescription, Trigger, DuelOnce;
        }
        private static EffectLimit RegisteredLimit(ClientCard card, int description)
        {
            var limits = Facts(card).Limits.Where(f => (f.Origin & (int)card.Location) != 0).ToList();
            var matches = limits.Where(f => description >= 16000 ? !f.DefaultDescription && f.Offset == description % 16 :
                description == 0 ? f.DefaultDescription : description == -1 && f.Trigger).ToList();
            if (matches.Count == 0 && description >= 16000 && description % 16 == 0)
                matches = limits.Where(f => f.DefaultDescription).ToList();
            return matches.Count > 0 && matches.Select(f => f.CountKey).Distinct().Count() == 1 ? matches[0] : null;
        }
        private static readonly Dictionary<int, CardFacts> semanticFacts = BuildSemanticFacts();
        private static readonly Dictionary<int, Recipe> scriptRecipes = BuildScriptRecipes();
        private static readonly CardFacts emptyFacts = new CardFacts();
        internal static CardFacts Facts(ClientCard card) => Facts(CardIdentity(card));
        internal static CardFacts Facts(int id) => semanticFacts.TryGetValue(id, out var facts) ? facts : emptyFacts;
        internal static TacticalFact Tactic(ClientCard source, int description)
        {
            if (source == null) return null;
            int id = description >= 16000 ? description / 16 : CardIdentity(source);
            var effects = Facts(id).Effects.Where(f => ((int)source.Location & f.Origin) != 0).ToList();
            if (description >= 16000)
            {
                var exact = effects.FirstOrDefault(f => !f.DefaultDescription && f.Offset == description % 16);
                if (exact != null || description % 16 != 0) return exact;
                var defaults = effects.Where(f => f.DefaultDescription).ToList();
                return defaults.Count == 1 ? defaults[0] : null;
            }
            if (description == 0)
            {
                var defaults = effects.Where(f => f.DefaultDescription).ToList();
                if (defaults.Count == 1) return defaults[0];
            }
            var triggers = effects.Where(f => f.Trigger).ToList();
            return (description == -1 || description == 0) && triggers.Count == 1 ? triggers[0] : null;
        }
        private static bool InteractionFact(TacticalFact f) => f.Quick && (f.Origin & 4) != 0 &&
            (f.Purpose == StoryLuckyExecutor.EffectPurpose.Negate || f.Purpose == StoryLuckyExecutor.EffectPurpose.QuickLink ||
             f.Purpose == StoryLuckyExecutor.EffectPurpose.TemporaryPair || f.Purpose == StoryLuckyExecutor.EffectPurpose.EnemyRemoval ||
             f.Purpose == StoryLuckyExecutor.EffectPurpose.TargetRemoval || f.Purpose == StoryLuckyExecutor.EffectPurpose.TargetNegate);
        private static int ProjectedSummonAttack(ClientCard card, IList<ClientCard> materials)
        {
            var facts = Facts(card);
            return facts.MaterialAttack > 0 ? facts.MaterialAttack * materials.Count :
                Attack(card) + facts.LinkAttack * materials.Select(LinkRating).DefaultIfEmpty(0).Max();
        }
    }
}
