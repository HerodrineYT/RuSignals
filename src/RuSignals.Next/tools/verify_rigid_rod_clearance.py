"""Continuous conservative collision proof for rigid eccentric follower rods.

Uses actual exported components. Full rotation-disk envelopes deliberately
overestimate all possible cam/wing angles, so disjoint boxes prove triangle
clearance between samples as well. Own lower drive and own upper eccentric
are the only intentional contacts excluded.
"""
import argparse
import json
from pathlib import Path
import numpy as np
from rebuild_hoods import ASSETS
from verify_semaphore_drive import pieces, drive_islands
from verify_semaphore_moving_mechanism import cam_mesh, profile


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--assets', type=Path, default=ASSETS)
    ap.add_argument('--report', type=Path, default=ASSETS.parents[2]/'artifacts/ru-signals-4.2.10/rigid-rod-continuous-clearance-independent.json')
    args = ap.parse_args()
    static, static_parts = pieces(args.assets/'SolidHoods/Semaphore/semaphore_static.rsm')
    _, lower_parts = pieces(args.assets/'SolidHoods/Semaphore/semaphore_lowerfittings.rsm')
    own_drive = {tuple(p['ids']) for p in drive_islands(static_parts)}
    obstacles = [('static', i, p) for i, p in enumerate(static_parts) if tuple(p['ids']) not in own_drive]
    obstacles += [('lowerfittings', i, p) for i, p in enumerate(lower_parts)]
    manifest = json.loads((args.assets/'Semaphore/semaphore_manifest.json').read_text(encoding='utf-8'))
    for item in manifest['parts']:
        if item['parent'] not in ('UpperArmPivot', 'LowerArmPivot'):
            continue
        part, parts = pieces(args.assets/('SolidHoods/Semaphore/'+item['key']+'.rsm'))
        pivot = np.array(item['pivot'])
        for index, mesh in enumerate(parts):
            vertices = part['vertices'][mesh['ids'], :3]
            radius = np.linalg.norm(vertices[:, :2], axis=1).max()
            lo = np.array([-radius, -radius, mesh['low'][2]])+pivot
            hi = np.array([radius, radius, mesh['high'][2]])+pivot
            obstacles.append((item['key'], index, dict(ids=mesh['ids'], low=lo, high=hi)))
    results = []
    for name, source_x, x, length, pivot_y, plane in [
        ('upper', .13, -.065, 6.96, 8.05, .31),
        ('lower', .065, .065, 5.7205993, 6.77, .27),
    ]:
        source_length = 6.96 if name == 'upper' else 5.68
        arm, cam = cam_mesh(args.assets, name)
        cam_radius = float(np.linalg.norm(arm['vertices'][cam['ids'], :2], axis=1).max())
        angles=(0,-45) if name=='upper' else (90,-45)
        actual_profile,_=profile(args.assets,name,x,pivot_y,angles,length)
        # Exact analytic extrema include edge switches and stationary polygon
        # intersections. Extra 10um padding includes float32 export rounding.
        top_min=actual_profile['minimum_top_y']-.00001
        top_max=actual_profile['maximum_top_y']+.00001
        _, rod_parts = pieces(args.assets/f'SolidHoods/Semaphore/semaphore_{name}rod.rsm')
        overlaps = []
        nearest = float('inf')
        for rod_index, rod in enumerate(rod_parts):
            lo, hi = rod['low'].astype(float).copy(), rod['high'].astype(float).copy()
            lo[0] += x-source_x;hi[0] += x-source_x
            lo[1] = top_min-length+(lo[1]-1.04)*length/source_length
            hi[1] = top_max-length+(hi[1]-1.04)*length/source_length
            lo[2] += plane+.59;hi[2] += plane+.59
            for key, index, obstacle in obstacles:
                if key == 'semaphore_'+name and set(obstacle['ids']) == set(cam['ids']):
                    continue
                separation = np.maximum(np.maximum(obstacle['low']-hi, lo-obstacle['high']), 0)
                nearest = min(nearest, float(np.linalg.norm(separation)))
                if np.all(hi > obstacle['low']+1e-6) and np.all(lo < obstacle['high']-1e-6):
                    overlaps.append(dict(rod_component=rod_index, obstacle_key=key, obstacle_component=index,
                                         rod_swept_min=lo.tolist(), rod_swept_max=hi.tolist(),
                                         obstacle_min=obstacle['low'].tolist(), obstacle_max=obstacle['high'].tolist()))
        results.append(dict(rod=name, target_x=x, plane_z=plane, conservative_minimum_clearance_m=nearest,
                            whole_rotation_cam_radius_m=cam_radius, rod_extrema='exact analytic polygon intersections', unexpected_swept_overlaps=overlaps))
    passed = not any(p['unexpected_swept_overlaps'] for p in results)
    report = dict(passed=passed, continuous_entire_rotation_envelope=True, rods=results,
                  scope='Actual shaft and both turnbuckles, every stationary component except own lower drive, every wing/tail/glass/counterweight and opposite eccentric across complete rotation disk; own cam and lower drive are intentional contacts; no sample gaps')
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(dict(passed=passed, rods=[dict(rod=p['rod'], nearest_gap_m=p['conservative_minimum_clearance_m'], overlaps=len(p['unexpected_swept_overlaps'])) for p in results])))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
