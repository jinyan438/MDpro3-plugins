using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using WindBot.Game;
using YGOSharp.Network.Enums;
using YGOSharp.Network.Utils;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    public static class StoryModelHooks
    {
        [ThreadStatic] internal static StoryModelSession LaunchSession;
        // Rewritten by the IL postprocessor; fail closed if a build skipped weaving.
        public static bool Installed() => false;

        public static bool TryHandle(GameBehavior behavior, GameAI ai, BinaryReader packet, ref int hint, ref GameMessage last)
        {
            if (!(ai.Executor is StoryExecutor executor) || executor.ModelSession == null) return false;
            long position = packet.BaseStream.Position;
            try
            {
                if (packet.ReadByte() != (byte)StocMessage.GameMsg) return false;
                var message = (GameMessage)packet.ReadByte();
                // The native engine is the final authority. Retry the original window
                // through WindBot if the core rejects a model response, instead of disconnecting.
                if (message == GameMessage.Retry && executor.PendingModelPacket != null)
                {
                    var original = executor.PendingModelPacket; executor.PendingModelPacket = null;
                    hint = executor.PendingModelHint;
                    executor.ModelSession.RejectedByEngine();
                    if (!executor.ModelSession.AllowLocalFallback)
                    {
                        if (executor.ModelSession.TryRequestModelOnlyTermination()) TerminateModelOnly(behavior);
                        return true;
                    }
                    using (var retry = new BinaryReader(new MemoryStream(original))) behavior.OnPacket(retry);
                    return true;
                }
                if (!IsDecision(message)) return false;
                executor.PendingModelPacket = null;
                if (!executor.ModelSession.CanAttempt)
                {
                    if (executor.ModelSession.TryRequestModelOnlyTermination()) TerminateModelOnly(behavior);
                    return !executor.ModelSession.AllowLocalFallback;
                }
                packet.BaseStream.Position = position + 1;
                int length = checked((int)(packet.BaseStream.Length - packet.BaseStream.Position));
                if (length < 2 || length > 65535) throw new InvalidDataException();
                var raw = packet.ReadBytes(length);
                var reply = executor.ModelSession.Decide(raw, Snapshot(ai.Executor.Duel, behavior.Deck), hint);
                if (reply == null || executor.ModelSession.Stopped)
                {
                    if (executor.ModelSession.TryRequestModelOnlyTermination()) TerminateModelOnly(behavior);
                    return !executor.ModelSession.AllowLocalFallback;
                }
                // A new model-selected action invalidates targets queued by an earlier
                // local fallback action. Do not clear queues during a fallback itself.
                if (message == GameMessage.SelectIdleCmd || message == GameMessage.SelectBattleCmd
                    || message == GameMessage.SelectChain || message == GameMessage.SelectEffectYn) ResetSelections(ai);
                var writer = GamePacketFactory.Create(CtosMessage.Response);
                writer.Write(reply); behavior.Connection.Send(writer);
                executor.PendingModelPacket = new[] { (byte)StocMessage.GameMsg }.Concat(raw).ToArray();
                executor.PendingModelHint = hint;
                if (message == GameMessage.SelectCard || message == GameMessage.SelectPlace
                    || message == GameMessage.SelectDisfield || message == GameMessage.SelectSum) hint = 0;
                last = message;
                return true;
            }
            catch (Exception ex)
            {
                executor.ModelSession.IntegrationFailure();
                if (Diagnostics.StoryModelSelfTest.Active) UnityEngine.Debug.Log("[StoryModelSelfTest] hook failure: " + ex);
                if (executor.ModelSession.TryRequestModelOnlyTermination()) TerminateModelOnly(behavior);
                return !executor.ModelSession.AllowLocalFallback;
            }
            finally { packet.BaseStream.Position = position; }
        }

        private static void TerminateModelOnly(GameBehavior behavior)
        {
            try { behavior.Game.Surrender(); } catch { }
        }

        private static bool IsDecision(GameMessage message)
        {
            int id = (int)message;
            return (id >= 10 && id <= 26 && id != 17) || id == 132 || (id >= 140 && id <= 143);
        }

        private static void ResetSelections(GameAI ai)
        {
            foreach (string name in new[] { "m_selector", "m_position", "m_attributes", "m_races" })
                (typeof(GameAI).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ai) as IList)?.Clear();
            foreach (string name in new[] { "m_selector_pointer", "m_option", "m_yesno" }) Set(ai, name, -1);
            foreach (string name in new[] { "m_materialSelectorHint", "m_place", "m_announce", "m_number" }) Set(ai, name, 0);
            Set(ai, "m_materialSelector", null);
        }
        private static void Set(GameAI ai, string field, object value) =>
            typeof(GameAI).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ai, value);

        internal static JObject Snapshot(WindBot.Game.Duel duel, Deck deck)
        {
            var players = new JArray();
            for (int player = 0; player < 2; player++)
            {
                var field = duel.Fields[player];
                players.Add(new JObject { ["controller"] = player, ["lifePoints"] = field.LifePoints,
                    ["deckCount"] = field.Deck?.Count ?? 0, ["handCount"] = field.Hand?.Count ?? 0,
                    ["extraCount"] = field.ExtraDeck?.Count ?? 0,
                    ["hand"] = Cards(field.Hand, player, false, player == 0),
                    ["monsters"] = Cards(field.MonsterZone, player), ["spells"] = Cards(field.SpellZone, player),
                    ["grave"] = Cards(field.Graveyard, player), ["banished"] = Cards(field.Banished, player),
                    ["extra"] = Cards(field.ExtraDeck, player, true, player == 0),
                    ["battling"] = Reference(field.BattlingMonster), ["underAttack"] = field.UnderAttack });
            }
            return new JObject { ["botWirePlayer"] = duel.IsFirst ? 0 : 1, ["turn"] = duel.Turn,
                ["turnPlayer"] = duel.Player, ["phase"] = duel.Phase.ToString(), ["players"] = players,
                // Composition only. It is sorted and never exposes the shuffled draw order.
                ["ownDeckComposition"] = new JArray((deck?.Cards ?? new YGOSharp.OCGWrapper.NamedCard[0])
                    .Concat(deck?.ExtraCards ?? new YGOSharp.OCGWrapper.NamedCard[0]).GroupBy(c => c.Id).OrderBy(g => g.Key)
                    .Select(g => new JObject { ["code"] = g.Key, ["count"] = g.Count() })),
                ["ownMainComposition"] = Composition(deck?.Cards),
                ["ownExtraComposition"] = Composition(deck?.ExtraCards),
                ["chain"] = new JArray(duel.CurrentChainInfo.Select(c => new JObject { ["code"] = c.ActivateId,
                    ["controller"] = c.ActivateController, ["player"] = c.ActivatePlayer,
                    ["location"] = (int)c.ActivateLocation, ["sequence"] = c.ActivateSequence, ["desc"] = c.ActivateDescription })),
                ["chainTargets"] = new JArray(duel.ChainTargets.Select(Reference)),
                ["lastSummonPlayer"] = duel.LastSummonPlayer,
                ["summoning"] = Cards(duel.SummoningCards, duel.LastSummonPlayer),
                ["solvingChainIndex"] = duel.SolvingChainIndex, ["negatedChains"] = new JArray(duel.NegatedChainIndexList) };
        }

        private static JArray Composition(System.Collections.Generic.IEnumerable<YGOSharp.OCGWrapper.NamedCard> cards) =>
            new JArray((cards ?? new YGOSharp.OCGWrapper.NamedCard[0]).GroupBy(c => c.Id).OrderBy(g => g.Key)
                .Select(g => new JObject { ["code"] = g.Key, ["count"] = g.Count() }));

        private static JArray Cards(System.Collections.Generic.IEnumerable<ClientCard> cards, int controller, bool publicZone = true, bool ownPrivate = false)
        {
            var result = new JArray();
            if (cards == null) return result;
            foreach (var card in cards)
            {
                if (card == null) { result.Add(JValue.CreateNull()); continue; }
                bool known = ownPrivate || publicZone && (controller == 0 || (card.Position & 5) != 0);
                var item = Reference(card);
                item["position"] = card.Position;
                if (known)
                {
                    item["code"] = card.Id; item["attack"] = card.Attack; item["defense"] = card.Defense;
                    item["level"] = card.Level; item["rank"] = card.Rank; item["type"] = card.Type;
                    item["attribute"] = card.Attribute; item["race"] = card.Race; item["disabled"] = card.Disabled;
                    item["link"] = card.LinkCount; item["linkMarkers"] = card.LinkMarker;
                    item["leftScale"] = card.LScale; item["rightScale"] = card.RScale;
                    item["overlays"] = new JArray(card.Overlays.Select(code => new JObject { ["code"] = code }));
                    item["equipTarget"] = Reference(card.EquipTarget); item["targets"] = new JArray(card.TargetCards.Select(Reference));
                }
                else item["unknown"] = true;
                result.Add(item);
            }
            return result;
        }
        private static JObject Reference(ClientCard card) => card == null ? null : new JObject
            { ["controller"] = card.Controller, ["location"] = (int)card.Location, ["sequence"] = card.Sequence };
    }
}
