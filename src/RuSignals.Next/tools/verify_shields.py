"""Verify all refined screens against the exact 2.0.10 repaired geometry."""
from pathlib import Path
from collections import Counter
import json
import numpy as np
from smooth_shields import ASSETS,OUTPUT,digest,screen
from rebuild_hoods import read,components

def verify():
    manifest=json.loads((OUTPUT/'manifest.json').read_text(encoding='utf-8'))
    models={};screens=0;faces_added=0
    for key,proof in manifest['models'].items():
        source=ASSETS/'SolidHoods'/key;target=OUTPUT/key
        assert proof['source_sha256']==digest(source) and proof['derived_sha256']==digest(target)
        before,after=read(source),read(target)
        assert len(before)==len(after)
        records=[]
        for old,new in zip(before,after):
            v,w=old['vertices'],new['vertices'];n=len(v)
            assert old['name']==new['name'] and old['texture']==new['texture'] and old['cutout']==new['cutout']
            assert np.array_equal(v,w[:n]),'Original vertex rows changed: '+key
            original_screens=[p for p in components(dict(vertices=v.tolist(),indices=old['indices'].reshape(-1).tolist())) if screen(old,p)]
            removed={tuple(f) for piece in original_screens for f in np.array(piece['indices']).reshape(-1,3)}
            kept=np.array([f for f in old['indices'] if tuple(f) not in removed])
            assert np.array_equal(kept,new['indices'][:len(kept)]),'Non-screen topology changed: '+key
            assert np.array_equal(v[:,:3].min(0),w[:,:3].min(0)) and np.array_equal(v[:,:3].max(0),w[:,:3].max(0)),'Overall bounds changed'
            assert np.isfinite(w).all() and np.max(np.abs(np.linalg.norm(w[:,3:6],axis=1)-1))<.001
            added=new['indices'][len(kept):]
            p=w[:,:3]
            normals=np.cross(p[added[:,1]]-p[added[:,0]],p[added[:,2]]-p[added[:,0]])
            assert (np.linalg.norm(normals,axis=1)>1e-10).all() and (np.einsum('ij,ij->i',normals,w[added,3:6].sum(1))>0).all()
            assert len({tuple(f) for f in added})==len(added)
            for original in original_screens:
                ov=v[np.unique(original['indices'])]
                centre=(ov[:,:3].min(0)+ov[:,:3].max(0))/2
                front=added[w[added,5].sum(1)>0]
                front=front[np.abs(w[front[:,0],2]-centre[2])<.00001]
                front=front[np.all((w[front,1]>=ov[:,1].min()-.00001)&(w[front,1]<=ov[:,1].max()+.00001),axis=1)]
                same_y=(w[front,1].min((0,1))>=ov[:,1].min()-.00001) and (w[front,1].max((0,1))<=ov[:,1].max()+.00001)
                assert same_y
                used=np.unique(front);boundary=w[used][np.abs(w[used,0]-centre[0])>.04]
                old_boundary=ov[(ov[:,5]>.9)&(np.abs(ov[:,0]-centre[0])>.04)]
                assert len(boundary)==100 and len(old_boundary)==16
                for row in old_boundary:assert np.linalg.norm(boundary-row,axis=1).min()<1e-7,'Original contour/UV lost'
                records.append(dict(old_contour_points=16,new_contour_points=100,original_vertices_and_uvs_retained=True))
                screens+=1
            faces_added+=len(new['indices'])-len(old['indices'])
        models[key]=dict(source_sha256=digest(source),derived_sha256=digest(target),screens=records,non_screen_geometry_identical=True)
    assert len(models)==12 and screens==18
    return dict(models=models,total_models=12,total_screens=screens,additional_triangles_all_variants=faces_added,original_bounds_preserved=True,all_non_screen_geometry_preserved=True)

if __name__=='__main__':
    import sys
    result=verify()
    if len(sys.argv)>1:Path(sys.argv[1]).write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='models'}))
