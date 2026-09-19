using WindBot.Game;
using WindBot.Game.AI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // A general executor for decks authored by the player. It deliberately has no archetype-specific list.
    [Deck("MDPro3Story", "MDPro3Story", "Test")]
    public sealed class StoryExecutor : DefaultExecutor
    {
        public StoryExecutor(GameAI ai, WindBot.Game.Duel duel) : base(ai, duel)
        {
            Diagnostics.StoryModeSelfTest.AttachBot(ai.Game);
            AddExecutor(ExecutorType.SpSummon);
            AddExecutor(ExecutorType.Activate, DefaultDontChainMyself);
            AddExecutor(ExecutorType.SummonOrSet, DefaultMonsterSummon);
            AddExecutor(ExecutorType.Repos, DefaultMonsterRepos);
            AddExecutor(ExecutorType.SpellSet);
        }
    }
}
