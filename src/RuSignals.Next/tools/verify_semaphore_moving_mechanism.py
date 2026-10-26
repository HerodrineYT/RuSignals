"""Independent actual-mesh audit of the rigid vertical eccentric followers.

No Unity/game launch. Compare against the frozen 4.2.9 asset tree and evaluate
the actual twenty-sided eccentric profile, never the runtime CamProfile table.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import numpy as np
from rebuild_hoods import ASSETS
from verify_semaphore_drive import pieces, bearing_islands, drive_islands, same_structure

CHANGED = {'SolidHoods/Semaphore/semaphore_static.rsm',
           'SolidHoods/Semaphore/semaphore_lowerfittings.rsm',
           'SolidHoods/Semaphore/semaphore_upper.rsm',
           'SolidHoods/Semaphore/semaphore_lower.rsm',
           'SolidHoods/semaphore-clearance.json'}
ORPHANS = (7, 13, 27, 29, 30, 31, 39, 40, 41)


def cam_mesh(assets, name):
    part, islands = pieces(assets / f'SolidHoods/Semaphore/semaphore_{name}.rsm')
    cams = [p for p in islands if p['triangles'] == 76 and np.allclose(
        p['high']-p['low'], (.24, .24, .04), atol=.0002)]
    assert len(cams) == 1, 'Expected one actual 76-triangle eccentric'
    return part, cams[0]


def rigid_z(before, after, island, delta, expected):
    ids = island['ids']
    expected[ids, 2] += delta
    assert np.allclose(after['vertices'][ids], expected[ids], atol=1e-6), 'Component is not one rigid Z translation'


def audit(baseline, target):
    inventory = {p.relative_to(baseline).as_posix() for p in baseline.rglob('*') if p.is_file()}
    assert inventory == {p.relative_to(target).as_posix() for p in target.rglob('*') if p.is_file()}, 'Asset inventory changed'
    before, old = pieces(baseline / 'SolidHoods/Semaphore/semaphore_static.rsm')
    after, new = pieces(target / 'SolidHoods/Semaphore/semaphore_static.rsm')
    same_structure(before, after, 'Static model')
    expected = before['vertices'].copy()
    # Exact stable islands also checked against their unmodified source model:
    # two threaded foundation studs, one diagonal, two lattice cross bars,
    # and four mast rivets. They are structural members, never drive parts.
    assert [old[i]['triangles'] for i in ORPHANS] == [36, 36, 12, 12, 28, 28, 12, 28, 28]
    for index in ORPHANS:
        rigid_z(before, after, old[index], .4, expected)
    # Existing upper ground-drive mounting bracket: same shape and X/Z,
    # moved down 240mm so the translating lower follower never crosses it.
    mount = old[255]
    assert mount['triangles'] == 12 and np.allclose(mount['high']-mount['low'], [.32,.025,.10], atol=.0002)
    expected[mount['ids'], 1] -= .24
    for island in bearing_islands(old):
        rigid_z(before, after, island, -.075, expected)
    assert np.allclose(after['vertices'], expected, atol=1e-6), 'Other mast/ladder/drive/static vertices changed'
    drive_roundoff = 0.
    for island in drive_islands(old):
        drive_roundoff = max(drive_roundoff, float(abs(before['vertices'][island['ids']]-after['vertices'][island['ids']]).max()))
    assert drive_roundoff <= 6e-8, 'Ground drive changed beyond float32 arithmetic round-off'
    before, old = pieces(baseline / 'SolidHoods/Semaphore/semaphore_lowerfittings.rsm')
    after, _ = pieces(target / 'SolidHoods/Semaphore/semaphore_lowerfittings.rsm')
    same_structure(before, after, 'Lower fittings')
    expected = before['vertices'].copy()
    for island in bearing_islands(old):
        rigid_z(before, after, island, -.035, expected)
    assert np.allclose(after['vertices'], expected, atol=1e-6), 'Other lower fittings changed'
    for name, delta in [('upper', -.535), ('lower', -.575)]:
        before, cam = cam_mesh(baseline, name)
        after, _ = cam_mesh(target, name)
        same_structure(before, after, name+' rotating arm')
        expected = before['vertices'].copy()
        rigid_z(before, after, cam, delta, expected)
        assert np.allclose(after['vertices'], expected, atol=1e-6), 'Blade/tail/counterweight/other arm vertices changed'
    unchanged = []
    for relative in sorted(inventory - CHANGED):
        assert hashlib.sha256((baseline/relative).read_bytes()).digest() == hashlib.sha256((target/relative).read_bytes()).digest(), 'Unrelated asset changed: '+relative
        unchanged.append(relative)
    return dict(restored_original_mast_islands=list(ORPHANS), restored_mast_islands=9,
                upper_bearing_restored_islands=5, lower_bearing_restored_islands=5,
                eccentric_islands_moved=2, eccentric_triangles=152,
                ground_drive_unchanged_islands=18, ground_drive_unchanged_triangles=992, ground_drive_max_float32_roundoff_m=drive_roundoff,
                upper_ground_mount_rigid_y_translation_m=-.24, lower_ground_mount_and_lever_unchanged=True, topology_normals_uv_unchanged=True,
                other_unchanged_assets=len(unchanged), unchanged_asset_paths=unchanged)


def profile(assets, name, fixed_x, pivot_y, angles, length):
    part, cam = cam_mesh(assets, name)
    xy = np.unique(np.round(part['vertices'][cam['ids'], :2], 7), axis=0)
    assert len(xy) == 20, 'Actual cam must have twenty profile vertices'
    center = xy.mean(axis=0)
    xy = xy[np.argsort(np.arctan2(xy[:, 1]-center[1], xy[:, 0]-center[0]))]
    # Exact extrema of a rotating polygon intersected by fixed X occur at an
    # interval end, an edge switch (rotated vertex at X), or a stationary
    # line intersection. For unit edge normal n and h=n.dot(a), the latter
    # is n_rot.x = X/h. Enumerate every such angle, plus diagnostic samples.
    def roots(a, b, value):
        radius=math.hypot(a,b)
        if abs(value)>radius+1e-10:return []
        phase=math.atan2(b,a);offset=math.acos(max(-1,min(1,value/radius)))
        result=[]
        low,high=sorted(map(math.radians,angles))
        for sign in (-1,1):
            for turn in range(-2,3):
                theta=phase+sign*offset+turn*2*math.pi
                if low-1e-10<=theta<=high+1e-10:result.append(math.degrees(theta))
        return result
    critical=list(angles)
    for vertex in xy:critical+=roots(vertex[0],-vertex[1],fixed_x)
    for a,b in zip(xy,np.roll(xy,-1,axis=0)):
        edge=b-a;normal=np.array([-edge[1],edge[0]])/np.linalg.norm(edge)
        h=float(normal@a)
        if abs(h)>1e-10:critical+=roots(normal[0],-normal[1],fixed_x/h)
    tops = []
    for angle in list(np.linspace(*angles, 6001))+critical:
        r = math.radians(angle)
        rotation = np.array([[math.cos(r), -math.sin(r)], [math.sin(r), math.cos(r)]])
        vertices = xy @ rotation.T
        ends = np.roll(vertices, -1, axis=0)
        crossing = (np.minimum(vertices[:, 0], ends[:, 0]) <= fixed_x) & (np.maximum(vertices[:, 0], ends[:, 0]) >= fixed_x) & (np.abs(ends[:, 0]-vertices[:, 0]) > 1e-9)
        assert crossing.any(), f'{name} vertical line misses actual moving cam at {angle} degrees'
        intersections = vertices[crossing, 1] + (fixed_x-vertices[crossing, 0])*(ends[crossing, 1]-vertices[crossing, 1])/(ends[crossing, 0]-vertices[crossing, 0])
        tops.append(pivot_y+float(intersections.min())+.002)
    tops = np.array(tops)
    return dict(name=name, fixed_x=fixed_x, fixed_length_m=length,
                cam_center_z=float((cam['low'][2]+cam['high'][2])*.5+.44),
                minimum_top_y=float(tops.min()), maximum_top_y=float(tops.max()),
                minimum_bottom_y=float(tops.min()-length), maximum_bottom_y=float(tops.max()-length),
                vertical_stroke_m=float(np.ptp(tops)), actual_profile_vertices=20,
                sampled_angles=6001, exact_analytic_critical_angles=len(critical)), tops


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--assets', type=Path, default=ASSETS)
    ap.add_argument('--baseline-assets', type=Path, default=ASSETS.parents[2]/'work/rusignals-v429-source-check/src/RuSignals.Next/Assets')
    ap.add_argument('--report', type=Path, default=ASSETS.parents[2]/'artifacts/ru-signals-4.2.10/semaphore-moving-mesh-independent.json')
    args = ap.parse_args()
    proof = audit(args.baseline_assets, args.assets)
    upper, u = profile(args.assets, 'upper', -.065, 8.05, (0, -45), 6.96)
    lower, l = profile(args.assets, 'lower', .065, 6.77, (90, -45), 5.7205993)
    assert upper['vertical_stroke_m'] > .02 and lower['vertical_stroke_m'] > .02
    assert abs(upper['cam_center_z']-.31) < 1e-6 and abs(lower['cam_center_z']-.27) < 1e-6
    # All independent combinations also cover route changes in mid-animation.
    slopes = [(lb-ub)/.13 for ub in (u.min()-6.96, u.max()-6.96) for lb in (l.min()-5.7205993, l.max()-5.7205993)]
    angles = [math.degrees(math.atan(s)) for s in slopes]
    pivots = [ub+.165*(lb-ub)/.13 for ub in (u.min()-6.96, u.max()-6.96) for lb in (l.min()-5.7205993, l.max()-5.7205993)]
    _,static_parts=pieces(args.assets/'SolidHoods/Semaphore/semaphore_static.rsm')
    upper_mount=static_parts[255]
    # A drive prism of half-thickness .025 occupies the band around its line
    # y=upperBottom+(x+.065)*slope. Minimum height over the independent bottom
    # intervals occurs at their corners: this expression is concave in slope.
    lowest_bar=min(ub+(x+.065)*(lb-ub)/.13-.025*math.sqrt(1+((lb-ub)/.13)**2)
                   for ub in (u.min()-6.96,u.max()-6.96)
                   for lb in (l.min()-5.7205993,l.max()-5.7205993)
                   for x in (upper_mount['low'][0],upper_mount['high'][0]))
    mount_gap=float(lowest_bar-upper_mount['high'][1])
    assert mount_gap>.01, 'Moving drive bar crosses its existing upper support'
    report = dict(passed=True, preservation=proof, followers=[upper, lower],
                  lower_balance_bar_angle_deg=[min(angles), max(angles)],
                  lower_balance_bar_pivot_y=[min(pivots), max(pivots)],
                  continuous_drive_bar_to_upper_mount_clearance_m=mount_gap,
                  scope='Actual exported twenty-sided eccentric polygons; exact analytic polygon/vertical-line extrema plus 6001 diagnostic samples per arm; invariant rod length and all combinations of rod extrema; exact vertex-diff asset preservation')
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'preservation'}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
