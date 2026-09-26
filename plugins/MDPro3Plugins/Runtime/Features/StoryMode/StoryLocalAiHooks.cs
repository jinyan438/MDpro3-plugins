using System.Collections.Generic;
using WindBot.Game;
using WindBot.Game.AI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // GameAI has no virtual tribute callback. The plugin's compile-time hook routes only
    // story executors through their material policy; all other decks keep the native method.
    public static class StoryLocalAiHooks
    {
        public static bool UsesStory(GameAI ai) => ai.Executor is StoryLuckyExecutor;
        public static IList<ClientCard> SelectTribute(GameAI ai, IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            return ai.OnSelectCard(cards, min, max, HintMsg.Release, cancelable);
        }
    }
}
