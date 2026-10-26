"""Shared, offline mechanical reconstruction based on the 8371-00 arrangement.

Functional geometry, not a certified factory-dimensioned 8371 drawing. Retains
the existing mast, blades and fixed lanterns; replaces only the wing transmission.
"""
import json, math
import numpy as np
from rebuild_hoods import ASSETS, OUTPUT, read, write, components, digest
import rebuild_semaphore_clearance as previous
from verify_semaphore_drive import pieces, drive_islands

class Mesh:
    def __init__(self): self.v=[]; self.f=[]
    def face(self,points,tile='black'):
        p=np.array(points,dtype=float);n=np.cross(p[1]-p[0],p[2]-p[0]);n/=np.linalg.norm(n)
        col,row={'black':(2,0),'steel':(3,0),'dark':(3,3),'brass':(1,1)}[tile]
        start=len(self.v)
        for i,x in enumerate(p):self.v.append([*x,*n,(col+.05+.9*(i%2))/4,1-(row+.05+.9*(i//2%2))/4])
        for i in range(1,len(p)-1):self.f.append([start,start+i,start+i+1])
    def box(self,c,size,tile='black'):
        c=np.array(c);s=np.array(size)/2;p=[c+s*np.array([x,y,z]) for x in [-1,1] for y in [-1,1] for z in [-1,1]]
        for face in [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]:self.face([p[i] for i in face],tile)
    def tube(self,a,b,r,tile='steel',sides=10):
        a,b=np.array(a,dtype=float),np.array(b,dtype=float);axis=b-a;axis/=np.linalg.norm(axis)
        u=np.cross(axis,[0,0,1] if abs(axis[2])<.9 else [0,1,0]);u/=np.linalg.norm(u);w=np.cross(axis,u)
        ring=[r*(u*math.cos(i*math.tau/sides)+w*math.sin(i*math.tau/sides)) for i in range(sides)]
        self.face([a+x for x in reversed(ring)],tile);self.face([b+x for x in ring],tile)
        for i in range(sides):j=(i+1)%sides;self.face([a+ring[i],a+ring[j],b+ring[j],b+ring[i]],tile)
    def ring(self,c,r,width,depth,tile='dark',sides=32):
        x,y,z=c
        for i in range(sides):
            a,b=i*math.tau/sides,(i+1)*math.tau/sides
            def point(rad,angle,zz):return [x+rad*math.cos(angle),y+rad*math.sin(angle),z+zz]
            for sign in [-1,1]:
                quad=[point(r-width,a,sign*depth/2),point(r-width,b,sign*depth/2),point(r,b,sign*depth/2),point(r,a,sign*depth/2)]
                self.face(quad[::-1] if sign>0 else quad,tile)
            for rad in [r-width,r]:
                quad=[point(rad,a,-depth/2),point(rad,b,-depth/2),point(rad,b,depth/2),point(rad,a,depth/2)]
                self.face(quad[::-1] if rad<r else quad,tile)
    def part(self,name):return dict(name=name,texture='semaphore_albedo.png',cutout=False,vertices=np.array(self.v),indices=np.array(self.f,dtype=np.int32))

def remove(part,ids):
    faces=part['indices'][~np.isin(part['indices'],list(ids)).any(axis=1)];used,inverse=np.unique(faces,return_inverse=True)
    part['vertices']=part['vertices'][used];part['indices']=inverse.reshape(-1,3)
def join(a,b):
    offset=len(a['vertices']);a['vertices']=np.concatenate([a['vertices'],b['vertices']]);a['indices']=np.concatenate([a['indices'],b['indices']+offset])
def save(name,mesh):write([mesh.part(name)],OUTPUT/'Semaphore'/f'{name}.rsm')
def rot(p,angle):
    a=math.radians(angle);c,s=math.cos(a),math.sin(a);return np.array([p[0]*c-p[1]*s,p[0]*s+p[1]*c])

def main():
    previous.main()
    path=OUTPUT/'Semaphore/semaphore_static.rsm';part,islands=pieces(path)
    old=drive_islands(islands);remove(part,{i for p in old for i in p['ids']})
    fixed=Mesh()
    # Fixed frame supports a separate wheel spindle and common output lever axle.
    fixed.box((.04,.78,.30),(.34,.56,.18));fixed.box((.04,.97,.34),(.34,.12,.14))
    fixed.tube((0,.97,.22),(0,.97,.565),.022,'steel',16)
    fixed.tube((.04,.97,.25),(.04,.97,.455),.018,'steel',16)
    # Continuous two-wire input, tangent to the common wheel. Lower guide
    # sheaves rotate about X; the cable turns through a real YZ quarter arc.
    for x in [.04-.1525,.04+.1525]:
        fixed.tube((x,.97,.43),(x,.33,.43),.0025,'steel',8)
        fixed.tube((x-.009,.33,.402),(x+.009,.33,.402),.0255,'dark',20)
        arc=[[x,.33-.028*math.sin(a),.402+.028*math.cos(a)] for a in np.linspace(0,math.pi/2,13)]
        for a,b in zip(arc,arc[1:]):fixed.tube(a,b,.0025,'steel',8)
        fixed.tube((x,.302,.402),(x,.302,-.85),.0025,'steel',8)
    wrap=[[.04+.1525*math.cos(a),.97+.1525*math.sin(a),.43] for a in np.linspace(0,math.pi,33)]
    for a,b in zip(wrap,wrap[1:]):fixed.tube(a,b,.0025,'steel',8)
    join(part,fixed.part('fixed transmission'));write([part],path)
    # Identical lever radii above/below make each rod a true parallelogram side.
    for name,plane in [('upper',.48),('lower',.54)]:
        file=OUTPUT/'Semaphore'/f'semaphore_{name}.rsm';p,=read(file)
        cs=components(dict(vertices=p['vertices'].tolist(),indices=p['indices'].reshape(-1).tolist()))
        cam=[c for c in cs if len(c['indices'])//3==76 and np.allclose(np.array(c['max'])-c['min'],(.24,.24,.04),atol=.0002)];assert len(cam)==1
        remove(p,set(cam[0]['indices']));crank=Mesh();z=plane-.44
        crank.tube((0,0,z-.014),(0,0,z+.014),.043,'dark',20)
        crank.tube((0,0,z),(.1847759065,-.0765366865,z),.019,'dark',10)
        crank.tube((.1847759065,-.0765366865,z-.020),(.1847759065,-.0765366865,z+.020),.018,'steel',12)
        join(p,crank.part('crank'));write([p],file)
    pin=np.array([.1847759065,-.0765366865,0.]);normal=np.array([-pin[1],pin[0],0.]);normal/=np.linalg.norm(normal)
    def drive_arm(fork):
        arm=Mesh();path=[np.zeros(3),pin*.35,pin*.70-normal*.06,pin] if fork else [np.zeros(3),pin]
        for a,b in zip(path,path[1:]):arm.tube(a,b,.017,'dark',10)
        arm.tube((0,0,-.018),(0,0,.018),.035,'dark',20)
        arm.tube(pin-[0,0,.020],pin+[0,0,.020],.018,'steel',12)
        arm.tube((0,0,0),(-.20,.16,0),.014,'dark',10)
        arm.box((-.20,.16,0),(.105,.12,.025));return arm
    write([drive_arm(True).part('lever')],OUTPUT/'Semaphore/semaphore_lever.rsm')
    save('mechanism8371_lower_arm',drive_arm(False))
    wheel=Mesh();wheel.tube((0,0,-.014),(0,0,.014),.145,'dark',48)
    wheel.ring((0,0,0),.1525,.0075,.034,'steel',48)
    # Machined groove centrelines evaluated in the wheel frame. Followers at
    # two radii move the coaxial arms; one branch dwells for single-wing entry.
    for k in [.35,.70]:
        path=[]
        for t in np.linspace(-1,1,121):
            arm=-45*abs(t) if k==.35 else 90-135*max(t,0)
            roller=rot([.1847759065*k,-.0765366865*k],arm)-[.04,0]
            xy=rot(roller,-60*t);path.append([*xy,.020])
        for a,b in zip(path,path[1:]):
            a,b=np.array(a),np.array(b);delta=b[:2]-a[:2];normal=np.array([-delta[1],delta[0],0.]);normal/=np.linalg.norm(normal)
            a[2]=b[2]=.0145
            wheel.face([a-normal*.0095,b-normal*.0095,b+normal*.0095,a+normal*.0095],'black')
            a[2]=b[2]=.018
            for sign in [-1,1]:
                lo,hi=sorted([.0095*sign,.0125*sign]);wheel.face([a+normal*lo,b+normal*lo,b+normal*hi,a+normal*hi],'steel')
    wheel.tube((0,0,-.02),(0,0,.028),.018,'steel',16);save('mechanism8371_wheel',wheel)
    clamp=Mesh();clamp.box((0,0,0),(.014,.023,.016),'steel');save('mechanism8371_clamp',clamp)
    roller=Mesh();roller.tube((0,0,0),(0,0,1),.009,'steel',12);save('mechanism8371_roller',roller)
    # No movable spectacle/filter mechanism: the user requests fixed lanterns.
    for unused in ['mechanism8371_upper_coupling','mechanism8371_lower_coupling']:
        file=OUTPUT/'Semaphore'/(unused+'.rsm')
        if file.exists():file.unlink()
    metadata={'reference':'https://poezdvl.com/vychislitelnaia-tehnika-na-zhd-rf/dvukrylyi-semafor.html',
              'scope':'Functional reconstruction of common profiled wheel, coaxial arms, pin-jointed rigid rods, wire transmission and fixed lanterns; inferred dimensions, not certified factory drawing.',
              'fixed_drive_axis':[0,.97],'wing_pin':[.1847759065,-.0765366865],'rod_lengths':[7.08,5.80],
              'rod_planes':[.48,.54],'wheel_radius':.1525,'wheel_axis':[.04,.97,.43],
              'shared_meshes':True,'individual_updates':0,'fixed_lanterns':True}
    (OUTPUT/'mechanism8371.json').write_text(json.dumps(metadata,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(metadata))
if __name__=='__main__':main()
