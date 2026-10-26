"""Offline MSTS binary shape / user OBJ and ACE converter for the RUS Signals pack.

No geometry is generated here: positions, normals, UVs, material assignments and
matrices come from the input files. Only triangle winding is adapted for Unity.
"""
import argparse
import hashlib
import json
import math
import struct
import zlib
from io import BytesIO
from pathlib import Path
import numpy as np
from PIL import Image
from inspect_shape import shape, Block

def u32(b, offset=0):
    return struct.unpack_from('<I', b, offset)[0]

def string(b):
    return b[2:2+2*struct.unpack_from('<H', b)[0]].decode('utf-16-le')

def table(block):
    items = block.children(4)
    assert len(items) == u32(block.data), block.name
    return items

def shape_parts(path):
    root = {b.name:b for b in shape(path).children()}
    pts = np.array([struct.unpack('<3f', b.data) for b in table(root['points'])])
    uv = np.array([struct.unpack('<2f', b.data) for b in table(root['uv_points'])])
    ns = np.array([struct.unpack('<3f', b.data) for b in table(root['normals'])])
    matrices = table(root['matrices'])
    images = [string(b.data) for b in table(root['images'])]
    shaders = [string(b.data) for b in table(root['shader_names'])]
    textures = [u32(b.data) for b in table(root['textures'])]
    states = [struct.unpack_from('<IIiII', b.data) for b in table(root['vtx_states'])]
    prims = []
    for b in table(root['prim_states']):
        tex = Block(b.data, 8)
        assert tex.name == 'tex_idxs' and u32(tex.data) == 1
        vs = u32(b.data, tex.end + 4)
        prims.append((textures[u32(tex.data, 4)], u32(b.data,4), vs, u32(b.data,tex.end+8)))
    result=[]
    for lod in table(root['lod_controls']):
        levels=lod.children()[1]
        level=table(levels)[0]  # highest detail LOD
        header, subs=level.children()
        hierarchy=header.children()[1]
        parents=struct.unpack_from('<'+'i'*u32(hierarchy.data),hierarchy.data,4)
        def matrix(i, seen=()):
            assert i not in seen
            vals=struct.unpack('<12f',matrices[i].data)
            m=np.eye(4); m[:3,:3]=np.array(vals[:9]).reshape(3,3).T; m[:3,3]=vals[9:]
            return matrix(parents[i],seen+(i,))@m if parents[i]>=0 else m
        for sub in table(subs):
            children={c.name:c for c in sub.children()}
            vertices=[]
            for v in table(children['vertices']):
                uvref=Block(v.data,20)
                assert uvref.name=='vertex_uvs' and u32(uvref.data)>=1
                vertices.append((u32(v.data,4),u32(v.data,8),u32(uvref.data,4)))
            prim_index=None
            for b in children['primitives'].children(4):
                if b.name=='prim_state_idx':
                    prim_index=u32(b.data); continue
                assert b.name=='indexed_trilist', b.name
                idx=b.children()[0]
                indices=np.array(struct.unpack_from('<'+'I'*u32(idx.data),idx.data,4)).reshape(-1,3)
                texture, shader, vs, alphatest=prims[prim_index]
                mi=states[vs][1]; transform=matrix(mi)
                used=sorted(set(indices.flatten().tolist())); mapping={v:i for i,v in enumerate(used)}
                p=np.array([pts[vertices[i][0]] for i in used]); p=(transform@np.c_[p,np.ones(len(p))].T).T[:,:3]
                n=np.array([ns[vertices[i][1]] for i in used]); n=(np.linalg.inv(transform[:3,:3]).T@n.T).T
                n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-9)
                # MSTS stores shape Z opposite to the lamp positions in sigcfg.dat.
                # Match the user-provided OBJ / Unity front-facing positive Z.
                p[:,2]*=-1; n[:,2]*=-1
                t=np.array([uv[vertices[i][2]] for i in used]); t[:,1]=1-t[:,1]
                faces=np.vectorize(mapping.get)(indices)
                # Reflecting Z changes handedness. Preserve the source topology
                # with one uniform reversal, including any matrix reflection.
                if np.linalg.det(transform[:3,:3]) > 0:
                    faces=faces[:,[0,2,1]]
                result.append(dict(name=matrices[mi].label, texture=Path(images[texture]).stem.lower()+'.png',
                                   cutout=bool(alphatest or 'Blend' in shaders[shader]), p=p,n=n,uv=t,f=faces))
    return result

def obj_parts(path):
    p=[]; n=[]; uv=[]; groups={}; name='mesh'; material=''
    for line in Path(path).read_text().splitlines():
        a=line.split()
        if not a: continue
        if a[0]=='v': p.append([float(v) for v in a[1:4]])
        elif a[0]=='vn': n.append([float(v) for v in a[1:4]])
        elif a[0]=='vt': uv.append([float(v) for v in a[1:3]])
        elif a[0]=='o': name=a[1]
        elif a[0]=='usemtl': material=a[1]
        elif a[0]=='f':
            assert len(a)==4, 'Expected triangles in supplied OBJ'
            groups.setdefault((name,material),[]).append([tuple(int(j)-1 for j in v.split('/')) for v in a[1:]])
    result=[]
    for (name,mat),triangles in groups.items():
        refs=list(dict.fromkeys(v for tri in triangles for v in tri)); mapping={v:i for i,v in enumerate(refs)}
        result.append(dict(name=name+'_'+mat,texture='t_signal.png',cutout=False,
            p=np.array([p[r[0]] for r in refs]), n=np.array([n[r[2]] for r in refs]),
            uv=np.array([uv[r[1]] for r in refs]), f=np.array([[mapping[r] for r in tri] for tri in triangles])))
    return result

def ace_png(path,out):
    b=Path(path).read_bytes(); d=zlib.decompress(b[16:]) if b[:8]==b'SIMISA@F' else b[16:]
    magic,flags,w,h,fmt,count=struct.unpack_from('<6I',d)
    assert magic==1, (path,fmt,flags)
    channels=[struct.unpack_from('<QQ',d,152+i*16) for i in range(count)]
    offset=152+count*16
    levels=1+int(math.log2(w)) if flags&1 else 1
    if flags&16:
        offset+=levels*4
        size=u32(d,offset); offset+=4
        codes={18:b'DXT1',20:b'DXT3',22:b'DXT5'}
        assert fmt in codes, (path,fmt)
        header=bytearray(128); header[:4]=b'DDS '
        struct.pack_into('<7I',header,4,124,0x81007,h,w,size,0,1)
        struct.pack_into('<II',header,76,32,4); header[84:88]=codes[fmt]
        struct.pack_into('<I',header,108,0x1000)
        Image.open(BytesIO(bytes(header)+d[offset:offset+size])).convert('RGBA').save(out)
        return dict(source=str(path),sha256=hashlib.sha256(b).hexdigest(),width=w,height=h)
    offset+=sum(4*max(1,h>>i) for i in range(levels))
    rgba=np.full((h,w,4),255,dtype=np.uint8)
    for y in range(h):
        rows={}
        for bits,channel in channels:
            assert bits in (1,8)
            size=(w*bits+7)//8
            raw=np.frombuffer(d[offset:offset+size],dtype=np.uint8); offset+=size
            rows[channel]=np.unpackbits(raw)[:w]*255 if bits==1 else raw
        for j,ch in enumerate((3,4,5)):
            rgba[y,:,j]=rows[ch]
        if 6 in rows: rgba[y,:,3]=rows[6]
        elif 2 in rows: rgba[y,:,3]=rows[2]
    Image.fromarray(rgba).save(out)
    return dict(source=str(path),sha256=hashlib.sha256(b).hexdigest(),width=w,height=h)

def unity_faces(part):
    # OBJ is already oriented; MSTS is corrected once in shape_parts when Z
    # is reflected. Smooth normals do not determine a face's visible side.
    # In particular, using them to flip individual visor faces tears the hood.
    return part['f'].copy()

def split_lenses(parts, model):
    """Separate the existing 16-triangle convex lenses, without adding triangles."""
    if model=='shunting':
        positions=[('Blue',.67),('MoonWhite',.885)]; radius,zmin,zmax=.13,.059,.068
    else:
        ground=1.5995299816131592
        positions=[('Red',6.825-ground),('Green',7.115-ground),('Yellow',7.405-ground)]
        radius,zmin,zmax=.14,.50775,.518
    result=[]; counts={name:0 for name,_ in positions}
    def subset(part,mask,name):
        indices=part['f'][mask]; used,inverse=np.unique(indices,return_inverse=True)
        return dict(part,name=name,p=part['p'][used],n=part['n'][used],uv=part['uv'][used],f=inverse.reshape(-1,3))
    for part in parts:
        t=part['p'][part['f']]; remaining=np.ones(len(t),dtype=bool)
        for name,y in positions:
            mask=((t[:,:,0]**2+(t[:,:,1]-y)**2).max(1)<radius**2)&(t[:,:,2].min(1)>zmin)&(t[:,:,2].max(1)<zmax)
            if mask.any():
                assert (remaining|~mask).all(), 'Overlapping lenses'
                result.append(subset(part,mask,name)); counts[name]+=int(mask.sum()); remaining&=~mask
        if remaining.any(): result.append(subset(part,remaining,part['name']))
    assert all(n==16 for n in counts.values()), counts
    return result

def write_mesh(parts,out):
    with open(out,'wb') as stream:
        stream.write(b'RUSMESH2'); stream.write(struct.pack('<I',len(parts)))
        for part in parts:
            for key in ('name','texture'):
                b=part[key].encode(); stream.write(struct.pack('<I',len(b))); stream.write(b)
            stream.write(struct.pack('<III',int(part['cutout']),len(part['p']),part['f'].size))
            stream.write(np.c_[part['p'],part['n'],part['uv']].astype('<f4').tobytes())
            stream.write(unity_faces(part).astype('<i4').tobytes())

def export_obj(parts,out):
    lines=['mtllib '+out.with_suffix('.mtl').name]; offset=1; mats=[]
    for i,part in enumerate(parts):
        lines+=['o '+part['name'], 'usemtl part'+str(i)]
        lines+=['v '+' '.join(format(v,'.9g') for v in p) for p in part['p']]
        lines+=['vt '+' '.join(format(v,'.9g') for v in uv) for uv in part['uv']]
        lines+=['vn '+' '.join(format(v,'.9g') for v in n) for n in part['n']]
        # In these unchanged XYZ coordinates, geometric normals and vertex
        # normals must agree in OBJ too; do not reverse the faces again.
        lines+=['f '+' '.join(f'{v+offset}/{v+offset}/{v+offset}' for v in tri) for tri in unity_faces(part)]
        mats+=['newmtl part'+str(i),'Kd 1 1 1','map_Kd '+part['texture'],'']
        offset+=len(part['p'])
    out.write_text('\n'.join(lines)+'\n'); out.with_suffix('.mtl').write_text('\n'.join(mats))

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--ru',type=Path,default=Path(r'D:\ru')); ap.add_argument('--obj',type=Path,required=True); ap.add_argument('--out',type=Path,required=True)
    args=ap.parse_args(); route=args.ru/'ROUTES/TestRoute_RUS'; args.out.mkdir(parents=True,exist_ok=True)
    models={'shunting':(args.obj,obj_parts(args.obj)), 'mast':(route/'SHAPES/T_Signal_Head_YGR-new.s',shape_parts(route/'SHAPES/T_Signal_Head_YGR-new.s'))}
    report={'models':{},'textures':{},'source_units':'metres; no scaling; positive Z is the signal face',
            'winding':'source triangle order preserved; MSTS Z reflection reverses indices once; corrected in 1.1.1'}
    for name,(source,parts) in models.items():
        ground=min(a['p'][:,1].min() for a in parts)
        for part in parts: part['p'][:,1]-=ground
        parts=split_lenses(parts,name)
        models[name]=(source,parts)
        for part in parts:
            assert np.isfinite(part['p']).all() and np.isfinite(part['n']).all() and np.isfinite(part['uv']).all()
            assert part['f'].min()>=0 and part['f'].max()<len(part['p'])
            assert len(part['p'])<65535
        write_mesh(parts,args.out/(name+'.rsm')); export_obj(parts,args.out/(name+'.obj'))
        p=np.concatenate([a['p'] for a in parts])
        report['models'][name]=dict(source=str(source),sha256=hashlib.sha256(source.read_bytes()).hexdigest(),ground_offset=float(ground),parts=len(parts),vertices=len(p),triangles=sum(len(a['f']) for a in parts),min=p.min(axis=0).tolist(),max=p.max(axis=0).tolist())
        for part in parts:
            texture=part['texture']
            if texture not in report['textures']:
                src=route/'TEXTURES'/Path(texture).with_suffix('.ace')
                report['textures'][texture]=ace_png(src,args.out/texture)
    # Verify supplied model is the same two-lens body as the RUS file at LOD0.
    original=shape_parts(route/'SHAPES/T_Signal_Small_GR.s')
    def triangles(parts):
        return sorted(tuple(sorted(tuple(np.round(v,5)) for v in part['p'][tri])) for part in parts for tri in part['f'])
    report['obj_matches_ru_triangles']=triangles(original)==triangles(models['shunting'][1])
    (args.out/'conversion-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))

if __name__=='__main__': main()
