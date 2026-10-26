"""Independent floating head-fitting regression; never edits model assets.

Connections are actual triangle intersections or containment in a closed
convex solid. Bounding boxes only select candidates. Moving parts never count
as mast supports. Baseline IDs refer exclusively to frozen release4.3.2.
"""
from pathlib import Path
import argparse,collections,hashlib,json
import numpy as np
from rebuild_hoods import ASSETS,read
from verify_semaphore_drive import pieces
from verify_semaphore_8371_clearance import triangle_overlap

SCOPE={'static':(132,134,135,136,144,145,146,168,169,170,238,239),'lowerfittings':(21,22)}


def signatures(part,faces=None):
    result=collections.Counter()
    for face in part['indices'] if faces is None else faces:
        rows=np.asarray(part['vertices'][face],dtype='<f4')
        result[hashlib.sha256(min(np.roll(rows,-i,axis=0).tobytes()for i in range(3))).hexdigest()]+=1
    return result


def solid_planes(tris):
    vertices=np.unique(np.round(tris.reshape(-1,3),6),axis=0)
    edges=collections.Counter()
    for tri in np.round(tris,6):
        for a,b in zip(tri,np.roll(tri,-1,axis=0)):edges[tuple(sorted((tuple(a),tuple(b))))]+=1
    if not edges or any(n!=2 for n in edges.values()):return None
    center=vertices.mean(0);normals=np.cross(tris[:,1]-tris[:,0],tris[:,2]-tris[:,0]);length=np.linalg.norm(normals,axis=1)
    normals=normals[length>1e-10]/length[length>1e-10,None];origins=tris[length>1e-10,0]
    inward=np.sum(normals*(center-origins),axis=1)>0;normals[inward]*=-1
    offsets=np.sum(normals*origins,axis=1)
    if (vertices@normals.T-offsets).max()>1e-5:return None
    return normals,offsets


def contact(a,b):
    for tri in a['tris']:
        if triangle_overlap(tri,b['tris']):return True
    for outer,inner in ((a,b),(b,a)):
        planes=outer['solid']
        if planes is not None:
            normals,offsets=planes
            if np.any(np.all(inner['tris'].reshape(-1,3)@normals.T-offsets<=1e-6,axis=1)):return True
    return False


def load_nodes(assets):
    nodes=[];parts={}
    for key in SCOPE:
        part,islands=pieces(assets/f'SolidHoods/Semaphore/semaphore_{key}.rsm');parts[key]=(part,islands)
        for index,p in enumerate(islands):
            faces=part['indices'][np.isin(part['indices'],p['ids']).all(axis=1)]
            tris=part['vertices'][faces,:3].astype(float)
            nodes.append(dict(key=key,index=index,low=p['low'],high=p['high'],tris=tris,solid=solid_planes(tris),signatures=signatures(part,faces)))
    return nodes,parts


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--assets',type=Path,default=ASSETS)
    ap.add_argument('--baseline-assets',type=Path,default=ASSETS.parents[2]/'dist/RuSignals-4.3.2/RuSignals/Assets')
    ap.add_argument('--report',type=Path,default=ASSETS.parents[2]/'artifacts/ru-signals-4.3.3/semaphore-floating-fittings-verification.json')
    args=ap.parse_args();nodes,actual=load_nodes(args.assets);baseline,before=load_nodes(args.baseline_assets)
    lows=np.array([n['low']for n in nodes]);highs=np.array([n['high']for n in nodes]);graph=[set()for _ in nodes];edges=0
    for i,a in enumerate(nodes):
        candidates=np.flatnonzero(np.all(a['high']>=lows-1e-6,axis=1)&np.all(a['low']<=highs+1e-6,axis=1))
        for j in candidates:
            if j<=i:continue
            if contact(a,nodes[j]):graph[i].add(int(j));graph[j].add(i);edges+=1
    roots=[i for i,n in enumerate(nodes)if n['key']=='static' and n['high'][1]-n['low'][1]>7.5 and n['low'][2]>-.25 and n['high'][2]<.25]
    assert len(roots)==8,'Expected eight actual full-height mast angles'
    reached=set(roots);front=list(roots);parent={i:None for i in roots}
    while front:
        i=front.pop()
        for j in graph[i]-reached:reached.add(j);front.append(j);parent[j]=i
    def support_path(i):
        result=[]
        while i is not None:
            result.append(dict(part=nodes[i]['key'],component=nodes[i]['index']));i=parent[i]
        return result
    failures=[];checked=[];allowed={key:collections.Counter()for key in SCOPE}
    for old in baseline:
        if old['index']not in SCOPE[old['key']]:continue
        allowed[old['key']]+=old['signatures']
        matches=[i for i,n in enumerate(nodes)if n['key']==old['key']and n['signatures']==old['signatures']]
        # Exact baseline pieces may be removed or rigidly reattached. A later
        # match by intrinsic attributes covers translations without a new-ID oracle.
        if not matches:
            shape=old['tris']-old['tris'].reshape(-1,3).mean(0)
            old_center=old['tris'].reshape(-1,3).mean(0)
            matches=[i for i,n in enumerate(nodes)if n['key']==old['key']and n['tris'].shape==old['tris'].shape
                     and np.allclose(n['tris'].reshape(-1,3).mean(0)[:2],old_center[:2],atol=1e-6)
                     and np.allclose(n['tris']-n['tris'].reshape(-1,3).mean(0),shape,atol=1e-6)]
        floating=[i for i in matches if i not in reached]
        item=dict(baseline_part=old['key'],baseline_component=old['index'],triangles=len(old['tris']),baseline_min=old['low'].tolist(),baseline_max=old['high'].tolist(),matching_current_components=[nodes[i]['index']for i in matches],floating_current_components=[nodes[i]['index']for i in floating],physical_support_paths=[support_path(i)for i in matches if i in reached])
        checked.append(item)
        if floating:failures.append(item)
    preservation=[];preserved=True;new_floating=[]
    for key in SCOPE:
        oldpart,_=before[key];newpart,_=actual[key];old=signatures(oldpart);new=signatures(newpart)
        required=old-allowed[key];missing=required-new
        if missing:preserved=False
        preservation.append(dict(part=key,required_exact_triangles=sum(required.values()),missing_required_triangles=sum(missing.values()),authorized_fitting_triangles=sum(allowed[key].values()),added_triangles=sum((new-old).values()),removed_triangles=sum((old-new).values())))
        for i,n in enumerate(nodes):
            if n['key']==key and n['signatures']-old and i not in reached:
                new_floating.append(dict(part=key,component=n['index'],new_triangles=sum((n['signatures']-old).values()),minimum=n['low'].tolist(),maximum=n['high'].tolist()))
    rails=[]
    for key,index in (('static',233),('static',234),('lowerfittings',18),('lowerfittings',19)):
        old=next(n for n in baseline if n['key']==key and n['index']==index)
        matches=[i for i,n in enumerate(nodes)if n['key']==key and not(old['signatures']-n['signatures'])]
        rails.append(dict(part=key,baseline_component=index,connected=bool(matches)and all(i in reached for i in matches),physical_support_paths=[support_path(i)for i in matches if i in reached]))
    cases=[]
    for key in SCOPE:
        matches=[i for i,n in enumerate(nodes)if n['key']==key and len(n['tris'])==44 and np.allclose(n['high']-n['low'],(.26,.39,.18),atol=.0002)]
        assert len(matches)==1,'One actual lantern case must be identified in '+key
        i=matches[0];cases.append(dict(part=key,component=nodes[i]['index'],connected=i in reached,minimum=nodes[i]['low'].tolist(),maximum=nodes[i]['high'].tolist(),physical_support_path=support_path(i)if i in reached else []))
    unchanged={};changed_other=[]
    editable={f'SolidHoods/Semaphore/semaphore_{key}.rsm'for key in SCOPE}
    metadata={'SolidHoods/semaphore-clearance.json'}
    for p in args.baseline_assets.rglob('*'):
        if not p.is_file():continue
        relative=p.relative_to(args.baseline_assets).as_posix()
        if relative in editable or relative in metadata:continue
        q=args.assets/relative
        if not q.exists()or hashlib.sha256(p.read_bytes()).digest()!=hashlib.sha256(q.read_bytes()).digest():changed_other.append(relative)
        else:unchanged[relative]=hashlib.sha256(q.read_bytes()).hexdigest()
    passed=not failures and not new_floating and all(r['connected']for r in rails+cases)and preserved and not changed_other
    report=dict(passed=passed,baseline=str(args.baseline_assets),target=str(args.assets),actual_contact_edges=edges,mast_root_components=roots,mast_connected_components=len(reached),checked_fittings=checked,floating_fittings=failures,new_or_reshaped_floating_fittings=new_floating,lantern_guide_rails=rails,lantern_cases=cases,retained_triangle_attributes_exact=preserved,preservation=preservation,unexpected_changed_assets=changed_other,unchanged_assets_sha256=unchanged,scope='Only the explicit14 baseline head-fitting components may change or disappear; exact oriented float32 triangle attributes of all other mast/ladder/lantern/frame parts retained; all kinematic assets byte-preserved; actual static triangle/closed-solid graph must connect each retained, moved, reshaped or added fitting, all four untouched lantern guide rails and both untouched lantern cases to a full-height mast angle')
    args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(passed=passed,floating=[(f['baseline_part'],f['baseline_component'])for f in failures],exact_preservation=preserved,unexpected_changed_assets=changed_other,report=str(args.report))))
    return 0 if passed else 1


if __name__=='__main__':raise SystemExit(main())
