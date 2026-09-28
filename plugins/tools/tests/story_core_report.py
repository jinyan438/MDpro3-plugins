"""Report paired native-core results without using the AI's own evaluation score."""
import argparse
import json
import statistics
from pathlib import Path


def load(folder):
    folder = Path(folder).resolve()
    rows = {}
    for line in (folder / 'results.tsv').read_text(encoding='utf-8-sig').splitlines():
        label, seed, actions, summons, elapsed, board = line.split('\t')
        if label in rows:
            raise ValueError(f'Duplicate result: {label}')
        rows[label] = dict(seed=int(seed), actions=int(actions), summons=int(summons), ms=int(elapsed), board=board,
                           ids=[int(x.split(':')[0]) for x in board.split(' | ') if x])
    for line in (folder / 'endstates.tsv').read_text(encoding='utf-8-sig').splitlines():
        label, lp, hand, monsters, spells, grave, banished, extra = line.split('\t')
        rows[label].update(lp=int(lp), hand=hand.split(',') if hand else [], monsters=monsters, spells=spells,
                           grave=grave, banished=banished, extra=extra)
    cases = (folder / 'cases.tsv').read_text(encoding='utf-8-sig').splitlines()
    labels = [x.split('\t')[0] for x in cases if x and not x.startswith('#')]
    if set(rows) != set(labels) or any('hand' not in r for r in rows.values()):
        raise ValueError(f'Incomplete run: {folder}')
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    trace = (folder / 'trace.log').read_text(encoding='utf-8-sig')
    if any(x in trace for x in ['CORE Retry ', 'Lua:', 'Prompt limit exceeded']):
        raise ValueError(f'Failed core protocol: {folder}')
    # Count real opponent responses that accept an offered effect, not mere
    # presence of a hand trap in the seeded deck.
    interrupted = set()
    label = None
    for line in trace.splitlines():
        if line.startswith('SCENARIO '):
            label = line.split()[1]
        if line.startswith('RESPONSE 1 SelectChain ') and not line.endswith('FF-FF-FF-FF'):
            interrupted.add(label)
        if line.startswith('RESPONSE 1 SelectEffectYn 01-00-00-00'):
            interrupted.add(label)
    return folder, rows, manifest, interrupted


parser = argparse.ArgumentParser()
parser.add_argument('--pair', nargs=3, action='append', metavar=('NAME', 'BEFORE', 'AFTER'), required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
out = Path(args.output).resolve()
if root not in out.parents:
    raise ValueError('Report output must stay inside plugins')

decks = [('synchro', '0201 同调'), ('xyz', '0401 超量'), ('fusion', '0512 融合'), ('link', '0013 链接')]
metrics = [
    ('synchro', '流天救世星龙', {40939228}, False),
    ('synchro', '星尘龙', {44508094}, False),
    ('xyz', '未来龙皇', {26973555}, False),
    ('xyz', '未来龙皇＋蚀之双子', {26973555, 45852939}, True),
    ('fusion', '舞狮子神姬', {54701958}, False),
    ('fusion', 'S:P', {29301450}, False),
    ('fusion', '舞香姬', {81196066}, False),
    ('link', '阿波罗萨', {4280258}, False),
    ('link', '女男爵', {84815190}, False),
    ('link', '阿波罗萨或女男爵', {4280258, 84815190}, False),
    ('link', '阿波罗萨＋女男爵', {4280258, 84815190}, True),
]

lines = ['# 本地通用 AI：召唤与做场优化成对验证', '',
         '基线是本次工作开始时冻结的插件程序集，包含此前已有的 AI 优化；不是最早的 Lucky AI。', '',
         '读取故事存档的四副 10 级卡组，固定完整主卡组顺序、额外卡组和种子。运行实际 ocgcore、已安装的 Lua、服务器过滤与 WindBot 回调，到第二回合开始停止。'
         '对比双方使用相同核心、数据库、脚本、输入与测试程序；只替换 AI 程序集。存档和卡组没有改写。', '',
         '指标直接统计核心最终场面，不使用本次修改后的 AI 自评分。表中“出现”不等于一次可用干扰；抗性、已用次数、剩余素材、手牌和对局上下文仍影响强度。'
         '**这些是首回合做场与协议合法性测试，不是完整对局胜率。**', '',
         '无干扰组每副前 24 个样本用于定位问题，后 24 个起手用于新增样本验证。中断组给对手固定一张指定手坑，其余为无干扰卡；'
         '固定测试执行器在核心首次提供合法机会时发动，泡影优先选择正在连锁的对方怪兽。没有模拟人类选择最佳打断点。', '']
machine = {}
for name, before, after in args.pair:
    bp, b, bm, bi = load(before)
    ap, a, am, ai = load(after)
    if set(b) != set(a) or any(b[k]['seed'] != a[k]['seed'] for k in b):
        raise ValueError('Unpaired cases')
    for key in ['cases', 'core', 'cards', 'scripts', 'harness']:
        if bm['hashes'][key] != am['hashes'][key]:
            raise ValueError(f'Input mismatch in {name}: {key}')
    if bm['interruption'] != am['interruption']:
        raise ValueError('Different opponent scenarios')
    lines += [f'## {name}：{len(a)} 对', '',
              f'完整完成：修改前 {len(b)} 场、修改后 {len(a)} 场；没有核心 Retry、Lua 错误或提示循环。', '']
    if bm['interruption'] != 'none':
        lines += [f'对手实际选择发动手坑的场次：修改前 {len(bi)}，修改后 {len(ai)}。其余场景未走到该手坑的合法窗口。', '']
    lines += ['| 卡组 | 终场出现 | 修改前 | 修改后 | 样本数 |', '| --- | --- | ---: | ---: | ---: |']
    counts = []
    for mechanic, title, ids, all_required in metrics:
        keys = [k for k in a if k.startswith(mechanic + '-')]
        has = lambda row: ids <= set(row['ids']) if all_required else bool(ids & set(row['ids']))
        old, new = sum(has(b[k]) for k in keys), sum(has(a[k]) for k in keys)
        lines.append(f'| {dict(decks)[mechanic]} | {title} | {old} | {new} | {len(keys)} |')
        counts.append(dict(deck=mechanic, metric=title, before=old, after=new, n=len(keys)))
    if bm['interruption'] == 'none':
        lines += ['', '### 新增起手（编号 24–47）', '', '| 卡组 | 终场出现 | 修改前 | 修改后 | 样本数 |', '| --- | --- | ---: | ---: | ---: |']
        for mechanic, title, ids, all_required in metrics:
            keys = [k for k in a if k.startswith(mechanic + '-') and int(k.rsplit('-', 1)[1]) >= 24]
            has = lambda row: ids <= set(row['ids']) if all_required else bool(ids & set(row['ids']))
            lines.append(f'| {dict(decks)[mechanic]} | {title} | {sum(has(b[k]) for k in keys)} | {sum(has(a[k]) for k in keys)} | {len(keys)} |')
    lines += ['', '### 消耗与耗时', '', '| 卡组 | 平均剩余手牌：前→后 | 特召事件总数：前→后 | 总耗时 ms：前→后 | 后版单回合中位 / P95 / 最长 ms |', '| --- | ---: | ---: | ---: | ---: |']
    for mechanic, title in decks:
        keys = [k for k in a if k.startswith(mechanic + '-')]
        times = sorted(a[k]['ms'] for k in keys)
        p95 = times[min(len(times) - 1, int(len(times) * .95))]
        lines.append(f'| {title} | {statistics.mean(len(b[k]["hand"]) for k in keys):.2f} → {statistics.mean(len(a[k]["hand"]) for k in keys):.2f} | '
                     f'{sum(b[k]["summons"] for k in keys)} → {sum(a[k]["summons"] for k in keys)} | '
                     f'{sum(b[k]["ms"] for k in keys)} → {sum(a[k]["ms"] for k in keys)} | {statistics.median(times):.0f} / {p95} / {max(times)} |')
    lines += ['', '耗时包含整个首回合的核心推进和所有 AI 决策，不含编译、初始化、网络与动画；不是单次决策耗时。'
              '同时召唤多只按一个 SpSummoning 事件计。更多召唤、更多留场卡或更少手牌本身不代表更强。', '',
              '### 全部逐场结果', '',
              '保留未展开、发生取舍及退步的起手。手牌列为张数，完整 ID、LP、攻防、无效状态、超量素材及各区资源在 endstates.tsv。', '',
              '| 起手 | 修改前终场 | 修改后终场 | 手牌前→后 | 耗时 ms 前→后 |', '| --- | --- | --- | ---: | ---: |']
    for k in sorted(a, key=lambda k: (next(i for i, (d, _) in enumerate(decks) if k.startswith(d)), int(k.rsplit('-', 1)[1]))):
        old = b[k]['board'].replace(' | ', '；') or '空场'
        new = a[k]['board'].replace(' | ', '；') or '空场'
        lines.append(f'| {k} | {old} | {new} | {len(b[k]["hand"])} → {len(a[k]["hand"])} | {b[k]["ms"]} → {a[k]["ms"]} |')
    lines += ['', '### 可复核文件与哈希', '', f'- 基线：`{bp}`', f'- 修改后：`{ap}`', '',
              '| 项目 | SHA256 |', '| --- | --- |', f'| 修改前程序集 | `{bm["hashes"]["assembly"]}` |',
              f'| 修改后程序集 | `{am["hashes"]["assembly"]}` |']
    for key in ['cases', 'core', 'cards', 'scripts', 'harness']:
        lines.append(f'| 共用 {key} | `{am["hashes"][key]}` |')
    lines.append('')
    machine[name] = dict(counts=counts, before=before, after=after, interruption_before=len(bi), interruption_after=len(ai), n=len(a))

out.parent.mkdir(parents=True, exist_ok=True)
out.write_text('\n'.join(lines) + '\n', encoding='utf-8')
out.with_suffix('.json').write_text(json.dumps(machine, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'Paired report: {out}')
