using RuSignals;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using System.Text.Json;

var passed = new List<string>();
void Equal<T>(string name, T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{name}: expected {expected}, got {actual}");
    passed.Add(name);
}
SignalState Free(NextPermission next = NextPermission.Normal) => new()
    { BlockReady=true, ShuntingAllowed=true, Next=next };
foreach (var role in new[] { SignalRole.Through, SignalRole.Entry, SignalRole.Exit, SignalRole.Route })
{
    Equal(role+" clear",Indication.Green,SignalRules.Select(role,Free()));
    Equal(role+" next closed",Indication.Yellow,SignalRules.Select(role,Free(NextPermission.Stop)));
    var occupied=Free();occupied.Occupied=true;
    Equal(role+" occupied",Indication.Stop,SignalRules.Select(role,occupied));
    foreach (int mask in Enumerable.Range(1,15))
    {
        var s=Free();s.Diverging=true;
        s.BlockReady=(mask&1)==0;s.RouteBlocked=(mask&2)!=0;
        s.ReservedByOther=(mask&4)!=0;s.ReservationRequired=(mask&8)!=0;
        Equal(role+" protection "+mask,Indication.Stop,SignalRules.Select(role,s));
    }
}
foreach (var role in new[] { SignalRole.Entry, SignalRole.Exit, SignalRole.Route })
{
    var s=Free(NextPermission.Stop);s.Diverging=true;
    Equal(role+" diverging to stop",Indication.DoubleYellow,SignalRules.Select(role,s));
    s.Next=NextPermission.Normal;
    Equal(role+" diverging to open",Indication.DoubleYellowUpperFlashing,SignalRules.Select(role,s));
    s.Next=NextPermission.Reduced;
    Equal(role+" successive diverging",Indication.DoubleYellowUpperFlashing,SignalRules.Select(role,s));
}
Equal("entry anticipates restriction",Indication.FlashingYellow,SignalRules.Select(SignalRole.Entry,Free(NextPermission.Reduced)));
foreach(bool diverging in new[]{false,true})foreach(var permission in Enum.GetValues<NextPermission>())
{
    var terminal=Free(permission);terminal.TerminalArrival=true;terminal.Diverging=diverging;
    Equal("terminal entry restricts "+diverging+" / "+permission,Indication.DoubleYellow,SignalRules.Select(SignalRole.Entry,terminal));
    foreach(int mask in Enumerable.Range(1,31))
    {
        var denied=terminal;denied.BlockReady=(mask&1)==0;denied.RouteBlocked=(mask&2)!=0;
        denied.ReservedByOther=(mask&4)!=0;denied.ReservationRequired=(mask&8)!=0;denied.Occupied=(mask&16)!=0;
        Equal("terminal entry protection "+diverging+" / "+permission+" / "+mask,Indication.Stop,SignalRules.Select(SignalRole.Entry,denied));
    }
}
Equal("route anticipates restriction",Indication.FlashingYellow,SignalRules.Select(SignalRole.Route,Free(NextPermission.Reduced)));
Equal("exit warns of reduced next",Indication.FlashingYellow,SignalRules.Select(SignalRole.Exit,Free(NextPermission.Reduced)));
var preEntry=Free(NextPermission.Reduced);preEntry.NextIsEntry=true;
Equal("pre-entry yellow flashes",Indication.FlashingYellow,SignalRules.Select(SignalRole.Through,preEntry));
Equal("through warns of reduced next regardless of entry classification",Indication.FlashingYellow,SignalRules.Select(SignalRole.Through,Free(NextPermission.Reduced)));
Equal("distant stop",Indication.Yellow,SignalRules.Select(SignalRole.Distant,Free(NextPermission.Stop)));
Equal("distant restriction",Indication.FlashingYellow,SignalRules.Select(SignalRole.Distant,Free(NextPermission.Reduced)));
Equal("distant clear",Indication.Green,SignalRules.Select(SignalRole.Distant,Free()));
Equal("repeater stop is dark",Indication.Dark,SignalRules.Select(SignalRole.Repeater,Free(NextPermission.Stop)));
Equal("repeater open is green",Indication.Green,SignalRules.Select(SignalRole.Repeater,Free(NextPermission.Reduced)));
foreach (var indication in Enum.GetValues<Indication>())
    Equal("stable id "+indication, true, !string.IsNullOrWhiteSpace(SignalRules.AspectId(indication)));
Equal("stable open id", "OPEN", SignalRules.AspectId(Indication.Green));
Equal("stable stop id", "STOP", SignalRules.AspectId(Indication.Stop));
Equal("stable caution id", "CAUTION", SignalRules.AspectId(Indication.Yellow));
var shunt=Free();shunt.Occupied=true;
Equal("shunt into occupied track",Indication.ShuntingWhite,SignalRules.Select(SignalRole.Shunting,shunt));
shunt.ShuntingAllowed=false;
Equal("shunt prohibited",Indication.Stop,SignalRules.Select(SignalRole.Shunting,shunt));
Equal("yellow has no invented speed",0f,SignalRules.PassingSpeed(Indication.Yellow));
Equal("flashing yellow no speed here",0f,SignalRules.PassingSpeed(Indication.FlashingYellow));
Equal("diverging speed",40f,SignalRules.PassingSpeed(Indication.DoubleYellow));
Equal("invitation speed",20f,SignalRules.PassingSpeed(Indication.Invitation));
Equal("white is not train authority",NextPermission.Stop,SignalRules.Permission(Indication.ShuntingWhite));
Equal("invitation is not open train route",NextPermission.Stop,SignalRules.Permission(Indication.Invitation));
Equal("buffer always red",Indication.Stop,SignalRules.Select(SignalRole.BufferStop,Free()));
Equal("table disconnected red",Indication.Stop,SignalRules.Select(SignalRole.Turntable,Free()));
var table=Free();table.Connected=true;
Equal("table connected white",Indication.ShuntingWhite,SignalRules.Select(SignalRole.Turntable,table));

Signal NewSignal() { var s=new Signal { Block=new TrackBlock() }; s.Controller.Signal=s;return s; }
RuAspect Aspect(Signal signal, SignalRole role, Indication indication, bool manual=false) => new(
    new RuAspectDefinition {Role=role,Indication=indication,ManualOnly=manual,DisallowPassing=indication==Indication.Stop}, signal);
void Expect(Signal s, SignalRole role, Indication expected, string name)
{
    var matches=Enum.GetValues<Indication>().Where(i=>Aspect(s,role,i,i==Indication.Invitation).MeetsConditions()).ToArray();
    Equal(name,expected.ToString(),string.Join(",",matches));
    s.CurrentAspect=Aspect(s,role,expected);
}
void Link(Signal a,Signal b) => a.Block.NextController=b.Controller;
var a=NewSignal();var b=NewSignal();var c=NewSignal();Link(a,b);Link(b,c);c.Block.Occupied=true;
var terminalEntry=NewSignal();terminalEntry.Block.IsDeadEnd=true;
Expect(terminalEntry,SignalRole.Entry,Indication.DoubleYellow,"free terminal arrival without next signal opens restrictively");
terminalEntry.Block.Occupied=true;Expect(terminalEntry,SignalRole.Entry,Indication.Stop,"occupied terminal receiving track stays closed");
terminalEntry.Block.Occupied=false;SignalsMod.Settings.SpecialReservation=true;
Expect(terminalEntry,SignalRole.Entry,Indication.DoubleYellow,"terminal entry opens without reservation even when the native option is enabled");
terminalEntry.HasReservation=true;Expect(terminalEntry,SignalRole.Entry,Indication.DoubleYellow,"reserved terminal arrival opens without next controller");
terminalEntry.ReservedByOther=true;Expect(terminalEntry,SignalRole.Entry,Indication.Stop,"terminal arrival never bypasses conflicting reservation");
terminalEntry.ReservedByOther=false;SignalsMod.Settings.SpecialReservation=false;
// Entry admission is automatic, while reservations still protect other roles.
var automaticEntry=NewSignal();var automaticNext=NewSignal();Link(automaticEntry,automaticNext);
automaticNext.HasReservation=true;automaticNext.CurrentAspect=Aspect(automaticNext,SignalRole.Exit,Indication.Green);
SignalsMod.Settings.SpecialReservation=true;
Expect(automaticEntry,SignalRole.Entry,Indication.Green,"clear straight entry opens without a route reservation");
Equal("automatic entry does not reserve its route as a side effect",false,automaticEntry.HasReservation);
automaticEntry.IsShunting=true;Expect(automaticEntry,SignalRole.Entry,Indication.Stop,"entry shunting retains the native reservation requirement");automaticEntry.IsShunting=false;
automaticEntry.Operation=SignalOperationMode.SemiManual;automaticEntry.ManualOverrideAspect=-1;
Expect(automaticEntry,SignalRole.Entry,Indication.Stop,"entry semi-manual shunting retains the native reservation requirement");automaticEntry.Operation=SignalOperationMode.Automatic;
var entryPlain=new RailTrack{name="[track through]"};var entrySide=new RailTrack{name="[track diverging]"};
var entryPoints=new Junction{selectedBranch=1,outBranches=new(){new(){track=entryPlain},new(){track=entrySide}}};entrySide.inJunction=entryPoints;
var originalEntryTracks=automaticEntry.Block.Tracks;
automaticEntry.Block.Tracks=new[]{new TrackInfo{Track=entrySide,IsJunctionTrack=true,Direction=TrackDirection.Out}};
Expect(automaticEntry,SignalRole.Entry,Indication.DoubleYellowUpperFlashing,"unreserved entry opens toward an aligned side track");
entryPoints.selectedBranch=0;Expect(automaticEntry,SignalRole.Entry,Indication.Stop,"unreserved side entry closes when points select another track");entryPoints.selectedBranch=1;
automaticNext.CurrentAspect=Aspect(automaticNext,SignalRole.Exit,Indication.Stop);
Expect(automaticEntry,SignalRole.Entry,Indication.DoubleYellow,"unreserved side entry warns of a closed next signal");
automaticEntry.Block.IsDeadEnd=true;automaticEntry.Block.NextController=null;
Expect(automaticEntry,SignalRole.Entry,Indication.DoubleYellow,"unreserved side entry admits a clear terminal track restrictively");
automaticEntry.Block.IsDeadEnd=false;Link(automaticEntry,automaticNext);automaticEntry.Block.Tracks=originalEntryTracks;
automaticNext.CurrentAspect=Aspect(automaticNext,SignalRole.Exit,Indication.Green);
automaticEntry.Block.Occupied=true;Expect(automaticEntry,SignalRole.Entry,Indication.Stop,"automatic entry still stops on occupied receiving track");automaticEntry.Block.Occupied=false;
automaticEntry.ReservedByOther=true;Expect(automaticEntry,SignalRole.Entry,Indication.Stop,"automatic entry still stops on a conflicting reservation");automaticEntry.ReservedByOther=false;
automaticEntry.Block.ShouldBeUpdated=true;Expect(automaticEntry,SignalRole.Entry,Indication.Stop,"automatic entry still rejects a stale route");automaticEntry.Block.ShouldBeUpdated=false;
foreach(var protectedRole in new[]{SignalRole.Exit,SignalRole.Route,SignalRole.Shunting})
    Expect(automaticEntry,protectedRole,Indication.Stop,protectedRole+" retains the native reservation requirement");
var automaticApproach=NewSignal();automaticApproach.HasReservation=true;Link(automaticApproach,automaticEntry);
Expect(automaticEntry,SignalRole.Entry,Indication.Green,"automatic entry recovers when the clear route is restored");
Expect(automaticApproach,SignalRole.Through,Indication.Green,"preceding signal accepts an unreserved open entry");
automaticEntry.Block.Occupied=true;
Expect(automaticApproach,SignalRole.Through,Indication.Yellow,"preceding signal rejects an occupied automatic entry even with stale green");automaticEntry.Block.Occupied=false;
automaticEntry.CurrentAspect=new ForeignAspect();
Expect(automaticApproach,SignalRole.Through,Indication.Yellow,"foreign aspects retain their reservation requirement");
SignalsMod.Settings.SpecialReservation=false;
Expect(c,SignalRole.Through,Indication.Stop,"adapter occupied C red");
Expect(b,SignalRole.Through,Indication.Yellow,"adapter B yellow before C");
Expect(a,SignalRole.Through,Indication.Green,"adapter A green two free blocks");
c.Block.Occupied=false;
Expect(c,SignalRole.Through,Indication.Yellow,"missing next stays caution");
Expect(b,SignalRole.Through,Indication.Green,"cleared C releases B");
// Two station exits approach the same turnout from its different branches.
// TrackWalker retains both inbound branch tracks even when only one is aligned.
// DVSignals MatchingPathAspect checks these In tracks against selectedBranch.
var mergeStraight=new RailTrack { name="[track through]" };
var mergeDiverging=new RailTrack { name="[track diverging]" };
var merge=new Junction { outBranches=new(){new(){track=mergeStraight},new(){track=mergeDiverging}} };
mergeStraight.inJunction=merge;mergeDiverging.inJunction=merge;
var exitStraight=NewSignal();var exitDiverging=NewSignal();
exitStraight.Block.Tracks=new[]{new TrackInfo{Track=mergeStraight,IsJunctionTrack=true,Direction=TrackDirection.In}};
exitDiverging.Block.Tracks=new[]{new TrackInfo{Track=mergeDiverging,IsJunctionTrack=true,Direction=TrackDirection.In}};
Link(exitStraight,b);Link(exitDiverging,b);
merge.selectedBranch=0;
Expect(exitStraight,SignalRole.Exit,Indication.Green,"merge straight exit aligned opens");
Expect(exitDiverging,SignalRole.Exit,Indication.Stop,"merge other exit must not also show green");
merge.selectedBranch=1;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"merge switch change closes previous exit");
Expect(exitDiverging,SignalRole.Exit,Indication.DoubleYellowUpperFlashing,"merge selected diverging exit warns of reduced speed");
merge.selectedBranch=0;
Expect(exitDiverging,SignalRole.Exit,Indication.Stop,"merge changing back closes diverging exit");
Expect(exitStraight,SignalRole.Exit,Indication.Green,"merge changing back releases straight exit");
// This is a physical route check, independent of optional reservation settings.
foreach(var role in new[]{SignalRole.Through,SignalRole.Entry,SignalRole.Exit,SignalRole.Route,SignalRole.Shunting})
{
    Expect(exitDiverging,role,Indication.Stop,role+" unaligned inbound branch always stops");
    exitDiverging.HasReservation=true;
    SignalsMod.Settings.SpecialReservation=true;
    Expect(exitDiverging,role,Indication.Stop,role+" reservation does not override unaligned points");
    exitDiverging.HasReservation=false;
    SignalsMod.Settings.SpecialReservation=false;
}
exitStraight.ReservedByOther=true;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"aligned merge cannot override another reservation");
exitStraight.ReservedByOther=false;exitStraight.Block.Occupied=true;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"aligned merge cannot override occupied block");
exitStraight.Block.Occupied=false;
// An explicitly requested branch is an additional condition, even when the
// current physical path is itself aligned.
exitStraight.Controller.GroupJunction=merge;
exitStraight.Controller.RequiredJunctionBranch=1;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"controller requested a different branch");
exitStraight.Controller.RequiredJunctionBranch=0;
Expect(exitStraight,SignalRole.Exit,Indication.Green,"controller required branch matches actual route");
exitStraight.Controller.GroupJunction=null;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"missing junction cannot confirm required branch");
exitStraight.Controller.RequiredJunctionBranch=null;
Expect(exitStraight,SignalRole.Exit,Indication.Green,"no explicit branch requirement allows aligned route");
var approach=NewSignal();Link(approach,exitStraight);
merge.selectedBranch=1;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"point change closes exit before upstream update");
Expect(approach,SignalRole.Through,Indication.Yellow,"upstream warns after competing route selected");
merge.selectedBranch=0;
Expect(exitStraight,SignalRole.Exit,Indication.Green,"restored points release exit before upstream update");
Expect(approach,SignalRole.Through,Indication.Green,"upstream releases after route restored");
merge.selectedBranch=2;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"invalid selected branch closes route");
merge.selectedBranch=0;mergeStraight.inJunction=null;
Expect(exitStraight,SignalRole.Exit,Indication.Stop,"missing turnout data closes route");
mergeStraight.inJunction=merge;
var independentExit=NewSignal();Link(independentExit,b);
Expect(exitStraight,SignalRole.Exit,Indication.Green,"aligned exit on clear path remains open");
Expect(independentExit,SignalRole.Exit,Indication.Green,"independent non-conflicting exit may also be open");
var entry=NewSignal();entry.Controller.Type=SignalType.Entry;
var branchTrack=new RailTrack { name="[track diverging]" };
var straightTrack=new RailTrack { name="[track through]" };
var junction=new Junction { selectedBranch=1,outBranches=new(){new(){track=straightTrack},new(){track=branchTrack}} };
branchTrack.inJunction=junction;straightTrack.inJunction=junction;
entry.Block.Tracks=new[]{new TrackInfo{Track=branchTrack,IsJunctionTrack=true,Direction=TrackDirection.Out}};
Expect(entry,SignalRole.Entry,Indication.DoubleYellow,"entry actual diverging block");
junction.selectedBranch=0;
Expect(entry,SignalRole.Entry,Indication.Stop,"stale facing route after switch change stops");
junction.selectedBranch=1;
Expect(entry,SignalRole.Entry,Indication.DoubleYellow,"facing route restored");
Link(a,entry);
Expect(a,SignalRole.Through,Indication.FlashingYellow,"chain pre-entry sees 2Y");
Expect(a,SignalRole.Distant,Indication.FlashingYellow,"distant sees 2Y");
entry.Block.Tracks[0].Direction=TrackDirection.In;junction.selectedBranch=0;
Expect(entry,SignalRole.Entry,Indication.Stop,"inbound branch against selected points stops");
junction.selectedBranch=1;
Expect(entry,SignalRole.Entry,Indication.DoubleYellow,"inbound diverging branch requires reduced speed");
entry.Block.Tracks[0].Direction=TrackDirection.Out;junction.selectedBranch=0;
Expect(entry,SignalRole.Entry,Indication.Stop,"outbound route against selected branch stops");
junction.selectedBranch=1;
Expect(entry,SignalRole.Entry,Indication.DoubleYellow,"outbound route restored");
entry.Block.Tracks[0].Track=straightTrack;junction.selectedBranch=0;
Expect(entry,SignalRole.Entry,Indication.Yellow,"trailing straight branch no false divergence");
entry.CurrentAspect=Aspect(entry,SignalRole.Entry,Indication.Invitation,true);
Expect(a,SignalRole.Through,Indication.Yellow,"invitation never clears previous signal");
Equal("invitation does not light repeater",Indication.Dark,SignalRules.Select(SignalRole.Repeater,Free(NextPermission.Stop)));
Equal("invitation never selected automatically",false,Aspect(entry,SignalRole.Entry,Indication.Invitation,true).MeetsConditions());
entry.CurrentAspect=Aspect(entry,SignalRole.Exit,Indication.ShuntingWhite);
Expect(a,SignalRole.Through,Indication.Yellow,"white does not clear previous signal");
entry.CurrentAspect=null;
Expect(a,SignalRole.Through,Indication.Yellow,"dark next caution");
entry.Block=null;
Expect(entry,SignalRole.Entry,Indication.Stop,"missing own block stops");
entry.Block=new TrackBlock{IsSelfLoop=true};
Expect(entry,SignalRole.Entry,Indication.Stop,"self loop stops");
entry.Block=new TrackBlock();entry.ReservedByOther=true;
Expect(entry,SignalRole.Entry,Indication.Stop,"another reservation stops");
entry.ReservedByOther=false;SignalsMod.Settings.SpecialReservation=true;
Expect(entry,SignalRole.Entry,Indication.Yellow,"straight entry opens without requiring reservation");
entry.HasReservation=true;
Expect(entry,SignalRole.Entry,Indication.Yellow,"own reservation releases");
SignalsMod.Settings.SpecialReservation=false;entry.IsShunting=true;entry.Block.Occupied=true;
Expect(entry,SignalRole.Exit,Indication.ShuntingWhite,"native shunting conversion permits occupied track");
entry.ShuntingAllowed=false;
Expect(entry,SignalRole.Exit,Indication.Stop,"native shunting conversion respects prohibition");
var manual=NewSignal();
manual.Operation=SignalOperationMode.SemiManual;
manual.AllAspects=new[]{Aspect(manual,SignalRole.Exit,Indication.ShuntingWhite)};
manual.ManualOverrideAspect=-1;manual.Block.Occupied=true;
Expect(manual,SignalRole.Exit,Indication.ShuntingWhite,"manual shunting request permits occupied siding");
manual.ReservedByOther=true;
Expect(manual,SignalRole.Exit,Indication.Stop,"manual shunting cannot override conflicting reservation");
manual.ReservedByOther=false;manual.Block.Tracks=exitDiverging.Block.Tracks;merge.selectedBranch=0;
Expect(manual,SignalRole.Exit,Indication.Stop,"manual shunting cannot override unaligned points");
merge.selectedBranch=1;
Expect(manual,SignalRole.Exit,Indication.ShuntingWhite,"manual shunting resumes after points aligned");
manual.ShuntingAllowed=false;
Expect(manual,SignalRole.Exit,Indication.Stop,"manual shunting respects native prohibition");
manual.ShuntingAllowed=true;SignalsMod.Settings.SpecialReservation=true;
Expect(manual,SignalRole.Exit,Indication.Stop,"manual shunting respects reservation requirement");
manual.HasReservation=true;
Expect(manual,SignalRole.Exit,Indication.ShuntingWhite,"reserved manual shunting releases");
SignalsMod.Settings.SpecialReservation=false;manual.Operation=SignalOperationMode.Automatic;
Expect(manual,SignalRole.Exit,Indication.Stop,"automatic restores train occupancy check");
var stale=NewSignal();stale.Block.ShouldBeUpdated=true;
Expect(stale,SignalRole.Exit,Indication.Stop,"dirty own block stops until rebuilt");
stale.CurrentAspect=Aspect(stale,SignalRole.Exit,Indication.Green);
var beforeStale=NewSignal();Link(beforeStale,stale);
Expect(beforeStale,SignalRole.Entry,Indication.Yellow,"stale green ahead cannot clear previous signal");
Expect(beforeStale,SignalRole.Distant,Indication.Yellow,"distant warns before stale home block");
Expect(beforeStale,SignalRole.Repeater,Indication.Dark,"repeater remains dark before stale home block");
stale.Block.ShouldBeUpdated=false;
Expect(beforeStale,SignalRole.Entry,Indication.Green,"rebuilt open next block clears previous");
SignalsMod.Settings.SpecialReservation=true;beforeStale.HasReservation=true;
Expect(beforeStale,SignalRole.Entry,Indication.Yellow,"stale next green without required native reservation warns");
stale.HasReservation=true;
Expect(beforeStale,SignalRole.Entry,Indication.Green,"reserved next restores permission with native option");
SignalsMod.Settings.SpecialReservation=false;stale.HasReservation=false;beforeStale.HasReservation=false;
Expect(beforeStale,SignalRole.Exit,Indication.Green,"disabling native option opens clear exit without reservations");
var insideRoute=NewSignal();insideRoute.Block.Tracks=exitDiverging.Block.Tracks;
var requiredInside=new BasicSignalController{RequiredJunctionBranch=0};
var routeGroup=new JunctionSignalGroup {ReverseJunctionSignal=requiredInside, JunctionSignal=requiredInside};
SignalManager.Instance.Groups[merge]=routeGroup;merge.selectedBranch=1;
Expect(insideRoute,SignalRole.Route,Indication.Stop,"downstream inbound required route mismatch stops");
routeGroup.Branches[mergeDiverging]=new BasicSignalController{RequiredJunctionBranch=1};
Expect(insideRoute,SignalRole.Route,Indication.DoubleYellow,"branch-specific requirement overrides reverse fallback");
insideRoute.Block.Tracks=new[]{new TrackInfo{Track=mergeDiverging,IsJunctionTrack=true,Direction=TrackDirection.Out}};
Expect(insideRoute,SignalRole.Route,Indication.Stop,"downstream outbound required route mismatch stops");
requiredInside.RequiredJunctionBranch=1;
Expect(insideRoute,SignalRole.Route,Indication.DoubleYellow,"downstream required route correctly aligned");
SignalManager.Instance.Groups.Clear();
foreach(var indication in Enum.GetValues<Indication>())
{
    Equal("cab explicit prohibition wins "+indication,CabIndication.Red,
        CabDisplayRules.Select(indication,true,true,false,true,out _));
}
Equal("cab green",CabIndication.Green,CabDisplayRules.Select(Indication.Green,false,true,false,false,out _));
Equal("cab yellow",CabIndication.Yellow,CabDisplayRules.Select(Indication.Yellow,false,true,false,false,out _));
Equal("cab flash yellow",CabIndication.Yellow,CabDisplayRules.Select(Indication.FlashingYellow,false,true,false,false,out var cabNote));
Equal("cab flash yellow detail",CabDetail.None,cabNote);
Equal("cab 2Y caution",CabIndication.Yellow,CabDisplayRules.Select(Indication.DoubleYellow,false,true,false,false,out cabNote));
Equal("cab 2Y stop detail",CabDetail.None,cabNote);
Equal("cab 2Y flash caution",CabIndication.Yellow,CabDisplayRules.Select(Indication.DoubleYellowUpperFlashing,false,true,false,false,out cabNote));
Equal("cab 2Y flash detail",CabDetail.None,cabNote);
Equal("cab white is not train authority",CabIndication.Red,CabDisplayRules.Select(Indication.ShuntingWhite,false,true,false,true,out _));
Equal("cab dedicated shunting white",CabIndication.White,CabDisplayRules.Select(Indication.ShuntingWhite,false,true,true,true,out _));
Equal("cab invitation unreserved",CabIndication.Red,CabDisplayRules.Select(Indication.Invitation,false,true,false,false,out _));
Equal("cab invitation reserved",CabIndication.YellowRed,CabDisplayRules.Select(Indication.Invitation,false,true,false,true,out cabNote));
Equal("cab invitation detail",CabDetail.None,cabNote);
Equal("cab stale data unknown",CabIndication.Unknown,CabDisplayRules.Select(Indication.Green,false,false,false,true,out _));
Equal("cab dark unknown",CabIndication.Unknown,CabDisplayRules.Select(Indication.Dark,false,true,false,true,out _));
foreach (var role in new[]{SignalRole.Through,SignalRole.Entry,SignalRole.Exit,SignalRole.Route})
{
    foreach(var aspect in new[]{Indication.Green,Indication.Yellow,Indication.FlashingYellow})
        Equal("semaphore main route "+role+aspect,SovietSemaphore.SemaphoreState.Main,SemaphoreRules.Pose(role,aspect,false,true));
    foreach(var aspect in new[]{Indication.DoubleYellow,Indication.DoubleYellowUpperFlashing})
    {
        Equal("semaphore diverging route "+role+aspect,SovietSemaphore.SemaphoreState.Diverging,SemaphoreRules.Pose(role,aspect,false,true));
        Equal("single wing cannot open diverging "+role+aspect,SovietSemaphore.SemaphoreState.Stop,SemaphoreRules.Pose(role,aspect,false,false));
    }
    foreach(var aspect in new[]{Indication.Stop,Indication.Invitation,Indication.ShuntingWhite})
        Equal("semaphore arms stay closed "+role+aspect,SovietSemaphore.SemaphoreState.Stop,SemaphoreRules.Pose(role,aspect,false,true));
    Equal("semaphore dark "+role,SovietSemaphore.SemaphoreState.Dark,SemaphoreRules.Pose(role,Indication.Dark,false,true));
    Equal("semaphore shunting never raises arms "+role,SovietSemaphore.SemaphoreState.Stop,SemaphoreRules.Pose(role,Indication.Green,true,true));
}
foreach(var role in new[]{SignalRole.Shunting,SignalRole.Distant,SignalRole.Repeater,SignalRole.BufferStop,SignalRole.Turntable})
    Equal("semaphore rejects unsupported role "+role,SovietSemaphore.SemaphoreState.Stop,SemaphoreRules.Pose(role,Indication.Green,false,true));
foreach(var role in new[]{SignalRole.Shunting,SignalRole.Turntable})
{
    Equal("mechanical shunting opens only on native shunting permission "+role,SovietSemaphore.SemaphoreState.Main,SemaphoreRules.Pose(role,Indication.ShuntingWhite,true,false));
    Equal("mechanical shunting stop "+role,SovietSemaphore.SemaphoreState.Stop,SemaphoreRules.Pose(role,Indication.Stop,true,false));
    Equal("mechanical shunting dark "+role,SovietSemaphore.SemaphoreState.Dark,SemaphoreRules.Pose(role,Indication.Dark,true,false));
}
var assetPath=args.Length>1?args[1]:Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src/RuSignals.Next/Assets"));
var semaphoreJson=File.ReadAllText(Path.Combine(assetPath,"Semaphore/semaphore_manifest.json"));
var semaphoreParts=SovietSemaphore.SemaphoreManifest.Read(semaphoreJson);
Equal("runtime semaphore manifest loads all twelve parts",12,semaphoreParts.Length);
Equal("runtime semaphore manifest retains arm parent","UpperArmPivot",semaphoreParts.Single(p=>p.key=="semaphore_upper").parent);
Equal("runtime semaphore manifest retains pivot",true,Math.Abs(semaphoreParts.Single(p=>p.key=="semaphore_upper").pivot[1]-8.05f)<.0001f);
foreach(var bad in new[]{"null","{}","{\"parts\":[]}","{\"parts\":[null]}",
    "{\"parts\":[{\"key\":\"../mesh\",\"mesh\":\"x\",\"parent\":\"root\",\"pivot\":[0,0,0]}]}",
    "{\"parts\":[{\"key\":\"mesh\",\"mesh\":\"x\",\"parent\":\"root\",\"pivot\":[0,0]}]}"})
{
    bool rejected=false;
    try { SovietSemaphore.SemaphoreManifest.Read(bad); } catch(InvalidDataException) { rejected=true; }
    Equal("runtime semaphore manifest rejects "+bad,true,rejected);
}
foreach(string station in new[]{"HB","CW","OR","FF","MF"})
{
    Equal("square-shield district "+station,true,ShieldRegionRules.IndustrialStation(station));
    Equal("square-shield district ignores case "+station,true,ShieldRegionRules.IndustrialStation(station.ToLowerInvariant()));
}
foreach(string station in new[]{"SM","GF","CS","SW","FM","OWN","",null,"HBO","FF1"})
    Equal("other districts stay oval "+station,false,ShieldRegionRules.IndustrialStation(station));
foreach(float sign in new[]{-1f,1f})
{
    Equal("industrial centre",true,ShieldRegionRules.Near(0,0));
    Equal("industrial radius exact X",true,ShieldRegionRules.Near(sign*1800,0));
    Equal("industrial radius exact Z",true,ShieldRegionRules.Near(0,sign*1800));
    Equal("outside industrial radius X",false,ShieldRegionRules.Near(sign*1800.1f,0));
    Equal("outside industrial radius Z",false,ShieldRegionRules.Near(0,sign*1800.1f));
    Equal("round radius instead of square",false,ShieldRegionRules.Near(sign*1500,sign*1500));
}
Equal("invalid industrial position",false,ShieldRegionRules.Near(float.NaN,0));
var compactProfiles=MastChecks.Run(assetPath,(name,value)=>Equal(name,true,value));
Equal("wall console excludes concrete mast",false,TunnelMountRules.IsHeadPart("Body_0_Footing_concrete"));
Equal("wall console excludes ladder",false,TunnelMountRules.IsHeadPart("Body_1_Stairway"));
Equal("wall console retains housing",true,TunnelMountRules.IsHeadPart("Body_2_Signal"));
Equal("wall console retains lens",true,TunnelMountRules.IsHeadPart("Green"));
foreach(float wall in new[]{2.1f,2.3f,2.6f,3.1f,3.7f,4.4f})foreach(float width in new[]{.281f,.429f})
{
    bool fits=TunnelMountRules.Fit(2.05f,wall,width,out float shift);
    Equal("console clearance decision "+wall+" "+width,wall>=2*width+TunnelMountRules.WallGap+TunnelMountRules.TrackClearance,fits);
    if(fits)
    {
        Equal("console preserves inner train clearance "+wall+" "+width,true,2.05f+shift-width>=TunnelMountRules.TrackClearance-.00001f);
        Equal("console outward displacement bounded "+wall+" "+width,true,shift<=TunnelMountRules.MaxOutwardShift);
        Equal("console housing clears wall "+wall+" "+width,true,2.05f+shift+width<=wall-TunnelMountRules.WallGap+.00001f);
    }
}
foreach(float wall in new[]{float.NaN,float.PositiveInfinity,-2f,0f})
    Equal("console rejects invalid wall "+wall,false,TunnelMountRules.Fit(2.05f,wall,.28f,out _));
Equal("wide diamond repeater retains original clearance",true,TunnelMountRules.Fit(2.05f,3.1f,.71f,out var diamondShift)&&diamondShift>=0);
Equal("wide diamond cannot move into original train clearance",false,TunnelMountRules.Fit(2.05f,2.8f,.71f,out _));
if(args.Length>0) File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0])!,"compact-profiles.json"),
    JsonSerializer.Serialize(compactProfiles,new JsonSerializerOptions{WriteIndented=true}));
Equal("normal lamp emission preserved",RouteBoardRules.MainLampEmission,LampVisualRules.Emission(false));
Equal("blue lamp daylight emission stronger",true,LampVisualRules.Emission(true)>LampVisualRules.Emission(false));
Equal("blue halo dimmer than prior release",true,LampVisualRules.Glare(true)>0&&LampVisualRules.Glare(true)<1.6f);
Equal("blue lamp spill dimmer than prior release",true,LampVisualRules.Intensity(true)>0&&LampVisualRules.Intensity(true)<2.8f);
Equal("local lamp spill range bounded",true,LampVisualRules.Range>0&&LampVisualRules.Range<=4);
Equal("narrow local lamp beam",55f,LampVisualRules.SpotAngle);
foreach(float distance in new[]{0f,29f,30f})Equal("full local brightness at "+distance,1f,LampVisualRules.SpillGain(distance));
foreach(float distance in new[]{100f,101f,150f,float.PositiveInfinity,float.NaN})Equal("local source absent at "+distance,0f,LampVisualRules.SpillGain(distance));
Equal("half local brightness at 65 m",.5f,LampVisualRules.SpillGain(65));
float previous=1;
for(int distance=31;distance<100;distance++){float value=LampVisualRules.SpillGain(distance);Equal("continuous decreasing local gain "+distance,true,value>0&&value<previous);previous=value;}
Equal("yard track number", "7", DesignationRules.Number("[Y]CW-B7S"));
Equal("service track number", "8", DesignationRules.Number("[Y]MF-T8"));
Equal("junction has no track number", null, DesignationRules.Number("[track diverging]"));
Equal("shunting Latin label", "M15", DesignationRules.Format(SignalRole.Shunting,false,15));
Equal("turntable shares shunting label", "M8", DesignationRules.Format(SignalRole.Turntable,true,8));
Equal("exit uses owner track", "Ch7", DesignationRules.Format(SignalRole.Exit,true,1,"7"));
Equal("entry short", "N", DesignationRules.Format(SignalRole.Entry,false,1));
Equal("distant names home", "PN", DesignationRules.Format(SignalRole.Distant,false,1,null,"N"));
Equal("repeater distinct", "PvN", DesignationRules.Format(SignalRole.Repeater,false,1,null,"N"));
Equal("internal turntable key", true, DesignationRules.ServiceName("T_CW_1-1"));
Equal("custom name retained", false, DesignationRules.ServiceName("ЧМ3"));
Equal("custom designation Latin", "ChM3", DesignationRules.Latin("ЧМ3"));
Equal("lowercase designation Latin", "chm3", DesignationRules.Latin("чм3"));
Equal("already Latin identity retained", "T_MF_1-8", DesignationRules.Latin("T_MF_1-8"));
Equal("duplicate suffix Latin", "A", DesignationRules.Suffix(0));
foreach(var role in Enum.GetValues<SignalRole>())foreach(bool even in new[]{false,true})
    Equal("every signal role uses Latin "+role+" "+even,true,DesignationRules.Format(role,even,15,"7","Ch4").All(c=>c<128));
foreach(var destination in new[]{"CW","HB",null,""})
    Equal("combined local route "+destination,destination=="CW",CombinedExitRules.Shunting("CW",destination,true,false,300));
Equal("long dead end remains train route",false,CombinedExitRules.Shunting("CW","CW",true,false,1500));
Equal("unknown destination stays train route",false,CombinedExitRules.Shunting(null,null,true,false,300));
Equal("through exit train route",false,CombinedExitRules.Shunting("CW","CW",false,false,300));
Equal("pure shunting destination",true,CombinedExitRules.Shunting("CW","CW",false,true,300));
var combinedExit=NewSignal();combinedExit.Controller.Station="CW";combinedExit.Block.Station="CW";combinedExit.Block.Length=300;combinedExit.Block.IsDeadEnd=true;
combinedExit.Definition.Aspects=new[]{new RuAspectDefinition{AutoShunting=true}};
Equal("dwarf exit local dead end white",Indication.ShuntingWhite,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.Block.Occupied=true;
Equal("dwarf exit shunting may enter occupied",Indication.ShuntingWhite,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.ShuntingAllowed=false;
Equal("native shunting prohibition retained",Indication.Stop,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.ShuntingAllowed=true;combinedExit.ReservedByOther=true;
Equal("automatic white cannot bypass reservation",Indication.Stop,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.ReservedByOther=false;combinedExit.Block.IsSelfLoop=true;
Equal("automatic white cannot bypass loop",Indication.Stop,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.Block.IsSelfLoop=false;combinedExit.Block.IsDeadEnd=false;
Equal("occupied train exit still red",Indication.Stop,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.Block.Occupied=false;
Equal("ordinary train exit towards stop yellow",Indication.Yellow,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
combinedExit.Definition.Aspects=new[]{new RuAspectDefinition{AutoShunting=false}};combinedExit.Block.IsDeadEnd=true;
Equal("other exits unchanged",Indication.Yellow,SignalRules.Select(SignalRole.Exit,SignalReader.Read(combinedExit,SignalRole.Exit)));
var exitWarn=Free(NextPermission.Reduced);exitWarn.NextIsEntry=true;
Equal("exit warns before restricted entry",Indication.FlashingYellow,SignalRules.Select(SignalRole.Exit,exitWarn));
foreach(string name in new[]{"[track through]","[track through] 23","[track through] 0","[track through](Clone)","[track through](Clone) 99","[track through] (Clone) 23","[track through]12","[track through](Clone)12","[track through](Clone)7(Clone)12","[track through] 23(Clone)7","[track through] (Clone)0(Clone)3"})
    Equal("native and DoubleTrack straight "+name,false,JunctionRouteRules.Diverging(name));
foreach(string name in new[]{"[track diverging]","[track diverging] 23","[track through] diverging","[track through]x","[track through](Clone)7x","[track through]12_Siding",null})
    Equal("unknown or diverging branch "+name,true,JunctionRouteRules.Diverging(name));
mergeStraight.name="[track through] 23";merge.selectedBranch=0;
Expect(exitStraight,SignalRole.Exit,Indication.Green,"DoubleTrack suffixed straight exit green");
foreach(string name in new[]{"[track through](Clone)12","[track through](Clone)7(Clone)12"})
{mergeStraight.name=name;Expect(exitStraight,SignalRole.Exit,Indication.Green,"Split DoubleTrack straight exit green "+name);}
Link(exitStraight,c);c.Block.Occupied=true;Expect(c,SignalRole.Through,Indication.Stop,"next occupied for dwarf route regression");
Expect(exitStraight,SignalRole.Exit,Indication.Yellow,"DoubleTrack straight before closed signal single yellow");
c.Block.Occupied=false;Link(exitStraight,b);
mergeDiverging.name="[track diverging] 23";merge.selectedBranch=1;
Expect(exitDiverging,SignalRole.Exit,Indication.DoubleYellowUpperFlashing,"DoubleTrack actual divergence retains two yellows");
merge.selectedBranch=0;Expect(exitDiverging,SignalRole.Exit,Indication.Stop,"suffix correction never bypasses misaligned turnout");
combinedExit.Definition.Aspects=new[]{new RuAspectDefinition{AutoShunting=true}};
combinedExit.Block.IsDeadEnd=false;combinedExit.Block.Station="CW";
var localExit=NewSignal();localExit.Controller.Station="CW";localExit.Controller.Type=SignalType.Exit;
Link(combinedExit,localExit);combinedExit.Block.Length=1500;
Expect(combinedExit,SignalRole.Exit,Indication.ShuntingWhite,"native main block skips yard signals and ends at another local exit");
combinedExit.Block.Occupied=true;
Expect(combinedExit,SignalRole.Exit,Indication.ShuntingWhite,"local movement into occupied siding is shunting");
combinedExit.ShuntingAllowed=false;Expect(combinedExit,SignalRole.Exit,Indication.Stop,"local movement respects native shunting prohibition");
combinedExit.ShuntingAllowed=true;combinedExit.Block.Occupied=false;
localExit.Controller.Station="HB";localExit.CurrentAspect=Aspect(localExit,SignalRole.Exit,Indication.Green);
Expect(combinedExit,SignalRole.Exit,Indication.Green,"first station CW does not turn departure towards HB into white");
localExit.Controller.Station="CW";localExit.Controller.Type=SignalType.Entry;
Expect(combinedExit,SignalRole.Exit,Indication.Green,"same-station entry endpoint remains train route");
localExit.Controller.Type=SignalType.Exit;
combinedExit.Block.FullRoute=new TrackBlock{NextController=b.Controller};
Expect(combinedExit,SignalRole.Exit,Indication.Green,"AB window uses whole route and cannot invent a local manoeuvre");
combinedExit.Block.FullRoute=null;localExit.Controller.Station=null;
Expect(combinedExit,SignalRole.Exit,Indication.Green,"unknown destination cannot invent white permission");
NormativeChecks.Run((name,expected,actual)=>Equal(name,expected,actual));
exitDiverging.Controller.PrefabType=PrefabType.Exit;merge.selectedBranch=1;
Expect(exitDiverging,SignalRole.Exit,Indication.Green,"ISI side departure across aligned turnout can show green");
exitDiverging.Definition.Aspects=new[]{new RuAspectDefinition{Mechanical=true}};
Expect(exitDiverging,SignalRole.Exit,Indication.DoubleYellowUpperFlashing,"Mechanical side departure retains two-arm route indication");
exitDiverging.Definition.Aspects=new[]{new RuAspectDefinition()};c.CurrentAspect=Aspect(c,SignalRole.Through,Indication.Green);
Link(exitDiverging,c);c.IsOff=true;
Expect(exitDiverging,SignalRole.Exit,Indication.Yellow,"Switched off next cannot supply stale green permission");
c.IsOff=false;c.Block.Tracks=Array.Empty<TrackInfo>();
Expect(exitDiverging,SignalRole.Exit,Indication.Yellow,"Empty next block cannot supply stale green permission");
SignalsMod.Settings.SpecialReservation=false;
var foreignHost=NewSignal();var foreignHome=NewSignal();Link(foreignHost,foreignHome);
foreignHome.CurrentAspect=new ForeignAspect();
Expect(foreignHost,SignalRole.Entry,Indication.Green,"Confirmed foreign train aspect remains compatible");
foreignHome.Block.Occupied=true;
Expect(foreignHost,SignalRole.Entry,Indication.Yellow,"Foreign stale green cannot bypass occupation");
foreignHome.Block.Occupied=false;foreignHome.Block.ShouldBeUpdated=true;
Expect(foreignHost,SignalRole.Entry,Indication.Yellow,"Foreign stale green cannot bypass dirty block");
foreignHome.Block.ShouldBeUpdated=false;foreignHome.ReservedByOther=true;
Expect(foreignHost,SignalRole.Entry,Indication.Yellow,"Foreign stale green cannot bypass another reservation");
foreignHome.ReservedByOther=false;foreignHome.Block.IsSelfLoop=true;
Expect(foreignHost,SignalRole.Entry,Indication.Yellow,"Foreign stale green cannot bypass self-loop");
var json=JsonSerializer.Serialize(new { passed=passed.Count, scenarios=passed, game_runtime="not tested; adapter uses test doubles" },new JsonSerializerOptions{WriteIndented=true});
if(args.Length>0) File.WriteAllText(args[0],json);
Console.WriteLine($"PASS: {passed.Count} signalling, cab adapter and mast geometry checks.");

sealed class ForeignAspect:Signals.Game.Aspects.IAspect
{
    public bool DisallowPassing=>false;
    public Signals.Common.Aspects.AspectBaseDefinition GetDefinition()=>new();
}
