"""Check the actual lower drive-to-mast clearance without launching Unity."""
from pathlib import Path
import argparse
import hashlib
import json
import numpy as np
from rebuild_hoods import ASSETS, read, components

ROOT = ASSETS.parents[2]
CHANGED = {'SolidHoods/Semaphore/semaphore_static.rsm', 'SolidHoods/Semaphore/semaphore_lowerfittings.rsm', 'SolidHoods/Semaphore/semaphore_lever.rsm',
           'SolidHoods/semaphore-clearance.json'}
DRIVE_SHAPES = (
    ('case', 44, (.38, .47, .23), 1),
    ('cover', 44, (.32, .34, .027), 1),
    ('cover bolt', 28, (.03, .03, .014), 4),
    ('hand wheel', 288, (.282, .282, .0312), 2),
    ('wheel hub', 60, (.08, .08, .12), 2),
    ('horizontal spoke', 12, (.11, .015, .015), 2),
    ('diagonal spoke', 12, (.068, .1028, .015), 4),
    ('control wire', 12, (.004, .55, .004), 2),
)


def pieces(path):
    part, = read(path)
    result = []
    for item in components(dict(vertices=part['vertices'].tolist(), indices=part['indices'].reshape(-1).tolist())):
        ids = sorted(set(item['indices']))
        vertices = part['vertices'][ids]
        result.append(dict(ids=ids, triangles=len(item['indices']) // 3,
                           low=vertices[:, :3].min(0), high=vertices[:, :3].max(0)))
    return part, result


def drive_islands(islands):
    selected = []
    for name, triangles, size, count in DRIVE_SHAPES:
        matches = [dict(p, role=name) for p in islands
                   if p['triangles'] == triangles
                   and np.allclose(p['high'] - p['low'], size, atol=.0002)
                   and p['low'][0] > -.10 and p['high'][0] < .40
                   and p['low'][1] > .33 and p['high'][1] < 1.12]
        assert len(matches) == count, f'{name}: expected {count} independently identified components, found {len(matches)}'
        selected.extend(matches)
    assert len(selected) == 18 and sum(p['triangles'] for p in selected) == 992
    return selected


def same_structure(a, b, label):
    assert a['name'] == b['name'] and a['texture'] == b['texture'] and a['cutout'] == b['cutout'], label + ': part or material changed'
    assert a['vertices'].shape == b['vertices'].shape and np.array_equal(a['indices'], b['indices']), label + ': topology changed'
    assert np.array_equal(a['vertices'][:, 3:], b['vertices'][:, 3:]), label + ': normals or UV changed'


def bearing_islands(islands):
    housing = [p for p in islands if p['triangles'] == 44 and np.allclose(p['high'] - p['low'], [.29, .20, .23], atol=.0002)]
    assert len(housing) == 1, 'One existing bearing housing must be identified'
    middle_y = (housing[0]['low'][1] + housing[0]['high'][1]) / 2
    bolts = [p for p in islands if p['triangles'] == 20
             and np.allclose(p['high'] - p['low'], [.0294, .034, .02], atol=.0002)
             and np.isclose(abs((p['low'][0] + p['high'][0]) / 2), .10, atol=.0002)
             and np.isclose(abs((p['low'][1] + p['high'][1]) / 2 - middle_y), .065, atol=.0002)]
    assert len(bolts) == 4, 'Exactly four existing bearing fasteners must be identified'
    return housing + bolts


def audit(baseline, target):
    assert {p.relative_to(baseline).as_posix() for p in baseline.rglob('*') if p.is_file()} == {
        p.relative_to(target).as_posix() for p in target.rglob('*') if p.is_file()}, 'Asset file inventory changed'
    base_part, base_pieces = pieces(baseline / 'SolidHoods/Semaphore/semaphore_static.rsm')
    actual_part, actual_pieces = pieces(target / 'SolidHoods/Semaphore/semaphore_static.rsm')
    base_drive, actual_drive = drive_islands(base_pieces), drive_islands(actual_pieces)
    base_case = next(p for p in base_drive if p['role'] == 'case')
    actual_case = next(p for p in actual_drive if p['role'] == 'case')
    delta = float(actual_case['low'][2] - base_case['low'][2])
    same_structure(base_part, actual_part, 'Static mast and mechanism')
    original, actual = base_part['vertices'], actual_part['vertices']
    assert np.array_equal(original[:, :2], actual[:, :2]), 'Static model X/Y positions changed'
    expected = original.copy()
    moved_ids = set()
    for part in base_drive:
        ids = part['ids']
        expected[ids, 2] += delta
        moved_ids.update(ids)
        assert np.allclose(actual[ids, :3], expected[ids, :3], atol=.000001), part['role'] + ': not one rigid common Z translation'
    base_bearing = bearing_islands(base_pieces)
    actual_bearing = bearing_islands(actual_pieces)
    bearing_delta = float(actual_bearing[0]['low'][2] - base_bearing[0]['low'][2])
    for part in base_bearing:
        ids = part['ids']
        expected[ids, 2] += bearing_delta
        moved_ids.update(ids)
        assert np.allclose(actual[ids, :3], expected[ids, :3], atol=.000001), 'Upper bearing housing/fasteners did not undergo the same rigid translation'
    brackets = [p for p in base_pieces if p['triangles'] == 12
                and np.allclose(p['high'] - p['low'], [.32, .025, .28], atol=.0002)
                and np.isclose((p['low'][0] + p['high'][0]) / 2, .10, atol=.0002)
                and any(np.isclose((p['low'][1] + p['high'][1]) / 2, y, atol=.0002) for y in (.5725, .9125))]
    assert len(brackets) == 2, 'Exactly two existing drive support brackets must be identified'
    for bracket in brackets:
        ids = bracket['ids']
        rear, front = bracket['low'][2], bracket['high'][2]
        new_rear, new_front = actual[ids, 2].min(), actual[ids, 2].max()
        expected[ids, 2] = new_rear + (original[ids, 2] - rear) * (new_front - new_rear) / (front - rear)
        moved_ids.update(ids)
        mast_front = max(float(p['high'][2]) for p in base_pieces if p['high'][1] - p['low'][1] > 7.5 and p['low'][2] > -.25 and p['high'][2] < .25)
        assert new_rear < mast_front and new_front > actual_case['low'][2], 'Refitted support does not span the existing mast front and case rear'
        assert np.allclose(actual[ids, :3], expected[ids, :3], atol=.000001), 'Support bracket was changed other than along Z'
    untouched = np.ones(len(original), dtype=bool)
    untouched[list(moved_ids)] = False
    assert np.array_equal(original[untouched], actual[untouched]), 'Geometry outside the 18 drive islands, five bearing islands and two brackets changed (including mast/ladder/cross-braces)'
    assert np.allclose(actual[:, :3], expected[:, :3], atol=.000001), 'Unexpected static mesh vertex change'
    old_lever, = read(baseline / 'SolidHoods/Semaphore/semaphore_lever.rsm')
    new_lever, = read(target / 'SolidHoods/Semaphore/semaphore_lever.rsm')
    same_structure(old_lever, new_lever, 'Drive lever')
    lever_expected = old_lever['vertices'][:, :3].copy()
    lever_delta = float(new_lever['vertices'][0, 2] - old_lever['vertices'][0, 2])
    lever_expected[:, 2] += lever_delta
    assert np.allclose(new_lever['vertices'][:, :3], lever_expected, atol=.000001), 'Drive lever was not one rigid Z translation'
    old_lower, old_lower_pieces = pieces(baseline / 'SolidHoods/Semaphore/semaphore_lowerfittings.rsm')
    new_lower, new_lower_pieces = pieces(target / 'SolidHoods/Semaphore/semaphore_lowerfittings.rsm')
    same_structure(old_lower, new_lower, 'Lower bearing fittings')
    old_lower_bearing, new_lower_bearing = bearing_islands(old_lower_pieces), bearing_islands(new_lower_pieces)
    lower_delta = float(new_lower_bearing[0]['low'][2] - old_lower_bearing[0]['low'][2])
    expected_lower = old_lower['vertices'].copy()
    lower_ids = set()
    for part in old_lower_bearing:
        expected_lower[part['ids'], 2] += lower_delta
        lower_ids.update(part['ids'])
    untouched_lower = np.ones(len(expected_lower), dtype=bool)
    untouched_lower[list(lower_ids)] = False
    assert np.array_equal(old_lower['vertices'][untouched_lower], new_lower['vertices'][untouched_lower]), 'Lower shaft/other hardware changed'
    assert np.allclose(expected_lower[:, :3], new_lower['vertices'][:, :3], atol=.000001), 'Lower housing/bolts did not undergo one rigid translation'
    unchanged = []
    for before in sorted(p for p in baseline.rglob('*') if p.is_file()):
        relative = before.relative_to(baseline).as_posix()
        if relative in CHANGED:
            continue
        after = target / relative
        assert after.is_file() and hashlib.sha256(before.read_bytes()).digest() == hashlib.sha256(after.read_bytes()).digest(), 'Unrelated asset changed: ' + relative
        unchanged.append(relative)
    return dict(drive_islands=18, drive_triangles=992, rigid_drive_delta_z_m=delta,
                upper_bearing_islands=5, lower_bearing_islands=5, bearing_triangles=248,
                upper_bearing_delta_z_m=bearing_delta, lower_bearing_delta_z_m=lower_delta,
                total_rigid_islands=28, total_rigid_triangles=1240, lever_delta_z_m=lever_delta,
                drive_support_brackets=2, static_vertices_untouched=int(untouched.sum()),
                lower_fitting_vertices_untouched=int(untouched_lower.sum()),
                mast_ladder_cross_braces_preserved=True, topology_normals_uv_materials_preserved=True,
                other_unchanged_assets=len(unchanged), unchanged_asset_paths=unchanged,
                drive_component_roles={name: count for name, _, _, count in DRIVE_SHAPES})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--assets', type=Path, default=ASSETS)
    ap.add_argument('--baseline-assets', type=Path, default=ROOT / 'dist/RuSignals-4.2.7/RuSignals/Assets')
    ap.add_argument('--report', type=Path, default=ASSETS.parents[2] / 'work/semaphore-drive-clearance.json')
    args = ap.parse_args()
    _, islands = pieces(args.assets / 'SolidHoods/Semaphore/semaphore_static.rsm')
    bodies = [p for p in islands if p['triangles'] == 44 and np.allclose(p['high'] - p['low'], [.38, .47, .23], atol=.0002)]
    assert len(bodies) == 1, 'The actual 380 x 470 x 230 mm drive case must be unique'
    body, = bodies
    legs = [p for p in islands if p['high'][1] - p['low'][1] > 7.5 and p['low'][2] > -.25 and p['high'][2] < .25]
    assert len(legs) == 8, 'Expected eight original mast-angle mesh components'
    mast_front = max(float(p['high'][2]) for p in legs)
    gap = min(float(p['low'][2]) for p in drive_islands(islands)) - mast_front
    passed = .01 <= gap <= .03
    report = dict(passed=passed, drive_case_min=body['low'].tolist(), drive_case_max=body['high'].tolist(),
                  mast_front_z=mast_front, drive_side='front', actual_gap_m=gap, required_minimum_gap_m=.01,
                  required_maximum_gap_m=.03,
                  scope='Actual disconnected drive-case and mast-angle components, identified by shape; no fitting anchors or runtime pose oracle')
    report['preservation_audit'] = audit(args.baseline_assets, args.assets)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=passed, actual_gap_m=gap,
                          drive_islands=report['preservation_audit']['drive_islands'],
                          drive_triangles=report['preservation_audit']['drive_triangles'],
                          delta_z_m=report['preservation_audit']['rigid_drive_delta_z_m'],
                          other_unchanged_assets=report['preservation_audit']['other_unchanged_assets'])))
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
