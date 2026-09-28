"""Audit the paired resource-development runs; never use the AI's own score."""
import csv
import hashlib
import json
import math
import re
import statistics
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNS = ROOT / '.selfcheck/story'
DECKS = [('synchro', '0201 同调'), ('xyz', '0401 超量'), ('fusion', '0512 融合'), ('link', '0013 链接')]
PAIRS = [
    ('tuning', '调优样本 96–111', 'development-before', 'development-final-tuning'),
    ('validation', '扩展验证 112–143', 'development-holdout-before', 'development-final-validation'),
    ('unseen', '最终保留样本 152–167', 'development-unseen-before', 'development-unseen-after'),
    ('ash', '灰流丽 144–151', 'development-ash-before', 'development-ash-after'),
    ('imperm', '无限泡影 144–151', 'development-imperm-before', 'development-imperm-after'),
    ('nibiru', '原始生命态 尼比鲁 144–151', 'development-nibiru-before', 'development-nibiru-after'),
    ('pressure', '四回合压力 144–151', 'development-pressure-before', 'development-pressure-after'),
]
METRICS = [
    ('synchro', '流天救世星龙', {40939228}, 1),
    ('synchro', '至少一只星尘龙', {44508094}, 1),
    ('xyz', '未来龙皇', {26973555}, 1),
    ('xyz', '未来龙皇＋蚀之双子', {26973555, 45852939}, 2),
    ('fusion', '舞狮子神姬', {54701958}, 1),
    ('fusion', 'S:P', {29301450}, 1),
    ('fusion', '舞香姬', {81196066}, 1),
    ('link', '神弓', {4280258}, 1),
    ('link', '女男爵', {84815190}, 1),
    ('link', '神弓或女男爵', {4280258, 84815190}, 1),
    ('link', '神弓＋女男爵', {4280258, 84815190}, 2),
    ('link', '神弓／女男爵／克里斯提亚／法·王·兽至少一种', {4280258, 84815190, 59509952, 88581108}, 1),
    ('link', '上述四种至少两种同时留场', {4280258, 84815190, 59509952, 88581108}, 2),
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_rows(path, columns):
    rows = {}
    for row in csv.reader(path.read_text(encoding='utf-8-sig').splitlines(), delimiter='\t'):
        if not row or row[0].startswith('#'):
            continue
        if len(row) != columns or row[0] in rows:
            raise ValueError(f'Malformed or duplicate row in {path}: {row[0]}')
        rows[row[0]] = row[1:]
    return rows


def load(label):
    folder = RUNS / ('core-ai-' + label)
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    cases = read_rows(folder / 'cases.tsv', 4)
    results = read_rows(folder / 'results.tsv', 6)
    states = read_rows(folder / 'endstates.tsv', 8)
    events = read_rows(folder / 'interactions.tsv', 9)
    if not cases or any(rows.keys() != cases.keys() for rows in (results, states, events)):
        raise ValueError('Incomplete run: ' + label)
    for key, name in [('cases', 'cases.tsv'), ('assembly', 'Assembly-CSharp.dll'),
                      ('core', 'ocgcore.dll'), ('cards', 'cards.cdb')]:
        if digest(folder / name) != manifest['hashes'][key]:
            raise ValueError(f'Input changed after run: {label}/{key}')
    for key, path in [('harness', ROOT / 'tools/tests/StoryCoreAiTests.cs'),
                      ('scripts', ROOT.parent / 'MDPro3/Data/script.zip')]:
        if digest(path) != manifest['hashes'][key]:
            raise ValueError(f'Current {key} differs from tested input: {label}')
    trace = (folder / 'trace.log').read_text(encoding='utf-8-sig')
    log = (folder / 'run.log').read_text(encoding='utf-8-sig')
    for marker in ['CORE Retry ', 'Lua:', 'Prompt limit exceeded', 'Decision time limit exceeded',
                   'Core rejected response', 'Expected one response', 'Unhandled Exception']:
        if marker in trace or marker in log:
            raise ValueError(f'Core failure {marker}: {label}')
    started, ended, interrupted = [], [], set()
    active = None
    for line in trace.splitlines():
        if line.startswith('SCENARIO '):
            active = line.split()[1]
            started.append(active)
        elif line.startswith('END '):
            ended.append(line[4:].split('\t')[0])
        elif (line.startswith('RESPONSE 1 SelectChain ') and not line.endswith('FF-FF-FF-FF')) or \
                line.startswith('RESPONSE 1 SelectEffectYn 01-00-00-00'):
            interrupted.add(active)
    if started != list(cases) or ended != list(cases):
        raise ValueError('Missing/duplicated scenario boundaries: ' + label)
    rows = {}
    for name, r in results.items():
        s, e = states[name], events[name]
        if int(cases[name][0]) != int(r[0]) or int(s[0]) != int(e[5]):
            raise ValueError('Seed/LP mismatch: ' + name)
        rows[name] = dict(seed=int(r[0]), actions=int(r[1]), summons=int(r[2]), ms=int(r[3]), board=r[4],
                          ids=[int(x.split(':')[0]) for x in r[4].split(' | ') if x],
                          lp=int(s[0]), hand=s[1].split(',') if s[1] else [], monsterStates=s[2], spellStates=s[3],
                          grave=s[4], banished=s[5], extra=s[6], turn=int(e[0]), attacks=int(e[1]),
                          ownNegations=int(e[2]), ownAttackStops=int(e[3]), ownEffectMoves=int(e[4]),
                          enemyLp=int(e[6]), spOwnBanishes=int(e[7]))
    return dict(label=label, manifest=manifest, cases=cases, rows=rows,
                interrupted=sorted(interrupted), traceSha256=digest(folder / 'trace.log'))


def metrics(before, after):
    output = []
    for deck, name, ids, count in METRICS:
        keys = [k for k in before if k.startswith(deck + '-')]
        if not keys:
            continue
        has = lambda row: len(set(row['ids']) & ids) >= count
        output.append(dict(deck=deck, metric=name, n=len(keys),
                           before=sum(has(before[k]) for k in keys), after=sum(has(after[k]) for k in keys),
                           gained=[k for k in keys if has(after[k]) and not has(before[k])],
                           lost=[k for k in keys if has(before[k]) and not has(after[k])]))
    return output


def metric_table(rows):
    lines = ['| 卡组 | 终场出现 | 修改前 | 修改后 | 样本数 |', '| --- | --- | ---: | ---: | ---: |']
    for r in rows:
        lines.append(f"| {dict(DECKS)[r['deck']]} | {r['metric']} | {r['before']} | {r['after']} | {r['n']} |")
    return lines


def main():
    payload = dict(cohorts={})
    for key, title, old, new in PAIRS:
        before, after = load(old), load(new)
        if before['cases'] != after['cases']:
            raise ValueError('Paired decks/seeds differ: ' + key)
        for field in ('cases', 'core', 'cards', 'scripts', 'harness'):
            if before['manifest']['hashes'][field] != after['manifest']['hashes'][field]:
                raise ValueError('Paired input differs: ' + key + '/' + field)
        for field in ('turns', 'interruption'):
            if before['manifest'][field] != after['manifest'][field]:
                raise ValueError('Paired condition differs: ' + key + '/' + field)
        payload['cohorts'][key] = dict(title=title, before=before, after=after,
                                     metrics=metrics(before['rows'], after['rows']))
    # Every final run must test the same AI, and every baseline the same snapshot.
    for version in ('before', 'after'):
        if len({c[version]['manifest']['hashes']['assembly'] for c in payload['cohorts'].values()}) != 1:
            raise ValueError('Mixed AI versions: ' + version)
    opening = {}
    for version in ('before', 'after'):
        opening[version] = {k: v for cohort in ('tuning', 'validation', 'unseen')
                            for k, v in payload['cohorts'][cohort][version]['rows'].items()}
    payload['openingMetrics'] = metrics(opening['before'], opening['after'])
    pairs = sum(len(c['before']['rows']) for c in payload['cohorts'].values())
    checks = int(re.search(r'PASS \((\d+) checks\)', (RUNS / 'local-ai-tests/results.log').read_text(encoding='utf-8-sig'))[1])
    if digest(RUNS / 'local-ai-tests/Assembly-CSharp.dll') != digest(RUNS / 'Assembly-CSharp.story.dll'):
        raise ValueError('Local tests did not run against the current woven assembly')
    rules_log = (RUNS / 'development-rules.log').read_text(encoding='utf-8-sig')
    if 'StoryMode tests: PASS (86 checks)' not in rules_log or 'StoryModelConfig tests: PASS (38 checks)' not in rules_log:
        raise ValueError('Missing story/config verification')
    old_log = (RUNS / 'development-baseline-regressions/results.log').read_text(encoding='utf-8-sig')
    baseline_failures = len(re.findall(r'^Extension\w+:', old_log, re.MULTILINE))
    payload['checks'] = dict(local=checks, startingLocal=3923, newChecks=checks - 3923,
                             baselineFailingScenarios=baseline_failures, pairedCases=pairs, executions=pairs * 2)
    payload['wovenAssemblySha256'] = digest(RUNS / 'Assembly-CSharp.story.dll')
    payload['runtimeSourceHashes'] = {p.name: digest(p) for p in sorted((ROOT / 'MDPro3Plugins/Runtime/Features/StoryMode').glob('*.cs'))}
    lines = ['# 本地通用 AI：资源复用与召唤做场优化', '',
        '基线为本轮开始时冻结的工作区版本，包含此前已有的展开、效果安全和战斗优化；不是 Git HEAD，也不是最初的 Lucky AI。仅修改 `plugins`，四副实际 10 级角色卡组只读使用，故事存档未改写。', '',
        '## 实现', '',
        '- 在同一有限搜索中连接通召、检索、支付费用、墓地复用、融合与额外召唤。相同物理副本仍分别消耗，但行为等价的搜索状态合并；搜索深度、区域、属性、次数和实例状态参与区分。待结算的已建模诱发只获得搜索优先级，不冒充已兑现的终场收益。',
        '- 月光狼／虎可先放入真实可用的灵摆区；虎苏生无效、不能攻击且结束阶段破坏，黄鼬／舞香姬回收刻度后能重置实例次数。狼使用场上／墓地素材并除外，不虚构送墓诱发；月光舞踏会仅建模发动当回合的送墓效果。',
        '- 多层融合要求至少三张真实素材，额外素材数量受对方怪兽数限制，实际扣除其原本攻击力对应 LP 并拒绝致死支付。融合结算规划保留真实未用的通召机会。',
        '- 革命同调士墓地苏生按 1 星继续规划，记录一局一次，未知堆墓只消耗牌库数量，不编造具体卡片与收益。识别的次元替换状态下不会虚构成功苏生。',
        '- 穆恩的连接召唤送墓、天空神骑士的弃牌检索、许珀里翁的真实除外费用、大天使的四天使条件进入规划。双向特召封锁安排在展开末尾；普通苏生不能重放连接召唤限定诱发。',
        '- 选项和选卡对齐当前结算的连锁：天空的圣水执行规划的怪兽检索，穆恩保留规划的送墓，不被默认可选分支改成回收。用精确效果编号和当前来源约束，避免遗留全局“是／否”答案。',
        '- Link 素材必须在支付后仍有合法额外怪兽区或箭头落点；识别对方公开箭头。核心提供某个召唤，不再被误当成任意素材组合都合法。I:P 的预期转换同样检查落点及持续特召封锁，不把被自己克里斯提亚封住的快链当作可用干扰。', '',
        '预算保持原有 **7 步主动动作、40 个常规保留状态、5000 个生成状态、45000 次素材检查**；已有候选资源评估入口的首层上限仍为 64。没有提高预算来换取结果。', '',
        '## 验证方法', '',
        f'本地 AI **{checks} 项检查通过**，相对本轮开始新增 **{checks - 3923} 项**。新增行为在冻结旧程序集上有 **{baseline_failures} 个场景组失败**，新版通过。真实游戏源码编译、IL 注入、符号、幂等和 API 不匹配拒绝检查通过；故事规则 86 项、模型配置 38 项通过。', '',
        '实际运行 ocgcore、当前安装的 Lua、卡片数据库、服务端过滤与 WindBot 回调。每对使用相同完整主卡组顺序、额外卡组、种子、核心、卡库、脚本和测试程序，仅替换 AI 程序集。报告生成器校验文件哈希、逐场边界和所有输出完整性；原始卡片 ID、LP、攻防、无效状态、超量素材和各区资源保存在 JSON 与运行目录。', '',
        '无干扰编号 96–111 用于调优；112–143 用于扩展验证，其中退步追踪发现圣水／穆恩的选项衔接问题并据此修正，因此该组不再称为独立留出集。最终再冻结 152–167，每副 16 个未用于调优的新起手。灰流丽／泡影／陨石以及四回合压力均使用 144–151，每副 8 个起手。', '',
        f'最终共 **{pairs} 对、{pairs * 2} 次核心执行**，全部完成，无核心 Retry、Lua 错误、提示循环或 60 秒场景时限触发。中间探针版本不混入最终统计。', '',
        '## 无干扰首回合合计', '',
        '每副共 64 个起手，到第二回合开始停止。以下为终场出现对应卡的场次，直接读取核心，不用 AI 自评分。卡名出现不等于一份可用干扰：仍应结合素材数、抗性、已用次数和上下文判断；不是完整对局胜率。', '']
    lines += metric_table(payload['openingMetrics'])
    lines += ['', '最终未参与调优的保留样本单列：', '']
    lines += metric_table(payload['cohorts']['unseen']['metrics'])
    def result(cohort, name):
        rows = payload['openingMetrics'] if cohort == 'opening' else payload['cohorts'][cohort]['metrics']
        row = next(r for r in rows if r['metric'] == name)
        return f"{row['before']} → {row['after']}（每版 {row['n']} 场）"
    lines += ['',
        f"结果并非全面单调提升：全部无干扰样本的流天救世星龙为 **{result('opening', '流天救世星龙')}**；最终保留组为 **{result('unseen', '流天救世星龙')}**，同组未来龙皇＋蚀之双子为 **{result('unseen', '未来龙皇＋蚀之双子')}**。这些负向结果与增益一并保留。", '',
        f"四回合压力下，未来龙皇＋蚀之双子的终场共存为 **{result('pressure', '未来龙皇＋蚀之双子')}**。更强的无干扰做场不保证每个受干扰终场都更好；部分终场也受提前结束影响，需结合后面的对方 LP 和逐场状态评估。", '']
    lines += ['', '## 分组结果、消耗与逐场审计', '',
        '手坑对手在首次核心合法窗口发动，泡影优先选择连锁中的怪兽，不模拟人类最优打断点。四回合压力对手使用固定顺序的泡影、雷击、黑洞、基因扭曲战狼及相同的发动／通召／有利攻击策略，到第五回合开始或提前结束停止。它用于检查受干扰后的路线与跨回合状态，不代表竞技环境胜率。', '']
    for key, c in payload['cohorts'].items():
        b, a = c['before']['rows'], c['after']['rows']
        lines += [f"### {c['title']}：{len(a)} 对", '']
        if key not in ('tuning', 'validation', 'unseen', 'pressure'):
            lines += [f"对手实际选择发动手坑的场次：前 {len(c['before']['interrupted'])}，后 {len(c['after']['interrupted'])}；其余未出现合法发动窗口。", '']
        lines += metric_table(c['metrics'])
        lines += ['', '| 卡组 | 平均剩余手牌 前→后 | 平均 LP 前→后 | 特召事件总数 前→后 | 后版整个场景中位／P95／最长 ms |',
                  '| --- | ---: | ---: | ---: | ---: |']
        for deck, title in DECKS:
            keys = [k for k in a if k.startswith(deck + '-')]
            ms = sorted(a[k]['ms'] for k in keys)
            hand = [statistics.mean(len(run[k]['hand']) for k in keys) for run in (b, a)]
            life = [statistics.mean(run[k]['lp'] for k in keys) for run in (b, a)]
            special = [sum(run[k]['summons'] for k in keys) for run in (b, a)]
            lines.append(f'| {title} | {hand[0]:.2f} → {hand[1]:.2f} | {life[0]:.0f} → {life[1]:.0f} | {special[0]} → {special[1]} | '
                         f'{statistics.median(ms):.0f}／{ms[math.ceil(len(ms) * .95) - 1]}／{max(ms)} |')
        if key == 'pressure':
            lines += ['', '| 卡组 | 提前胜利 前→后 | 提前失败 前→后 | 平均对方 LP 前→后 | 己方无效己方连锁 前→后 |',
                      '| --- | ---: | ---: | ---: | ---: |']
            for deck, title in DECKS:
                keys = [k for k in a if k.startswith(deck + '-')]
                wins = [sum(run[k]['enemyLp'] <= 0 for k in keys) for run in (b, a)]
                losses = [sum(run[k]['lp'] <= 0 for k in keys) for run in (b, a)]
                enemy = [statistics.mean(run[k]['enemyLp'] for k in keys) for run in (b, a)]
                negates = [sum(run[k]['ownNegations'] for k in keys) for run in (b, a)]
                lines.append(f'| {title} | {wins[0]} → {wins[1]} | {losses[0]} → {losses[1]} | {enemy[0]:.0f} → {enemy[1]:.0f} | {negates[0]} → {negates[1]} |')
        lines += ['', '<details>', '<summary>全部逐场终场（包含未展开、取舍与退步）</summary>', '',
                  '| 起手 | 修改前 | 修改后 | 手牌数 前→后 | LP 前→后 | 时间 ms 前→后 |', '| --- | --- | --- | ---: | ---: | ---: |']
        for k in b:
            old, new = b[k]['board'].replace(' | ', '；') or '空场', a[k]['board'].replace(' | ', '；') or '空场'
            lines.append(f"| {k} | {old} | {new} | {len(b[k]['hand'])} → {len(a[k]['hand'])} | {b[k]['lp']} → {a[k]['lp']} | {b[k]['ms']} → {a[k]['ms']} |")
        lines += ['', '</details>', '', f"原始目录：`.selfcheck/story/core-ai-{c['before']['label']}`、`.selfcheck/story/core-ai-{c['after']['label']}`。", '']
    lines += ['## 局限与复现', '',
        '新增已验证的资源转换和修复模拟／执行偏差，不能保证每个起手都变强。同调、融合仍有卡手与未识别的深层路线；有些链接场面会改为 I:P 加其他怪兽，不能直接等价为神弓或额外一次干扰。JSON 的每项指标保留 gained／lost 样本列表，以上全部逐场表保留负向结果。', '',
        '耗时为整段核心推进与所有 AI 决策，不含编译、初始化、动画与网络；四回合压力的时间覆盖四回合。部分前后测试在同机并行，时间含 CPU 竞争，不用于证明性能提升。更多召唤、更少手牌或更多留场卡本身不等于更强。', '',
        '效果模型仍有边界：月光舞踏会只建模第一效果，穆恩的可选厄斯回收未作为额外搜索分支，未知堆墓／抽牌不预测身份。只对安装的卡库与脚本验证，更新脚本后需重跑。没有改角色卡组、初始资源或核心规则来制造提升。', '',
        '```powershell',
        'plugins/tools/story-local-ai-test.ps1',
        'plugins/tools/story-test.ps1',
        'plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label development-unseen-after -Cases plugins/.selfcheck/story/development-unseen.tsv',
        'plugins/tools/story-core-ai-test.ps1 -SkipCompile -Label development-pressure-after -Cases plugins/.selfcheck/story/development-interruptions.tsv -Interruption pressure -Turns 4',
        'python plugins/tools/tests/story_development_report.py',
        '```', '',
        '其余组用上述原始目录的 `cases.tsv` 与相同干扰参数重放。基线用 `-ReuseAssembly` 和对应 before 标签，保留冻结旧程序集；勿用当前程序集覆盖。', '',
        '本轮未运行会写入 `plugins` 外部目录的启动器同步／发布重建。下一次正常启动器同步重建后加载本轮源码，当前运行中的客户端不会自动热更新。', '',
        '## SHA256', '', '| 项目 | SHA256 |', '| --- | --- |']
    for version in ('before', 'after'):
        lines.append(f"| {version} 程序集 | `{payload['cohorts']['unseen'][version]['manifest']['hashes']['assembly']}` |")
    for field in ('core', 'cards', 'scripts', 'harness'):
        lines.append(f"| 相同 {field} | `{payload['cohorts']['unseen']['after']['manifest']['hashes'][field]}` |")
    for key, c in payload['cohorts'].items():
        lines.append(f"| {key} cases | `{c['after']['manifest']['hashes']['cases']}` |")
    (ROOT / 'STORY-AI-DEVELOPMENT-REPORT.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    (ROOT / 'STORY-AI-DEVELOPMENT-REPORT.json').write_text(json.dumps(payload, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(checks=payload['checks'], opening=payload['openingMetrics']), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
