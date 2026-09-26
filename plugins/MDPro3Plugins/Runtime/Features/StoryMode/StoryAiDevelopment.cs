using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using WindBot.Game;
using WindBot.Game.AI;
using YGOSharp.OCGWrapper.Enums;

namespace MDPro3.Plugins.Features.StoryMode
{
    internal sealed partial class StoryAiEvaluation
    {
        // A bounded forward model, not a second duel core. Only the FIRST action comes
        // from the core's current legal list. Every response starts from the new live state.
        private const int DevelopmentDepth = 5, DevelopmentWidth = 40, DevelopmentChecks = 45000, DevelopmentNodes = 5000;
        private sealed class Body
        {
            internal ClientCard Card;
            internal int Attack, Zone;
            internal bool Fresh, FromExtra, EffectsBlocked;
        }
        private sealed class DevelopmentState
        {
            internal List<Body> Board;
            internal List<ClientCard> Extra, Enemy, Reserve;
            internal HashSet<int> Credited;
            internal bool HalqUsed;
            internal ClientCard PendingHalq, Addition;
            internal ExtraPlan First;
            internal float Credit, Score;
            internal int Depth, Life;
        }
        private sealed class SearchBudget
        {
            internal int Checks, Nodes;
            internal bool Exhausted => Checks >= DevelopmentChecks || Nodes >= DevelopmentNodes;
        }
        private string developmentCacheKey;
        private ExtraPlan developmentCache;
        private readonly HashSet<int> usedDevelopmentEffects = new HashSet<int>();
        private readonly string developmentDeckFile;
        private List<int> developmentDeckList;
        private bool developmentDeckLoaded;
        internal int LastDevelopmentNodes { get; private set; }

        private IEnumerable<ClientCard> KnownDevelopmentDeck()
        {
            var known = Bot.Deck.Where(c => c.Id != 0).ToList();
            if (known.Count == Bot.Deck.Count || string.IsNullOrEmpty(developmentDeckFile)) return known;
            if (!developmentDeckLoaded)
            {
                developmentDeckLoaded = true;
                // Own submitted deck composition, never opponent information or deck order.
                developmentDeckList = WindBot.Game.Deck.Load(developmentDeckFile)?.Cards.Select(c => c.Id).ToList();
            }
            if (developmentDeckList == null) return known;
            var outside = Bot.Hand.Concat(Bot.GetMonsters()).Concat(Bot.GetSpells()).Concat(Bot.Graveyard).Concat(Bot.Banished)
                .Concat(Enemy.GetMonsters().Where(c => c.Owner == 0)).Distinct().ToList();
            if (outside.Any(c => c.Id == 0)) return known;
            var remaining = developmentDeckList.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
            foreach (int id in outside.Where(c => c.Owner == 0).Select(c => c.Id).Concat(outside.SelectMany(c => c.Overlays)))
                if (remaining.ContainsKey(id)) remaining[id]--;
            if (remaining.Values.Any(i => i < 0) || remaining.Values.Sum() != Bot.Deck.Count) return known;
            return remaining.OrderBy(p => p.Key).SelectMany(p => Enumerable.Range(0, p.Value)
                .Select(_ => new ClientCard(p.Key, CardLocation.Deck, -1) { Controller = 0 })).ToList();
        }

        internal void ResetDevelopmentTurn() { usedDevelopmentEffects.Clear(); developmentCacheKey = null; }
        internal void NoteDevelopmentEffect(int description)
        {
            if (description == 50588353 * 16 || description == 90290572 * 16 || description == 48589580 * 16 ||
                description == 38342335 * 16 || description == 2857636 * 16) usedDevelopmentEffects.Add(description / 16);
            developmentCacheKey = null;
        }

        private DevelopmentState InitialDevelopment(IEnumerable<ClientCard> roots)
        {
            return new DevelopmentState
            {
                Board = Bot.MonsterZone.Select((c, i) => c == null ? null : new Body
                    { Card = c, Attack = Attack(c), Zone = i, FromExtra = c.LastLocation == CardLocation.Extra }).Where(b => b != null).ToList(),
                Extra = Bot.ExtraDeck.Concat(roots).Where(c => c.Id != 0 && c.Data != null).Distinct().OrderBy(c => c.Id).ToList(),
                Enemy = Enemy.GetMonsters(),
                Reserve = Bot.Hand.Concat(KnownDevelopmentDeck()).Where(c => c.Id != 0 && c.Data != null).ToList(),
                Credited = new HashSet<int>(usedDevelopmentEffects), HalqUsed = usedDevelopmentEffects.Contains(50588353), Life = Bot.LifePoints
            };
        }

        private DevelopmentState CopyDevelopment(DevelopmentState state)
        {
            return new DevelopmentState
            {
                Board = new List<Body>(state.Board), Extra = new List<ClientCard>(state.Extra), Enemy = new List<ClientCard>(state.Enemy),
                Reserve = new List<ClientCard>(state.Reserve), Credited = new HashSet<int>(state.Credited),
                HalqUsed = state.HalqUsed, PendingHalq = state.PendingHalq, First = state.First, Addition = state.Addition,
                Credit = state.Credit, Depth = state.Depth, Life = state.Life
            };
        }

        private float TerminalValue(DevelopmentState state)
        {
            float value = state.Board.Sum(b => BodyValue(b)) + state.Credit;
            // Do not count a shared once-per-turn negate twice merely by making two copies.
            foreach (var group in state.Board.Where(b => !b.EffectsBlocked && LiveInteraction(b.Card)).GroupBy(b => b.Card.Id))
                if (group.Count() > 1 && (group.Key == 84815190 || group.Key == 27548199 || group.Key == 65741786 || group.Key == 63101468))
                    value -= (group.Count() - 1) * 1600;
            foreach (var body in state.Board.Where(b => b.Fresh && b.Card.Id == 27548199))
                if (!Bot.Graveyard.Any(c => Has(c, CardType.Link)) && !state.Credited.Contains(-27548199)) value -= 1900;
            value += Enemy.GetMonsters().Where(c => !state.Enemy.Contains(c)).Sum(BoardScore);
            if (duel.Player == 0 && duel.Phase == DuelPhase.Main1 && duel.Turn > 1 && state.Enemy.Count == 0 &&
                state.Board.Where(b => b.Fresh || b.Card.IsAttack() && !b.Card.Attacked).Sum(b => b.Attack) >= Enemy.LifePoints)
                value += 6000;
            // Equivalent end boards prefer the shorter route with fewer exposure windows.
            return value - state.Depth * 45;
        }

        private float BodyValue(Body body) => BoardValue(body.Card, body.Attack) - (body.EffectsBlocked && LiveInteraction(body.Card) ? 1900 : 0);
        private bool ProtectedDevelopmentBody(Body body) => !body.EffectsBlocked && !body.Card.IsDisabled() &&
            (LiveInteraction(body.Card) || Has(body.Card, CardType.Link) && LinkRating(body.Card) >= 3 ||
             Has(body.Card, CardType.Synchro | CardType.Fusion) && Level(body.Card) >= 7 || Has(body.Card, CardType.Xyz) && body.Attack >= 2500);

        private static int Markers(ClientCard c) => c.LinkMarker != 0 ? c.LinkMarker : c.Data?.Defense ?? 0;
        private static int LinkedMainZones(IEnumerable<Body> board)
        {
            int result = 0;
            foreach (var body in board.Where(b => Has(b.Card, CardType.Link)))
            {
                int marker = Markers(body.Card), zone = body.Zone;
                if (zone >= 5)
                {
                    int center = zone == 5 ? 1 : 3;
                    if ((marker & 1) != 0) result |= 1 << (center - 1);
                    if ((marker & 2) != 0) result |= 1 << center;
                    if ((marker & 4) != 0) result |= 1 << (center + 1);
                }
                else
                {
                    if ((marker & 8) != 0 && zone > 0) result |= 1 << (zone - 1);
                    if ((marker & 32) != 0 && zone < 4) result |= 1 << (zone + 1);
                }
            }
            return result;
        }

        private int DevelopmentPlace(DevelopmentState state, ClientCard card, bool extra, bool coreOffered = false)
        {
            int occupied = state.Board.Aggregate(0, (mask, b) => mask | 1 << b.Zone);
            int main = (~occupied) & 31, linked = LinkedMainZones(state.Board);
            if (extra && Has(card, CardType.Link))
            {
                if ((occupied & 96) == 0)
                {
                    bool blocked5 = Enemy.MonsterZone[6] != null && state.Enemy.Contains(Enemy.MonsterZone[6]);
                    bool blocked6 = Enemy.MonsterZone[5] != null && state.Enemy.Contains(Enemy.MonsterZone[5]);
                    if (!blocked5) return 5;
                    if (!blocked6) return 6;
                }
                main &= linked;
                // The live core may know an opposing arrow / special permission omitted by
                // this conservative model. This exception applies ONLY to an offered root.
                if (main == 0 && coreOffered) main = (~occupied) & 31;
            }
            else if ((main & ~linked) != 0) main &= ~linked; // keep arrow destinations free
            for (int i = 0; i < 5; i++) if ((main & (1 << i)) != 0) return i;
            return -1;
        }

        private IEnumerable<DevelopmentState> ExtraSuccessors(DevelopmentState state, IEnumerable<ClientCard> destinations, SearchBudget budget, bool root)
        {
            foreach (var destination in destinations.OrderBy(c => c.Id))
            {
                if (budget.Exhausted) yield break;
                var recipe = MaterialRecipe(destination);
                if (recipe == null || !state.Extra.Contains(destination)) continue;
                var own = state.Board.Where(b => b.Fresh || b.Card.IsFaceup()).ToList();
                var pool = own.Select(b => b.Card).ToList();
                if (recipe.EnemyOne) pool.AddRange(state.Enemy.Where(c => c.IsFaceup()));
                if (pool.Count > 14) continue;
                for (int mask = 1; mask < (1 << pool.Count); mask++)
                {
                    int enemyMask = mask >> own.Count;
                    if ((enemyMask & (enemyMask - 1)) != 0) continue;
                    if (budget.Exhausted) yield break;
                    budget.Checks++;
                    var materials = pool.Where((c, i) => (mask & (1 << i)) != 0).ToList();
                    if (!ValidMaterials(destination, recipe, materials, c => own.Any(b => b.Card == c && b.FromExtra))) continue;
                    int attack = Attack(destination);
                    if (destination.Id == 4280258) attack = materials.Count * 800;
                    if (destination.Id == 86066372) attack += materials.Max(LinkRating) * 1000;
                    float bodyValue = BoardValue(destination, attack), removed = materials.Where(c => c.Controller == 1).Sum(BoardScore);
                    bool lethal = duel.Player == 0 && duel.Turn > 1 && duel.Phase == DuelPhase.Main1 && state.Enemy.Count == 0 &&
                        attack + state.Board.Where(b => !materials.Contains(b.Card) && (b.Fresh || b.Card.IsAttack() && !b.Card.Attacked)).Sum(b => b.Attack) >= Enemy.LifePoints;
                    // Previously established bosses keep their protection. New bridge bodies
                    // may be converted only because a complete, higher-scoring route exists.
                    if (own.Any(b => materials.Contains(b.Card) && ProtectedDevelopmentBody(b) &&
                        (bodyValue + removed < BodyValue(b) + 250 || LiveInteraction(b.Card) && !LiveInteraction(destination) &&
                         state.Enemy.Count == 0 && Enemy.GetSpellCount() == 0 && !lethal))) continue;
                    var next = CopyDevelopment(state);
                    next.Board.RemoveAll(b => materials.Contains(b.Card)); next.Enemy.RemoveAll(materials.Contains);
                    int zone = DevelopmentPlace(next, destination, true, root);
                    if (zone < 0) continue;
                    next.Board.Add(new Body { Card = destination, Attack = attack, Zone = zone, Fresh = true, FromExtra = true });
                    next.Extra.Remove(destination); next.Depth++;
                    if (!Has(destination, CardType.Xyz) && materials.Any(c => c.Controller == 0 && Has(c, CardType.Link))) next.Credited.Add(-27548199);
                    if (destination.Id == 50588353 && !next.HalqUsed) next.PendingHalq = destination;
                    if (next.Credited.Add(destination.Id) && destination.Id != 50588353)
                        next.Credit += ImmediatePayoff(destination) * .7f;
                    if (next.First == null && next.Addition == null)
                        next.First = new ExtraPlan { Destination = destination, Materials = materials, Zone = zone,
                            Hint = Has(destination, CardType.Link) ? HintMsg.LinkMaterial : Has(destination, CardType.Xyz) ? HintMsg.XyzMaterial : HintMsg.SynchroMaterial };
                    next.Score = TerminalValue(next); budget.Nodes++;
                    yield return next;
                }
            }
        }

        private IEnumerable<DevelopmentState> EffectSuccessors(DevelopmentState state, SearchBudget budget)
        {
            if (state.PendingHalq != null)
            {
                var choices = state.Reserve.Where(c => Has(c, CardType.Tuner) && Level(c) <= 3 && !Has(c, CardType.SpSummon))
                    .GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id).ToList();
                foreach (var card in choices)
                {
                    int zone = DevelopmentPlace(state, card, false);
                    if (zone < 0 || budget.Exhausted) break;
                    var next = CopyDevelopment(state); next.PendingHalq = null; next.HalqUsed = true;
                    next.Reserve.Remove(card); next.Board.Add(new Body { Card = card, Attack = Attack(card), Zone = zone, Fresh = true, EffectsBlocked = true });
                    next.Score = TerminalValue(next); budget.Nodes++; yield return next;
                }
                // Declining an optional summon is a real branch, not an invented resource.
                var decline = CopyDevelopment(state); decline.PendingHalq = null;
                decline.Score = TerminalValue(decline); yield return decline;
                yield break;
            }
            bool venus = state.Board.Any(b => b.Card.Id == 64734921 && !b.Card.IsDisabled() && !b.EffectsBlocked &&
                (b.Fresh || state.Depth > 0 || duel.MainPhase != null && duel.MainPhase.ActivableCards.Contains(b.Card)));
            var ball = state.Reserve.FirstOrDefault(c => c.Id == 39552864);
            if (venus && ball != null && state.Life > 500 && !budget.Exhausted)
            {
                int zone = DevelopmentPlace(state, ball, false);
                if (zone >= 0)
                {
                    var next = CopyDevelopment(state); next.Reserve.Remove(ball); next.Life -= 500; next.Depth++;
                    next.Credit -= 100; next.Board.Add(new Body { Card = ball, Attack = Attack(ball), Zone = zone, Fresh = true });
                    next.Score = TerminalValue(next); budget.Nodes++; yield return next;
                }
            }
        }

        private static string DevelopmentStateKey(DevelopmentState state)
        {
            Func<ClientCard, int> key = RuntimeHelpers.GetHashCode;
            return string.Join(",", state.Board.OrderBy(b => key(b.Card)).Select(b => key(b.Card) + ":" + b.Attack + ":" + b.Zone + ":" + b.EffectsBlocked)) + "/" +
                string.Join(",", state.Extra.Select(key).OrderBy(i => i)) + "/" + string.Join(",", state.Reserve.Select(key).OrderBy(i => i)) + "/" +
                string.Join(",", state.Enemy.Select(key).OrderBy(i => i)) + "/" + string.Join(",", state.Credited.OrderBy(i => i)) + "/" + state.HalqUsed + "/" +
                (state.PendingHalq == null ? 0 : key(state.PendingHalq)) + "/" + state.Life + "/" + (state.Addition == null ? 0 : key(state.Addition));
        }

        private void SearchDevelopment(List<DevelopmentState> frontier, SearchBudget budget, Action<DevelopmentState> visit)
        {
            var visited = new Dictionary<string, float>();
            for (int depth = 0; depth < DevelopmentDepth && frontier.Count > 0; depth++)
            {
                var next = new List<DevelopmentState>();
                int width = depth == 0 && frontier.All(s => s.Addition != null) ? 64 : DevelopmentWidth;
                foreach (var state in frontier.OrderByDescending(s => s.Score).Take(width))
                {
                    visit(state);
                    if (budget.Exhausted || state.Depth >= DevelopmentDepth) continue;
                    var successors = EffectSuccessors(state, budget);
                    if (state.PendingHalq == null) successors = successors.Concat(ExtraSuccessors(state, state.Extra, budget, false));
                    foreach (var child in successors)
                    {
                        visit(child);
                        var key = DevelopmentStateKey(child);
                        if (visited.TryGetValue(key, out float score) && score >= child.Score) continue;
                        visited[key] = child.Score;
                        next.Add(child);
                    }
                }
                frontier = next.OrderByDescending(s => s.Score).Take(DevelopmentWidth).ToList();
                if (budget.Exhausted) break;
            }
            LastDevelopmentNodes = budget.Nodes;
        }

        private string DevelopmentFingerprint(IList<ClientCard> roots)
        {
            var text = new StringBuilder().Append(duel.Turn).Append('/').Append(duel.Player).Append('/').Append((int)duel.Phase)
                .Append('/').Append(Bot.LifePoints).Append('/').Append(Enemy.LifePoints).Append('/').Append(Bot.Deck.Count);
            foreach (var c in Bot.GetMonsters().Concat(Enemy.GetMonsters()).Concat(Enemy.GetSpells()).Concat(Bot.Hand)
                .Concat(Bot.Graveyard).Concat(Bot.Banished).Concat(Bot.ExtraDeck).Concat(Bot.Deck.Where(c => c.Id != 0)))
                text.Append('|').Append(RuntimeHelpers.GetHashCode(c)).Append(':').Append(c.Id).Append(':').Append((int)c.Location)
                    .Append(':').Append(c.Controller).Append(':').Append(c.Owner).Append(':').Append(c.Sequence).Append(':').Append(c.Position).Append(':').Append(c.Type).Append(':').Append(c.Level)
                    .Append(':').Append(c.Race).Append(':').Append(c.Attack).Append(':').Append(c.Defense).Append(':').Append(c.Disabled)
                    .Append(':').Append(c.Attacked).Append(':').Append((int)c.LastLocation).Append(':').Append(c.LinkMarker);
            text.Append(" roots ");
            foreach (var c in roots.OrderBy(c => c.Id)) text.Append(RuntimeHelpers.GetHashCode(c)).Append(',');
            text.Append(" effects ").Append(string.Join(",", usedDevelopmentEffects.OrderBy(i => i)));
            if (duel.MainPhase != null) foreach (var c in duel.MainPhase.ActivableCards) text.Append(RuntimeHelpers.GetHashCode(c)).Append(',');
            return text.ToString();
        }

        internal ExtraPlan PlanDevelopment(IList<ClientCard> offered)
        {
            var roots = offered.Where(c => c.Location == CardLocation.Extra).Distinct().OrderBy(c => c.Id).ToList();
            var key = DevelopmentFingerprint(roots);
            if (key == developmentCacheKey) return developmentCache;
            var initial = InitialDevelopment(roots); float baseline = TerminalValue(initial), best = baseline + 100;
            var budget = new SearchBudget(); ExtraPlan result = null;
            var seeds = ExtraSuccessors(initial, roots, budget, true).ToList();
            Action<DevelopmentState> consider = state =>
            {
                if (state.First != null && state.Score > best + .01f)
                {
                    best = state.Score; result = state.First;
                    result.Gain = best - baseline;
                }
            };
            foreach (var seed in seeds) consider(seed);
            SearchDevelopment(seeds, budget, consider);
            developmentCacheKey = key; developmentCache = result;
            return result;
        }

        internal Dictionary<ClientCard, float> DevelopmentBonuses(IList<ClientCard> offered)
        {
            var result = offered.Distinct().ToDictionary(c => c, _ => 0f);
            if (offered.Count == 0 || Bot.GetMonsterCount() >= 6 || Bot.ExtraDeck.Count == 0) return result;
            var initial = InitialDevelopment(new ClientCard[0]);
            var bases = new Dictionary<ClientCard, float>(); var seeds = new List<DevelopmentState>();
            foreach (var card in offered.Distinct().OrderBy(c => c.Id))
            {
                if (!Has(card, CardType.Monster) || card.Controller != 0 || card.Location == CardLocation.MonsterZone) continue;
                int zone = DevelopmentPlace(initial, card, card.Location == CardLocation.Extra);
                if (zone < 0) continue;
                var seed = CopyDevelopment(initial); seed.Addition = card; seed.Reserve.Remove(card); seed.Extra.Remove(card);
                seed.Board.Add(new Body { Card = card, Attack = Attack(card), Zone = zone, Fresh = true, FromExtra = card.Location == CardLocation.Extra });
                seed.Score = TerminalValue(seed); bases[card] = seed.Score; seeds.Add(seed);
            }
            SearchDevelopment(seeds, new SearchBudget(), state =>
            {
                if (state.Addition != null) result[state.Addition] = Math.Max(result[state.Addition], state.Score - bases[state.Addition]);
            });
            return result;
        }
    }
}
