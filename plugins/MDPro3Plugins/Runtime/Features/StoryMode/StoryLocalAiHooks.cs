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
        public static void PrepareChain(GameAI ai, IList<ClientCard> cards, IList<int> descriptions, IList<bool> forced, int timing)
        {
            if (ai.Executor is StoryLuckyExecutor story) story.PrepareChainResponses(cards, descriptions, forced);
        }
        public static int FinishChain(int result, GameAI ai)
        {
            if (ai.Executor is StoryLuckyExecutor story) story.ClearChainResponses();
            return result;
        }
        public static void PrepareEffect(GameAI ai, ClientCard card, int description)
        {
            if (ai.Executor is StoryLuckyExecutor story)
                story.PrepareChainResponses(new[] { card }, new[] { description }, new[] { false });
        }
        public static bool FinishEffect(bool result, GameAI ai)
        {
            if (ai.Executor is StoryLuckyExecutor story) story.ClearChainResponses();
            return result;
        }
        public static MainPhaseAction FinishMain(MainPhaseAction result, GameAI ai)
        {
            if (ai.Executor is StoryLuckyExecutor story) story.NoteMainAction(result);
            return result;
        }
        public static int FinishNumber(int result, GameAI ai, IList<int> numbers) =>
            ai.Executor is StoryLuckyExecutor story ? story.SelectLevelNumber(numbers, result) : result;
        public static IList<ClientCard> SelectTribute(GameAI ai, IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            return ai.OnSelectCard(cards, min, max, HintMsg.Release, cancelable);
        }
    }
}
