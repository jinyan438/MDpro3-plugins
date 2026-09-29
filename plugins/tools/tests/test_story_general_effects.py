"""Semantic migration checks: changed identities/parameters and unsafe counterexamples."""
import re
import sqlite3
import sys
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_story_combo_effects import Reader, ROOT
from build_story_tactical_facts import extract, read_recipe, read_fusion


def search_script(cost='', extra='', condition='', target_filter='c:IsSetCard(0x43) and c:IsAbleToHand()'):
    return f'''local s,id=GetID()
function s.initial_effect(c)
 local e1=Effect.CreateEffect(c)
 e1:SetDescription(aux.Stringid(id,0))
 e1:SetType(EFFECT_TYPE_IGNITION)
 e1:SetRange(LOCATION_MZONE)
 e1:SetCategory(CATEGORY_SEARCH+CATEGORY_TOHAND)
 e1:SetCountLimit(1,id)
 e1:SetCondition(s.condition)
 e1:SetCost(s.cost)
 e1:SetTarget(s.target)
 e1:SetOperation(s.operation)
 c:RegisterEffect(e1)
end
function s.filter(c)
 return {target_filter}
end
function s.condition(e,tp,eg,ep,ev,re,r,rp)
 {condition}
end
function s.cost(e,tp,eg,ep,ev,re,r,rp,chk)
 {cost}
end
function s.target(e,tp,eg,ep,ev,re,r,rp,chk)
 if chk==0 then return Duel.IsExistingMatchingCard(s.filter,tp,LOCATION_DECK,0,1,nil) end
end
function s.operation(e,tp,eg,ep,ev,re,r,rp)
 local g=Duel.SelectMatchingCard(tp,s.filter,tp,LOCATION_DECK,0,1,1,nil)
 Duel.SendtoHand(g,nil,REASON_EFFECT)
 {extra}
end
'''.replace(' e1:SetCondition(s.condition)\n', '' if not condition else ' e1:SetCondition(s.condition)\n').replace(
    ' e1:SetCost(s.cost)\n', '' if not cost else ' e1:SetCost(s.cost)\n')


class GeneralEffectReaderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.archive=zipfile.ZipFile(ROOT.parent/'MDPro3/Data/script.zip')
        cls.constants={m[1]:int(m[2],0) for m in re.finditer(r'^(\w+)\s*=\s*(0x[\da-fA-F]+|\d+)\s*(?:--.*)?$',
            cls.archive.read('script/constant.lua').decode('utf-8-sig'), re.M)}
        with sqlite3.connect(f'file:{(ROOT.parent/"MDPro3/Data/locales/zh-CN/cards.cdb").as_posix()}?mode=ro',uri=True) as db:
            cls.types=dict(db.execute('select id,type from datas'))

    @classmethod
    def tearDownClass(cls): cls.archive.close()

    def reader(self, script, cid=90000111, card_type=33): return Reader(cid,script,self.constants,card_type)

    def real(self,cid,change=None):
        script=self.archive.read(f'script/c{cid}.lua').decode('utf-8-sig')
        return self.reader(change(script) if change else script,cid,self.types[cid])

    def test_anonymous_search_filter_and_shared_count(self):
        for cid in (91000111,91000123,92000222):
            for setcode in ('0x43','0xdf','0x13a'):
                p=self.reader(search_script(target_filter=f'c:IsSetCard({setcode}) and c:IsAbleToHand()'),cid).read()
                self.assertEqual(len(p),1)
                self.assertEqual(p[0]['KeyCode'],cid)
                self.assertIn(str(int(setcode,16)),p[0]['Filter'])
                self.assertEqual((p[0]['Kind'],p[0]['TargetFrom']),('Search',1))

    def test_real_resource_scripts_survive_identity_change(self):
        for cid in (9742784,77202120,35763582,50546208,38529357,26684111,90290572,92919429,6595475):
            old=self.real(cid); new_id=93000111
            clone=self.reader(old.script.replace(str(cid+1),str(new_id+1)).replace(str(cid),str(new_id)),new_id,self.types[cid])
            self.assertTrue(old.read(),cid)
            # Semantic flags/filters remain equal except actual script identities.
            import json
            old_data=json.dumps(old.read(),sort_keys=True).replace(str(cid*16+2),str(new_id*16+2)).replace(
                str(cid*16+1),str(new_id*16+1)).replace(str(cid+1),str(new_id+1)).replace(str(cid),str(new_id))
            self.assertEqual(old_data,json.dumps(clone.read(),sort_keys=True),cid)

    def test_empty_main_search_and_optional_draw_are_semantic(self):
        for cid in (93000101,93000102):
            old=self.real(63166095)
            clone=self.reader(old.script.replace('63166095',str(cid)),cid,2)
            p=clone.read()[0]
            self.assertTrue(p['EmptyMain'])
            self.assertEqual((p['BonusDraw'],p['DrawMinimum'],p['DrawGraveType']),(1,3,2))
            self.assertFalse(self.reader(clone.script.replace('GetSequence()<5','GetSequence()<4'),cid,2).read())
            self.assertFalse(self.reader(clone.script.replace('Duel.Draw(tp,1','Duel.Draw(1-tp,1'),cid,2).read())
            self.assertFalse(self.reader(clone.script.replace('Duel.Draw(tp,1,REASON_EFFECT)',
                'Duel.Draw(tp,1,REASON_EFFECT) Duel.SetLP(tp,100)'),cid,2).read())

    def test_delayed_search_and_summon_limit_survive_renaming(self):
        for cid in (93000201,93000202):
            old=self.real(90673288)
            script=old.script.replace('90673288',str(cid))
            clone=self.reader(script,cid,self.types[90673288])
            p=clone.read()[0]
            self.assertTrue(p['EndPhase'] and p['ArrivalTurnOnly'] and p['ExcludeGraveName'])
            self.assertEqual(extract(clone)['SummonOnceKey'],cid)
            self.assertFalse(self.reader(script.replace('EVENT_SPSUMMON_SUCCESS','EVENT_DAMAGE'),cid,self.types[90673288]).read())
            self.assertFalse(self.reader(script.replace('GetFlagEffect('+str(cid)+')~=0',
                'GetFlagEffect('+str(cid+1)+')~=0'),cid,self.types[90673288]).read())

    def test_arrival_flag_and_banish_cost_are_parameters(self):
        for old_id in (53251824,83236601,27780618,63184227):
            old=self.real(old_id); cid=93000301
            clone=self.reader(old.script.replace(str(old_id),str(cid)),cid,self.types[old_id])
            self.assertTrue(clone.read(),old_id)
        self.assertTrue(self.real(53251824).read()[0]['ArrivalTurnOnly'])
        self.assertFalse(self.real(53251824,lambda s:s.replace('SetLabel(53251824)','SetLabel(53251825)')).read())
        search=self.real(27780618).read()[-1]
        self.assertEqual((search['Cost'],search['CostFrom']),('BanishResource',16))
        for replacement in ('POS_FACEDOWN','REASON_EFFECT'):
            changed=self.real(27780618,lambda s:s.replace('POS_FACEUP' if replacement=='POS_FACEDOWN' else 'REASON_COST',replacement)).read()
            self.assertFalse(any(p.get('Cost')=='BanishResource' for p in changed))
        self.assertTrue(self.real(63184227).read()[0]['OwnTributeTrigger'])
        self.assertFalse(self.real(63184227,lambda s:s.replace('IsPreviousControler(tp)','IsPreviousControler(1-tp)')).read())

    def test_attribute_summon_lock_is_not_discarded(self):
        for attribute,mask in [('ATTRIBUTE_DARK',32),('ATTRIBUTE_LIGHT',16),('ATTRIBUTE_WATER',2)]:
            reader=self.real(87321742,lambda s:s.replace('ATTRIBUTE_DARK',attribute))
            self.assertEqual(reader.read()[0]['AllowedAttribute'],mask)
        self.assertFalse(self.real(87321742,lambda s:s.replace('return not c:IsAttribute(ATTRIBUTE_DARK)',
            'return not c:IsAttribute(ATTRIBUTE_DARK) or c:IsRace(RACE_DRAGON)')).read())

    def test_multiple_summon_locks_cannot_collapse_to_one(self):
        extra='''
 local other=Effect.CreateEffect(c)
 other:SetType(EFFECT_TYPE_FIELD)
 other:SetCode(EFFECT_CANNOT_SPECIAL_SUMMON)
 other:SetTargetRange(1,0)
 other:SetTarget(s.otherlimit)
 Duel.RegisterEffect(other,tp)
'''
        script=self.real(87321742).script.replace(':SetCode(EFFECT_CANNOT_SPECIAL_SUMMON)',
            ':SetCode(EFFECT_CANNOT_SPECIAL_SUMMON)'+extra)
        script+='\nfunction s.otherlimit(e,c) return c:IsRace(RACE_DRAGON) end\n'
        self.assertFalse(self.reader(script,93000321,self.types[87321742]).read())

    def test_life_cost_is_a_parameter(self):
        for amount in (300,700,1200):
            ps=self.real(77202120,lambda s:s.replace('700',str(amount))).read()
            self.assertEqual(ps[0]['Life'],amount)
            self.assertTrue(ps[0]['BodySynchroOnly'])

    def test_cost_and_target_filters_are_separate(self):
        p=self.reader(search_script(cost='Duel.DiscardHand(tp,Card.IsDiscardable,1,1,REASON_COST+REASON_DISCARD)',
            target_filter='c:IsRace(RACE_MACHINE) and c:IsLevelBelow(5) and c:IsAbleToHand()')).read()[0]
        self.assertEqual(p['Cost'],'Discard'); self.assertEqual(p['CostFrom'],2)
        self.assertIn('Race(c)',p['Filter']); self.assertNotIn('Race',p['CostFilter'])

    def test_additional_unaccounted_cost_is_rejected(self):
        base='Duel.DiscardHand(tp,Card.IsDiscardable,1,1,REASON_COST+REASON_DISCARD)'
        for added in ('Duel.PayLPCost(tp,2000)','Duel.Remove(unmodelled,POS_FACEUP,REASON_COST)',
                      'Duel.Release(e:GetHandler(),REASON_COST)'):
            self.assertEqual(self.reader(search_script(cost=base+'\n'+added)).read(),[])

    def test_cost_quantity_cannot_be_silently_reduced(self):
        for count in (2,3):
            self.assertEqual(self.reader(search_script(cost=f'Duel.DiscardHand(tp,Card.IsDiscardable,{count},{count},REASON_COST)')).read(),[])

    def test_unknown_condition_and_target_are_rejected(self):
        for script in (search_script(condition='return Duel.GetLP(tp)<2000'),
                       search_script(target_filter='s.unknown(c)'),
                       search_script().replace('SetOperation(s.operation)','SetOperation(aux.UnmodelledOperation)')):
            self.assertEqual(self.reader(script).read(),[])

    def test_no_extra_side_effect_is_projected_as_free(self):
        for extra in ('Duel.Destroy(e:GetHandler(),REASON_EFFECT)','Duel.Draw(1-tp,2,REASON_EFFECT)',
                      's.revive(e,tp)','Duel.PayLPCost(tp,1000)'):
            self.assertEqual(self.reader(search_script(extra=extra)).read(),[])

    def test_extra_summon_trigger_requires_complete_condition(self):
        text=self.real(90290572).script
        changed=text.replace('return e:GetHandler():IsSummonType(SUMMON_TYPE_LINK)',
                             'return e:GetHandler():IsSummonType(SUMMON_TYPE_LINK) and Duel.GetLP(tp)<2000')
        self.assertFalse(any(p.get('SummonTrigger') for p in self.reader(changed,90290572,self.types[90290572]).read()))

    def test_independent_soft_limits_receive_distinct_keys(self):
        text=search_script().replace('e1:SetCountLimit(1,id)','e1:SetCountLimit(1)').replace(' c:RegisterEffect(e1)',
            ' c:RegisterEffect(e1)\n local e2=e1:Clone()\n e2:SetDescription(aux.Stringid(id,1))\n c:RegisterEffect(e2)')
        ps=self.reader(text).read()
        self.assertEqual(len(ps),2); self.assertNotEqual(ps[0]['InstanceKey'],ps[1]['InstanceKey'])

    def test_script_reference_and_mode_values_are_extracted(self):
        self.assertEqual(extract(self.real(38529357))['ListedCodes'],[56433456])
        self.assertEqual(self.real(26684111).read()[0]['PreferredOption'],26684111*16+1)
        self.assertEqual(self.real(90290572).read()[0]['DeclineYesNo'],90290572*16+2)
        changed=self.real(26684111,lambda s:s.replace('aux.Stringid(26684111,1)','aux.Stringid(26684111,7)')).read()[0]
        self.assertEqual(changed['PreferredOption'],26684111*16+7)

    def test_return_must_reach_hand_is_semantic(self):
        self.assertTrue(self.real(50546208).read()[0]['ReturnNeedsHand'])
        self.assertFalse(self.real(81196066).read()[-1].get('ReturnNeedsHand',False))

    def test_stat_based_negation_is_parameterized(self):
        for amount in (500,800,1000):
            facts=extract(self.real(4280258,lambda s:s.replace('800',str(amount))))
            self.assertEqual(facts['MaterialAttack'],amount)
            self.assertEqual(next(f['AttackCost'] for f in facts['Effects'] if f['Purpose']=='Negate'),amount)

    def test_unknown_removal_filter_never_becomes_any_target(self):
        r=self.real(12580477,lambda s:s.replace('aux.TRUE','s.unknown'))
        self.assertFalse(extract(r).get('Effects'))

    def test_tactical_cost_retains_quantity_and_filter(self):
        f=extract(self.real(8267140))['Effects'][0]
        self.assertEqual(f['Life'],1000); self.assertEqual(f['Hint'],'Remove')
        f=extract(self.real(90290572))['Effects'][0]
        self.assertEqual(f['CostLocations'],4); self.assertIn('Race(c)',f['CostFilter'])
        r=self.real(8267140,lambda s:s.replace('Duel.PayLPCost(tp,1000)','Duel.PayLPCost(tp,1000)\nDuel.DiscardHand(tp,aux.TRUE,1,1,REASON_COST)'))
        self.assertFalse(extract(r).get('Effects'))

    def test_removal_options_use_descriptor_not_position(self):
        facts=extract(self.real(14532163))['Effects'][0]
        self.assertEqual([f['Option'] for f in facts['Modes']],[14532163*16,14532163*16+1])
        self.assertEqual(facts['Modes'][0]['TargetFilter'],'c.IsAttack()')

    def test_recipe_and_fusion_not_bound_to_identity(self):
        for cid in (2857636,86066372,24094653,87931906,47705572):
            r=self.real(cid); renamed=self.reader(r.script.replace(str(cid),'93000111'),93000111,r.card_type)
            self.assertEqual(read_recipe(r),read_recipe(renamed),cid)
            self.assertEqual(read_fusion(r),read_fusion(renamed),cid)


if __name__=='__main__': unittest.main()
