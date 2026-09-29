using System.Collections.Generic;
using WindBot.Game;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Offline facts from public card scripts. No Lua execution, network access,
    // opponent hidden cards, or per-decision parsing is needed at runtime.
    internal static partial class StoryAiScriptEffects
    {
        internal enum Kind { Destroy, Banish, SendGrave, ReturnHand, ReturnDeck, Disable, Negate, StopAttack }
        internal sealed class Profile
        {
            internal Kind Purpose;
            internal CardLocation Locations;
            internal int Minimum, Flags;
        }
        private static readonly Dictionary<int, Profile> profiles = Build();
        private static readonly Dictionary<int, (int Description, int Origin)> attackTriggers = BuildAttackTriggers();
        private static void Add(Dictionary<int, Profile> result, Kind kind, int locations, int minimum, int flags, int[] descriptions)
        {
            var profile = new Profile { Purpose = kind, Locations = (CardLocation)locations, Minimum = minimum, Flags = flags };
            foreach (int description in descriptions) result.Add(description, profile);
        }
        internal static Profile Find(int description)
        {
            // 0 and -1 are generic prompts, not effect #0. Copied effects use
            // their original script's description, not the copier's printed text.
            if (description < 16000) return null;
            return profiles.TryGetValue(description, out var result) ? result : null;
        }
        internal static Profile FindAttackTrigger(ClientCard source)
        {
            // The core's generic optional-trigger prompt is normalized to -1.
            // Only scripts with one verified optional trigger can use this map;
            // never guess effect #0 or borrow an ignition/graveyard effect.
            return source != null && attackTriggers.TryGetValue(StoryAiEvaluation.CardIdentity(source), out var trigger) &&
                ((int)source.Location & trigger.Origin) != 0 ? Find(trigger.Description) : null;
        }
    }
}
