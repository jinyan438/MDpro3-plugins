using WindBot.Game;
using WindBot.Game.AI;

namespace MDPro3.Plugins.Features.StoryMode
{
    // Plugin-owned deterministic strategy for arbitrary story decks.
    // Keep this independent so story AI behavior can be changed without modifying the base game.
    public abstract partial class StoryLuckyExecutor : DefaultExecutor
    {
        protected StoryLuckyExecutor(GameAI ai, WindBot.Game.Duel duel) : base(ai, duel)
        {
            evaluation = new StoryAiEvaluation(duel, ai.Game?.DeckFile);
            // Phase transitions and generic setting from DefaultExecutor must not preempt planning.
            for (int i = Executors.Count - 1; i >= 0; i--)
                if (Executors[i].CardId == -1) Executors.RemoveAt(i);

            AddExecutor(ExecutorType.GoToBattlePhase, StoryEvenlyBattle);

            AddExecutor(ExecutorType.Activate, _CardId.GalaxyCyclone, DefaultGalaxyCyclone);
            AddExecutor(ExecutorType.Activate, _CardId.BookOfMoon, StoryBookOfMoon);
            AddExecutor(ExecutorType.Activate, _CardId.CompulsoryEvacuationDevice, DefaultCompulsoryEvacuationDevice);
            AddExecutor(ExecutorType.Activate, _CardId.CallOfTheHaunted, DefaultCallOfTheHaunted);
            AddExecutor(ExecutorType.Activate, _CardId.Scapegoat, DefaultScapegoat);
            AddExecutor(ExecutorType.Activate, _CardId.MaxxC, StoryDrawPressure);
            AddExecutor(ExecutorType.Activate, _CardId.AshBlossom, StoryAshBlossom);
            AddExecutor(ExecutorType.Activate, _CardId.GhostOgreAndSnowRabbit, StoryGhostOgre);
            AddExecutor(ExecutorType.Activate, _CardId.GhostBelle, DefaultGhostBelleAndHauntedMansion);
            AddExecutor(ExecutorType.Activate, _CardId.EffectVeiler, StoryDisableMonster);
            AddExecutor(ExecutorType.Activate, _CardId.CalledByTheGrave, StoryCalledByTheGrave);
            AddExecutor(ExecutorType.Activate, _CardId.InfiniteImpermanence, StoryDisableMonster);
            AddExecutor(ExecutorType.Activate, _CardId.BreakthroughSkill, StoryDisableMonster);
            AddExecutor(ExecutorType.Activate, _CardId.SolemnJudgment, DefaultSolemnJudgment);
            AddExecutor(ExecutorType.Activate, _CardId.SolemnWarning, DefaultSolemnWarning);
            AddExecutor(ExecutorType.Activate, _CardId.SolemnStrike, DefaultSolemnStrike);
            AddExecutor(ExecutorType.Activate, _CardId.HeavyStorm, DefaultHeavyStorm);
            AddExecutor(ExecutorType.Activate, _CardId.HammerShot, DefaultHammerShot);
            AddExecutor(ExecutorType.Activate, _CardId.SmashingGround, DefaultSmashingGround);
            AddExecutor(ExecutorType.Activate, _CardId.PotOfDesires, DefaultPotOfDesires);
            AddExecutor(ExecutorType.Activate, _CardId.AllureofDarkness, DefaultAllureofDarkness);
            AddExecutor(ExecutorType.Activate, _CardId.DimensionalBarrier, DefaultDimensionalBarrier);
            AddExecutor(ExecutorType.Activate, _CardId.InterruptedKaijuSlumber, DefaultInterruptedKaijuSlumber);

            AddExecutor(ExecutorType.SpSummon, _CardId.JizukirutheStarDestroyingKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.GadarlatheMysteryDustKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.GamecieltheSeaTurtleKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.RadiantheMultidimensionalKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.KumongoustheStickyStringKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.ThunderKingtheLightningstrikeKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.DogorantheMadFlameKaiju, DefaultKaijuSpsummon);
            AddExecutor(ExecutorType.SpSummon, _CardId.SuperAntiKaijuWarMachineMechaDogoran, DefaultKaijuSpsummon);

            AddExecutor(ExecutorType.SpSummon, _CardId.EvilswarmExcitonKnight, StoryExcitonSummon);
            AddExecutor(ExecutorType.Activate, _CardId.EvilswarmExcitonKnight, DefaultEvilswarmExcitonKnightEffect);

            AddExecutor(ExecutorType.Summon, _CardId.SandaionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.GabrionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.MichionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.ZaphionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.HailonTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.RaphionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.SadionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.MetaionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.KamionTheTimelord, DefaultTimelordSummon);
            AddExecutor(ExecutorType.Summon, _CardId.LazionTheTimelord, DefaultTimelordSummon);

            AddExecutor(ExecutorType.Summon, _CardId.LeftArmofTheForbiddenOne, JustDontIt);
            AddExecutor(ExecutorType.Summon, _CardId.RightLegofTheForbiddenOne, JustDontIt);
            AddExecutor(ExecutorType.Summon, _CardId.LeftLegofTheForbiddenOne, JustDontIt);
            AddExecutor(ExecutorType.Summon, _CardId.RightArmofTheForbiddenOne, JustDontIt);
            AddExecutor(ExecutorType.Summon, _CardId.ExodiaTheForbiddenOne, JustDontIt);

            RegisterStoryPolicy();
            RegisterArtworkRules();
        }

        private void RegisterArtworkRules()
        {
            // Lua uses the original script for alternate artwork. Keep its dedicated
            // rule and veto as well, in the same order as the original registration.
            for (int i = Executors.Count - 1; i >= 0; i--)
            {
                var rule = Executors[i];
                if (rule.CardId <= 0) continue;
                for (int offset = 1; offset < 20; offset++)
                {
                    var art = YGOSharp.OCGWrapper.NamedCard.Get(rule.CardId + offset);
                    if (art?.Alias == rule.CardId)
                        Executors.Insert(i + 1, new CardExecutor(rule.Type, art.Id, rule.Func));
                }
            }
        }

    }
}
