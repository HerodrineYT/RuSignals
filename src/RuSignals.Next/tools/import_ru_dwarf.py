"""Import the complete original RUS five-lens dwarf, including all fixings.

The two cast housings, feet, bolts and foundation retain source positions and
proportions. Only a rigid translation centres it on the existing track offset.
The same manifold hood repair used by the main pack seals the original shells.
"""
from pathlib import Path
import os,hashlib,json,sys
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(Path(__file__).resolve().parent))
from convert_assets import shape_parts
from build_model_pack import split_fans,oriented_triangles
import rebuild_hoods as repair
ASSETS=ROOT/'src/RuSignals.Next/Assets'
SOURCE=Path(os.environ['RUSIGNALS_SHAPES'])/'T_Signal_Small_YGY_WR.s'
KEY='exit_small_two_yellows'

def main():
    raw=shape_parts(SOURCE);ground=min(p['p'][:,1].min()for p in raw)
    original=repair.read(ASSETS/(KEY+'.rsm'))
    target={p['name']:p for p in original if not p['name'].startswith('Body_')}
    centres=np.array([p['vertices'][np.argmax(p['vertices'][:,2]),:3]for p in target.values()])
    shift=np.array([(centres[:,0].min()+centres[:,0].max())/2,-ground,centres[0,2]-.06736])
    for p in raw:p['p']+=shift
    names=['Yellow','Green','MoonWhite','Yellow2','Red']
    parts,_=split_fans(raw,names)
    assert oriented_triangles(raw)==oriented_triangles(parts)
    bodies=[];lenses=[];lights=[]
    for p in parts:
        v=np.c_[p['p'],p['n'],p['uv']].copy()
        data=dict(name=p['name'],texture='t_signal.png',cutout=0,vertices=v,indices=p['f'].copy())
        if p['name'].startswith('Body_'):bodies.append(data);continue
        # Keep the shared four-ring glass shader's UV datum for either style.
        uv=target[p['name']]['vertices'][:,6:8];src=v[:,6:8]
        v[:,6:8]=(src-src.min(0))/(src.max(0)-src.min(0))*(uv.max(0)-uv.min(0))+uv.min(0)
        lenses.append(data)
        centre=v[np.argmax(v[:,2]),:3];radius=np.max(np.linalg.norm(v[:,:2]-centre[:2],axis=1))
        lights.append((p['name'],centre,radius))
    body=dict(name='Body_0_Signal',texture='t_signal.png',cutout=0,vertices=np.concatenate([p['vertices']for p in bodies]))
    offset=0;triangles=[]
    for part in bodies:triangles.append(part['indices']+offset);offset+=len(part['vertices'])
    body['indices']=np.concatenate(triangles)
    temporary=ROOT/'work/rusignals-round-dwarf';temporary.mkdir(parents=True,exist_ok=True)
    repair.write([body,*lenses],temporary/(KEY+'.rsm'))
    repair.ASSETS=temporary;repair.OUTPUT=ASSETS/'RoundedDwarf'
    record=repair.rebuild(KEY,lights)
    assert len(record['hoods'])==5
    record.update(source=str(SOURCE),source_shape_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
                  atlas='t_signal.png',complete_original_model=True,source_scale=1,translation=shift.tolist(),
                  lens_centres={name:centre.tolist()for name,centre,_ in lights},
                  source_triangle_order_verified=True,source_housings_and_fixings_preserved=True,
                  square_profile_preserved=True,foundation_geometry_preserved=True)
    (repair.OUTPUT/'manifest.json').write_text(json.dumps(record,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(hoods=5,lenses=names,output_sha256=record['derived_sha256'])))

if __name__=='__main__':main()
