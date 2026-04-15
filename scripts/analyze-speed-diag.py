#!/usr/bin/env python3
"""Analyze LocalSynapse speed-diag.log for M0-A Extraction diagnosis.

Reads EXTRACT_FILE and PARSE_DETAIL events and generates markdown report.
Default output: Docs/diagnostics/M0-A-analysis.md
"""
import argparse
import re
import statistics
import sys
from collections import defaultdict
from pathlib import Path

LINE_RE = re.compile(r'^(?P<ts>\S+)\t(?P<elapsed>\S+)\t(?P<cat>\S+)\t(?P<kvs>.*)$')
KV_RE = re.compile(r'(\w+)=(?:"((?:[^"\\]|\\.)*)"|(\S+))')


def parse_kvs(raw):
    return {m.group(1): (m.group(2) if m.group(2) is not None else m.group(3))
            for m in KV_RE.finditer(raw)}


def parse_log(path, since=None):
    extract_events, detail_events = [], []
    skipped = 0
    for line in path.read_text(encoding='utf-8', errors='replace').splitlines():
        if not line or line.startswith('#'):
            continue
        m = LINE_RE.match(line)
        if not m:
            skipped += 1
            continue
        if since and m.group('ts') < since:
            continue
        kvs = parse_kvs(m.group('kvs'))
        cat = m.group('cat')
        if cat == 'EXTRACT_FILE':
            extract_events.append(kvs)
        elif cat == 'PARSE_DETAIL':
            detail_events.append(kvs)
    if skipped:
        print(f'Warning: skipped {skipped} malformed lines', file=sys.stderr)
    return extract_events, detail_events


def aggregate_by_ext(events):
    by = defaultdict(lambda: {'count': 0, 'extract_ms': [], 'total_ms': [],
                              'result': defaultdict(int)})
    for e in events:
        ext = e.get('ext', 'unknown')
        by[ext]['count'] += 1
        try:
            by[ext]['extract_ms'].append(int(e.get('extract_ms', 0)))
            by[ext]['total_ms'].append(int(e.get('total_ms', 0)))
        except ValueError:
            pass
        by[ext]['result'][e.get('result', 'unknown')] += 1
    return by


def size_bucket(n):
    try:
        n = int(n)
    except (ValueError, TypeError):
        return 'unknown'
    if n < 0:
        return 'unknown'
    if n < 100_000:
        return '<100KB'
    if n < 1_000_000:
        return '100KB-1MB'
    if n < 10_000_000:
        return '1MB-10MB'
    return '>10MB'


def pct(xs, p):
    if not xs:
        return 0
    xs = sorted(xs)
    k = int(len(xs) * p / 100)
    return xs[min(k, len(xs) - 1)]


def render_md(ext_agg, detail_events, extract_events):
    out = ['# M0-A Extraction 진단 분석 보고서\n']

    total_time = sum(sum(v['total_ms']) for v in ext_agg.values())
    total_count = sum(v['count'] for v in ext_agg.values())
    files_per_min = total_count / max(total_time / 60000, 0.001)

    out.append('## 요약\n')
    out.append(f'- 총 EXTRACT_FILE 이벤트: {total_count}')
    out.append(f'- 총 소요 시간: {total_time / 1000:.1f}s ({total_time / 60000:.1f} min)')
    out.append(f'- 평균 파일/분: {files_per_min:.1f}')
    out.append(f'- 총 PARSE_DETAIL 이벤트: {len(detail_events)}\n')

    # 파서별 집계
    out.append('## 파서별 집계\n')
    out.append('| ext | count | p50 extract | p95 | max | total_ms | 비중 |')
    out.append('|---|---|---|---|---|---|---|')
    for ext, v in sorted(ext_agg.items(), key=lambda x: -sum(x[1]['total_ms'])):
        ems = v['extract_ms']
        total_ms_sum = sum(v['total_ms'])
        share = total_ms_sum / max(total_time, 1) * 100
        out.append(
            f"| {ext} | {v['count']} | {pct(ems, 50)} | {pct(ems, 95)} | "
            f"{max(ems) if ems else 0} | {total_ms_sum} | {share:.1f}% |"
        )

    # 크기 버킷별
    out.append('\n## 크기 버킷별\n')
    out.append('| bucket | count | avg extract_ms |')
    out.append('|---|---|---|')
    bucket_ems = defaultdict(list)
    for e in extract_events:
        try:
            ms = int(e.get('extract_ms', 0))
            bucket_ems[size_bucket(e.get('size_bytes', -1))].append(ms)
        except ValueError:
            pass
    bucket_order = ['<100KB', '100KB-1MB', '1MB-10MB', '>10MB', 'unknown']
    for bkt in bucket_order:
        if bkt in bucket_ems:
            ems = bucket_ems[bkt]
            out.append(f"| {bkt} | {len(ems)} | {statistics.mean(ems):.0f} |")

    # 파서×단계 집계
    out.append('\n## 파서×단계 집계\n')
    out.append('| ext | stage | count | p50 | p95 | max |')
    out.append('|---|---|---|---|---|---|')
    stage_agg = defaultdict(list)
    for e in detail_events:
        try:
            ms = int(e.get('time_ms', 0))
            stage_agg[(e.get('ext', '?'), e.get('stage', '?'))].append(ms)
        except ValueError:
            pass
    for (ext, stage), ms in sorted(stage_agg.items(),
                                   key=lambda x: -max(x[1]) if x[1] else 0):
        out.append(
            f"| {ext} | {stage} | {len(ms)} | {pct(ms, 50)} | "
            f"{pct(ms, 95)} | {max(ms) if ms else 0} |"
        )

    # 실패 원인
    out.append('\n## 실패 원인\n')
    out.append('| ext | result | count |')
    out.append('|---|---|---|')
    has_failures = False
    for ext, v in ext_agg.items():
        for r, c in v['result'].items():
            if r.startswith('error') or r == 'skip_cloud':
                out.append(f"| {ext} | {r} | {c} |")
                has_failures = True
    if not has_failures:
        out.append('| (none) | | |')

    # Top 20 느린 파일 (SEC-D3 옵션 A — Ryan 승인)
    out.append('\n## Top 20 느린 파일\n')
    out.append('| path | ext | size | total_ms |')
    out.append('|---|---|---|---|')
    top20 = sorted(
        extract_events,
        key=lambda e: -int(e.get('total_ms', 0) or 0)
    )[:20]
    for e in top20:
        try:
            sz = int(e.get('size_bytes', -1))
        except ValueError:
            sz = -1
        if sz > 1_048_576:
            sz_str = f"{sz / 1_048_576:.1f}MB"
        elif sz > 0:
            sz_str = f"{sz / 1024:.0f}KB"
        else:
            sz_str = 'unknown'
        out.append(
            f"| `{e.get('path', '?')}` | {e.get('ext', '?')} | "
            f"{sz_str} | {e.get('total_ms', 0)} |"
        )

    # Top bottleneck 판정
    out.append('\n## Top bottleneck 판정\n')
    out.append('(총시간 비중 > 15% + 해당 ext의 단일 stage 비중 > 70% → Top 후보)\n')
    for ext, v in sorted(ext_agg.items(),
                         key=lambda x: -sum(x[1]['total_ms']))[:5]:
        share = sum(v['total_ms']) / max(total_time, 1) * 100
        stages = {s: sum(ms) for (e, s), ms in stage_agg.items() if e == ext}
        if not stages:
            out.append(f"- `{ext}`: 총시간 비중 {share:.1f}%, stage 데이터 없음")
            continue
        max_stage = max(stages.items(), key=lambda x: x[1])
        stage_share = max_stage[1] / max(sum(stages.values()), 1) * 100
        verdict = ' **← Top 후보**' if share > 15 and stage_share > 70 else ''
        out.append(
            f"- `{ext}`: 총시간 비중 {share:.1f}%, 주요 stage "
            f"`{max_stage[0]}` ({stage_share:.1f}%){verdict}"
        )

    return '\n'.join(out) + '\n'


def default_log_path():
    if sys.platform == 'win32':
        import os
        appdata = os.environ.get('LOCALAPPDATA')
        if appdata:
            return Path(appdata) / 'LocalSynapse' / 'speed-diag.log'
    # macOS / Linux: .NET Environment.SpecialFolder.LocalApplicationData returns XDG ~/.local/share
    return Path.home() / '.local' / 'share' / 'LocalSynapse' / 'speed-diag.log'


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--input', type=Path, default=default_log_path(),
                    help='Path to speed-diag.log (default: platform-specific)')
    ap.add_argument('--output', type=Path,
                    default=Path('Docs/diagnostics/M0-A-analysis.md'),
                    help='Output markdown path')
    ap.add_argument('--since', type=str, default=None,
                    help='ISO timestamp; only process events after this')
    args = ap.parse_args()

    if not args.input.exists():
        print(f'Input not found: {args.input}', file=sys.stderr)
        return 1

    extract_events, detail_events = parse_log(args.input, args.since)
    if not extract_events:
        print('No EXTRACT_FILE events found.', file=sys.stderr)
        return 2

    ext_agg = aggregate_by_ext(extract_events)
    md = render_md(ext_agg, detail_events, extract_events)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(md, encoding='utf-8')
    print(f'Wrote {args.output} ({len(md)} bytes, '
          f'{len(extract_events)} extract / {len(detail_events)} detail events)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
