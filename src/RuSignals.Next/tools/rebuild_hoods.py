"""Clean original RUS visor topology while retaining its actual contour vertices.

The source RSM files remain intact. Derived models retain housing faces, removing obsolete visor side strips, and retain
normals, materials, lenses and texture coordinates. Original visor contours are unchanged. Runs offline, never in the game.
"""
from pathlib import Path
from collections import Counter
import hashlib,json,struct
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
ASSETS=ROOT/'src/RuSignals.Next/Assets'
OUTPUT=ASSETS/'SolidHoods'
KEYS=('shunting','mast','entry','exit','exit_mainline','exit_small_two_yellows','exit_small','shunting_mast','distant','repeater','junction','turntable')

def components(part):
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

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def read(path):
    parts=[]
    with path.open('rb')as stream:
        def integer():return struct.unpack('<I',stream.read(4))[0]
        def string():return stream.read(integer()).decode('utf-8')
        assert stream.read(8)==b'RUSMESH2'
        for _ in range(integer()):
            name,texture=string(),string();cutout=integer();vc,ic=integer(),integer()
            vertices=np.frombuffer(stream.read(vc*32),dtype='<f4').reshape(-1,8).copy()
            indices=np.frombuffer(stream.read(ic*4),dtype='<u4').reshape(-1,3).copy()
            parts.append(dict(name=name,texture=texture,cutout=cutout,vertices=vertices,indices=indices))
        assert not stream.read(1)
    return parts

def write(parts,path):
    path.parent.mkdir(parents=True,exist_ok=True)
    with path.open('wb')as stream:
        def integer(value):stream.write(struct.pack('<I',value))
        def string(value):data=value.encode();integer(len(data));stream.write(data)
        stream.write(b'RUSMESH2');integer(len(parts))
        for part in parts:
            string(part['name']);string(part['texture']);integer(part['cutout'])
            integer(len(part['vertices']));integer(part['indices'].size)
            stream.write(part['vertices'].astype('<f4').tobytes());stream.write(part['indices'].astype('<u4').tobytes())

def lamps(key,lower=0):
    result=[]
    for part in read(ASSETS/(key+'.rsm')):
        if part['name'].startswith('Body_'):continue
        points=part['vertices'][:,:3];center=points[np.argmax(points[:,2])].astype(float)
        radius=float(np.max(np.linalg.norm(points[:,:2]-center[:2],axis=1)))
        center[1]-=lower
        result.append((part['name'],center,radius))
    return result

def ear_clip(points,loop):
    # Triangulate a boundary strip without filling the optical opening.
    p=points[loop];normal=np.sum(np.cross(p,np.roll(p,-1,axis=0)),axis=0)
    axes=[i for i in range(3)if i!=int(np.argmax(np.abs(normal)))];uv=p[:,axes]
    area=np.sum(uv[:,0]*np.roll(uv[:,1],-1)-uv[:,1]*np.roll(uv[:,0],-1));sign=1 if area>0 else -1
    def cross(a,b,c):
        u,v=b-a,c-a;return u[0]*v[1]-u[1]*v[0]
    ids=list(range(len(loop)));tri=[]
    while len(ids)>3:
        found=False
        for n,i in enumerate(ids):
            prev,nxt=ids[n-1],ids[(n+1)%len(ids)]
            if sign*cross(uv[prev],uv[i],uv[nxt])<=1e-12:continue
            inside=False
            for j in ids:
                if j in (prev,i,nxt):continue
                if min(sign*cross(uv[prev],uv[i],uv[j]),sign*cross(uv[i],uv[nxt],uv[j]),sign*cross(uv[nxt],uv[prev],uv[j]))>=-1e-12:inside=True;break
            if inside:continue
            tri.append([loop[prev],loop[i],loop[nxt]]);ids.pop(n);found=True;break
        assert found,'Cannot close original boundary without crossing faces'
    tri.append([loop[i]for i in ids]);return tri

def clean_shell(original,faces,center):
    lookup={};points=[];uv=[];fs=[]
    for face in faces:
        ids=[]
        for i in face:
            v=original[i];key=tuple(np.round(v[:3],5))
            if key not in lookup:lookup[key]=len(points);points.append(v[:3].astype(float));uv.append(v[6:8])
            ids.append(lookup[key])
        if len(set(ids))==3:fs.append(ids)
    points=np.asarray(points);uv=np.asarray(uv);unique={}
    for face in fs:unique.setdefault(tuple(sorted(face)),face)
    fs=list(unique.values());removed=len(faces)-len(fs)
    # Some RUS zero-thickness quads contain both diagonal tessellations.
    # Keep one tessellation, rather than overlapping four triangles.
    while True:
        groups={}
        for face in fs:
            for i in range(3):groups.setdefault(tuple(sorted((face[i],face[(i+1)%3]))),[]).append(face)
        patch=None
        keys={tuple(sorted(f))for f in fs}
        from itertools import combinations
        for adjacent in groups.values():
            nodes=set(i for f in adjacent for i in f)
            if len(nodes)==4 and all(tuple(c)in keys for c in combinations(sorted(nodes),3)):
                patch=nodes;break
        if patch is None:break
        candidates=[f for f in fs if set(f)<=patch];chosen=candidates[:2]
        assert len(set(chosen[0])&set(chosen[1]))==2
        fs=[f for f in fs if not set(f)<=patch]+chosen;removed+=2
    edges=Counter(tuple(sorted((f[i],f[(i+1)%3])))for f in fs for i in range(3))
    assert max(edges.values())<=2,'Non-manifold original intersection'
    border=[edge for edge,count in edges.items()if count==1]
    if len(faces)<=40:
        # Thin source skin: retain exterior points exactly, place a new inner
        # wall inward, and bridge each boundary edge. No enlarged silhouette.
        count=len(points);direction=points-center
        direction[:,2]=0;length=np.linalg.norm(direction,axis=1);direction/=length[:,None]
        inner=points-direction*.002
        inner=np.clip(inner,points.min(0),points.max(0))
        points=np.concatenate((points,inner));uv=np.concatenate((uv,uv))
        fs+= [[c+count,b+count,a+count]for a,b,c in list(fs)]
        for a,b in border:fs.extend(([a,b,b+count],[a,b+count,a+count]))
    else:
        adjacent={}
        for a,b in border:adjacent.setdefault(a,[]).append(b);adjacent.setdefault(b,[]).append(a)
        assert all(len(x)==2 for x in adjacent.values()),'Branching rim'
        while adjacent:
            start=next(iter(adjacent));loop=[];prev=None;cur=start
            while True:
                loop.append(cur);nxt=next(x for x in adjacent[cur]if x!=prev);prev,cur=cur,nxt
                if cur==start:break
            fs.extend(ear_clip(points,loop))
            for i in loop:del adjacent[i]
    # Orient every adjacent face consistently, then choose positive volume.
    edges={}
    for j,f in enumerate(fs):
        for i in range(3):edges.setdefault(tuple(sorted((f[i],f[(i+1)%3]))),[]).append(j)
    assert all(len(adj)==2 for adj in edges.values()),'Open repaired rim'
    done=set();todo=[0];done.add(0)
    while todo:
        j=todo.pop();f=fs[j]
        for i in range(3):
            a,b=f[i],f[(i+1)%3];k=next(k for k in edges[tuple(sorted((a,b)))]if k!=j)
            if k in done:continue
            g=fs[k]
            if any(g[n]==a and g[(n+1)%3]==b for n in range(3)):fs[k]=g[::-1]
            done.add(k);todo.append(k)
    assert len(done)==len(fs),'Disconnected repair'
    ids=np.array(fs);local=points-points.mean(0)
    volume=np.sum(np.einsum('ij,ij->i',local[ids[:,0]],np.cross(local[ids[:,1]],local[ids[:,2]])))/6
    if volume<0:ids=ids[:,::-1];volume=-volume
    assert volume>1e-9,'Zero-thickness repaired visor'
    vectors=np.cross(points[ids[:,1]]-points[ids[:,0]],points[ids[:,2]]-points[ids[:,0]])
    lengths=np.linalg.norm(vectors,axis=1);assert lengths.min()>1e-10,'Degenerate repaired face'
    normals=vectors/lengths[:,None];vertex_faces={}
    for j,f in enumerate(ids):
        for i in f:vertex_faces.setdefault(i,[]).append(j)
    result=[];indices=[]
    for j,f in enumerate(ids):
        start=len(result)
        for i in f:
            near=[k for k in vertex_faces[i]if np.dot(normals[j],normals[k])>.82]
            n=vectors[near].sum(0);n/=np.linalg.norm(n)
            result.append([*points[i],*n,*uv[i]])
        indices.append([start,start+1,start+2])
    return np.array(result),np.array(indices),dict(triangles=len(ids),removed_overlapping_triangles=removed,closed_manifold=True,positive_volume_m3=float(volume),original_contour_vertices_preserved=True,source_bounds_preserved=True)

def rebuild(key,light_positions):
    parts=read(ASSETS/(key+'.rsm'));records=[];found=Counter();strips=0
    for part in parts:
        if not (part['name'].startswith('Body_')or key.startswith('Rounded/')):continue
        original=part['vertices'];removed=[];added=[];new_faces=[];offset=0
        for piece in components(dict(vertices=original.tolist(),indices=part['indices'].reshape(-1).tolist())):
            lo,hi=np.array(piece['min']),np.array(piece['max']);size=hi-lo;middle=(hi+lo)*.5
            # Disconnected straight side plates in the YGR import cut across
            # the optical aperture. They are not part of the curved visor.
            if key.startswith('Rounded/') and lo[2]>.39 and hi[2]>.8 and .20<size[0]<.30 and size[1]<.34:
                removed.extend(tuple(f)for f in np.array(piece['indices']).reshape(-1,3));strips+=1;continue
            matches=[(n,c,r)for n,c,r in light_positions if lo[2]>=c[2]-.065 and hi[2]>c[2]+.14 and size[0]>.15 and .08<size[1]<.34 and abs(middle[0]-c[0])<size[0]*.20 and lo[1]-.025<c[1]<hi[1]+.025]
            if not matches:continue
            assert len(matches)==1,(key,matches)
            name,center,_=matches[0];found[name]+=1;faces=np.array(piece['indices']).reshape(-1,3)
            v,f,record=clean_shell(original,faces,center)
            source_points=original[list(set(piece['indices'])),:3]
            assert np.allclose(v[:,:3].min(0),source_points.min(0),atol=.000001)and np.allclose(v[:,:3].max(0),source_points.max(0),atol=.000001)
            added.append(v);new_faces.append(f+offset);offset+=len(v);removed.extend(tuple(face)for face in faces)
            records.append(dict(lamp=name,part=part['name'],old_triangles=len(faces),source_min=source_points.min(0).tolist(),source_max=source_points.max(0).tolist(),**record))
        if added:
            removed=set(removed);kept=np.array([f for f in part['indices']if tuple(f)not in removed]);used,inverse=np.unique(kept,return_inverse=True)
            part['vertices']=np.concatenate((original[used],*added));part['indices']=np.concatenate((inverse.reshape(-1,3),np.concatenate(new_faces)+len(used)))
    assert len(found)==len(light_positions)and all(n==1 for n in found.values()),(key,found)
    destination=OUTPUT/(key+'.rsm');write(parts,destination)
    return dict(source_sha256=digest(ASSETS/(key+'.rsm')),derived_sha256=digest(destination),hoods=records,obsolete_visor_strips_removed=strips,non_visor_faces_preserved=True)

def main():
    manifest={}
    for key in KEYS:manifest[key]=rebuild(key,lamps(key))
    rounded=json.loads((ASSETS/'Rounded/source-manifest.json').read_text())
    for key,item in rounded.items():
        for variant in ('full','compact','tunnel'):
            name='Rounded/'+key+'_'+variant
            manifest[name]=rebuild(name,lamps(key,0 if variant=='full'else item['lowering']))
            if variant=='compact':
                parts=read(OUTPUT/('Rounded/'+key+'_full.rsm'))
                for part in parts:part['vertices'][:,1]-=item['lowering']
                write(parts,OUTPUT/(name+'.rsm'));manifest[name]['derived_sha256']=digest(OUTPUT/(name+'.rsm'))
    report=dict(models=len(manifest),hoods=sum(len(item['hoods'])for item in manifest.values()),source_shape_preserved=True,
                models_detail=manifest)
    (OUTPUT/'manifest.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({key:len(item['hoods'])for key,item in manifest.items()}))

if __name__=='__main__':main()
