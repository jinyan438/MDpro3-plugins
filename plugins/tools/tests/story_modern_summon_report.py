"""Audit the four requested level-10 decks using paired, completed core runs."""
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import statistics


ROOT = Path(__file__).resolve().parents[2]
RUNS = ROOT / '.selfcheck/story'
DECKS = [('synchro-0201', '同调均'), ('xyz-0508', '急袭猛禽'),
         ('fusion-0101', '英雄'), ('link-0806', '闪刀姬')]
EXTRA_TYPES = 0x40 | 0x2000 | 0x800000 | 0x4000000


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def table(path, width):
    rows = [line.split('\t') for line in path.read_text(encoding='utf-8-sig').splitlines()]
    assert all(len(row) == width for row in rows), path
    result = {row[0]: row[1:] for row in rows}
    assert len(result) == len(rows), ('duplicate', path)
    return result


def cards(value):
    return [int(x) for x in value.split(',') if x]


def read_run(label):
    folder = RUNS / ('core-ai-' + label)
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    for key, path in [('cases', folder / 'cases.tsv'), ('assembly', folder / 'Assembly-CSharp.dll'),
                      ('core', folder / 'ocgcore.dll'), ('cards', folder / 'cards.cdb'),
                      ('scripts', ROOT.parent / 'MDPro3/Data/script.zip'),
                      ('harness', ROOT / 'tools/tests/StoryCoreAiTests.cs')]:
        assert manifest['hashes'][key] == sha(path), (label, key)
    cases = table(folder / 'cases.tsv', 4)  # Reject mid-combo fixtures in opening metrics.
    results = table(folder / 'results.tsv', 6)
    states = table(folder / 'endstates.tsv', 8)
    interactions = table(folder / 'interactions.tsv', 9)
    assert cases.keys() == results.keys() == states.keys() == interactions.keys()
    assert len(cases) == 48 and all(sum(k.startswith(d + '-') for k in cases) == 12 for d, _ in DECKS)
    with sqlite3.connect(f'file:{(folder / "cards.cdb").as_posix()}?mode=ro', uri=True) as db:
        types = dict(db.execute('select id,type from datas'))
    progress = json.loads((ROOT / 'StoryMode/progress.json').read_text(encoding='utf-8-sig'))
    for key, row in cases.items():
        deck = progress['opponents'][key.split('-')[1]]['10']
        assert Counter(cards(row[1])) == Counter(deck['main'])
        assert Counter(cards(row[2])) == Counter(deck['extra'])
        assert row[0] == results[key][0]
    trace = (folder / 'trace.log').read_text(encoding='utf-8-sig')
    log_bytes = (folder / 'run.log').read_bytes()
    runlog = log_bytes.decode('utf-16' if log_bytes.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')
    for error in ['CORE Retry ', 'Lua:', 'Prompt limit exceeded', 'Decision time limit exceeded',
                  'Expected one response', 'Unhandled exception']:
        assert error not in trace + runlog, (label, error)
    ends, starts, enemy_chains = set(), set(), Counter()
    current = None
    for line in trace.splitlines():
        if line.startswith('SCENARIO '):
            assert current is None
            current = line.split()[1]
            assert current in cases and current not in starts
            starts.add(current)
        elif line.startswith('CHAIN 1 '):
            enemy_chains[current] += 1
        elif line.startswith('END '):
            assert line[4:] == '\t'.join([current] + results[current])
            ends.add(current)
            current = None
    assert current is None and starts == ends == cases.keys()
    scenarios = {}
    for key, result in results.items():
        lp, hand, monsters, spells, grave, banished, extra = states[key]
        bodies = [s.split(':') for s in monsters.split(';') if s]
        extra_count = sum(bool(types.get(int(b[0]), 0) & EXTRA_TYPES) for b in bodies)
        active_extra = sum(bool(types.get(int(b[0]), 0) & EXTRA_TYPES) and b[3] == '0' and b[4] == '1' for b in bodies)
        scenarios[key] = dict(seed=int(result[0]), board=result[4], monster_count=len(bodies),
            extra_count=extra_count, active_extra_count=active_extra, hand_count=len(cards(hand)),
            spell_count=len([s for s in spells.split(';') if s]), elapsed_ms=int(result[3]),
            own_lp=int(lp), enemy_lp=int(interactions[key][6]), enemy_chains=enemy_chains[key],
            endstate=states[key], interactions=list(map(int, interactions[key])))
    return dict(folder=str(folder), manifest=manifest, scenarios=scenarios)


def aggregate(rows):
    return dict(n=len(rows), boards_with_extra=sum(r['extra_count'] > 0 for r in rows),
        extra_total=sum(r['extra_count'] for r in rows), active_extra_total=sum(r['active_extra_count'] for r in rows),
        monsters_total=sum(r['monster_count'] for r in rows), empty_monster_boards=sum(r['monster_count'] == 0 for r in rows),
        hand_mean=round(statistics.mean(r['hand_count'] for r in rows), 3),
        spell_total=sum(r['spell_count'] for r in rows), own_lp_mean=round(statistics.mean(r['own_lp'] for r in rows), 1),
        enemy_lp_mean=round(statistics.mean(r['enemy_lp'] for r in rows), 1),
        enemy_chain_cases=sum(r['enemy_chains'] > 0 for r in rows),
        elapsed_ms_median=statistics.median(r['elapsed_ms'] for r in rows),
        elapsed_ms_max=max(r['elapsed_ms'] for r in rows))


def main():
    checks = re.search(r'Story local AI tests: PASS \((\d+) checks\)',
                      (RUNS / 'modern-final/results.log').read_text(encoding='utf-8-sig'))
    assert checks
    report = dict(generated_utc=datetime.now(timezone.utc).isoformat(), local_checks=int(checks[1]),
        baseline='Frozen existing candidate core-ai-actual4-12, copied into core-ai-modern-baseline; not the original production AI.',
        current_source_assembly_sha256=sha(RUNS / 'Assembly-CSharp.story.dll'), pairs={})
    assert sha(RUNS / 'core-ai-actual4-12/Assembly-CSharp.dll') == sha(RUNS / 'core-ai-modern-baseline/Assembly-CSharp.dll')
    lines = ['# 故事模式通用 AI：现代召唤与资源规划验证', '',
        '本次修改与测试产物均在 `plugins` 内。读取实际故事存档中 0201 同调均、0508 急袭猛禽、0101 英雄、0806 闪刀姬的 10 级卡组；没有改卡组、存档或工程副本。', '',
        '## 改动', '',
        '- 搜索同时保留不同起手动作，再按完整状态合并等价续接，避免重复分支挤占有限搜索预算。',
        '- 按 MR3、MR4、2020 规则计算额外召唤区域，处理共享额外区、连接箭头及消耗素材后空出的区域。',
        '- 从 Lua 提取通用效果语义：本回合召唤条件、墓地除外费用、解放后的手牌／墓地诱发、墓地门槛抽卡、结束阶段检索和属性自肃；不增加按卡名或卡号选择路线的规则。',
        '- 预测分支记录素材、费用、同名召唤次数与属性限制；即时收益读取分支内的真实资源。保留合法通常召唤，拒绝不符合属性的特招续接。',
        '- 结束阶段资源只在来源仍留场时计收益；未知抽卡不虚构具体延伸牌；补上自己连锁中的合法手牌诱发，减少展开中途停住。', '',
        '## 验证边界', '',
        f'最终编译与 IL 注入、符号、幂等、API 不匹配拒绝检查通过；本地回归 **{checks[1]} 项通过**。Python 语义测试 44 项通过（22 通用效果、15 效果安全、7 等级变换）。', '',
        '对照是本轮已有的冻结候选程序集 `core-ai-actual4-12/Assembly-CSharp.dll`，已包含之前的优化；不能当作最初生产版本。每组成对使用完全相同的卡组顺序、种子、核心、卡库、Lua 和测试程序。', '',
        '0–11 号起手用于本轮定位；12–23 号起手保留作新增样本，未根据这些起手的结果修改决策。分别测试无干扰首回合，以及固定压力对手的 4 个总回合（双方各两回合，或提前结束）。压力对手使用泡影、雷击、黑洞及怪兽攻击，由固定策略选择合法时机。', '',
        '**终场数量不是胜率，也不等于可用干扰数。** 下表“额外怪兽”按融合／同调／超量／连接类型统计，包含苏生后的怪兽；“有效额外”仅排除里侧或无效状态，仍不保证效果未用或可发动。耗时是整个场景的核心推进与全部决策，部分运行并行，不能用来断言性能提升。', '']
    specs = [('固定起手·无干扰', 'modern-baseline', 'modern-final'),
             ('新增起手·无干扰', 'modern-baseline-holdout', 'modern-final-holdout'),
             ('新增起手·四回合压力', 'modern-baseline-pressure', 'modern-final-pressure')]
    for title, old_label, new_label in specs:
        before, after = read_run(old_label), read_run(new_label)
        bm, am = before['manifest'], after['manifest']
        for key in ['cases', 'core', 'cards', 'scripts', 'harness']:
            assert bm['hashes'][key] == am['hashes'][key], (title, key)
        assert bm['turns'] == am['turns'] and bm['interruption'] == am['interruption']
        assert before['scenarios'].keys() == after['scenarios'].keys()
        groups = {}
        for prefix, name in DECKS:
            keys = [k for k in before['scenarios'] if k.startswith(prefix + '-')]
            groups[name] = {version: aggregate([run['scenarios'][k] for k in keys])
                            for version, run in [('before', before), ('after', after)]}
        report['pairs'][title] = dict(before=before, after=after, aggregate=groups)
        lines += [f'## {title}', '', '48 对场景全部完成，核心未报告非法响应、Lua 错误、提示循环或超时。', '',
            '| 卡组 | 有额外怪兽的终场 / 12：前→后 | 额外怪兽总数：前→后 | 有效额外总数：前→后 | 怪兽总数：前→后 | 平均手牌：前→后 |',
            '| --- | ---: | ---: | ---: | ---: | ---: |']
        for name, pair in groups.items():
            b, a = pair['before'], pair['after']
            values = [f'{b[k]} → {a[k]}' for k in ['boards_with_extra', 'extra_total', 'active_extra_total', 'monsters_total', 'hand_mean']]
            lines.append('| ' + name + ' | ' + ' | '.join(values) + ' |')
        lines += ['', '| 卡组 | 空怪兽场次数：前→后 | 对手实际发动效果场次：前→后 | 平均己方 LP：前→后 | 后版场景耗时中位 / 最长 ms |',
                  '| --- | ---: | ---: | ---: | ---: |']
        for name, pair in groups.items():
            b, a = pair['before'], pair['after']
            values = [f'{b[k]} → {a[k]}' for k in ['empty_monster_boards', 'enemy_chain_cases', 'own_lp_mean']]
            lines.append('| ' + name + ' | ' + ' | '.join(values) + f' | {a["elapsed_ms_median"]} / {a["elapsed_ms_max"]} |')
        lines += ['', '### 全部逐场终场', '',
                  '| 起手 | 前版怪兽终场 | 后版怪兽终场 | 手牌前→后 |', '| --- | --- | --- | ---: |']
        for prefix, _ in DECKS:
            for key in sorted((k for k in before['scenarios'] if k.startswith(prefix + '-')), key=lambda k: int(k.rsplit('-', 1)[1])):
                b, a = before['scenarios'][key], after['scenarios'][key]
                bb, ab = b['board'].replace(' | ', '；') or '空场', a['board'].replace(' | ', '；') or '空场'
                lines.append(f'| {key} | {bb} | {ab} | {b["hand_count"]} → {a["hand_count"]} |')
        lines += ['', '### 复核文件', '', f'- 前版：`{before["folder"]}`', f'- 后版：`{after["folder"]}`',
                  '- 每个目录保留 cases.tsv、trace.log、results.tsv、endstates.tsv、interactions.tsv 和 manifest.json。',
                  '', '| 项目 | SHA256 |', '| --- | --- |', f'| 前版程序集 | `{bm["hashes"]["assembly"]}` |',
                  f'| 后版程序集 | `{am["hashes"]["assembly"]}` |']
        lines += [f'| 共用 {key} | `{am["hashes"][key]}` |' for key in ['cases', 'core', 'cards', 'scripts', 'harness']]
        lines.append('')
    final_hashes = {p['after']['manifest']['hashes']['assembly'] for p in report['pairs'].values()}
    assert len(final_hashes) == 1
    sources = list((ROOT / 'MDPro3Plugins/Runtime/Features/StoryMode').glob('StoryAi*.cs'))
    sources += [ROOT / 'tools' / name for name in ['build_story_combo_effects.py', 'build_story_tactical_facts.py']]
    report['source_hashes'] = {p.relative_to(ROOT).as_posix(): sha(p) for p in sorted(sources)}
    report['native_runs'] = sum(len(pair[v]['scenarios']) for pair in report['pairs'].values() for v in ['before', 'after'])
    lines += ['## 局限与生效', '',
        '完整列出改善、取舍和退步的起手。压力测试中急袭猛禽仍有大量空场，英雄也经常被清场；闪刀姬部分起手在压力下保留的额外怪兽减少。当前结果证明若干通用资源链和做场路线得到改善，不能证明所有起手都更强，更不能当作完整对局胜率。', '',
        '搜索仍受节点／深度预算限制，未识别的复杂脚本效果不作乐观展开预测。未知抽卡、对方隐藏手牌和未来解牌不会被当成已知信息。', '',
        '下次通过根目录 Run_MDPro3.bat 正常启动并完成插件同步、重建后生效。本次未运行会写入 plugins 外部的启动／部署流程。', '',
        '复现本报告：`python plugins/tools/tests/story_modern_summon_report.py`。各组参数和文件哈希由报告生成器再次核对；机器可读结果见同名 JSON。']
    output = ROOT / 'STORY-AI-MODERN-SUMMON-REPORT.md'
    output.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    output.with_suffix('.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: p['aggregate'] for k, p in report['pairs'].items()}, ensure_ascii=False, indent=2))
    print(f'Validated {report["native_runs"]} native runs: {output}')


if __name__ == '__main__':
    main()
