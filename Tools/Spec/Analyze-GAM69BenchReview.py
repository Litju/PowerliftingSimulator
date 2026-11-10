"""GAM-69 bounded physical review of a bench telemetry CSV (stdlib only).

usage: python -I Analyze-GAM69BenchReview.py <gateC-legal.csv>

Reports, for the lift proper (Start..Rack, from the command column):
  1. geometric contact vs load bearing per support region;
  2. foot-loop actuation: commanded leg depth vs its saturation bounds and the
     measured foot forces it regulates;
  3. wrist channels at peak demand: requested / applied / realized and the
     resulting bar motion and grip continuity.
"""
import csv
import sys

DEPTH_MIN, DEPTH_MAX = -0.004, 0.012
FOOT_TARGET_N = 110.0


def f(r, k):
    return float(r[k])


def main(path):
    rows = list(csv.DictReader(open(path, newline='')))
    cmds = [(int(r['tick']), r['command']) for r in rows if r['command'] not in ('None', '')]
    start = next(t for t, c in cmds if c == 'Start')
    rack = next(t for t, c in cmds if c == 'Rack')
    lp = [r for r in rows if start <= int(r['tick']) <= rack]
    print(f'{path}: lift proper ticks {start}..{rack} ({len(lp)})')

    print('\n1. contact (rule: geometric) vs load (diagnostic)')
    for name, gap, force, flag in [('head', 'headGap', 'headN', 'headContact'),
                                   ('shoulders', 'shouldersGap', 'shouldersN', 'shouldersContact'),
                                   ('buttocks', 'buttocksGap', 'buttocksN', 'buttocksContact'),
                                   ('left foot', 'lFootGap', 'lFootN', 'lFootPlanted'),
                                   ('right foot', 'rFootGap', 'rFootN', 'rFootPlanted')]:
        g = [f(r, gap) * 1000 for r in lp]
        n = [f(r, force) for r in lp]
        lost = sum(1 for r in lp if r[flag] != '1')
        print(f'  {name:10s} gap max {max(g):6.2f} mm  force min {min(n):6.1f} mean {sum(n)/len(n):6.1f} N  '
              f'<5 N ticks {sum(1 for x in n if x < 5):3d}  rule-contact lost {lost}')

    print('\n2. foot loop (leg depth reference, + into the floor)')
    for side, d, n in [('left', 'lLegDepth', 'lFootN'), ('right', 'rLegDepth', 'rFootN')]:
        if d not in rows[0]:
            print('  (no leg depth column)')
            break
        depth = [f(r, d) * 1000 for r in lp]
        force = [f(r, n) for r in lp]
        at_min = sum(1 for x in depth if x <= DEPTH_MIN * 1000 + 1e-3)
        at_max = sum(1 for x in depth if x >= DEPTH_MAX * 1000 - 1e-3)
        err = [FOOT_TARGET_N - x for x in force]
        rms = (sum(e * e for e in err) / len(err)) ** 0.5
        print(f'  {side:5s} depth {min(depth):6.2f}..{max(depth):6.2f} mm (bounds {DEPTH_MIN*1000:.0f}..{DEPTH_MAX*1000:.0f}); '
              f'ticks at bound min {at_min} max {at_max}; force {min(force):5.1f}..{max(force):5.1f} N, rms error {rms:5.1f} N')
    total = [f(r, 'lFootN') + f(r, 'rFootN') for r in lp]
    print(f'  both feet {min(total):.1f}..{max(total):.1f} N (target sum {2*FOOT_TARGET_N:.0f})')

    print('\n3. wrist channels (degrees, rotation vector in joint space; demand = PD torque / ceiling)')
    D = ['lWristTwistDemand', 'lWristSwingDemand', 'rWristTwistDemand', 'rWristSwingDemand']
    sat = [r for r in lp if max(f(r, k) for k in D) > 0.95]
    print(f'  ticks with a wrist channel > 0.95: {len(sat)} of {len(lp)}')
    peak = max(lp, key=lambda r: max(f(r, k) for k in D))
    t0 = int(peak['tick'])
    for r in rows:
        t = int(r['tick'])
        if abs(t - t0) <= 4:
            def v(p):
                return '(' + ','.join(f'{f(r, p + a):6.2f}' for a in 'XYZ') + ')'
            print(f'  t{t} {r["phase"]:8s} pS {f(r,"pressS"):.3f} barY {f(r,"barY"):.4f} vy {f(r,"barVy"):+.3f} '
                  f'L req{v("lWristReq")} app{v("lWristApp")} real{v("lWristReal")} tw {f(r,"lWristTwistDemand"):.2f} sw {f(r,"lWristSwingDemand"):.2f} '
                  f'ceil {f(r,"lWristCeilingNm"):.0f} | R tw {f(r,"rWristTwistDemand"):.2f} sw {f(r,"rWristSwingDemand"):.2f} '
                  f'| sep {1000*f(r,"lGripSep"):.2f}/{1000*f(r,"rGripSep"):.2f} mm slip {1000*f(r,"lSlip"):.2f}/{1000*f(r,"rSlip"):.2f} mm')
    after = [r for r in lp if int(r['tick']) > t0]
    if after:
        print(f'  after the peak: min bar vy {min(f(r,"barVy") for r in after):+.3f} m/s until Rack; '
              f'max grip separation {max(max(f(r,"lGripSep"), f(r,"rGripSep")) for r in lp)*1000:.2f} mm over the lift; '
              f'max slip {max(max(f(r,"lSlip"), f(r,"rSlip")) for r in lp)*1000:.2f} mm (rule limit 10 mm)')


if __name__ == '__main__':
    main(sys.argv[1])
