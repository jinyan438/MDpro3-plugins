using System;
using System.Collections.Generic;
using System.Linq;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Facts are extracted structurally from scripts, not selected by deck/card ID.
    // All profiles use the same planner, target selection and numeric protocol.
    internal static partial class StoryAiLevelEffects
    {
        internal enum Scope { Self, Target, Summoned, All }
        internal enum Cost { None, Life, BanishSelf }
        internal sealed class Filter
        {
            internal int Min = 1, Max = 99, Attribute, Race, Type, NotType;
            internal int[] Sets = new int[0], NotLevels = new int[0], ExcludeIds = new int[0];
            internal bool Matches(ClientCard card, int level) => level >= Min && level <= Max && !NotLevels.Contains(level) &&
                !ExcludeIds.Contains(StoryAiEvaluation.CardIdentity(card)) &&
                (Attribute == 0 || (StoryAiEvaluation.Attribute(card) & Attribute) != 0) &&
                (Race == 0 || (StoryAiEvaluation.Race(card) & Race) != 0) &&
                (Type == 0 || StoryAiEvaluation.Has(card, (CardType)Type)) &&
                (NotType == 0 || !StoryAiEvaluation.Has(card, (CardType)NotType)) &&
                (Sets.Length == 0 || Sets.Any(card.HasSetcode));
        }
        internal sealed class Profile
        {
            internal int SourceId, Description, Minimum = 1, Maximum = 1, Life;
            internal CardLocation From;
            internal Scope Scope;
            internal Cost Cost;
            internal Filter Filter = new Filter();
            internal bool Delta, Activation, Trigger, GenericTrigger, Predictable, Consume, ExcludeSource, Once;
            internal bool SelfAlso = false, AtTarget = false;
            internal bool SpecialArrival;
            internal string Limit = "";
            internal int[] Values = new int[0], Numbers = new int[0], Options = new int[0], Forced = new int[0];

            internal bool MatchesDescription(int description) => description == Description ||
                Activation && description == 0 || GenericTrigger && description == -1;

            internal int Result(int level, int choice)
            {
                int value = Values[choice];
                for (int i = 0; i + 1 < Forced.Length; i += 2)
                    if (Forced[i] == level && value != Forced[i + 1]) return 0;
                // Native level reductions bottom out at one; user-selected
                // reductions with a numeric/option prompt must remain positive.
                int result = Delta ? level + value : value;
                return result > 0 ? result : Numbers.Length == 0 && Options.Length == 0 ? 1 : 0;
            }
        }
        private static readonly List<Profile> profiles = Build();
        private static readonly Dictionary<int, List<Profile>> bySource = profiles.GroupBy(p => p.SourceId).ToDictionary(g => g.Key, g => g.ToList());
        internal static IEnumerable<Profile> For(ClientCard source) => source != null &&
            bySource.TryGetValue(StoryAiEvaluation.CardIdentity(source), out var found) ? found : Enumerable.Empty<Profile>();

        internal static Profile Find(ClientCard source, int description)
        {
            var candidates = For(source).Where(p => (p.From & source.Location) != 0 && p.MatchesDescription(description)).ToList();
            // No inference from printed paragraph numbers or an ambiguous prompt.
            return candidates.Count == 1 ? candidates[0] : null;
        }
    }
}
