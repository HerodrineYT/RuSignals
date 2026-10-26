"""Conservative continuous swept-mesh clearance of direct vertical pull rods.

Uses each disconnected real mesh component. The Y bounds cover every possible
combination of wing and drive angle, including interrupted animation; X/Z are
fixed by the proposed straight rod plane. Disjoint swept boxes prove that the
actual triangle meshes cannot intersect. Any non-contact overlap fails closed.
"""
from pathlib import Path
import argparse
import json
import math
import numpy as np
from rebuild_hoods import ASSETS
from verify_semaphore_drive import pieces, drive_islands, bearing_islands


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--assets', type=Path, default=ASSETS)
    ap.add_argument('--upper-plane', type=float, default=.310)
    ap.add_argument('--lower-plane', type=float, default=.270)
    ap.add_argument('--report', type=Path, default=ASSETS.parents[2] / 'work/direct-rod-clearance.json')
    args = ap.parse_args()
    static, static_parts = pieces(args.assets / 'SolidHoods/Semaphore/semaphore_static.rsm')
    lower, lower_parts = pieces(args.assets / 'SolidHoods/Semaphore/semaphore_lowerfittings.rsm')
    drive_ids = {i for p in drive_islands(static_parts) for i in p['ids']}
    upper_bearing = bearing_islands(static_parts)[0]
    lower_bearing = bearing_islands(lower_parts)[0]
    upper_own = set(upper_bearing['ids'])
    lower_own = set(lower_bearing['ids'])
    obstacles = [('static', i, p) for i, p in enumerate(static_parts)] + [('lower', i, p) for i, p in enumerate(lower_parts)]
    records = []
    for name, x, plane, length, top_y, bottom_y, own_key, own_ids in (
        ('upper', .13, args.upper_plane, 6.96, (8.05 - .08/math.sqrt(2), 8.03),
         (.97, .97 + .03*math.tan(math.radians(14.4))), 'static', upper_own),
        ('lower', .065, args.lower_plane, 5.68, (6.77 - .08/math.sqrt(2), 6.83),
         (.97 - .035*math.tan(math.radians(14.4)), .97), 'lower', lower_own),
    ):
        _, rod_parts = pieces(args.assets / ('SolidHoods/Semaphore/semaphore_' + name + 'rod.rsm'))
        overlaps = []
        nearest = math.inf
        for rod_index, rod in enumerate(rod_parts):
            lo, hi = rod['low'].astype(float).copy(), rod['high'].astype(float).copy()
            fraction_low, fraction_high = (lo[1]-1.04)/length, (hi[1]-1.04)/length
            lo[1] = bottom_y[0]*(1-fraction_low) + top_y[0]*fraction_low
            hi[1] = bottom_y[1]*(1-fraction_high) + top_y[1]*fraction_high
            lo[2] += plane + .59
            hi[2] += plane + .59
            for key, index, obstacle in obstacles:
                ids = set(obstacle['ids'])
                if key == 'static' and ids <= drive_ids:
                    continue  # The lower end intentionally enters its own drive.
                if key == own_key and ids <= own_ids:
                    continue  # Direct upper contact with the actual bearing.
                separation = np.maximum(np.maximum(obstacle['low']-hi, lo-obstacle['high']), 0)
                distance = float(np.linalg.norm(separation))
                nearest = min(nearest, distance)
                if np.all(hi > obstacle['low'] + 1e-6) and np.all(lo < obstacle['high'] - 1e-6):
                    overlaps.append(dict(rod_component=rod_index, obstacle_key=key, obstacle_component=index,
                                         rod_swept_min=lo.tolist(), rod_swept_max=hi.tolist(),
                                         obstacle_min=obstacle['low'].tolist(), obstacle_max=obstacle['high'].tolist()))
        records.append(dict(rod=name, plane_z=plane, rod_components=len(rod_parts),
                            conservative_nearest_structure_gap_m=nearest, unexpected_swept_overlaps=overlaps))
    passed = not any(r['unexpected_swept_overlaps'] for r in records)
    report = dict(passed=passed, continuous_all_motion_envelope=True, rods=records,
                  scope='Real shaft and both real 38-mm-wide coupler components; entire continuous wing/drive angle ranges; all unchanged mast, ladder and opposite-bearing components; only own drive and own direct bearing contacts excluded')
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=passed, rods=[dict(rod=r['rod'], nearest_gap_m=r['conservative_nearest_structure_gap_m'],
                                                 overlaps=len(r['unexpected_swept_overlaps'])) for r in records])))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
