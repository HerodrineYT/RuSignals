// Repeatable microbenchmarks of production adapters with the same SDK doubles
// as the regression suite. These numbers are not game frame times or FPS.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RuSignals;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using UnityEngine;

TrackSignalController Native(RailTrack t,TrackDirection d,double span)=>new(new(),t,d,new(t,AutomaticBlock.TravelDirection(d),(int)span,span));
var lines=Enumerable.Range(0,8).Select(_=>new RailTrack(8000)).ToArray();
var entrances=new List<TrackSignalController>();
foreach(var line in lines)
{
    entrances.Add(Native(line,TrackDirection.Out,20));
    var exit=Native(line,TrackDirection.Out,7980);
    Native(line,TrackDirection.In,7980);
    var reverseExit=Native(line,TrackDirection.In,20);
    line.Ends[TrackDirection.Out]=exit;line.Ends[TrackDirection.In]=reverseExit;
}
RailTrackRegistryBase.RailTracks=lines;
Calls.Call(typeof(AutomaticBlock),"Generate");
foreach(var c in SignalManager.Instance.AllControllers)c.UpdateBlocks();
var forward=SignalManager.Instance.AllControllers.Where(c=>AutomaticBlock.IsSignal(c.Signals[0]) && c.PlacementInfo.Value.Track==lines[0] && c.PlacementInfo.Value.Direction==TrackDirection.In).OrderBy(c=>c.PlacementInfo.Value.Span).ToArray();
if(forward.Length<4)throw new Exception("Benchmark topology not generated");
var occupied=(Occupied)typeof(AutomaticBlock).GetMethod("TestOccupied",BindingFlags.Static|BindingFlags.NonPublic).CreateDelegate(typeof(Occupied));
var a=forward[0].Signals[0];var b=forward[2].Signals[0];var entry=entrances[0].Signals[0];
bool result=false;long checksum=0;
var results=new List<object>();
void Measure(string name,Action action)
{
    const int iterations=100000;
    for(int i=0;i<10000;i++)action();
    var samples=new List<(double ms,long bytes)>();
    for(int repeat=0;repeat<5;repeat++)
    {
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        var timer=new Stopwatch();long before=GC.GetAllocatedBytesForCurrentThread();timer.Start();
        for(int i=0;i<iterations;i++)action();
        timer.Stop();samples.Add((timer.Elapsed.TotalMilliseconds,GC.GetAllocatedBytesForCurrentThread()-before));
    }
    var sample=samples.OrderBy(s=>s.ms).ElementAt(2);
    results.Add(new{name,iterations,median_ms=sample.ms,bytes_per_operation=(double)sample.bytes/iterations});
    Console.WriteLine($"{name}: {sample.ms:F2} ms, {(double)sample.bytes/iterations:F1} B/op");
}
Measure("clear interval occupancy",()=>{occupied(a.Block,CrossingCheckMode.Ignore,false,ref result);checksum+=result?1:0;});
Measure("disjoint interval conflicts",()=>checksum+=AutomaticBlock.Conflicts(a.Block,b.Block)?1:0);
Measure("same interval conflicts",()=>checksum+=AutomaticBlock.Conflicts(a.Block,a.Block)?1:0);
Measure("corridor direction, no reservations",()=>{Time.frameCount++;checksum+=AutomaticBlock.DirectionAllowed(a)?1:0;});
TrackReserver.ReserveForSignal(entry);
Measure("same corridor direction comparison",()=>checksum+=AutomaticBlock.SameDirection(a,entry)?1:0);
Measure("corridor direction, live reservation",()=>checksum+=AutomaticBlock.DirectionAllowed(a)?1:0);
Measure("two digit panel mask",()=>checksum+=(long)RouteSymbols.Mask("22"));
Measure("track number normalization",()=>checksum+=RouteSymbols.Number("007").Length);
TrackReserver.ClearAll();
foreach(var c in SignalManager.Instance.AllControllers)foreach(var s in c.Signals)s.CurrentAspect=new RuAspect(new RuAspectDefinition{Role=SignalRole.Through,Indication=Indication.Green},s);
var order=new[]{Indication.Stop,Indication.Invitation,Indication.DoubleYellow,Indication.DoubleYellowUpperFlashing,Indication.Yellow,Indication.FlashingYellow,Indication.Green,Indication.ShuntingWhite};
entry.AllAspects=order.Select(i=>(Signals.Game.Aspects.IAspect)new RuAspect(new RuAspectDefinition{Role=SignalRole.Entry,Indication=i,DisallowPassing=i==Indication.Stop,ManualOnly=i==Indication.Invitation},entry)).ToArray();
TrackBlock.OccupancyQueries=0;entry.Update();var scansPerPass=TrackBlock.OccupancyQueries;
if(((RuAspectDefinition)entry.CurrentAspect.GetDefinition()).Indication!=Indication.Green)throw new Exception("Invalid selection benchmark");
Measure("full native entry aspect-selection pass",()=>{entry.Update();checksum++;});
#if REWRITE
var parked=new Bogie();parked.traveller.Span=5;lines[0].bogies.Add(parked);Time.frameCount++;
if(!AutomaticBlock.DirectionAllowed(entry))throw new Exception("Stationary approach direction benchmark is blocked");
Measure("stationary approach direction proof",()=>checksum+=AutomaticBlock.DirectionAllowed(entry)?1:0);
parked.traveller.Span=40;parked.rb.velocity=new(0,0,1);Time.frameCount++;AutomaticBlock.DirectionAllowed(entry);
parked.traveller.Span=5;parked.rb.velocity=new(0,0,-1);Time.frameCount++;
if(!AutomaticBlock.DirectionAllowed(entry))throw new Exception("Reverse withdrawal direction benchmark is blocked");
Measure("reversed-out approach direction proof",()=>checksum+=AutomaticBlock.DirectionAllowed(entry)?1:0);
lines[0].bogies.Clear();Time.frameCount++;
AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();
var receptionRail=new RailTrack(500);
var corridor=new Corridor(new[]{RailSpan.Whole(receptionRail,TrackDirection.Out)});
AutomaticBlock.Network.Corridors.Add(corridor);AutomaticBlock.Network.Tracks.Add(receptionRail,(corridor,TrackDirection.Out));AutomaticBlock.Network.Ready=true;
var reception=Native(receptionRail,TrackDirection.Out,400).Signals[0];
foreach(var aspect in reception.AllAspects)((RuAspectDefinition)aspect.GetDefinition()).Role=SignalRole.Entry;
reception.Controller.UpdateBlocks();
var previousLimit=new BasicSignalController(new(),new(receptionRail,TrackDirection.Out,200,200));
receptionRail.Ends[TrackDirection.In]=previousLimit;
var previous=Native(receptionRail,TrackDirection.In,300).Signals[0];previous.Controller.UpdateBlocks();
if(!TrackReserver.ReserveForSignal(previous)||!AutomaticBlock.DirectionAllowed(reception))throw new Exception("Reception approach reservation benchmark is blocked");
Measure("entry with opposing reservation wholly before mast",()=>checksum+=AutomaticBlock.DirectionAllowed(reception)?1:0);
#endif

var glyphs=Enumerable.Range(1,99).Select(i=>i.ToString()).Concat(new[]{"\\","|","/","Left","Right","-","","00","100","007"," 7 ","-1","unknown"}).Select(s=>new{text=s,normal=RouteSymbols.Mask(s),small=RouteSymbols.Mask(s,true)}).ToArray();
var output=new{scope="net8 SDK doubles; same topology/workload for both versions; not Unity/game FPS",corridors=8,signals=AutomaticBlock.SignalCount,checksum,scansPerPass,glyphs,results};
File.WriteAllText(args[0],JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
delegate bool Occupied(TrackBlock block,CrossingCheckMode mode,bool ignoreLogic,ref bool result);
