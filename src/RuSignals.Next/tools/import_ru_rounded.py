"""Import original RUS rounded heads; preserve source surfaces and texture UVs."""
import os,sys,json,hashlib,struct
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(Path(__file__).resolve().parent))
from convert_assets import shape_parts,write_mesh
from build_model_pack import split_fans,oriented_triangles
ASSETS=ROOT/'src/RuSignals.Next/Assets'
SOURCE=Path(os.environ['RUSIGNALS_SHAPES'])
report={}

def components(part):
    """Separate connected solids across UV and normal seams without splitting faces."""
    vertices=part['vertices'];weld={};nodes=[]
    for vertex in vertices:
        position=tuple(round(x,5)for x in vertex[:3]);nodes.append(weld.setdefault(position,len(weld)))
    parents=list(range(len(weld)))
    def find(i):
        while i!=parents[i]:parents[i]=parents[parents[i]];i=parents[i]
        return i
    def union(a,b):parents[find(a)]=find(b)
    indices=part['indices']
    for i in range(0,len(indices),3):
        a,b,c=(nodes[j]for j in indices[i:i+3]);union(a,b);union(a,c)
    groups={}
    for i in range(0,len(indices),3):groups.setdefault(find(nodes[indices[i]]),[]).extend(indices[i:i+3])
    return [dict(min=[round(min(vertices[i][a]for i in set(ids)),4)for a in range(3)],
                 max=[round(max(vertices[i][a]for i in set(ids)),4)for a in range(3)],indices=ids)
            for ids in groups.values()]
def merge(parts):
    result=dict(parts[0],name='RuSignals original rounded head');offset=0;vs=[];ns=[];uv=[];faces=[]
    for p in parts:
        vs.append(p['p']);ns.append(p['n']);uv.append(p['uv']);faces.append(p['f']+offset);offset+=len(p['p'])
    result.update(p=np.concatenate(vs),n=np.concatenate(ns),uv=np.concatenate(uv),f=np.concatenate(faces),cutout=any(p['cutout']for p in parts));return result
def base(key):
    path=ASSETS/(key+'.rsm');positions={};top=-float('inf')
    with path.open('rb')as stream:
        def integer():return struct.unpack('<I',stream.read(4))[0]
        def text():return stream.read(integer()).decode()
        stream.read(8)
        for _ in range(integer()):
            name,texture=text(),text();integer();vc,ic=integer(),integer();v=np.frombuffer(stream.read(vc*32),dtype='<f4').reshape(-1,8);stream.read(ic*4)
            top=max(top,float(v[:,1].max()))
            if not name.startswith('Body_'):positions[name]=v[np.argmax(v[:,2]),:3].astype(float)
    return top,positions
def subset(p,indices):
    used,inverse=np.unique(indices,return_inverse=True);return dict(p,p=p['p'][used],n=p['n'][used],uv=p['uv'][used],f=inverse.reshape(-1,3))
for key,source,groups in [
    ('shunting_mast','T_Signal_Head_WB_speciale.s',[('MoonWhite','Blue')]),
    ('mast','T_Signal_Head_YGR.s',[('Yellow','Green','Red')]),
    ('entry','T_Signal_Head_YGR.s',[('Yellow','Green','Red'),('Yellow2','MoonWhite')]),
    ('junction','T_Signal_Head_YGR.s',[('Yellow','Green','Red'),('Yellow2','Green2','MoonWhite')])]:
    top,target=base(key);pieces=[];provenance=[]
    for names in groups:
        path=SOURCE/(source if len(names)==3 else 'T_Signal_Head_WB_speciale.s')
        raw=shape_parts(path);lampnames=['A','B','C']if len(names)==3 else ['A','B'];split,lamps=split_fans(raw,lampnames)
        assert oriented_triangles(raw)==oriented_triangles(split)
        heads=[p for p in split if p['name'].endswith('_Signal')]
        first=np.array(lamps[0]['position'])-np.array([0,0,.02]);last=np.array(lamps[-1]['position'])-np.array([0,0,.02])
        dst=target[names[0]];end=target[names[-1]];scale=(dst[1]-end[1])/(first[1]-last[1])
        for p in heads:
            p['p']=p['p'].copy();p['p'][:,1]=(p['p'][:,1]-first[1])*scale+dst[1];p['p'][:,0]+=dst[0]-first[0];p['p'][:,2]+=dst[2]-first[2]
            p['n']=p['n'].copy();p['n'][:,1]/=scale;p['n']/=np.linalg.norm(p['n'],axis=1,keepdims=True)
        pieces+=heads;provenance.append({'source':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'y_scale':scale,'lenses':names})
    # All selected heads share the original RUS atlas. Use their own material.
    texture='t_signal.png'
    for p in pieces:p['texture']=texture
    full=merge(pieces);lowest=min(p[1]for p in target.values());lower=max(0,min(top-.8-3.15,lowest-.8-(1.65 if key in ('entry','junction')else .85)))
    compact=dict(full,p=full['p']-np.array([0,lower,0]))
    selected=[]
    for p in pieces:
        data={'vertices':np.c_[p['p'],p['n'],p['uv']].tolist(),'indices':p['f'].reshape(-1).tolist()}
        for c in components(data):
            lo,hi=np.array(c['min']),np.array(c['max'])
            if lo[2]>=.15 and hi[0]-lo[0]>.20:selected.extend([(p,c['indices'])])
    assert selected,key
    tunnel=merge([subset(p,ids)for p,ids in selected]);tunnel['p']=tunnel['p']-np.array([0,lower,0])
    folder=ASSETS/'Rounded';folder.mkdir(exist_ok=True)
    for suffix,mesh in [('full',full),('compact',compact),('tunnel',tunnel)]:write_mesh([mesh],folder/(key+'_'+suffix+'.rsm'))
    report[key]={'parts':provenance,'texture':texture,'lowering':lower,'triangles':len(full['f']),'tunnel_triangles':len(tunnel['f']),'min':full['p'].min(0).tolist(),'max':full['p'].max(0).tolist(),'source_triangle_order_verified':True}
(ASSETS/'Rounded/source-manifest.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))
