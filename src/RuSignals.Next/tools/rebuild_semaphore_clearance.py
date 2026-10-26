"""Separate semaphore equipment without changing the source assets or animation axes."""
import json
import numpy as np
from rebuild_hoods import ASSETS, OUTPUT, read, write, components, digest

DRIVE_FORWARD=.554858808
LEVER_FORWARD=.860

def relocate_drive(part):
    # Identify complete existing drive islands by shape, never by a broad
    # low-height selection (which also includes mast braces and ladder bolts).
    pieces=components(dict(vertices=part['vertices'].tolist(),indices=part['indices'].reshape(-1).tolist()))
    shapes=((44,(.38,.47,.23),1),(44,(.32,.34,.027),1),
            (28,(.03,.03,.014),4),(288,(.282,.282,.0312),2),
            (60,(.08,.08,.12),2),(12,(.11,.015,.015),2),
            (12,(.068,.1028,.015),4),(12,(.004,.55,.004),2))
    selected=[]
    for triangles,size,count in shapes:
        matches=[p for p in pieces if len(p['indices'])//3==triangles
                 and np.allclose(np.array(p['max'])-p['min'],size,atol=.0002)
                 and p['min'][0]>-.10 and p['max'][0]<.40
                 and p['min'][1]>.33 and p['max'][1]<1.12]
        assert len(matches)==count,('Unexpected lower drive shape',size,len(matches))
        selected.extend(matches)
    assert len(selected)==18 and sum(len(p['indices'])//3 for p in selected)==992
    for piece in selected:
        part['vertices'][list(set(piece['indices'])),2]+=DRIVE_FORWARD
    # Refit the existing two mounts to the front mast face and drive case.
    mounts=[p for p in pieces if len(p['indices'])//3==12
            and np.allclose(np.array(p['max'])-p['min'],(.32,.025,.28),atol=.0002)
            and np.isclose((p['min'][0]+p['max'][0])*.5,.10,atol=.0002)
            and any(np.isclose((p['min'][1]+p['max'][1])*.5,y,atol=.0002)for y in (.5725,.9125))]
    assert len(mounts)==2,'Unexpected lower drive mounts'
    for piece in mounts:
        ids=list(set(piece['indices']));z=part['vertices'][ids,2]
        rear,front=z.min(),z.max()
        part['vertices'][ids,2]=.20+(z-rear)*(.30-.20)/(front-rear)
        if (piece['min'][1]+piece['max'][1])*.5>.8:part['vertices'][ids,1]-=.24

def bracket(part,lo,hi):
    points=np.array([[x,y,z]for x in (lo[0],hi[0])for y in (lo[1],hi[1])for z in (lo[2],hi[2])])
    quads=((0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3))
    vertices=[];faces=[];uv=part['vertices'][0,6:8]
    for ids in quads:
        p=points[list(ids)];n=np.cross(p[1]-p[0],p[2]-p[0]);n/=np.linalg.norm(n);base=len(vertices)
        vertices.extend([*v,*n,*uv]for v in p);faces.extend(([base,base+1,base+2],[base,base+2,base+3]))
    base=len(part['vertices']);part['vertices']=np.concatenate((part['vertices'],np.array(vertices)))
    part['indices']=np.concatenate((part['indices'],np.array(faces)+base))

def main():
    manifest=json.loads((ASSETS/'Semaphore/semaphore_manifest.json').read_text(encoding='utf-8'))
    records={}
    restored_front_mast=0
    for item in manifest['parts']:
        key='Semaphore/'+item['key'];source=ASSETS/(key+'.rsm');parts=read(source);moved=0;removed_plate=0
        for part in parts:
            if item['key']=='semaphore_static':
                # The old letter H belongs to one separate plate in the mast
                # mesh, below the actual signal name. Remove just that island.
                remove=set()
                for piece in components(dict(vertices=part['vertices'].tolist(),indices=part['indices'].reshape(-1).tolist())):
                    lo,hi=piece['min'],piece['max']
                    if lo[1]>1.93 and hi[1]<2.27 and lo[2]>.24 and hi[2]<.28 and max(abs(lo[0]),abs(hi[0]))<.13:
                        remove.update(piece['indices']);removed_plate+=len(piece['indices'])//3
                if remove:
                    faces=part['indices'][~np.isin(part['indices'],list(remove)).any(axis=1)]
                    used,inverse=np.unique(faces,return_inverse=True)
                    part['vertices']=part['vertices'][used];part['indices']=inverse.reshape(-1,3)
            v=part['vertices']
            if item['key']=='semaphore_static':
                for piece in components(dict(vertices=v.tolist(),indices=part['indices'].reshape(-1).tolist())):
                    ids=list(set(piece['indices']));lo=np.array(piece['min']);hi=np.array(piece['max']);size=hi-lo
                    # Rungs and rails now sit behind, rather than inside, the drive.
                    if hi[2]<-.34 and lo[1]>.35:
                        v[ids,2]-=.55;moved+=1
                    elif lo[2]<-.38 and hi[2]<-.12 and .1<size[0]<.2 and size[1]<.03:
                        t=np.clip((-.125-v[ids,2])/.255,0,1);v[ids,2]-=.55*t;moved+=1
                    # Stretch the rear service platform to meet the relocated ladder.
                    elif lo[1]>6.9 and hi[1]<7.18 and lo[2]<-.44:
                        t=np.clip((-.10-v[ids,2])/.36,0,1);v[ids,2]-=.55*t;moved+=1
                    elif lo[1]>7.1 and hi[1]<8 and lo[2]>-.05:
                        if lo[0]>-.16 and hi[0]<.16 and hi[1]<7.95 and lo[2]<.30:
                            # These are mast face plates and their fasteners,
                            # not lantern equipment. Keep them on the mast.
                            restored_front_mast+=1
                            if lo[1]>7.32 and hi[1]<7.38:
                                # This transverse plate was already spaced
                                # away from the mast in the original export.
                                v[ids,2]-=.105 if len(piece['indices'])//3==12 else .106
                        else:
                            v[ids,2]+=.3;moved+=1
                    elif lo[1]>7.97 and size[0]<.16 and lo[2]>=.05 and hi[2]>.67:
                        if lo[2]>.68:v[ids,2]+=.3
                        else:v[ids,2]+=.3*np.clip((v[ids,2]-.06)/.62,0,1)
                        moved+=1
            elif item['key']=='semaphore_lowerfittings':
                for piece in components(dict(vertices=v.tolist(),indices=part['indices'].reshape(-1).tolist())):
                    ids=list(set(piece['indices']));lo=np.array(piece['min']);hi=np.array(piece['max']);size=hi-lo
                    if hi[1]<6.72:v[ids,2]+=.3;moved+=1
                    elif size[0]<.16 and lo[2]>=.05 and hi[2]>.67:
                        if lo[2]>.68:v[ids,2]+=.3
                        else:v[ids,2]+=.3*np.clip((v[ids,2]-.06)/.62,0,1)
                        moved+=1
            elif item['key'] in ('semaphore_upperrod','semaphore_lowerrod','semaphore_lever'):
                v[:,2]-=.4;moved+=1
                if item['key']=='semaphore_lever':v[:,2]+=LEVER_FORWARD
            else:
                # Local Z is invariant under the existing rotation around Z.
                # Move the wing, selector and glass together to retain their gaps.
                v[:,2]+=.3;moved+=1
        # Mounts span the newly created gaps; moving machinery is never used
        # as a support. These boxes are baked into the shared static mesh.
        if item['key']=='semaphore_static':
            for y in (.56,.90):bracket(parts[0],(-.06,y,-.47),(.26,y+.025,-.19))
        if item['key'] in ('semaphore_static','semaphore_lowerfittings'):
            lower=0 if item['key']=='semaphore_static'else 1.28
            for y in (7.40,7.83):
                # Crossbar meets both original lantern guide rails; the
                # return meets the mast angle. Neither relies on moving rods.
                bracket(parts[0],(-.40,y-lower,.274),(-.089,y+.025-lower,.320))
                bracket(parts[0],(-.17,y-lower,.11),(-.10,y+.025-lower,.320))
        if item['key']=='semaphore_static':relocate_drive(parts[0])
        if item['key'] in ('semaphore_upper','semaphore_lower'):
            # The original eccentric stays on its original rotating spindle.
            # Move only that complete cam along the spindle to the rod plane.
            cams=[]
            for part in parts:
                for piece in components(dict(vertices=part['vertices'].tolist(),indices=part['indices'].reshape(-1).tolist())):
                    if len(piece['indices'])//3==76 and np.allclose(np.array(piece['max'])-piece['min'],(.24,.24,.04),atol=.0002):
                        cams.append((part,list(set(piece['indices']))))
            assert len(cams)==1,'Expected one original drive eccentric per wing'
            part,ids=cams[0];part['vertices'][ids,2]-=.535 if item['key']=='semaphore_upper' else .575
        if item['key']=='semaphore_static':assert removed_plate==44,'Unexpected lower letter plate geometry'
        target=OUTPUT/(key+'.rsm');write(parts,target)
        records[key]=dict(source_sha256=digest(source),derived_sha256=digest(target),components_repositioned=moved,added_mounts=6 if item['key']=='semaphore_static'else 4 if item['key']=='semaphore_lowerfittings'else 0)
        if removed_plate:records[key]['lower_letter_plate_triangles_removed']=removed_plate
    assert restored_front_mast==10,('Expected ten mast face plates and fasteners',restored_front_mast)
    report=dict(head_forward_m=.3,drive_forward_adjustment_m=DRIVE_FORWARD,lever_forward_adjustment_m=LEVER_FORWARD,
                direct_rod_planes_z_m=[.310,.270],cam_rear_adjustments_m=[.535,.575],cam_islands_shifted=2,
                original_mast_members_restored=9,bearing_housings_preserved=True,
                drive_islands_shifted=18,drive_brackets_fitted=2,upper_drive_bracket_down_m=.24,raw_rod_rear_m=.4,
                ladder_rear_m=.55,animation_axes_preserved=True,restored_front_mast_fittings=restored_front_mast,connected_lantern_mounts=4,models=records)
    (OUTPUT/'semaphore-clearance.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report))

if __name__=='__main__':main()
