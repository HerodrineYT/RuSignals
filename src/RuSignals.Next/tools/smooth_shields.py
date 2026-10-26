"""Refine ONLY the planar oval screens in verified RUS meshes, offline.

Periodic monotone cubic interpolation retains every original contour point,
the straight sides, original extrema/UVs, depth and all non-screen triangles.
Hoods, housings, mounts and lenses are copied without touching their data.
"""
from pathlib import Path
import hashlib,json
import numpy as np
from rebuild_hoods import read,write,components

ROOT=Path(__file__).resolve().parents[3]
ASSETS=ROOT/'src/RuSignals.Next/Assets'
OUTPUT=ASSETS/'SmoothShields'
SUBDIVISIONS=8

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def screen(part,piece):
    lo,hi=np.array(piece['min']),np.array(piece['max']);size=hi-lo
    return size[0]>.7 and size[1]>1 and size[2]<.00002

def refine(part,piece):
    ids=np.array(piece['indices']).reshape(-1,3)
    v=part['vertices'];points=np.unique(v[np.unique(ids),:3],axis=0)
    lo,hi=points.min(0),points.max(0);centre=(lo+hi)/2
    # The original screen is a pair of front/back triangle fans. Only their
    # outer points are used; the one or two fan centres remain unmodified.
    outer=points[(np.abs(points[:,0]-centre[0])>.04)]
    outer=outer[np.argsort(np.arctan2(outer[:,1]-centre[1],outer[:,0]-centre[0]))]
    assert len(outer)==16,(len(outer),lo,hi)
    delta=np.roll(outer,-1,axis=0)-outer
    lengths=np.linalg.norm(delta,axis=1)
    slopes=delta/lengths[:,None]
    tangent=np.zeros_like(outer)
    for i in range(len(outer)):
        a,b=slopes[i-1],slopes[i];ha,hb=lengths[i-1],lengths[i]
        same=a*b>0
        tangent[i,same]=(3*(ha+hb))/((2*hb+ha)/a[same]+(hb+2*ha)/b[same])
    outline=[];parameters=[]
    for i,a in enumerate(outer):
        b=outer[(i+1)%len(outer)];h=lengths[i]
        # Straight vertical sides and the short flat top/bottom are unchanged.
        count=1 if abs(delta[i,0])<1e-6 or abs(delta[i,1])<1e-6 else SUBDIVISIONS
        for k in range(count):
            t=k/count
            parameters.append((i,t))
            outline.append((2*t**3-3*t*t+1)*a+(t**3-2*t*t+t)*h*tangent[i]+(-2*t**3+3*t*t)*b+(t**3-t*t)*h*tangent[(i+1)%len(outer)])
    outline=np.array(outline)
    assert np.allclose(outline.min(0),lo,atol=1e-6) and np.allclose(outline.max(0),hi,atol=1e-6)
    for point in outer:assert np.linalg.norm(outline-point,axis=1).min()<1e-8
    added=[];faces=[]
    for sign in (1,-1):
        selected=np.unique(ids[np.sign(v[ids,5]).sum(1)*sign>0])
        source=v[selected]
        centerrow=source[np.linalg.norm(source[:,:2]-centre[:2],axis=1).argmin()].copy()
        start=len(added);added.append(centerrow)
        for p,(segment,t) in zip(outline,parameters):
            # Existing coordinates and UVs are retained literally where present.
            match=np.linalg.norm(source[:,:3]-p,axis=1).argmin()
            a=source[np.linalg.norm(source[:,:3]-outer[segment],axis=1).argmin()]
            b=source[np.linalg.norm(source[:,:3]-outer[(segment+1)%len(outer)],axis=1).argmin()]
            row=source[match].copy() if np.linalg.norm(source[match,:3]-p)<1e-8 else np.r_[p,[0,0,sign],a[6:8]*(1-t)+b[6:8]*t]
            added.append(row)
        for i in range(len(outline)):
            f=[start,start+i+1,start+(i+1)%len(outline)+1]
            if sign<0:f=f[::-1]
            faces.append(f)
    return np.array(added),np.array(faces),dict(original_boundary_points=16,refined_boundary_points=len(outline),bounds_preserved=True,original_uvs_preserved=True)

def main():
    records={}
    for key in ('shunting_mast','mast','entry','junction'):
        for variant in ('full','compact','tunnel'):
            relative='Rounded/'+key+'_'+variant+'.rsm'
            source=ASSETS/'SolidHoods'/relative;parts=read(source);proof=[]
            for part in parts:
                v=part['vertices'];remove=set();added=[];newfaces=[];offset=0
                for piece in components(dict(vertices=v.tolist(),indices=part['indices'].reshape(-1).tolist())):
                    if not screen(part,piece):continue
                    vs,fs,record=refine(part,piece);proof.append(record)
                    remove.update(tuple(f) for f in np.array(piece['indices']).reshape(-1,3))
                    added.append(vs);newfaces.append(fs+offset);offset+=len(vs)
                assert proof,relative
                keep=np.array([f for f in part['indices'] if tuple(f) not in remove])
                # Keep all original rows: non-screen geometry retains exact
                # indices, binary vertex positions, normals and UVs.
                part['vertices']=np.concatenate((v,*added))
                part['indices']=np.concatenate((keep,np.concatenate(newfaces)+len(v)))
            target=OUTPUT/relative;write(parts,target)
            records[relative]=dict(source_sha256=digest(source),derived_sha256=digest(target),screens=proof,non_screen_vertex_rows_and_faces_preserved=True)
    OUTPUT.mkdir(parents=True,exist_ok=True)
    (OUTPUT/'manifest.json').write_text(json.dumps(dict(subdivisions=SUBDIVISIONS,models=records),indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(models=len(records),screens=sum(len(r['screens']) for r in records.values()))))
if __name__=='__main__':main()
