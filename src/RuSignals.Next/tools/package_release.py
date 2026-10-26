"""Verify the renamed build and package it for manual installation."""
from pathlib import Path
from datetime import datetime,timezone
import hashlib,json,os,re,shutil,zipfile

ROOT=Path(__file__).resolve().parents[3]
VERSION='4.3.4'
SOURCE=ROOT/'src/RuSignals.Next'
BRIDGE=ROOT/'src/RuSignals.Next.RsgBridge'
REPORTS=ROOT/f'artifacts/ru-signals-{VERSION}'
# The previous brand is an external audit input, never a runtime alias.
FORMER=os.environ['RUSIGNALS_FORBIDDEN_BRAND']
OLD_NAME=re.compile('(?<![A-Za-z])(?:'+re.escape(FORMER.title())+'|'+re.escape(FORMER.upper())+'|'+re.escape(FORMER.lower())+'(?![a-z]))')

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def write(p,value):
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def require(value,message):
    if not value:raise RuntimeError(message)
def tree(root):
    return sorted(p for p in root.rglob('*') if p.is_file() and not {'bin','obj','__pycache__'}.intersection(p.relative_to(root).parts))
def scan_name(path,data):
    require(OLD_NAME.search(path) is None,'Former branding in path: '+path)
    if Path(path).suffix in {'.cs','.csproj','.py','.md','.json','.shader','.props','.ps1','.txt','.log'}:
        require(OLD_NAME.search(data.decode('utf-8-sig')) is None,'Former branding in text: '+path)
    if Path(path).suffix in {'.dll','.rsm'}:
        tokens=[FORMER.title(),FORMER.upper(),FORMER.lower()+'.signals']
        require(not any(token.encode(encoding) in data for token in tokens for encoding in ['ascii','utf-16le']),'Former branding in binary: '+path)

def main():
    info=read(SOURCE/'Info.json')
    require(info['Id']=='ru.signals' and info['Version']==VERSION and info['AssemblyName']=='RuSignals.dll' and info['EntryMethod']=='RuSignals.Mod.Load','Inconsistent mod identity')
    require(info['Requirements']==['DVSignals'] and info['Author']=='Herodrine_','Mod dependency or author changed')
    dll=SOURCE/'bin/Release/net48/RuSignals.dll';bridge=BRIDGE/'bin/Release/net48/RuSignals.RsgBridge.dll'
    checks={}
    required=read(SOURCE/'tools/qa-required-markers.json')
    for key,gates in required.items():
        folder=REPORTS/('' if key=='main' else key)
        result=read(folder/'unity-rewrite-verification.json')
        log=(folder/'unity-rewrite.log').read_text(encoding='utf-8-sig')
        require(result['dll_sha256']==sha(dll),'QA DLL differs from release')
        require(result['passed']>=gates['minimum_checks'],'Native QA checks missing: '+key)
        require(all(marker in log for marker in gates['markers']),'Native QA marker missing: '+key)
        require('Exception:' not in log and 'Found no leaked weakptrs.' in log,'Native QA did not exit cleanly: '+key)
        checks[key]=result['passed']
    rules=read(REPORTS/'rule-tests.json');blocks=read(REPORTS/'block-tests.json')
    require(rules['passed']==895 and blocks['passed']==830,'Rules or reservation checks missing')
    proof=read(REPORTS/'change-verification.json')
    require(proof['semaphore_mechanism_unchanged'] and proof['fixed_lanterns'] and proof['fixed_green_yellow_colours'] and proof['unchanged_assets']==158 and proof['other_production_inputs_unchanged'],'Unexpected logic or visual change')
    require(not proof['reviewed_asset_changes'],'C# platform fitting must preserve every source resource')
    require(proof['dll_sha256']==sha(dll) and proof['bridge_sha256']==sha(bridge),'Change proof covers another build')
    for name,value in proof['source_sha256'].items():require(sha(ROOT/name)==value,'Verified source changed: '+name)
    for name,value in proof['asset_sha256'].items():require(sha(SOURCE/'Assets'/name)==value,'Verified asset changed: '+name)
    mutation=read(REPORTS/'mutation-verification.json')
    require(mutation['mutations']==12 and mutation['all_detected'] and not mutation['active_sources_modified'],'Boundary regression mutation checks missing')
    for key in required:
        folder=REPORTS/('' if key=='main' else key)
        colours=read(folder/'semaphore-fixed-colours-verification.json')
        require(colours['pose_samples']==5184 and colours['red_visible_frames']==colours['colour_changes']==colours['wrong_final_states']==0 and colours['upper_colour']=='green' and colours['lower_colour']=='yellow','Native fixed green/yellow on/off proof missing')
        auxiliary=read(folder/'semaphore-auxiliary-removal-verification.json')
        require(auxiliary['unwanted_nodes']==auxiliary['unwanted_renderers']==0,'Removed auxiliary lantern still exists')
        platform=read(folder/'platform-support-verification.json')
        require(platform['passed'] and platform['supports']==platform['adjusted_supports']==platform['lower_contacts']==platform['upper_contacts']==2 and platform['unrelated_changed_vertices']==platform['changed_uvs']==0 and platform['indices_identical'] and platform['idempotent'] and platform['maximum_cap_shape_error_m']<.0001 and platform['maximum_upper_cap_centre_drift_m']<.0001 and platform['maximum_face_normal_error']<.001,'C# platform support repair is not verified')
        linkage=read(folder/'semaphore-fixed-mechanism-verification.json')
        require(linkage['pose_samples']==2796 and linkage['clones']==2 and linkage['failed_fixed_axle_checks']==linkage['failed_fixed_material_hinge_checks']==linkage['failed_rigid_vertical_rod_checks']==0,'Native fixed axle and material hinge proof missing')
        require(linkage['maximum_top_hinge_drift_in_arm_m']<.0001 and linkage['maximum_bottom_hinge_drift_in_drive_arm_m']<.0001 and linkage['maximum_rod_length_change_m']<.0001 and linkage['maximum_rod_horizontal_axis_offset_m']<.0001,'Native rigid vertical linkage tolerance failed')
        require(linkage['maximum_fixed_optical_window_mismatch_m']==0,'Fixed lantern moved')
        transmission=read(folder/'semaphore-8371-transmission-verification.json')
        require(transmission['pose_samples']==2796 and transmission['failed_transmission_checks']==transmission['failed_actual_roller_profile_contacts']==0 and transmission['maximum_wheel_axle_translation_m']==0 and transmission['maximum_opposed_wire_length_error_m']<.0001 and transmission['counterweight_checks']==6 and transmission['minimum_counterweight_rise_on_opening_m']>0,'Native common wheel transmission proof missing')
        optics=read(folder/'semaphore-8371-additional-verification.json')
        require(optics['fixed_lantern_instances']==4 and optics['forbidden_lens_mechanism_parts']==optics['actual_roller_opposite_drive_arm_collisions']==0,'Lens mechanism removal or opposite arm clearance failed')
        boundary=read(folder/'route-boundary-audit-verification.json')
        require(boundary['checks']==106 and boundary['actual_bogies'] and boundary['actual_crossing_colliders'] and boundary['both_directions'] and boundary['train_profiles']==4,'Native boundary audit missing')
        reverse=read(folder/'reverse-movement-verification.json')
        require(reverse['checks']==336 and reverse['profiles']==6 and reverse['directions']==2 and reverse['cycles_per_profile']==3 and reverse['actual_bogies_and_velocities'] and reverse['last_bogie_guard'] and reverse['manual_and_foreign_reservation_guards'] and reverse['live_conflict_release'],'Native reverse gameplay validation missing')
        reception=read(folder/'entry-approach-reservation-verification.json')
        require(reception['checks']==112 and reception['profiles']==2 and reception['directions']==2 and reception['native_update_and_reserve'] and reception['saved_footprint_guard'],'Native reception reservation proof missing')
        if key!='power-current':
            capture=read(folder/'dispatcher-interval-capture-verification.json')
            require(capture['checks']==27 and capture['unadapted_red_cases']==4 and capture['approach_excluded'] and capture['disjoint_occupancy'] and capture['manual_overlap_opposing_guards'],'Actual ADS interval compatibility proof missing')
        require(read(folder/'yellow-palette-verification.json')['checks']==395,'Yellow palette/GPU check missing')
        hover=read(folder/'hover-name-verification.json')
        require(hover['native_slots']==35 and hover['checks']==1081 and hover['foreign_packs_unchanged'] and hover['all_native_aspects_checked'] and hover['native_sprites_preserved'] and hover['original_name_sprite'] and hover['original_name_colour_and_order'] and hover['aspect_caption_blank'] and hover['shared_name_sprite'],'Original hover-name validation missing')
        entry=read(folder/'automatic-entry-verification.json')
        require(entry['native_route_checks']==13 and entry['native_terminal_profiles']==2 and entry['entry_without_reservation'] and entry['side_tracks_and_terminals'] and entry['points_and_occupation_guards'] and entry['conflicting_reservations_rejected'] and entry['other_roles_retain_requirement'] and entry['no_implicit_reservation'],'Native automatic-entry validation missing')
        route=read(folder/'route-hover-verification.json')
        require(route['displays']==9 and route['checks']==109 and route['transparent_gpu_background'] and route['digits_and_directions'] and route['shared_sprite'] and route['text_scale']==1.25 and route['native_layout_scale']==1 and route['plain_world_text'] and route['cached_hover_text'],'Scaled route hover validation missing')
        layout=read(folder/'route-hover-layout-verification.json')
        require(layout['checks']==4 and layout['card_width']==60 and layout['original_row_height']==60 and layout['centres_aligned'],'Native HUD layout validation missing')
        scale=read(folder/'route-text-scale-verification.json')
        require(scale['checks']==16 and scale['scale']==1.25 and scale['baseline_font_points']==24 and scale['enlarged_font_points']==30 and scale['digits_and_three_directions'],'Actual TMP percentage-size parser validation missing')
        mechanical=read(folder/'semaphore-hud-verification.json')
        require(mechanical['slots']==13 and mechanical['checks']==357 and mechanical['physical_lamp_colours_checked'] and mechanical['all_aspects_checked'],'Mechanical HUD validation missing')
        direction=read(folder/'route-direction-verification.json')
        require(direction['checks']==8 and direction['curved_mainline'] and direction['reverse_travel'] and direction['reversed_track_point_order'] and direction['stale_route_rejected'],'Native route direction validation missing')
    model=read(REPORTS/'semaphore-model-verification.json')
    require(model['passed'] and model['unchanged_assets']==158 and model['original_optical_assets_unchanged'] and model['original_rod_assets_unchanged'],'Semaphore model preservation audit missing')
    for name,value in model['current_mesh_sha256'].items():require(sha(SOURCE/'Assets'/name)==value,'Model proof covers another mesh')
    preservation=read(REPORTS/'semaphore-preservation-independent.json')
    require(preservation['passed'] and preservation['retained_triangle_attributes_exact'],'Independent retained geometry audit missing')
    for name,value in preservation['current_mesh_sha256'].items():require(sha(SOURCE/'Assets'/name)==value,'Independent preservation audit covers another mesh')
    fittings=read(REPORTS/'semaphore-floating-fittings-verification.json')
    require(fittings['passed'] and len(fittings['checked_fittings'])==14 and not fittings['floating_fittings'] and not fittings['new_or_reshaped_floating_fittings'] and len(fittings['lantern_guide_rails'])==4 and all(r['connected'] for r in fittings['lantern_guide_rails']) and fittings['retained_triangle_attributes_exact'] and not fittings['unexpected_changed_assets'],'Actual fixed head supports are not connected to the mast')
    require(len(fittings['lantern_cases'])==2 and all(r['connected'] and r['physical_support_path'] for r in fittings['lantern_cases']),'Actual lantern housings have no physical support path to the mast')
    clearance=read(REPORTS/'rod-static-clearance-independent.json')
    require(clearance['passed'] and not clearance['proposal_only'] and not clearance['actual_triangle_intersections'] and not clearance['roller_opposite_arm_triangle_intersections'] and len(clearance['continuous_static_rod_proof'])==2 and all(not r['overlapping_envelopes'] and r['conservative_continuous_minimum_gap_m']>0 for r in clearance['continuous_static_rod_proof']) and clearance['continuous_lower_roller_upper_arm_clearance_m']>0,'Continuous actual rod and follower clearance proof missing')
    source_files=[ROOT/'Directory.Build.props']
    for folder in [SOURCE,BRIDGE,ROOT/'tests/RuSignals.Next.Tests',ROOT/'tests/RuSignals.Next.Rules',ROOT/'tests/RuSignals.Next.Performance']:
        source_files.extend(tree(folder))
    for p in source_files:scan_name(p.relative_to(ROOT).as_posix(),p.read_bytes())
    for p in [dll,bridge]:scan_name(p.name,p.read_bytes())
    report=dict(version=VERSION,created_utc=datetime.now(timezone.utc).isoformat(),dll_sha256=sha(dll),bridge_sha256=sha(bridge),native_checks=checks,rules=rules['passed'],blocks=blocks['passed'],game_map_tested=False,multiplayer_modified=False,game_installed=False,fps_tested=False)
    write(REPORTS/'release-verification.json',report)
    stage=ROOT/f'dist/RuSignals-{VERSION}/RuSignals'
    require(not stage.exists(),'Release already staged; preserve immutable releases')
    stage.mkdir(parents=True)
    for name in ['Info.json','README_RU.md','ARCHITECTURE_RU.md','SIGNALLING_RU.md']:shutil.copy2(SOURCE/name,stage/name)
    shutil.copy2(SOURCE/'Content/REFERENCES_RU.md',stage/'SEMAPHORE_REFERENCES_RU.md')
    for p in [dll,bridge]:shutil.copy2(p,stage/p.name)
    shutil.copytree(SOURCE/'Assets',stage/'Assets')
    verification=stage/'verification';verification.mkdir()
    # Development logs contain machine paths; ship structured checks only.
    public=[REPORTS/name for name in ['release-verification.json','change-verification.json','reverse-movement-verification.json','route-boundary-audit-verification.json','mutation-verification.json','adapter-benchmarks.json','hover-name-verification.json','route-hover-verification.json','route-hover-layout-verification.json','route-text-scale-verification.json','semaphore-hud-verification.json','route-direction-verification.json','semaphore-model-verification.json','automatic-entry-verification.json','unity-rewrite-verification.json','rule-tests.json','block-tests.json','yellow-palette-verification.json','material-sharing-verification.json','lens-variation-verification.json','palette-reference-verification.json','native-optimization-verification.json','lossless-runtime-verification.json']]
    public.extend(REPORTS/name for name in ['entry-approach-reservation-verification.json','dispatcher-interval-capture-verification.json'])
    public.extend(REPORTS/name for name in ['semaphore-linkage-regression.json','semaphore-fixed-mechanism-verification.json','semaphore-8371-transmission-verification.json','semaphore-8371-additional-verification.json','rod-static-clearance-independent.json','semaphore-preservation-independent.json'])
    public.append(REPORTS/'semaphore-fixed-colours-verification.json')
    public.append(REPORTS/'semaphore-auxiliary-removal-verification.json')
    public.append(REPORTS/'semaphore-floating-fittings-verification.json')
    public.append(REPORTS/'platform-support-verification.json')
    for p in public:
        require(p.is_file(),'Report missing: '+p.name)
        scan_name(p.name,p.read_bytes());shutil.copy2(p,verification/p.name)
    manifest={p.relative_to(stage).as_posix():sha(p) for p in tree(stage)}
    write(stage/'package-manifest.json',dict(version=VERSION,sha256=manifest))
    for p in tree(stage):
        scan_name(p.relative_to(stage).as_posix(),p.read_bytes())
        require('changelog' not in p.name.casefold().replace('_','').replace('-',''),'Changelog must not be bundled')
    source_manifest=REPORTS/'source-manifest.json'
    write(source_manifest,dict(version=VERSION,sha256={p.relative_to(ROOT).as_posix():sha(p) for p in source_files}))
    qa_inputs=[REPORTS/name for name in ['palette-baseline-3.0.11.json','palette-baseline-3.0.14.json','material-reference.json']]
    for p in qa_inputs:scan_name(p.name,p.read_bytes())
    for archive,files,base in [(ROOT/f'dist/RuSignals-{VERSION}.zip',tree(stage),stage.parent),(ROOT/f'dist/RuSignals-{VERSION}-source.zip',source_files+public+qa_inputs+[source_manifest],ROOT)]:
        require(not archive.exists(),'Release already exists: '+archive.name)
        with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
            for p in files:z.write(p,p.relative_to(base).as_posix())
        with zipfile.ZipFile(archive) as z:
            require(z.testzip() is None,'Corrupt release ZIP')
            for p in files:require(hashlib.sha256(z.read(p.relative_to(base).as_posix())).hexdigest()==sha(p),'ZIP payload differs')
        print(archive.name+' SHA256 '+sha(archive))

if __name__=='__main__':main()
