"""Actual triangle collision regression for the 8371 vertical moving rods.

No Unity/game launch. Samples the input wheel parameter and checks actual
exported rod surfaces against all static mast/ladder/lantern/transmission parts.
The report distinguishes exact triangle intersections from broad-phase boxes.
"""
from pathlib import Path
import argparse,json,math
import numpy as np
from rebuild_hoods import ASSETS
from verify_semaphore_drive import pieces


def triangles(part,piece):
    faces=part['indices'][np.isin(part['indices'],piece['ids']).all(axis=1)]
    return part['vertices'][faces,:3].astype(float)


def triangle_overlap(a,b):
    """SAT for actual triangle pairs, including in-plane coplanar axes."""
    ae=np.roll(a,-1,axis=0)-a
    be=np.roll(b,-1,axis=1)-b
    an=np.cross(ae[0],ae[1]);bn=np.cross(be[:,0],be[:,1])
    axes=[np.broadcast_to(an,(len(b),3)),bn]
    axes += [np.cross(np.broadcast_to(e,(len(b),3)),be[:,j]) for e in ae for j in range(3)]
    axes += [np.broadcast_to(np.cross(an,e),(len(b),3)) for e in ae]
    axes += [np.cross(bn,be[:,j]) for j in range(3)]
    alive=np.ones(len(b),bool)
    for axis in axes:
        norm=np.linalg.norm(axis,axis=1);valid=norm>1e-10
        normal=axis/np.maximum(norm[:,None],1e-30)
        pa=np.einsum('ij,kj->ik',normal,a);pb=np.einsum('ij,ikj->ik',normal,b)
        separated=(pa.max(1)<pb.min(1)-1e-7)|(pb.max(1)<pa.min(1)-1e-7)
        alive &= ~(valid&separated)
        if not alive.any():return False
    return bool(alive.any())


def projected_distance(point,tris):
    a=tris[:,:,:2];edge=np.roll(a,-1,axis=1)-a;delta=point-a
    fraction=np.clip(np.sum(delta*edge,axis=2)/np.maximum(np.sum(edge*edge,axis=2),1e-30),0,1)
    distance=np.linalg.norm(delta-fraction[:,:,None]*edge,axis=2).min(1)
    cross=edge[:,:,0]*delta[:,:,1]-edge[:,:,1]*delta[:,:,0]
    ab=a[:,1]-a[:,0];ac=a[:,2]-a[:,0];area=ab[:,0]*ac[:,1]-ab[:,1]*ac[:,0]
    inside=((cross>=-1e-10).all(1)|(cross<=1e-10).all(1))&(abs(area)>1e-12)
    distance[inside]=0
    return float(distance.min())


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--assets',type=Path,default=ASSETS)
    ap.add_argument('--samples',type=int,default=1001)
    ap.add_argument('--pin',type=float,nargs=2,help='Optional proposal-only pin XY; never edits exported assets')
    ap.add_argument('--report',type=Path,default=ASSETS.parents[2]/'artifacts/ru-signals-4.3.4/rod-static-clearance-independent.json')
    args=ap.parse_args();meta=json.loads((args.assets/'SolidHoods/mechanism8371.json').read_text())
    pin=np.array(args.pin if args.pin is not None else meta['wing_pin']);obstacles=[]
    for key in ('static','lowerfittings'):
        part,ps=pieces(args.assets/f'SolidHoods/Semaphore/semaphore_{key}.rsm')
        for i,p in enumerate(ps):obstacles.append((key,i,p,triangles(part,p)))
    lows=np.array([p['low'] for _,_,p,_ in obstacles]);highs=np.array([p['high'] for _,_,p,_ in obstacles])
    records=[];broad_checks=0;triangle_checks=0;continuous_static=[]
    for name,source_x,source_length,pivot_y,length,plane in [('upper',.13,6.96,8.05,7.08,.48),('lower',.065,5.68,6.77,5.80,.54)]:
        part,ps=pieces(args.assets/f'SolidHoods/Semaphore/semaphore_{name}rod.rsm')
        source=part['vertices'][:,:3].astype(float);rod_triangles=[triangles(part,p) for p in ps]
        low_angle,high_angle=(-45,0) if name=='upper' else (-45,90)
        critical=[low_angle,high_angle]
        for base in (math.degrees(math.atan2(-pin[1],pin[0])),math.degrees(math.atan2(pin[0],pin[1]))):
            critical += [base+n*180 for n in range(-2,3) if low_angle<=base+n*180<=high_angle]
        rotated=np.array([[pin[0]*math.cos(math.radians(a))-pin[1]*math.sin(math.radians(a)),pin[0]*math.sin(math.radians(a))+pin[1]*math.cos(math.radians(a))+pivot_y] for a in critical])
        envelope_overlaps=[];minimum_gap=math.inf
        for ri,p in enumerate(ps):
            lo=p['low'].astype(float).copy();hi=p['high'].astype(float).copy()
            lo[0]+=rotated[:,0].min()-source_x;hi[0]+=rotated[:,0].max()-source_x
            lo[1]=(lo[1]-1.04)*length/source_length+rotated[:,1].min()-length
            hi[1]=(hi[1]-1.04)*length/source_length+rotated[:,1].max()-length
            lo[2]+=plane+.59;hi[2]+=plane+.59
            separation=np.maximum(np.maximum(lows-hi,lo-highs),0)
            minimum_gap=min(minimum_gap,float(np.linalg.norm(separation,axis=1).min()))
            envelope_overlaps += [dict(rod_component=ri,obstacle_key=obstacles[i][0],obstacle_component=obstacles[i][1]) for i in np.flatnonzero(np.all(hi>lows+1e-7,axis=1)&np.all(lo<highs-1e-7,axis=1))]
        continuous_static.append(dict(rod=name,conservative_continuous_minimum_gap_m=minimum_gap,overlapping_envelopes=envelope_overlaps))
        found=set()
        for t in np.linspace(-1,1,args.samples):
            angle=-45*abs(t) if name=='upper' else 90-135*max(t,0)
            rad=math.radians(angle);c,s=math.cos(rad),math.sin(rad)
            top_x=pin[0]*c-pin[1]*s;top_y=pivot_y+pin[0]*s+pin[1]*c
            scale=length/source_length
            for rod_index,(p,rt) in enumerate(zip(ps,rod_triangles)):
                v=rt.copy();v[:,:,0]+=top_x-source_x;v[:,:,1]=(v[:,:,1]-1.04)*scale+top_y-length;v[:,:,2]+=plane+.59
                lo=v.min((0,1));hi=v.max((0,1))
                candidates=np.flatnonzero(np.all(hi>lows+1e-7,axis=1)&np.all(lo<highs-1e-7,axis=1));broad_checks+=len(candidates)
                for oi in candidates:
                    key,index,obstacle,ot=obstacles[oi]
                    pair=(rod_index,key,index)
                    if pair in found:continue
                    triangle_checks+=len(v)*len(ot)
                    collision=any(triangle_overlap(triangle,ot) for triangle in v)
                    if collision:
                        found.add(pair);records.append(dict(rod=name,rod_component=rod_index,obstacle_key=key,obstacle_component=index,drive_parameter=float(t),angle_deg=float(angle),rod_min=lo.tolist(),rod_max=hi.tolist(),obstacle_min=obstacle['low'].tolist(),obstacle_max=obstacle['high'].tolist()))
    # Each real long follower shaft must also clear the opposite output arm.
    # This catches the layer-stack error hidden by checking only rod/housing.
    roller,roller_parts=pieces(args.assets/'SolidHoods/Semaphore/mechanism8371_roller.rsm')
    roller_tri=triangles(roller,roller_parts[0]);roller_cross=[]
    arm_data=[pieces(args.assets/'SolidHoods/Semaphore/semaphore_lever.rsm'),pieces(args.assets/'SolidHoods/Semaphore/mechanism8371_lower_arm.rsm')]
    for i,(k,plane) in enumerate(zip((.35,.70),(.48,.54))):
        other_plane=(.54,.48)[i];op,ops=arm_data[1-i];found=set()
        for t in np.linspace(-1,1,args.samples):
            angles=(-45*abs(t),90-135*max(t,0));angle=angles[i]-angles[1-i]
            r=math.radians(angle);c,s=math.cos(r),math.sin(r);rotation=np.array([[c,-s],[s,c]])
            rv=roller_tri.copy();rv[:,:,:2]=(rv[:,:,:2]+k*pin)@rotation.T
            rv[:,:,2]=.4445-other_plane+rv[:,:,2]*(plane-.4445)
            lo=rv.min((0,1));hi=rv.max((0,1))
            for pi,p in enumerate(ops):
                if pi in found or not(np.all(hi>p['low']+1e-7) and np.all(lo<p['high']-1e-7)):continue
                ot=triangles(op,p)
                if any(triangle_overlap(a,ot) for a in rv):
                    found.add(pi);roller_cross.append(dict(roller=('upper','lower')[i],opposite_arm_component=pi,drive_parameter=float(t),relative_angle_deg=float(angle)))
    # Continuous lower-follower proof: projected triangles conservatively
    # contain every depth intersection. The curve distance is 1-Lipschitz;
    # subtract half the maximum angular sample displacement and roller radius.
    upper,_=arm_data[0];upper_tris=upper['vertices'][upper['indices'],:3]
    minimum_projected=math.inf;delta_angles=np.linspace(0,135,6001)
    for angle in delta_angles:
        r=math.radians(angle);c,s=math.cos(r),math.sin(r)
        center=.70*np.array([pin[0]*c-pin[1]*s,pin[0]*s+pin[1]*c])
        minimum_projected=min(minimum_projected,projected_distance(center,upper_tris))
    roller_radius=float(np.linalg.norm(roller['vertices'][:,:2],axis=1).max())
    sampling_margin=float(np.linalg.norm(pin)*.70*math.radians(delta_angles[1]-delta_angles[0])/2)
    continuous_roller_gap=minimum_projected-roller_radius-sampling_margin
    report=dict(passed=not records and not roller_cross and not any(r['overlapping_envelopes'] for r in continuous_static) and continuous_roller_gap>0,pin_xy=pin.tolist(),proposal_only=args.pin is not None,input_parameter_samples=args.samples,broad_phase_candidates=broad_checks,triangle_pair_checks=triangle_checks,actual_triangle_intersections=records,roller_opposite_arm_triangle_intersections=roller_cross,continuous_static_rod_proof=continuous_static,continuous_lower_roller_upper_arm_clearance_m=continuous_roller_gap,continuous_roller_projection_sampling_margin_m=sampling_margin,scope='Actual RSM surfaces with triangle SAT; continuous rigid-rod component envelopes from analytic trigonometric extrema; lower follower versus actual projected upper-arm triangles over full0..135deg relative arc with rigorous Lipschitz sample margin')
    args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(passed=report['passed'],collisions=len(records),roller_cross_collisions=len(roller_cross),witnesses=records,roller_witnesses=roller_cross)))
    return 0 if report['passed'] else 1


if __name__=='__main__':raise SystemExit(main())
