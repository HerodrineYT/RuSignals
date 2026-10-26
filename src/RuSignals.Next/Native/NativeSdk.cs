using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using HarmonyLib;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;

// All non-public SDK access lives at this boundary, resolved once. No custom
// track objects are substituted for the game's native objects.
internal static class NativeSdk
{
    private static readonly ConstructorInfo BlockCtor=AccessTools.Constructor(typeof(TrackBlock),new[]{typeof(IEnumerable<TrackInfo>),typeof(BasicSignalController)});
    private static readonly MethodInfo SetLength=AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.Length));
    private static readonly MethodInfo SetLoop=AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.IsSelfLoop));
    private static readonly MethodInfo SetDead=AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.IsDeadEnd));
    private static readonly MethodInfo SetExtras=AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.ExtraTracks));
    private static readonly MethodInfo SetPlacement=AccessTools.PropertySetter(typeof(BasicSignalController),nameof(BasicSignalController.PlacementInfo));
    // Compile field reads once, rather than reflecting during every occupancy,
    // direction or reservation query. Each delegate still reads the LIVE SDK
    // field: replacing a world collection cannot leave a cached old instance.
    private static Func<T> StaticField<T>(Type owner,string name)=>Expression.Lambda<Func<T>>(
        Expression.Convert(Expression.Field(null,AccessTools.Field(owner,name)),typeof(T))).Compile();
    private static readonly Func<HashSet<RailTrack>> OccupiedField=StaticField<HashSet<RailTrack>>(typeof(TrackChecker),"s_occupied");
    private static readonly Func<HashSet<Signal>> SignalsField=StaticField<HashSet<Signal>>(typeof(TrackReserver),"s_signals");
    private static readonly Func<Dictionary<RailTrack,Signal>> ReservationsField=StaticField<Dictionary<RailTrack,Signal>>(typeof(TrackReserver),"s_reservations");
    private static readonly Func<IDictionary> TimersField=StaticField<IDictionary>(typeof(TrackReserver),"s_clearRoutines");
    private static readonly Func<IDictionary> TimesField=StaticField<IDictionary>(typeof(TrackReserver),"s_times");
    private static readonly FieldInfo WarningsField=AccessTools.Field(typeof(SignalManager),"_distantSignals");
    internal static void AddWarning(DistantSignalController warning)=>((IList)WarningsField.GetValue(SignalManager.Instance)).Add(warning);
    internal static HashSet<RailTrack> ManualOccupancy=>OccupiedField();
    internal static HashSet<Signal> Reservations=>SignalsField();
    internal static Dictionary<RailTrack,Signal> ReservationTracks=>ReservationsField();
    internal static IDictionary Timers=>TimersField();
    internal static IDictionary Times=>TimesField();
    internal static TrackBlock CreateBlock(RailSpan[] spans,BasicSignalController next,TrackBlock source,RailTrack approach,RailTrack behindJunction)
    {
        var tracks=new TrackInfo[spans.Length];double length=0;
        for(int i=0;i<tracks.Length;i++){tracks[i]=new TrackInfo(spans[i].Track,spans[i].Direction);length+=spans[i].Length;}
        var block=(TrackBlock)BlockCtor.Invoke(new object[]{tracks,next});
        if(approach!=null||behindJunction!=null)
        {
            var extras=new List<RailTrack>();foreach(var track in block.ExtraTracks)if(track!=approach&&track!=behindJunction)extras.Add(track);
            SetExtras.Invoke(block,new object[]{extras.ToArray()});
        }
        SetLength.Invoke(block,new object[]{(float)length});SetLoop.Invoke(block,new object[]{source.IsSelfLoop});
        SetDead.Invoke(block,new object[]{next==null&&source.IsDeadEnd});return block;
    }
    internal static void RefreshSpan(BasicSignalController controller)
    {
        var p=controller.PlacementInfo.Value;p.Span=p.Track.GetKinkedPointSet().points[p.PointIndex].span;
        SetPlacement.Invoke(controller,new object[]{p});
    }
    internal static void RemoveWarning(DistantSignalController distant)
    {
        var callback=(Action<Signal,Signals.Game.Aspects.IAspect>)Delegate.CreateDelegate(typeof(Action<Signal,Signals.Game.Aspects.IAspect>),distant,AccessTools.Method(typeof(DistantSignalController),"UpdateFromHome"));
        distant.Home.AnyAspectChanged-=callback;
        ((IList)WarningsField.GetValue(SignalManager.Instance)).Remove(distant);
        distant.Destroy();
    }
}
