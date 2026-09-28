"""Structural extraction tests. All temporary outputs remain under plugins."""
import hashlib
import json
from pathlib import Path
import re
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_story_level_effects as build

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT.parent / 'MDPro3/Data/script.zip'
DB = ROOT.parent / 'MDPro3/Data/locales/zh-CN/cards.cdb'


class LevelExtraction(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.archive = zipfile.ZipFile(SCRIPTS)
        cls.constants = {m[1]: int(m[2], 0) for m in re.finditer(
            r'^(\w+)\s*=\s*(0x[0-9a-fA-F]+|\d+)\s*(?:--.*)?$',
            cls.archive.read('script/constant.lua').decode('utf-8-sig'), re.M)}

    @classmethod
    def tearDownClass(cls):
        cls.archive.close()

    def script(self, cid):
        return build.clean(self.archive.read(f'script/c{cid}.lua').decode('utf-8-sig'))

    def profiles(self, cid, text=None, card_type=33):
        return build.read_effects(cid, self.script(cid) if text is None else text, self.constants, card_type)[0]

    def test_relocated_ids_and_changed_levels(self):
        for old, new in [(98555327, 12345001), (85747929, 23456002), (83334932, 34567003),
                         (29092121, 45678004), (11234702, 56789005), (60283232, 67890006)]:
            with self.subTest(old=old):
                original = self.profiles(old)[0]
                altered = self.script(old).replace(str(old), str(new))
                # A literal transform is also free of the original card's numbers.
                if old == 98555327: altered = altered.replace('SetValue(4)', 'SetValue(7)')
                if old == 83334932: altered = altered.replace('SetValue(2)', 'SetValue(3)')
                if old == 85747929:
                    altered = altered.replace('==4 then lv=8', '==3 then lv=7').replace('==8 then lv=4', '==7 then lv=3').replace('tp,4,8)', 'tp,3,7)')
                profile = self.profiles(new, altered)[0]
                self.assertEqual(profile['source'], new)
                self.assertEqual(profile['scope'], original['scope'])
                self.assertEqual(profile['description'] // 16, new)
                if old == 98555327: self.assertEqual(profile['values'], [7])
                if old == 83334932: self.assertEqual(profile['values'], [3])
                if old == 85747929: self.assertEqual(profile['forced'], [3, 7, 7, 3])

    def test_reject_secondary_operations_and_unknown_guards(self):
        text = self.script(98555327)
        marker = 'c:RegisterEffect(e1)'
        # Inject into the operation, not the initializer.
        pos = text.rfind(marker)
        for statement in ['Duel.Draw(tp,1,REASON_EFFECT)', 'Duel.SpecialSummon(c,0,tp,tp,false,false,POS_FACEUP)',
                          'Duel.PayLPCost(tp,500)', 's.sideeffect(e)', 'c:SetMaterial(g)',
                          'if unknown_flag then return end', 'if c:IsFaceup() then return end',
                          'if c:GetLevel()>4 then return end',
                          'e1:SetCode(EFFECT_UPDATE_ATTACK)', 'e1:SetCode(EFFECT_CANNOT_SPECIAL_SUMMON)']:
            with self.subTest(statement=statement):
                self.assertEqual(self.profiles(98555327, text[:pos] + statement + '\n' + text[pos:]), [])

    def test_reject_unknown_costs_targets_and_filters(self):
        text = self.script(83334932)
        for altered in [text.replace('e2:SetOperation(c83334932.lvop)', 'e2:SetCost(c83334932.cost)\n e2:SetOperation(c83334932.lvop)'),
                        text.replace('c:IsRace(RACE_MACHINE)', 'c:IsRace(RACE_MACHINE) and c:IsCanBeFusionMaterial()'),
                        text.replace('c:IsRace(RACE_MACHINE)', 'c:IsRace(RACE_MACHINE) and c:IsRace(RACE_WARRIOR)'),
                        text.replace('Duel.SelectTarget(tp,c83334932.lvfilter', 's.sideeffect(e)\n Duel.SelectTarget(tp,c83334932.lvfilter'),
                        text.replace('Duel.IsExistingTarget(c83334932.lvfilter,tp,LOCATION_MZONE,0,1,nil) end',
                                     'Duel.IsExistingTarget(c83334932.lvfilter,tp,LOCATION_MZONE,0,1,nil) and c:GetLevel()>3 end'),
                        text.replace('e2:SetType(EFFECT_TYPE_IGNITION)', 'e2:SetType(EFFECT_TYPE_TRIGGER_F)')]:
            with self.subTest(altered=altered[-70:]):
                self.assertEqual(self.profiles(83334932, altered), [])

    def test_exact_multiplicity_and_costs(self):
        self.assertEqual((self.profiles(29092121)[0]['minimum'], self.profiles(29092121)[0]['maximum']), (2, 2))
        self.assertEqual((self.profiles(8129306)[0]['minimum'], self.profiles(8129306)[0]['maximum']), (1, 2))
        wheel = self.profiles(60283232)[0]
        self.assertEqual(wheel['numbers'], [1, 2, 3, 4])
        self.assertEqual(wheel['values'], [-1, -2, -3, -4])
        self.assertEqual(wheel['cost'], 'BanishSelf')
        self.assertEqual(wheel['limit'], '60283233')
        self.assertEqual(self.profiles(74741494, card_type=2)[0]['scope'], 'All')
        self.assertTrue(self.profiles(74741494, card_type=2)[0]['consume'])
        text = self.script(98555327).replace('e1:SetOperation(c98555327.lvop)', 'e1:SetCost(s.lpcost)\n e1:SetOperation(c98555327.lvop)')
        text += '\nfunction s.lpcost(e,tp,eg,ep,ev,re,r,rp,chk)\n if chk==0 then return Duel.CheckLPCost(tp,500) end\n Duel.PayLPCost(tp,500)\nend\n'
        self.assertEqual((self.profiles(98555327, text)[0]['cost'], self.profiles(98555327, text)[0]['life']), ('Life', 500))

    def test_prompt_ambiguity(self):
        text = self.script(85747929)
        self.assertTrue(self.profiles(85747929)[0]['genericTrigger'])
        duplicate = '\n local e4=e3:Clone()\n e4:SetDescription(aux.Stringid(85747929,2))\n e4:SetOperation(c85747929.thop)\n c:RegisterEffect(e4)\n'
        altered = text.replace('c:RegisterEffect(e3)', 'c:RegisterEffect(e3)' + duplicate)
        self.assertFalse(self.profiles(85747929, altered)[0]['genericTrigger'])
        self.assertEqual(self.profiles(85747929, altered.replace('aux.Stringid(85747929,2)', 'aux.Stringid(85747929,1)')), [])

    def test_counts_and_conditions_are_not_invented(self):
        self.assertFalse(self.profiles(49655592)[0]['once'])  # once per chain
        self.assertEqual(self.profiles(49655592)[0]['limit'], '')
        self.assertFalse(self.profiles(57458399)[0]['predictable'])
        text = self.script(98555327)
        shared = '\n local e9=e1:Clone()\n e9:SetDescription(aux.Stringid(98555327,3))\n e9:SetOperation(c98555327.target)\n c:RegisterEffect(e9)\n'
        text = text.replace('e1:SetCountLimit(1)', 'e1:SetCountLimit(1,98555327)')
        self.assertFalse(self.profiles(98555327, text.replace('c:RegisterEffect(e1)', 'c:RegisterEffect(e1)' + shared, 1))[0]['predictable'])
        self.assertEqual(self.profiles(98555327, text.replace('e1:SetCountLimit(1,98555327)', 'e1:SetCountLimit(1,unknown)')), [])
        self.assertEqual(self.profiles(98555327, text.replace('local e1=Effect.CreateEffect(c)', 'if unknown then\nlocal e1=Effect.CreateEffect(c)', 1)), [])

    def test_deterministic_generated_facts(self):
        with tempfile.TemporaryDirectory(dir=ROOT / '.selfcheck/story') as temporary:
            output, audit = Path(temporary) / 'facts.cs', Path(temporary) / 'audit.json'
            build.generate(SCRIPTS, DB, output, audit)
            self.assertEqual(output.read_bytes(), (ROOT / 'MDPro3Plugins/Runtime/Features/StoryMode/StoryAiLevelEffects.g.cs').read_bytes())
            result = json.loads(audit.read_text(encoding='utf8'))
            self.assertEqual(len(result['profiles']), 35)
            self.assertEqual(result['scripts_sha256'], hashlib.sha256(SCRIPTS.read_bytes()).hexdigest())
            self.assertEqual(result['database_sha256'], hashlib.sha256(DB.read_bytes()).hexdigest())


if __name__ == '__main__':
    unittest.main(verbosity=2)
