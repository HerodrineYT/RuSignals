// Controlled rail topology and Unity/SDK doubles. Production adapters are linked
// verbatim; the test exercises their private Harmony callbacks via reflection.
using System.Reflection;
using Signals.Game;
using Signals.Game.Railway;
using Signals.Game.Controllers;
using UnityEngine;
using RuSignals;
static class Calls
{
    internal static object Call(Type t,string name,params object[] args) => HarmonyLib.AccessTools.Method(t,name).Invoke(null,args);
}
namespace HarmonyLib
{
    public static class Priority{public const int Last=0;}
    public class Harmony{public void Patch(MethodBase m,HarmonyMethod prefix=null,HarmonyMethod postfix=null){if(m==null)throw new Exception("Missing patch target");}}
    public class HarmonyMethod{public int priority;public HarmonyMethod(Type t,string m){if(AccessTools.Method(t,m)==null)throw new Exception("Missing patch callback");}}
    public static class AccessTools
    {
        const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        public static ConstructorInfo Constructor(Type t,Type[] args)=>t.GetConstructor(All,null,args,null);
        public static MethodInfo Method(Type t,string name,Type[] args=null)=>args==null?t.GetMethod(name,All):t.GetMethod(name,All,null,args,null);
        public static FieldInfo Field(Type t,string n)=>t.GetField(n,All);
        public static MethodInfo PropertySetter(Type t,string n)=>t.GetProperty(n,All).GetSetMethod(true);
        public static MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n,All).GetGetMethod(true);
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 operator -(Vector3 a)=>new(-a.x,-a.y,-a.z);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public class Material{} public class MeshRenderer{public Material sharedMaterial;}
    public class Transform{public string name="junc-l";public Vector3 position;public Transform parent=>this;}
    public class GameObject{public bool activeSelf;public void SetActive(bool value)=>activeSelf=value;}
    public class Coroutine{}
    public class Rigidbody{public Vector3 velocity;}
    public static class Time{public static int frameCount;}
    public static class Debug{public static void Log(string s){}public static void LogError(string s)=>throw new Exception(s);}
}
namespace DV.Utils
{
    public class CoroutineManager{public static CoroutineManager Instance=new();public void Stop(Coroutine c){}}
}
public sealed class Bogie{public Traveller traveller=new();public Rigidbody rb=new();}
public sealed class Traveller{public double Span;}
public class Point{public double span;public Vector3 position,forward=new(0,0,1);}
public class PointSet
{
    public double span;public Point[] points;
    public PointSet(double length){span=length;points=Enumerable.Range(0,(int)length+1).Select(i=>new Point{span=i,position=new(0,0,i)}).ToArray();}
    public int GetPointIndexForSpan(double s)=>Math.Clamp((int)Math.Round(s),0,points.Length-1);
}
public class RailTrack
{
    public string Number=""; public string name="line";public bool isJunctionTrack,logic,table;public Junction inJunction;
    public PointSet points;public HashSet<Bogie> bogies=new();
    public Dictionary<TrackDirection,TrackInfo> Links=new();
    public Dictionary<TrackDirection,BasicSignalController> Ends=new();
    public RailTrack(double length){points=new(length);}
    public PointSet GetKinkedPointSet()=>points;
    public static (Point closestPoint,float distanceToPoint) GetClosestPoint(RailTrack track,Vector3 position,float minDistFromEnd=0)=> (track.points.points[track.points.GetPointIndexForSpan(position.z)],0);
}
public class Junction{public struct JunctionData{public string junctionIdLong;}public JunctionData junctionData;public Transform transform=new();public int selectedBranch;public Branch inBranch;public List<Branch> outBranches=new();public IEnumerable<RailTrack> GetAllTracks()=>outBranches.Select(b=>b.track).Concat(inBranch==null?Array.Empty<RailTrack>():new[]{inBranch.track});}
public class Branch{public RailTrack track;}
public static class RailTrackOnTrackBogiesExtensions{public static HashSet<Bogie> BogiesOnTrack(this RailTrack t)=>t.bogies;}
public static class RailTrackRegistryBase{public static RailTrack[] RailTracks=Array.Empty<RailTrack>();}
namespace Signals.Common
{
    public enum CrossingCheckMode{Ignore,WholeTrack,IntersectionOnly}
    public class SignalControllerDefinition{public GameObject gameObject=new();public Vector3 Facing;}
    public class SignalDefinition{public Aspects.AspectBaseDefinition[] Aspects=new[]{new Aspects.AspectBaseDefinition()};}
    public class SignalPack{public string ModId=Mod.Id;public SignalControllerDefinition Signal=new();}
}
namespace Signals.Common.Aspects{public class AspectBaseDefinition{public bool DisallowPassing,UsePassingSpeed;}}
namespace Signals.Game
{
    public interface IHudDisplayable{string DisplayText{get;}}
    public enum TrackDirection{Out,In}
    public enum SignalType{Mainline,Entry,Shunting}
    public enum PrefabType{Mainline,Exit,ExitPax,ExitMainline}
    public enum SignalOperationMode{Automatic,SemiManual}
    public struct SignalPlacementInfo
    {
        public RailTrack Track;public TrackDirection Direction;public int PointIndex;public double Span;
        public SignalPlacementInfo(RailTrack t,TrackDirection d,int i,double s){Track=t;Direction=d;PointIndex=i;Span=s;}
    }
    public class Signal
    {
        public Signals.Common.SignalDefinition Definition=new();
        public string NameOverride;
        public Action<Aspects.IAspect> AspectChanged;
        public void TurnOff(){CurrentAspect=null;AspectChanged?.Invoke(null);}
        static int NextId;public int Id=NextId++;public bool IsShunting,ShuntingAllowed=true,AllowReserving=true;
        public BasicSignalController Controller;public TrackBlock Block;public SignalOperationMode Operation;public int ManualOverrideAspect;
        public Aspects.IAspect CurrentAspect;public Aspects.IAspect[] AllAspects;
        public bool IsOff=>CurrentAspect==null;
        public Signal(BasicSignalController c){Controller=c;AllAspects=Enum.GetValues<Indication>().Select(i=>(Aspects.IAspect)new RuAspect(new RuAspectDefinition{Role=SignalRole.Through,Indication=i,DisallowPassing=i==Indication.Stop,ManualOnly=i==Indication.Invitation},this)).ToArray();}
        public BasicSignalController GetNextController()=>Block?.NextController;
        public void SetBlock(TrackBlock block){object[] args={this,block};Calls.Call(typeof(AutomaticBlock),"SetBlock",args);Block=(TrackBlock)args[1];}
        public void Update(){DecisionPass.Begin(this,out var previous);try{CurrentAspect=AllAspects.Cast<RuAspect>().FirstOrDefault(a=>a.MeetsConditions());}finally{DecisionPass.End(previous);}}
    }
    public static class SignalsMod{public static Settings Settings=new();}
    public class Settings{public bool SpecialReservation;}
    public class SignalManager
    {
        public static SignalManager Instance=new();public static Signals.Common.SignalPack CurrentPack=new();
        public List<BasicSignalController> AllControllers=new();
        private List<DistantSignalController> _distantSignals=new();
        public void AddDistant(DistantSignalController d)=>_distantSignals.Add(d);
        public bool TryGetSignal(int id,out Signal s){s=AllControllers.SelectMany(c=>c.Signals).FirstOrDefault(s=>s.Id==id);return s!=null;}
        public bool TryGetJunctionGroup(Junction j,out JunctionSignalGroup g){g=null;return false;}
        private static void ClearCaches(){}private void OnDestroy(){}
    }
    public class StationControllerCache{public static void Generate(){}}
    public class JunctionSignalGroup
    {
        public BasicSignalController JunctionSignal,ReverseJunctionSignal;
        public bool TryGetControllerForTrack(RailTrack t,out BasicSignalController c){c=null;return false;}
    }
}
namespace Signals.Game.Aspects
{
    public interface IAspect{bool DisallowPassing{get;}Signals.Common.Aspects.AspectBaseDefinition GetDefinition();}
    public abstract class AspectBase<T>:IAspect where T:Signals.Common.Aspects.AspectBaseDefinition
    {
        public T Definition;public Signal Signal;public BasicSignalController Controller=>Signal.Controller;public TrackBlock Block=>Signal.Block;
        protected AspectBase(Signals.Common.Aspects.AspectBaseDefinition d,Signal s){Definition=(T)d;Signal=s;}
        public bool DisallowPassing=>Definition.DisallowPassing;public Signals.Common.Aspects.AspectBaseDefinition GetDefinition()=>Definition;
        public abstract bool MeetsConditions();
    }
}
namespace Signals.Game.Controllers
{
    public class BasicSignalController
    {
        public Signals.Common.SignalControllerDefinition Definition;public SignalPlacementInfo? PlacementInfo{get;protected set;}
        public SignalType Type;public PrefabType PrefabType;public string NameOverride;public bool Exists=true;
        public int? RequiredJunctionBranch;public Junction GroupJunction;public Signal[] Signals;
        public BasicSignalController(Signals.Common.SignalControllerDefinition d,SignalPlacementInfo p){Definition=d;PlacementInfo=p;Signals=new[]{new Signal(this)};SignalManager.Instance.AllControllers.Add(this);}
        public Action<Signal,Signals.Game.Aspects.IAspect> AnyAspectChanged;
        public Signal GetControllerSignal()=>Signals[0];
        public virtual bool ShouldUpdate()=>false;
        public void Destroy(){Exists=false;SignalManager.Instance.AllControllers.Remove(this);}
        public bool OffsetOnTrack(int offset){object[] args={this,offset};Calls.Call(typeof(AutomaticBlock),"ConstrainMovement",args);var p=PlacementInfo.Value;p.PointIndex+=(int)args[1];PlacementInfo=p;Calls.Call(typeof(AutomaticBlock),"PositionChanged",this,true);return true;}
        public virtual IEnumerable<(Signal Signal,Dictionary<BasicSignalController,float> Controllers)> GetPotentialNextControllers(){yield break;}
        public void FlagAllBlocksForUpdating(){foreach(var s in Signals)if(s.Block!=null)s.Block.Dirty=true;}
        public virtual bool UpdateBlocks()=>false;
    }
    public class TrackSignalController:BasicSignalController
    {
        public RailTrack StartingTrack;public TrackDirection Direction;
        public TrackSignalController(Signals.Common.SignalControllerDefinition d,RailTrack t,TrackDirection dir,SignalPlacementInfo p):base(d,p){StartingTrack=t;Direction=dir;}
        public override bool UpdateBlocks(){foreach(var s in Signals)if(s.Block==null||s.Block.ShouldBeUpdated)s.SetBlock(TrackBlock.CreateUntilMainSignal(StartingTrack,Direction));return true;}
    }
    public class TurntableSignalController:BasicSignalController{public bool IsConnected;public TurntableSignalController(Signals.Common.SignalControllerDefinition d,SignalPlacementInfo p):base(d,p){}}
    public class DistantSignalController:BasicSignalController
    {
        public bool IsRepeater; public BasicSignalController Home;
        public DistantSignalController(Signals.Common.SignalControllerDefinition d,SignalPlacementInfo p):base(d,p){foreach(var a in Signals[0].AllAspects)((RuAspectDefinition)a.GetDefinition()).Role=SignalRole.Distant;}
        private void UpdateFromHome(Signal s,Signals.Game.Aspects.IAspect a){}
    }
}
namespace Signals.Game.Generation
{
    public static class SignalPlacer
    {
        public static bool IsLogicYardTrack(RailTrack t)=>t.logic;
        public static Signals.Common.SignalControllerDefinition InstantiateFromDef(Signals.Common.SignalControllerDefinition d,Vector3 p,Vector3 f,bool o,RailTrack t)=>new(){Facing=f};
    }
}
namespace Signals.Game.Railway
{
    public readonly record struct TrackInfo(RailTrack Track,TrackDirection Direction){public bool IsJunctionTrack=>Track.isJunctionTrack;}
    public static class TurntableHelper{public static bool IsTurntableEnd(RailTrack t)=>t.table;}
    public static class TrackWalker
    {
        public static List<TrackInfo> WalkTracks(RailTrack t,TrackDirection d,int n)=>t.Links.TryGetValue(d,out var next)?new(){next}:new();
    }
    public class TrackBlock
    {
        public string Station;
        public TrackInfo[] Tracks{get;private set;} public RailTrack[] ExtraTracks{get;private set;}=Array.Empty<RailTrack>();
        public float Length{get;private set;}public bool IsSelfLoop{get;private set;}public bool IsDeadEnd{get;private set;}
        private string _trackTrimmerNumber;
        public string TrackTrimmerNumber{get=>_trackTrimmerNumber??TrackUtils.NextTrackTrimmedNumber(Tracks);set=>_trackTrimmerNumber=value;}
        public bool Dirty;
        public bool ShouldBeUpdated{get{object[] args={this,Dirty};Calls.Call(typeof(AutomaticBlock),"RouteChanged",args);return (bool)args[1];}}
        public BasicSignalController NextController{get;private set;}
        public HashSet<RailTrack> AllTracks=>Tracks.Select(t=>t.Track).Concat(ExtraTracks).ToHashSet();
        private TrackBlock(IEnumerable<TrackInfo> tracks,BasicSignalController next){Tracks=tracks.ToArray();ExtraTracks=Tracks.Where(t=>t.IsJunctionTrack).SelectMany(t=>t.Track.inJunction.GetAllTracks()).ToArray();NextController=next;Length=(float)Tracks.Sum(t=>t.Track.points.span);}
        public static TrackBlock CreateUntilMainSignal(RailTrack t,TrackDirection d)
        {
            var list=new List<TrackInfo>();var visited=new HashSet<RailTrack>();BasicSignalController next=null;
            while(visited.Add(t)){list.Add(new(t,d));if(t.Ends.TryGetValue(d,out next))break;if(!t.Links.TryGetValue(d,out var p))break;t=p.Track;d=p.Direction;}
            return new TrackBlock(list,next);
        }
        public bool IsOccupied(Signals.Common.CrossingCheckMode mode){object[] args={this,mode,false};bool fallback=(bool)Calls.Call(typeof(AutomaticBlock),"Occupied",args);return fallback?Tracks.Select(t=>t.Track).Concat(ExtraTracks).Any(t=>TrackChecker.IsOccupied(t,mode)):(bool)args[2];}
        public bool IsOccupiedIgnoreLogic(Signals.Common.CrossingCheckMode mode){object[] args={this,mode,false};bool fallback=(bool)Calls.Call(typeof(AutomaticBlock),"OccupiedIgnoreLogic",args);return fallback?Tracks.Select(t=>t.Track).Concat(ExtraTracks).Any(t=>!t.logic&&TrackChecker.IsOccupied(t,mode)):(bool)args[2];}
        public static bool TracksMatch(TrackBlock a,TrackBlock b)=>false;
    }
    public static class TrackChecker
    {
        private static HashSet<RailTrack> s_occupied=new();
        public class TrackIntersectionPoints{public bool occupied;public RailTrack other;public List<(RailTrack Track,Transform Position)> IntersectionPoints=new();public TrackIntersectionPoints(){}public TrackIntersectionPoints(RailTrack track){}public bool TestTracks()=>occupied||IntersectionPoints.Any(p=>p.Track.bogies.Count>0);public bool TestIntersections()=>TestTracks();public bool HasIntersectionWithTrack(RailTrack t)=>t==other||IntersectionPoints.Any(p=>p.Track==t);}
        public static event Action<Dictionary<RailTrack,TrackIntersectionPoints>> OnMapBuilt;
        public static void BuildMap(Dictionary<RailTrack,TrackIntersectionPoints> map)=>OnMapBuilt?.Invoke(map);
        public static bool IsOccupied(RailTrack t,Signals.Common.CrossingCheckMode m)=>t.bogies.Count>0||s_occupied.Contains(t);
        public static void Mark(RailTrack t)=>s_occupied.Add(t);public static void Clear(RailTrack t)=>s_occupied.Remove(t);
    }
    public static class TrackReserver
    {
        private static Dictionary<RailTrack,Signal> s_reservations=new();private static HashSet<Signal> s_signals=new();
        private static Dictionary<Signal,Coroutine> s_clearRoutines=new();private static Dictionary<Signal,float> s_times=new();
        public static Action<Signal> ReservationMade,ReservationCleared;
        public static bool HasReservation(Signal s)=>s_signals.Contains(s);
        public static bool IsSignalReservedByAnother(Signal s){object[] a={s,false};bool native=(bool)Calls.Call(typeof(IntervalReservations),"Conflict",a);return native?s.Block.AllTracks.Any(t=>s_reservations.TryGetValue(t,out var x)&&x.Controller!=s.Controller):(bool)a[1];}
        public static IEnumerable<int> GetSignalIdsWithReservations()=>s_signals.Select(s=>s.Id);
        public static bool ReserveForSignal(Signal s){object[] a={s,false};if(!(bool)Calls.Call(typeof(AutomaticBlock),"BeforeReserve",a))return (bool)a[1];if(!(bool)Calls.Call(typeof(IntervalReservations),"Reserve",a))return (bool)a[1];foreach(var t in s.Block.AllTracks)s_reservations[t]=s;s_signals.Add(s);return true;}
        public static bool UpdateReservation(Signal s){object[] a={s,false};return (bool)Calls.Call(typeof(IntervalReservations),"Update",a)?true:(bool)a[1];}
        public static void ClearFromSignal(Signal s){if(!(bool)Calls.Call(typeof(IntervalReservations),"Clear",s,true))return;s_signals.Remove(s);foreach(var t in s_reservations.Where(x=>x.Value==s).Select(x=>x.Key).ToArray())s_reservations.Remove(t);}
        private static void ClearFromSignalInternal(Signal s,bool b){}
        public static void ClearAll(){s_reservations.Clear();s_signals.Clear();s_times.Clear();s_clearRoutines.Clear();IntervalReservations.Reset();}
    }
}
namespace RuSignals{public static class Mod{public const string Id="ru.signals";} public sealed class RoutePanel{public ulong Mask;public void Show(ulong mask){Mask=mask;}}}

namespace Signals.Common.Displays
{
    public abstract class DisplayBaseDefinition{}
}
namespace Signals.Game.Displays
{
    public abstract class DisplayBase<T> where T:Signals.Common.Displays.DisplayBaseDefinition
    {
        protected DisplayBase(Signals.Common.Displays.DisplayBaseDefinition d,Signal s){Definition=(T)d;Signal=s;}
        public T Definition;public Signal Signal;public Controllers.BasicSignalController Controller=>Signal.Controller;public string DisplayText;
        public abstract void UpdateDisplay();
    }
}
namespace Signals.Game.Railway
{
    public static class TrackUtils
    {
        public static string NextTrackTrimmedNumber(IEnumerable<TrackInfo> ts)=>ts.Select(t=>t.Track.Number).FirstOrDefault(t=>t.Length>0)??"";
        public static string JunctionStation(Junction junction)
        {
            string id=junction.junctionData.junctionIdLong;
            var parts=id.Split('-');
            return (id.StartsWith("S")||id.StartsWith("DTS"))&&parts.Length==3?parts[2]:"";
        }
    }
}

namespace RuSignals { internal static class SignalDesignations { internal static string Station(Signals.Game.Controllers.BasicSignalController c)=>null; } internal static class SemaphoreWarnings { internal static bool Home(Signals.Game.Controllers.BasicSignalController c)=>false; } }
