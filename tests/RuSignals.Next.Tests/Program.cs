using RuSignals;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using System.Text.Json;
using UnityEngine;
var passed=new List<string>();
void Check(string label,bool test){if(!test)throw new Exception(label);passed.Add(label);}
// Native contract: d is travel, placement direction is the opposite face.
TrackSignalController Native(RailTrack t,TrackDirection d,double span)=>new(new(),t,d,new(t,AutomaticBlock.TravelDirection(d),(int)span,span));
void Tick(){Time.frameCount++;foreach(var c in SignalManager.Instance.AllControllers)c.UpdateBlocks();for(int pass=0;pass<3;pass++)foreach(var c in SignalManager.Instance.AllControllers)foreach(var s in c.Signals)s.Update();}
Indication Aspect(BasicSignalController c)=>((RuAspectDefinition)c.Signals[0].CurrentAspect.GetDefinition()).Indication;
void Reset(){Calls.Call(typeof(AutomaticBlock),"Reset");TrackReserver.ClearAll();SignalsMod.Settings.SpecialReservation=false;SignalManager.Instance=new();Time.frameCount++;}
ReverseMovementChecks.Run(Check);
EntryApproachReservationChecks.Run(Check);
Check("no subdivision under two minimum blocks",BlockMath.Boundaries(807).Length==0);
foreach(double length in new[]{808,849,999,1000,1001,1399,1400,1401,2204,3300,5000,10000,47000})
{
 var points=BlockMath.Boundaries(length);double last=0;foreach(double p in points){Check("400-1000 m interval "+length+" "+p,p-last>=400 && p-last<=1000);last=p;}Check("400-1000 m final interval "+length,length-last>=400 && length-last<=1000);
}
Check("bad infinite length not generated",BlockMath.Boundaries(double.PositiveInfinity).Length==0);
for(int length=808;length<10000;length++)
{
 var spans=new[]{0d}.Concat(BlockMath.Boundaries(length)).Append((double)length).ToArray();
 for(int i=1;i<spans.Length-1;i++)
 {
  double move=BlockMath.MovementAllowance(spans[i]-spans[i-1],spans[i+1]-spans[i]);
  if(spans[i]-spans[i-1]-2*move<400 || spans[i+1]-spans[i]+2*move>1000)throw new Exception("Invalid movement allowance");
 }
}
Check("crossing move envelope preserves 400-1000 m across 9192 lengths",true);
Check("bad length fails closed",BlockMath.Boundaries(double.NaN).Length==0);
Check("touching intervals don't reserve each other",!BlockMath.Overlaps(0,1000,1000,2000));
Check("reversed intervals overlap",BlockMath.Overlaps(2000,1000,1500,500));
Check("unknown number dark",RouteSymbols.Mask("unknown")==0);
Check("zero invalid track number",RouteSymbols.Mask("00")==0);
Check("trim native leading zeros",RouteSymbols.Number("007")=="7");
for(int i=1;i<=99;i++)Check("track number "+i,RouteSymbols.Mask(i.ToString())>0 && RouteSymbols.Mask(i.ToString())<(1UL<<42));
Check("RUS tens column retained",(RouteSymbols.Mask("10")&RouteSymbols.Mask("11"))!=0);
Check("directions are distinct",RouteSymbols.Mask("Left")!=RouteSymbols.Mask("Right"));
Check("out of range dark",RouteSymbols.Mask("100")==0);
Reset();
var line=new RailTrack(8000);RailTrackRegistryBase.RailTracks=new[]{line};
var entrance=Native(line,TrackDirection.Out,20);var exit=Native(line,TrackDirection.Out,7980);
var revEntrance=Native(line,TrackDirection.In,7980);var revExit=Native(line,TrackDirection.In,20);
line.Ends[TrackDirection.Out]=exit;line.Ends[TrackDirection.In]=revExit;
var oldWarning=new DistantSignalController(new(),new(line,TrackDirection.Out,6980,6980)){Home=exit};
var repeater=new DistantSignalController(new(),new(line,TrackDirection.Out,7880,7880)){Home=exit,IsRepeater=true};
SignalManager.Instance.AddDistant(oldWarning);SignalManager.Instance.AddDistant(repeater);
Calls.Call(typeof(AutomaticBlock),"Generate");Tick();
Check("AB pre-entry replaces standalone warning",!oldWarning.Exists);
Check("visibility repeater preserved",repeater.Exists);
Check("single long physical track is subdivided both ways",AutomaticBlock.SignalCount>=8);
var forward=SignalManager.Instance.AllControllers.Where(c=>AutomaticBlock.IsSignal(c.Signals[0])&&c.PlacementInfo.Value.Direction==TrackDirection.In).OrderBy(c=>c.PlacementInfo.Value.Span).ToArray();
Check("forward AB face points toward approaching driver",forward.All(c=>c.Definition.Facing.z<0));
Check("forward route uses increasing spans despite inward face",forward.All(c=>c.Signals[0].Block.Tracks[0].Direction==TrackDirection.Out));
Check("reverse AB faces opposite direction",SignalManager.Instance.AllControllers.Where(c=>AutomaticBlock.IsSignal(c.Signals[0])&&c.PlacementInfo.Value.Direction==TrackDirection.Out).All(c=>c.Definition.Facing.z>0));
Check("entry native block ends at first new signal",entrance.Signals[0].Block.NextController==forward[0]);
Check("clear departure opens without request",Aspect(entrance)==Indication.Green);
Check("clear reverse departure needs no direction request",Aspect(revEntrance)==Indication.Green);
Check("automatic indications do not create native reservations",!TrackReserver.GetSignalIdsWithReservations().Any());
SignalsMod.Settings.SpecialReservation=true;Tick();
Check("native opt-in reservation still closes departure",Aspect(entrance)==Indication.Stop && Aspect(revEntrance)==Indication.Stop);
Check("generated AB stays automatic with native reservation option",Aspect(forward[0])==Indication.Green);
Check("reserve entry through native API",TrackReserver.ReserveForSignal(entrance.Signals[0]));Tick();
Check("reservation reflected in native set",TrackReserver.HasReservation(entrance.Signals[0]));
Check("native opt-in reserved departure opens",Aspect(entrance)==Indication.Green);
SignalsMod.Settings.SpecialReservation=false;Tick();
Check("manual reserve closes opposing departure even with free first block",Aspect(revEntrance)==Indication.Stop);
Check("manual reserve closes opposing AB beyond its reserved interval",SignalManager.Instance.AllControllers.Where(c=>AutomaticBlock.IsSignal(c.Signals[0])&&c.PlacementInfo.Value.Direction==TrackDirection.Out).All(c=>Aspect(c)==Indication.Stop));
Check("following automatic signal exempt from entry reservation",!TrackReserver.IsSignalReservedByAnother(forward[0].Signals[0]));
var timerMap=(System.Collections.IDictionary)HarmonyLib.AccessTools.Field(typeof(TrackReserver),"s_times").GetValue(null);timerMap[entrance.Signals[0]]=37f;
int made=0,cleared=0;TrackReserver.ReservationMade+=s=>made++;TrackReserver.ReservationCleared+=s=>cleared++;
Check("route reservation rebuild succeeds",TrackReserver.UpdateReservation(entrance.Signals[0]));
Check("rebuild preserves native reservation events",made==1 && cleared==1);
Check("rebuild retains original expiry",Equals(timerMap[entrance.Signals[0]],37f));
var another=Native(line,TrackDirection.Out,forward[2].PlacementInfo.Value.Span+10);another.UpdateBlocks();
Check("disjoint reservations on one physical rail",TrackReserver.ReserveForSignal(another.Signals[0]) && TrackReserver.HasReservation(entrance.Signals[0]));
TrackReserver.ClearFromSignal(entrance.Signals[0]);
Check("clearing one interval retains the other",TrackReserver.HasReservation(another.Signals[0]));
Check("freed interval can reserve again",TrackReserver.ReserveForSignal(entrance.Signals[0]));
TrackReserver.ClearFromSignal(another.Signals[0]);another.Destroy();
entrance.OffsetOnTrack(3);Tick();
Check("crossing position correction updates exact span",entrance.PlacementInfo.Value.Span==23);
Check("movement rebuilds adjacent boundary",Math.Abs(entrance.Signals[0].Block.Length-(forward[0].PlacementInfo.Value.Span-23))<.01);
for(int i=0;i<forward.Length-1;i++)
{
 Check("native next link "+i,forward[i].Signals[0].Block.NextController==forward[i+1]);
 Check("native length is interval "+i,Math.Abs(forward[i].Signals[0].Block.Length-(forward[i+1].PlacementInfo.Value.Span-forward[i].PlacementInfo.Value.Span))<.01);
}
Check("last native block links to entry",forward.Last().Signals[0].Block.NextController==exit);
var b=new Bogie();b.traveller.Span=(forward[2].PlacementInfo.Value.Span+forward[3].PlacementInfo.Value.Span)/2;b.rb.velocity=new(0,0,1);line.bogies.Add(b);Tick();
Check("own window occupied",forward[2].Signals[0].Block.IsOccupied(CrossingCheckMode.IntersectionOnly));
Check("other window same physical track free",!forward[0].Signals[0].Block.IsOccupied(CrossingCheckMode.IntersectionOnly));
Check("occupied block red",Aspect(forward[2])==Indication.Stop);
Check("preceding block yellow",Aspect(forward[1])==Indication.Yellow);
Check("two free blocks green",Aspect(forward[0])==Indication.Green);
forward[2].Signals[0].CurrentAspect=new RuAspect(new RuAspectDefinition{Role=SignalRole.Through,Indication=Indication.Green},forward[2].Signals[0]);
forward[1].Signals[0].Update();
Check("stale next green cannot hide occupied next block",Aspect(forward[1])==Indication.Yellow);Tick();
Check("opposing direction closed",SignalManager.Instance.AllControllers.Where(c=>AutomaticBlock.IsSignal(c.Signals[0])&&c.PlacementInfo.Value.Direction==TrackDirection.Out).All(c=>Aspect(c)==Indication.Stop));
Check("cannot reverse occupied corridor",!TrackReserver.ReserveForSignal(revEntrance.Signals[0]));
Check("window public query inside",AutomaticBlock.TryContains(forward[2].Signals[0].Block,line,b.traveller.Span,1,out bool contains)&&contains);
Check("window public query outside",AutomaticBlock.TryContains(forward[0].Signals[0].Block,line,b.traveller.Span,1,out contains)&&!contains);
Check("window public query wrong direction",AutomaticBlock.TryContains(forward[2].Signals[0].Block,line,b.traveller.Span,-1,out contains)&&!contains);
var follower=new Bogie();follower.traveller.Span=(forward[0].PlacementInfo.Value.Span+forward[1].PlacementInfo.Value.Span)/2;follower.rb.velocity=new(0,0,1);line.bogies.Add(follower);Tick();
Check("following train occupies an independent interval",Aspect(forward[0])==Indication.Stop && Aspect(forward[1])==Indication.Yellow && Aspect(forward[2])==Indication.Stop);
line.bogies.Remove(follower);Tick();
var crossing=new TrackChecker.TrackIntersectionPoints{occupied=true};Calls.Call(typeof(AutomaticBlock),"MapBuilt",new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>{{line,crossing}});
Check("crossing occupancy is preserved",forward[0].Signals[0].Block.IsOccupied(CrossingCheckMode.IntersectionOnly));
Check("crossings can be ignored by native query",!forward[0].Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
Calls.Call(typeof(AutomaticBlock),"MapBuilt",new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>());
b.traveller.Span=forward[3].PlacementInfo.Value.Span;Tick();
Check("rear bogie boundary holds previous block",forward[2].Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
Check("front bogie boundary occupies next",forward[3].Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
b.traveller.Span+=6;Tick();Check("tail clears block independently",!forward[2].Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
b.rb.velocity=new(0,0,-1);Tick();
Check("reversing keeps its occupied block closed",Aspect(forward[3])==Indication.Stop);
Check("reversing cannot override the opposing direction reservation",SignalManager.Instance.AllControllers.Where(c=>AutomaticBlock.IsSignal(c.Signals[0])&&c.PlacementInfo.Value.Direction==TrackDirection.Out).All(c=>Aspect(c)==Indication.Stop));
Check("clear route beyond a reversing train stays available",SignalRules.IsTrainPermission(Aspect(forward.Last())));
b.rb.velocity=new(0,0,1);Tick();
Check("forward movement clears a past reversal while occupied block stays red",Aspect(forward[0])==Indication.Green&&Aspect(forward[3])==Indication.Stop&&SignalRules.IsTrainPermission(Aspect(forward.Last())));
TrackChecker.Mark(line);Tick();Check("manual whole-track closure preserved",forward.All(c=>c.Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore)));TrackChecker.Clear(line);
line.bogies.Clear();TrackReserver.ClearFromSignal(entrance.Signals[0]);Tick();
Check("reservation cleared from native API",!TrackReserver.HasReservation(entrance.Signals[0]));
Check("clearing corridor and reservation releases both approaches automatically",Aspect(entrance)==Indication.Green && Aspect(revEntrance)==Indication.Green);
Check("empty corridor can reverse via reservation",TrackReserver.ReserveForSignal(revEntrance.Signals[0]));Tick();
Check("old direction closes after reversal",forward.All(c=>Aspect(c)==Indication.Stop));
Check("entry in conflict cannot reserve",!TrackReserver.ReserveForSignal(entrance.Signals[0]));
Check("automatic through cannot be reserved",!TrackReserver.ReserveForSignal(forward[0].Signals[0]));
TrackReserver.ClearFromSignal(revEntrance.Signals[0]);Tick();
Check("expired reverse request leaves no direction lock",Aspect(entrance)==Indication.Green && Aspect(revEntrance)==Indication.Green);
// No phone/radio requests: establish, hold and release direction from occupation.
b.traveller.Span=(forward[2].PlacementInfo.Value.Span+forward[3].PlacementInfo.Value.Span)/2;
b.rb.velocity=new(0,0,-1);line.bogies.Add(b);Tick();
Check("reverse train establishes direction automatically",Aspect(entrance)==Indication.Stop && SignalRules.IsTrainPermission(Aspect(revEntrance)));
b.rb.velocity=new(0,0,0);Tick();
Check("stopped train retains automatic direction",Aspect(entrance)==Indication.Stop && SignalRules.IsTrainPermission(Aspect(revEntrance)));
line.bogies.Clear();Tick();
Check("last bogie leaving restores empty corridor automatically",Aspect(entrance)==Indication.Green && Aspect(revEntrance)==Indication.Green);
Check("automatic direction does not create expiring reservations",!TrackReserver.GetSignalIdsWithReservations().Any());
var firstBogie=new Bogie();firstBogie.traveller.Span=50;firstBogie.rb.velocity=new(0,0,1);line.bogies.Add(firstBogie);Tick();
Check("unreserved departure still red if first interval occupied",Aspect(entrance)==Indication.Stop);
line.bogies.Clear();Tick();
SignalsMod.Settings.SpecialReservation=true;Tick();SignalsMod.Settings.SpecialReservation=false;Tick();
Check("disabling native reservation option restores automatic operation",Aspect(entrance)==Indication.Green && Aspect(revEntrance)==Indication.Green);
Check("direction validation alone succeeds on clear corridor",AutomaticBlock.TryReserveDirection(revEntrance.Signals[0]));
Check("validation without successful native reservation cannot lock direction",AutomaticBlock.DirectionAllowed(entrance.Signals[0]));
Check("optional reverse request succeeds",TrackReserver.ReserveForSignal(revEntrance.Signals[0]));
Check("reservation locks opposite direction in the same frame",!AutomaticBlock.DirectionAllowed(entrance.Signals[0]));
TrackReserver.ClearFromSignal(revEntrance.Signals[0]);
Check("native cancellation releases direction in the same frame",AutomaticBlock.DirectionAllowed(entrance.Signals[0]));
Check("different intervals have different native identity",!AutomaticBlock.Conflicts(forward[0].Signals[0].Block,forward[2].Signals[0].Block));
object[] matchArgs={forward[0].Signals[0].Block,forward[1].Signals[0].Block,true};Calls.Call(typeof(AutomaticBlock),"TracksMatch",matchArgs);Check("TracksMatch distinguishes intervals on one track",!(bool)matchArgs[2]);
// No frame tick: moving bogies, manual closures and native reservation changes
// must remain visible even when immutable geometry/drawings are cached.
var checkedBlock=forward[0].Signals[0].Block;
b.traveller.Span=(forward[0].PlacementInfo.Value.Span+forward[1].PlacementInfo.Value.Span)/2;line.bogies.Add(b);
Check("occupancy changes are visible within the same frame",checkedBlock.IsOccupied(CrossingCheckMode.Ignore));
line.bogies.Clear();Check("cleared occupancy is visible within the same frame",!checkedBlock.IsOccupied(CrossingCheckMode.Ignore));
TrackChecker.Mark(line);Check("manual closure is visible within the same frame",checkedBlock.IsOccupied(CrossingCheckMode.Ignore));TrackChecker.Clear(line);
var extraRail=new RailTrack(250);
var fullOther=TrackBlock.CreateUntilMainSignal(extraRail,TrackDirection.Out);
var fullLine=TrackBlock.CreateUntilMainSignal(line,TrackDirection.In);
Check("partial and whole physical track conflict in both argument orders",AutomaticBlock.Conflicts(checkedBlock,fullLine)&&AutomaticBlock.Conflicts(fullLine,checkedBlock));
Check("unrelated native and partial routes do not conflict",!AutomaticBlock.Conflicts(checkedBlock,fullOther)&&!AutomaticBlock.Conflicts(fullOther,checkedBlock));
HarmonyLib.AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.ExtraTracks)).Invoke(checkedBlock,new object[]{new[]{line}});
Check("primary track repeated in extra list does not enlarge reserved interval",!AutomaticBlock.Conflicts(checkedBlock,forward[2].Signals[0].Block));
HarmonyLib.AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.ExtraTracks)).Invoke(checkedBlock,new object[]{new[]{line,extraRail}});
Check("extra junction track conflicts in both argument orders",AutomaticBlock.Conflicts(checkedBlock,fullOther)&&AutomaticBlock.Conflicts(fullOther,checkedBlock));
TrackChecker.Mark(extraRail);Check("extra junction track occupancy is protected",checkedBlock.IsOccupied(CrossingCheckMode.Ignore));
extraRail.logic=true;Check("native ignore-logic option applies to extra junction tracks",!checkedBlock.IsOccupiedIgnoreLogic(CrossingCheckMode.Ignore));
extraRail.logic=false;TrackChecker.Clear(extraRail);
HarmonyLib.AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.ExtraTracks)).Invoke(checkedBlock,new object[]{Array.Empty<RailTrack>()});
Calls.Call(typeof(AutomaticBlock),"MapBuilt",new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>{{line,new(){other=extraRail}}});
Check("crossing conflict still protects a disjoint physical route",AutomaticBlock.Conflicts(checkedBlock,fullOther));
Calls.Call(typeof(AutomaticBlock),"MapBuilt",new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>());
var nativeSetField=HarmonyLib.AccessTools.Field(typeof(TrackReserver),"s_signals");var nativeSet=nativeSetField.GetValue(null);
nativeSetField.SetValue(null,new HashSet<Signal>{revEntrance.Signals[0]});
Check("replacement native reservation collection is read immediately",!AutomaticBlock.DirectionAllowed(entrance.Signals[0]));
nativeSetField.SetValue(null,nativeSet);
Check("restored native reservation collection is read immediately",AutomaticBlock.DirectionAllowed(entrance.Signals[0]));
for(int i=0;i<forward.Length;i++)forward[i].OffsetOnTrack(i%2==0?500:-500);
Tick();
Check("native crossing moves cannot bunch generated signals",forward.Zip(forward.Skip(1),(p,q)=>q.PlacementInfo.Value.Span-p.PlacementInfo.Value.Span).All(gap=>gap>=400&&gap<=1000));
// Two connected pieces with reversed native coordinate direction.
Reset();var a=new RailTrack(1800);var z=new RailTrack(2000);a.Links[TrackDirection.Out]=new(z,TrackDirection.In);z.Links[TrackDirection.Out]=new(a,TrackDirection.In);
var home=Native(z,TrackDirection.In,20);var back=Native(a,TrackDirection.In,20);z.Ends[TrackDirection.In]=home;a.Ends[TrackDirection.In]=back;
RailTrackRegistryBase.RailTracks=new[]{a,z};Calls.Call(typeof(AutomaticBlock),"Generate");Tick();
Check("runs span physical track joins",AutomaticBlock.SignalCount>=4);
var cross=SignalManager.Instance.AllControllers.First(c=>AutomaticBlock.IsSignal(c.Signals[0])&&c.PlacementInfo.Value.Track==a&&c.PlacementInfo.Value.Direction==TrackDirection.In&&c.Signals[0].Block.Tracks.Any(t=>t.Track==z));
Check("cross-track block preserves reversed direction",cross.Signals[0].Block.Tracks.Any(t=>t.Track==z&&t.Direction==TrackDirection.In));
Check("cross-track block excludes remainder after next mast",cross.Signals[0].Block.Length<1800);
var train=new Bogie();train.traveller.Span=1950;train.rb.velocity=new(0,0,-1);z.bogies.Add(train);Tick();
Check("occupation across reversed track join",cross.Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
// Short permissible interval with native end masts: include their crossing moves.
Reset();var shortLine=new RailTrack(848);RailTrackRegistryBase.RailTracks=new[]{shortLine};
var shortStart=Native(shortLine,TrackDirection.Out,20);var shortEnd=Native(shortLine,TrackDirection.Out,828);
var shortBack=Native(shortLine,TrackDirection.In,20);shortLine.Ends[TrackDirection.Out]=shortEnd;shortLine.Ends[TrackDirection.In]=shortBack;
Calls.Call(typeof(AutomaticBlock),"Generate");Tick();
var shortMiddle=shortStart.Signals[0].Block.NextController;
shortStart.OffsetOnTrack(30);shortEnd.OffsetOnTrack(-30);shortMiddle.OffsetOnTrack(30);Tick();
Check("400 m spacing protects original boundary mast as well",shortMiddle.PlacementInfo.Value.Span-shortStart.PlacementInfo.Value.Span>=400 && shortEnd.PlacementInfo.Value.Span-shortMiddle.PlacementInfo.Value.Span>=400);
// Exclusions and reset.
Reset();line=new RailTrack(9000){logic=true};RailTrackRegistryBase.RailTracks=new[]{line};Calls.Call(typeof(AutomaticBlock),"Generate");Check("station logic tracks excluded",AutomaticBlock.SignalCount==0);
Reset();line=new RailTrack(9000);RailTrackRegistryBase.RailTracks=new[]{line};Calls.Call(typeof(AutomaticBlock),"Generate");Check("dead-end without main boundary excluded",AutomaticBlock.SignalCount==0);

// Actual production RouteDisplay gating and native number lookup.
Reset();var yard=new RailTrack(500){Number="09"};var route=Native(yard,TrackDirection.Out,20);route.UpdateBlocks();
var displayDef=new RouteDisplayDefinition{Role=SignalRole.Entry,Visual=new GameObject(),Panel=new RoutePanel()};
var display=new RouteDisplay(displayDef,route.Signals[0]);
AutomaticBlock.FullRoute(route.Signals[0].Block).TrackTrimmerNumber="007";
route.Signals[0].CurrentAspect=new RuAspect(new RuAspectDefinition{Indication=Indication.Yellow},route.Signals[0]);display.UpdateDisplay();
Check("entry indicates selected block track",display.DisplayText=="7" && displayDef.Panel.Mask!=0);
displayDef.Role=SignalRole.Exit;displayDef.Small=true;display.UpdateDisplay();
Check("dwarf exits never have number housing",!displayDef.Visual.activeSelf && display.DisplayText=="");
displayDef.Small=false;display.UpdateDisplay();Check("plain exit without junction has no housing",!displayDef.Visual.activeSelf);
route.GroupJunction=new Junction();display.UpdateDisplay();Check("mainline junction exit indicates its departure track",display.DisplayText=="9" && displayDef.Visual.activeSelf);
displayDef.Role=SignalRole.Shunting;display.UpdateDisplay();Check("shunting role at junction never has housing",!displayDef.Visual.activeSelf);
displayDef.Role=SignalRole.Entry;route.Signals[0].IsShunting=true;display.UpdateDisplay();Check("native shunting flag excludes board even if entry role",!displayDef.Visual.activeSelf);route.Signals[0].IsShunting=false;
AutomaticBlock.FullRoute(route.Signals[0].Block).TrackTrimmerNumber="";display.UpdateDisplay();Check("unknown number is dark without invented direction",display.DisplayText=="");AutomaticBlock.FullRoute(route.Signals[0].Block).TrackTrimmerNumber="7";
var yardBogie=new Bogie();yardBogie.traveller.Span=50;yard.bogies.Add(yardBogie);display.UpdateDisplay();Check("stale open aspect cannot light board on occupied route",display.DisplayText=="");yard.bogies.Clear();
foreach(var indication in new[]{Indication.Stop,Indication.Invitation,Indication.ShuntingWhite,Indication.Dark})
{
 route.Signals[0].CurrentAspect=new RuAspect(new RuAspectDefinition{Indication=indication},route.Signals[0]);display.UpdateDisplay();
 Check("route board dark at "+indication,display.DisplayText=="" && displayDef.Panel.Mask==0);
}
route.Signals[0].CurrentAspect=new RuAspect(new RuAspectDefinition{Indication=Indication.Yellow},route.Signals[0]);route.Signals[0].Block.Dirty=true;display.UpdateDisplay();
Check("route board dark during route rebuild",display.DisplayText=="");route.Signals[0].Block.Dirty=false;display.UpdateDisplay();route.Signals[0].TurnOff();
Check("route board extinguishes on native TurnOff event",displayDef.Panel.Mask==0);
Check("normal panel clamp aligned with mast axis",Math.Abs(RouteBoardRules.MountZ+RouteBoardRules.OffsetZ(1))<1e-6);
Check("compact panel clamp aligned with mast axis",Math.Abs(RouteBoardRules.MountZ*RouteBoardRules.CompactScale+RouteBoardRules.OffsetZ(RouteBoardRules.CompactScale))<1e-6);
Check("route panel less emissive than main lamps",RouteBoardRules.Emission<1 && RouteBoardRules.Emission<RouteBoardRules.MainLampEmission);
foreach(string number in new[]{"2","3","7","10","23","99"})
{
 // Unity camera on +Z faces -Z: its screen-right is model -X.
 var lamps=RouteLamps.route_pointer.Select((v,i)=>(X:RouteBoardRules.LampX(v.x),Y:v.y,Lit:(RouteSymbols.Mask(number)&(1UL<<i))!=0)).ToArray();
 var rows=lamps.GroupBy(v=>v.Y).OrderByDescending(g=>g.Key).Select(g=>string.Concat(g.OrderByDescending(v=>v.X).Select(v=>v.Lit?'#':'.'))).ToArray();
 if(number=="7")Check("7 diagonal falls screen-left with Unity camera",rows[0].Count(c=>c=='#')>=3 && rows[6].IndexOf('#')<rows[1].LastIndexOf('#'));
 if(number=="3")Check("3 has screen-right lower curve rather than mirrored E",rows[5].LastIndexOf('#')>rows[5].IndexOf('#') || rows[5].IndexOf('#')>=3);
 if(number=="10")Check("10 tens column appears screen-left",rows.All(row=>row[0]=='#'));
}
// AB ends before the numbered station track. Preserve the route number without
// treating the whole original route as one occupied/reserved interval.
Reset();var approachLine=new RailTrack(4000);var numberedYard=new RailTrack(300){logic=true,Number="007"};
approachLine.Links[TrackDirection.Out]=new(numberedYard,TrackDirection.Out);
var numberedHome=Native(numberedYard,TrackDirection.Out,280);numberedYard.Ends[TrackDirection.Out]=numberedHome;
var approachSignal=Native(approachLine,TrackDirection.Out,20);
approachLine.Ends[TrackDirection.In]=Native(approachLine,TrackDirection.In,20);
RailTrackRegistryBase.RailTracks=new[]{approachLine,numberedYard};Calls.Call(typeof(AutomaticBlock),"Generate");Tick();
var originalRoute=TrackBlock.CreateUntilMainSignal(approachLine,TrackDirection.Out);
approachSignal.Signals[0].SetBlock(originalRoute);Tick();
var numberedDef=new RouteDisplayDefinition{Role=SignalRole.Entry,Visual=new GameObject(),Panel=new RoutePanel()};
var numberedDisplay=new RouteDisplay(numberedDef,approachSignal.Signals[0]);numberedDisplay.UpdateDisplay();
Check("numbered station is outside the short AB occupancy block",!approachSignal.Signals[0].Block.AllTracks.Contains(numberedYard));
Check("short AB block has no local track number",approachSignal.Signals[0].Block.TrackTrimmerNumber=="");
Check("route number survives AB interval truncation",numberedDisplay.DisplayText=="7" && numberedDef.Panel.Mask!=0);
numberedYard.bogies.Add(new Bogie());Tick();
Check("preserving number does not extend first AB occupancy interval",!approachSignal.Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
numberedYard.bogies.Clear();originalRoute.Dirty=true;numberedDisplay.UpdateDisplay();
Check("route changed beyond AB boundary invalidates cached number",approachSignal.Signals[0].Block.ShouldBeUpdated && numberedDisplay.DisplayText=="");
numberedYard.Number="12";Tick();numberedDisplay.UpdateDisplay();
Check("rebuilt route displays new destination number",numberedDisplay.DisplayText=="12");
// World-junction direction uses the complete route, not only the first AB
// interval or a distant station number. Route rebuilds still fail dark.
var worldJunction=new Junction{junctionData=new(){junctionIdLong="W-17"}};
worldJunction.outBranches.Add(new Branch{track=numberedYard});
worldJunction.outBranches.Add(new Branch{track=new RailTrack(30)});
approachSignal.GroupJunction=worldJunction;
numberedYard.points.points[^1].forward=new(.2f,0,1);
numberedDisplay.UpdateDisplay();
Check("world direction survives AB interval truncation",numberedDisplay.DisplayText=="/");
Check("world junction direction takes priority over distant station number",numberedDisplay.DisplayText!="12");
worldJunction.selectedBranch=1;numberedDisplay.UpdateDisplay();
Check("stale branch beyond AB boundary extinguishes direction",numberedDisplay.DisplayText=="");
worldJunction.selectedBranch=0;approachSignal.Signals[0].Block.Dirty=true;numberedDisplay.UpdateDisplay();
Check("direction dark during AB route rebuild",numberedDisplay.DisplayText=="");

// Three legs, a non-zero straight index, same-aspect switch, and trailing travel.
Reset();var common=new RailTrack(100);
var legs=new[]{new RailTrack(30),new RailTrack(30),new RailTrack(30){name="[track through]"}};
var junction=new Junction{junctionData=new(){junctionIdLong="W-24"}};
foreach(var leg in legs){leg.isJunctionTrack=true;leg.inJunction=junction;junction.outBranches.Add(new Branch{track=leg});}
legs[0].points.points[^1].forward=new(-.2f,0,1);legs[1].points.points[^1].forward=new(.2f,0,1);
var pointerController=Native(common,TrackDirection.Out,20);pointerController.GroupJunction=junction;
var pointerDef=new RouteDisplayDefinition{Role=SignalRole.Route,Visual=new GameObject(),Panel=new RoutePanel()};
var pointer=new RouteDisplay(pointerDef,pointerController.Signals[0]);
void SetLeg(int index,TrackDirection travel=TrackDirection.Out)
{
 junction.selectedBranch=index;common.Links[TrackDirection.Out]=new(legs[index],travel);
 pointerController.Signals[0].SetBlock(TrackBlock.CreateUntilMainSignal(common,TrackDirection.Out));
 pointerController.Signals[0].CurrentAspect=new RuAspect(new RuAspectDefinition{Role=SignalRole.Route,Indication=index==2?Indication.Yellow:Indication.DoubleYellow},pointerController.Signals[0]);
 pointer.UpdateDisplay();
}
SetLeg(0);Check("world left route displays backslash",pointer.DisplayText=="\\");
var unchangedAspect=pointerController.Signals[0].CurrentAspect;
junction.selectedBranch=1;common.Links[TrackDirection.Out]=new(legs[1],TrackDirection.Out);
pointerController.Signals[0].SetBlock(TrackBlock.CreateUntilMainSignal(common,TrackDirection.Out));pointer.UpdateDisplay();
Check("same open aspect updates world direction after switching",pointer.DisplayText=="/" && pointerController.Signals[0].CurrentAspect==unchangedAspect);
SetLeg(2);Check("through route displays vertical regardless of branch index",pointer.DisplayText=="|");
SetLeg(1,TrackDirection.In);Check("trailing route reverses direction for driver",pointer.DisplayText=="\\");
SetLeg(1);junction.junctionData.junctionIdLong="S-3-HB";legs[1].Number="007";pointer.UpdateDisplay();
Check("station junction keeps numeric track display",pointer.DisplayText=="7");
legs[1].Number="";pointer.UpdateDisplay();Check("station with unknown number stays dark",pointer.DisplayText=="");
junction.junctionData.junctionIdLong="DTS-3-HB";legs[1].Number="12";pointer.UpdateDisplay();
Check("double track station retains numeric board",pointer.DisplayText=="12");
junction.junctionData.junctionIdLong="W-24";legs[1].Number="";pointer.UpdateDisplay();
legs[1].bogies.Add(new Bogie());pointer.UpdateDisplay();Check("direction dark when its route is occupied",pointer.DisplayText=="");legs[1].bogies.Clear();
foreach(var indication in new[]{Indication.Stop,Indication.Invitation,Indication.ShuntingWhite,Indication.Dark})
{
 pointerController.Signals[0].CurrentAspect=new RuAspect(new RuAspectDefinition{Role=SignalRole.Route,Indication=indication},pointerController.Signals[0]);pointer.UpdateDisplay();
 Check("world direction dark at "+indication,pointer.DisplayText=="");
}
SetLeg(1);pointerDef.Small=true;pointer.UpdateDisplay();Check("dwarf has no directional board",!pointerDef.Visual.activeSelf && pointer.DisplayText=="");pointerDef.Small=false;
pointerController.Signals[0].IsShunting=true;pointer.UpdateDisplay();Check("shunting has no directional board",!pointerDef.Visual.activeSelf && pointer.DisplayText=="");pointerController.Signals[0].IsShunting=false;
SetLeg(1);junction.selectedBranch=9;pointer.UpdateDisplay();Check("invalid selected branch stays dark",pointer.DisplayText=="");
SetLeg(1);junction.outBranches[1].track=common;pointer.UpdateDisplay();Check("unrelated selected branch cannot light direction",pointer.DisplayText=="");junction.outBranches[1].track=legs[1];
SetLeg(1);legs[2].points.points[^1].forward=new(.5f,0,1);pointer.UpdateDisplay();
Check("left branch of a curved mainline is left even if both branches bend right",pointer.DisplayText=="\\");
SetLeg(1,TrackDirection.In);Check("curved mainline branch is relative to driver on reverse approach",pointer.DisplayText=="/");
legs[2].points.points[^1].forward=new(0,0,1);SetLeg(1);
Check("slope does not change turnout direction",RouteDirection.FromTangents(new(0,3,1),new(.2f,-4,1))=="/");
Check("map rotation does not change turnout direction",RouteDirection.FromTangents(new(1,0,0),new(1,0,-.2f))=="/");
Check("invalid tangent does not invent direction",RouteDirection.FromTangents(new(0,1,0),new(1,0,0))=="" && RouteDirection.FromTangents(new(float.NaN,0,1),new(1,0,0))=="");
Check("near straight geometry displays vertical",RouteDirection.FromTangents(new(0,0,1),new(.005f,0,1))=="|");
foreach(string symbol in new[]{"\\","|","/"})
{
 var mask=RouteSymbols.Mask(symbol);
 var rows=Enumerable.Range(0,7).Select(row=>Enumerable.Range(0,6).Where(col=>(mask&(1UL<<(row*6+col)))!=0).ToArray()).ToArray();
 Check("direction has visible lamps "+symbol,mask!=0 && mask<(1UL<<42));
 if(symbol=="\\")Check("backslash slopes screen-right downward",rows[0][0]<rows[5][0]);
 if(symbol=="/")Check("slash slopes screen-left downward",rows[0][0]>rows[5][0]);
 if(symbol=="|")Check("vertical bar uses one fixed column on every row",rows.All(r=>r.Length==1&&r[0]==2));
}
RouteBoundaryChecks.Run(Check);
DecisionPassChecks.Run(Check);
Console.WriteLine("PASS: "+passed.Count+" automatic-block integration and route-indicator checks (SDK doubles).");
if(args.Length>0){Directory.CreateDirectory(Path.GetDirectoryName(args[0])!);File.WriteAllText(args[0],JsonSerializer.Serialize(new{passed=passed.Count,scenarios=passed,game_runtime="not tested; production adapters exercised with SDK doubles"},new JsonSerializerOptions{WriteIndented=true}));}
