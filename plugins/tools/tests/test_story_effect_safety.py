"""Counterexamples for the conservative Lua effect-identity reader."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_story_effect_safety import read_effects


def script(operation='Duel.Destroy(g,REASON_EFFECT)', cost='', target=None, category='CATEGORY_DESTROY'):
    return f'''local s,id=GetID()
function s.initial_effect(c)
 local e1=Effect.CreateEffect(c)
 e1:SetDescription(aux.Stringid(id,0))
 e1:SetType(EFFECT_TYPE_IGNITION)
 e1:SetCategory({category})
 e1:SetProperty(EFFECT_FLAG_CARD_TARGET)
 e1:SetTarget(s.target)
 e1:SetCost(s.cost)
 e1:SetOperation(s.operation)
 c:RegisterEffect(e1)
end
function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
 {target or 'Duel.SelectTarget(tp,aux.TRUE,tp,LOCATION_ONFIELD,LOCATION_ONFIELD,1,1,nil)'}
end
function s.cost(e,tp,eg,ep,ev,re,r,rp,chk)
 {cost}
end
function s.operation(e,tp,eg,ep,ev,re,r,rp)
 {operation}
end
'''


class EffectSafetyReaderTests(unittest.TestCase):
    def test_simple_field_removal(self):
        self.assertEqual(read_effects(123456, script()), [(123456 * 16, ('Destroy', 12, 1, 0))])

    def test_cost_never_becomes_effect(self):
        self.assertEqual(read_effects(123456, script(operation='Duel.Draw(tp,1,REASON_EFFECT)',
            cost='Duel.Destroy(e:GetHandler(),REASON_COST)', category='CATEGORY_DRAW')), [])

    def test_mixed_payoff_not_assumed_harmful(self):
        for operation in ('Duel.Destroy(g,REASON_EFFECT)\nDuel.SpecialSummon(tc,0,tp,tp,false,false,POS_FACEUP)',
                          'Duel.Destroy(g,REASON_EFFECT)\nDuel.Draw(tp,1,REASON_EFFECT)',
                          'Duel.Destroy(g,REASON_EFFECT)\nDuel.Destroy(e:GetHandler(),REASON_EFFECT)'):
            self.assertEqual(read_effects(123456, script(operation=operation)), [])

    def test_indirect_payoff_is_unknown(self):
        text = script(operation='Duel.Destroy(g,REASON_EFFECT)\ns.revive(e,tp)')
        text += '\nfunction s.revive(e,tp)\nDuel.SpecialSummon(tc,0,tp,tp,false,false,POS_FACEUP)\nend'
        self.assertEqual(read_effects(123456, text), [])

    def test_unknown_target_restriction(self):
        text = script(target='Duel.SelectTarget(tp,s.filter,tp,LOCATION_MZONE,LOCATION_MZONE,1,1,nil)')
        text += '\nfunction s.filter(c)\nreturn c:IsAttribute(ATTRIBUTE_DARK)\nend'
        self.assertEqual(read_effects(123456, text), [])

    def test_two_step_target_not_inferred(self):
        selection = 'Duel.SelectTarget(tp,aux.TRUE,tp,LOCATION_MZONE,LOCATION_MZONE,1,1,nil)'
        self.assertEqual(read_effects(123456, script(target=selection + '\n' + selection)), [])

    def test_own_only_engine_keeps_existing_planner(self):
        self.assertEqual(read_effects(123456, script(target='Duel.SelectTarget(tp,aux.TRUE,tp,LOCATION_MZONE,0,1,1,nil)')), [])

    def test_missing_description_is_not_number_zero(self):
        self.assertEqual(read_effects(123456, script().replace(' e1:SetDescription(aux.Stringid(id,0))\n', '')), [])

    def test_shared_description_is_ambiguous(self):
        text = script().replace(' c:RegisterEffect(e1)', ' c:RegisterEffect(e1)\n local e2=e1:Clone()\n e2:SetOperation(s.other)\n c:RegisterEffect(e2)')
        self.assertEqual(read_effects(123456, text), [])

    def test_chain_negation_comes_from_operation(self):
        text = script(operation='Duel.NegateEffect(ev)').replace(' e1:SetType(EFFECT_TYPE_IGNITION)',
            ' e1:SetType(EFFECT_TYPE_QUICK_O)\n e1:SetCode(EVENT_CHAINING)')
        self.assertEqual(read_effects(123456, text), [(123456 * 16, ('Negate', 0, 0, 0))])

    def test_comments_do_not_create_negation(self):
        text = script(operation='-- Duel.NegateEffect(ev)\nDuel.Draw(tp,1,REASON_EFFECT)').replace(
            ' e1:SetType(EFFECT_TYPE_IGNITION)', ' e1:SetType(EFFECT_TYPE_QUICK_O)\n e1:SetCode(EVENT_CHAINING)')
        self.assertEqual(read_effects(123456, text), [])

    def test_discard_cost_is_retained_separately(self):
        for predicate in ('nil', 'aux.TRUE', 'Card.IsDiscardable'):
            text = script(cost=f'Duel.DiscardHand(tp,{predicate},1,1,REASON_COST+REASON_DISCARD)')
            self.assertEqual(read_effects(123456, text), [(123456 * 16, ('Destroy', 12, 1, 32))])

    def test_temporary_removal_requires_exchange_planning(self):
        self.assertEqual(read_effects(123456, script(category='CATEGORY_REMOVE',
            operation='Duel.Remove(g,POS_FACEUP,REASON_EFFECT+REASON_TEMPORARY)')), [])

    def test_nonzero_description_is_preserved(self):
        text = script().replace('aux.Stringid(id,0)', 'aux.Stringid(id,7)')
        self.assertEqual(read_effects(123456, text), [(123456 * 16 + 7, ('Destroy', 12, 1, 0))])


if __name__ == '__main__':
    unittest.main()
