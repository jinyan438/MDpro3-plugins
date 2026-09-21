using WindBot.Game;
using WindBot.Game.AI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // WindBot registration for the plugin-owned story AI implementation.
    [Deck("MDPro3Story", "MDPro3Story", "Test")]
    public sealed class StoryExecutor : StoryLuckyExecutor
    {
        public StoryExecutor(GameAI ai, WindBot.Game.Duel duel) : base(ai, duel)
        {
            Diagnostics.StoryModeSelfTest.AttachBot(ai.Game);
        }
    }
}
