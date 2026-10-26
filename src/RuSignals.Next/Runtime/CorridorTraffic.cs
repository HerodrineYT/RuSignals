using System;
using Signals.Game;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal sealed partial class BlockNetwork
{
    internal struct Directions
    {
        private readonly BlockNetwork owner;private readonly RailSpan[] spans;private int index;
        internal Directions(BlockNetwork network,Signal signal)
        {owner=network;spans=signal?.Block!=null&&network.Windows.TryGetValue(signal.Block,out var window)?window.Spans:Array.Empty<RailSpan>();index=0;Current=default;}
        public (Corridor Path,int Sign) Current{get;private set;}
        public Directions GetEnumerator()=>this;
        public bool MoveNext()
        {
            while(index<spans.Length)
            {
                var span=spans[index++];if(!owner.Tracks.TryGetValue(span.Track,out var link))continue;
                Current=(link.Path,link.Forward==span.Direction?1:-1);return true;
            }
            return false;
        }
    }
    internal bool SameDirection(Signal a,Signal b)
    {
        foreach(var x in new Directions(this,a))foreach(var y in new Directions(this,b))if(x.Path==y.Path&&x.Sign==y.Sign)return true;
        return false;
    }
    private bool Opposing(Corridor path,int sign,Signal except=null)
    {
        foreach(var reservation in NativeSdk.Reservations)
        {
            if(!SignalManager.Instance.TryGetSignal(reservation.Id,out var owner)||owner==except)continue;
            foreach(var direction in new Directions(this,owner))if(direction.Path==path&&direction.Sign!=sign)
            {
                if(except!=null&&Entry(except)&&ReservationBehindMast(except,owner,path,sign))continue;
                return true;
            }
        }
        return false;
    }
    private static bool Entry(Signal signal)
    {
        if(signal.IsShunting)return false;
        foreach(var aspect in signal.AllAspects)if(aspect.GetDefinition() is RuAspectDefinition definition&&definition.Role==SignalRole.Entry)return true;
        return false;
    }
    private bool ReservationBehindMast(Signal entry,Signal owner,Corridor path,int sign)
    {
        var block=IntervalReservations.ProtectedBlock(owner);
        if(block==null||!Windows.TryGetValue(block,out var protection)||!TryMast(entry,path,out double mast))return false;
        // Prove the saved claim on this shared corridor only. Other rails and
        // crossing/fouling claims still pass through IntervalReservations.Conflict.
        // A changed live route does not move the reservation's saved footprint.
        bool found=false;double offset=0;
        foreach(var rail in path.Spans)
        {
            double Along(double position)=>offset+(position-rail.Start)*(rail.Direction==TrackDirection.Out?1:-1);
            bool Behind(double a,double b)=>!double.IsNaN(a)&&!double.IsInfinity(a)&&!double.IsNaN(b)&&!double.IsInfinity(b)&&
                sign*(Along(a)-mast)<=0&&sign*(Along(b)-mast)<=0;
            foreach(var claim in protection.Spans)if(claim.Track==rail.Track)
            {found=true;if(!Behind(claim.Start,claim.End))return false;}
            // A full fouling-protection rail may reach past the mast even if
            // the primary interval is behind it. Do not discard that claim.
            if(!protection.Primary.Contains(rail.Track)&&Array.IndexOf(block.ExtraTracks,rail.Track)>=0)
            {found=true;if(!Behind(rail.Start,rail.End))return false;}
            offset+=rail.Length;
        }
        return found;
    }
    internal bool Allows(Signal signal)
    {
        if(!Ready)return true;
        PollTraffic();
        foreach(var direction in new Directions(this,signal))
        {
            var path=direction.Path;
            // Shunting back out is normal gameplay. Historical direction must
            // not close a clear route when every bogie is behind this mast.
            if((path.Unknown||(path.Occupied&&path.Direction!=direction.Sign))&&
                !ApproachOnly(signal,path,direction.Sign))return false;
            if(Opposing(path,direction.Sign,signal))return false;
        }
        return true;
    }
    private bool TryMast(Signal signal,Corridor path,out double mast)
    {
        mast=0;if(!Windows.TryGetValue(signal.Block,out var window))return false;
        foreach(var route in window.Spans)
        {
            double offset=0;
            foreach(var span in path.Spans)
            {
                if(route.Track==span.Track)
                {mast=offset+(route.Start-span.Start)*(span.Direction==TrackDirection.Out?1:-1);return !double.IsNaN(mast)&&!double.IsInfinity(mast);}
                offset+=span.Length;
            }
        }
        return false;
    }
    private bool ApproachOnly(Signal signal,Corridor path,int sign)
    {
        if(!TryMast(signal,path,out double mast))return false;
        double along=0;var manual=NativeSdk.ManualOccupancy;
        foreach(var span in path.Spans)
        {
            if(manual.Contains(span.Track))return false;
            foreach(var bogie in span.Track.BogiesOnTrack())
            {
                if(bogie==null||bogie.traveller==null)return false;
                double position=bogie.traveller.Span;
                if(double.IsNaN(position)||double.IsInfinity(position)||
                    sign*(along+(position-span.Start)*(span.Direction==TrackDirection.Out?1:-1)-mast)>=0)return false;
            }
            along+=span.Length;
        }
        // Stationary and reversing approach trains do not occupy the route
        // ahead. Manual closures and unknown bogies were rejected above.
        return true;
    }
    private void PollTraffic()
    {
        if(polledFrame==Time.frameCount)return;polledFrame=Time.frameCount;
        var manual=NativeSdk.ManualOccupancy;
        foreach(var path in Corridors)
        {
            bool occupied=false,uncertain=false;int movement=0;
            foreach(var span in path.Spans)
            {
                if(manual.Contains(span.Track)){occupied=true;uncertain=true;}
                foreach(var bogie in span.Track.BogiesOnTrack())
                {
                    occupied=true;if(bogie==null||bogie.traveller==null){uncertain=true;continue;}
                    if(double.IsNaN(bogie.traveller.Span)||double.IsInfinity(bogie.traveller.Span)){uncertain=true;continue;}
                    if(bogie.rb==null)continue;
                    var geometry=span.Track.GetKinkedPointSet();var point=geometry.points[geometry.GetPointIndexForSpan(bogie.traveller.Span)];
                    float speed=Vector3.Dot(bogie.rb.velocity,point.forward)*(span.Direction==TrackDirection.Out?1:-1);
                    if(Math.Abs(speed)<.05f)continue;
                    int sign=speed>0?1:-1;if(movement!=0&&movement!=sign)uncertain=true;movement=sign;
                }
            }
            if(!occupied){path.Occupied=path.Unknown=path.Conflict=false;continue;}
            // A reversal is not a collision. Re-evaluate actual conflicting
            // movement and reservations, rather than latching a direction
            // change until the entire physical corridor becomes empty.
            path.Conflict=uncertain||(movement!=0&&Opposing(path,movement));
            if(!path.Occupied)path.Unknown=movement==0||path.Conflict;
            if(!path.Conflict&&movement!=0){path.Direction=movement;path.Unknown=false;}
            if(path.Conflict)path.Unknown=true;
            path.Occupied=true;
        }
    }
}
