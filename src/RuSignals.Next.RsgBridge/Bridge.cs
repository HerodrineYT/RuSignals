using System;
using System.Reflection;
using HarmonyLib;
using RailwaySafetyGadgets;
using Signals.Game;
using Signals.Game.Railway;
namespace RuSignals.RsgBridge;
using Signal=global::Signals.Game.Signal;
public static class Bridge
{
    private static PropertyInfo source;
    private static FieldInfo controller,mode,captured,shuntingAtPassage,reservedAtPassage;
    private static object shuntingModeValue;
    [ThreadStatic] private static bool shuntingMode;
    public static void Install(Harmony h)
    {
        var adapter=AccessTools.TypeByName("RailwaySafetyGadgets.SignalsIntegration.SignalsAdapter");
        var reading=adapter?.GetNestedType("Reading",BindingFlags.NonPublic);
        source=AccessTools.Property(reading,"Signal");
        var evaluate=AccessTools.Method(adapter,"Evaluate",new[]{typeof(Signal),typeof(bool),typeof(bool),typeof(CabSignalDetail).MakeByRefType(),typeof(bool).MakeByRefType()});
        bool modern=evaluate!=null;
        if(!modern)evaluate=AccessTools.Method(adapter,"Evaluate",new[]{typeof(Signal),typeof(bool),typeof(bool),typeof(bool),typeof(CabSignalDetail).MakeByRefType(),typeof(bool).MakeByRefType()});
        var prohibited=AccessTools.PropertyGetter(reading,"ProhibitsPassing");var contains=AccessTools.Method(reading,"Contains");
        if(source==null||evaluate==null||evaluate.ReturnType!=typeof(CabAspect)||prohibited==null||contains==null)throw new NotSupportedException("Unsupported RSG adapter API");
        controller=AccessTools.Field(reading,"controller");mode=AccessTools.Field(reading,"Mode");captured=AccessTools.Field(reading,"PassageCaptured");shuntingAtPassage=AccessTools.Field(reading,"ShuntingPermissionAtPassage");
        reservedAtPassage=AccessTools.Field(reading,"ReservedAtPassage");
        if(modern&&(controller==null||mode==null||captured==null||shuntingAtPassage==null||reservedAtPassage==null))throw new NotSupportedException("Unsupported RSG mode API");
        if(modern)shuntingModeValue=Enum.Parse(mode.FieldType,"Shunting");
        h.Patch(evaluate,prefix:new HarmonyMethod(typeof(Bridge),modern?nameof(Evaluate):nameof(EvaluateLegacy)));
        h.Patch(prohibited,postfix:new HarmonyMethod(typeof(Bridge),nameof(Prohibits)));
        h.Patch(contains,postfix:new HarmonyMethod(typeof(Bridge),nameof(Contains)));
        SpeedIntegration.Install(h);
        if(modern)
        {
            h.Patch(source.GetGetMethod(true),postfix:new HarmonyMethod(typeof(Bridge),nameof(CombinedSignal)));
            h.Patch(AccessTools.Method(reading,"ReadDisplay"),prefix:new HarmonyMethod(typeof(Bridge),nameof(ReadingBegin)),finalizer:new HarmonyMethod(typeof(Bridge),nameof(End)));
            h.Patch(AccessTools.Method(reading,"Capture"),postfix:new HarmonyMethod(typeof(Bridge),nameof(Capture)));
            foreach(var method in adapter.GetMethods(BindingFlags.Public|BindingFlags.Instance))
                if(method.Name=="ReadCab")h.Patch(method,prefix:new HarmonyMethod(typeof(Bridge),nameof(CabBegin)),finalizer:new HarmonyMethod(typeof(Bridge),nameof(End)));
        }
    }
    private static bool Reserved(Signal signal)=>signal.Block!=null&&!signal.Block.ShouldBeUpdated&&TrackReserver.HasReservation(signal)&&!TrackReserver.IsSignalReservedByAnother(signal);
    private static bool ShuntingReading(object reading)=>reading!=null&&mode!=null&&mode.DeclaringType.IsInstanceOfType(reading)&&Equals(mode.GetValue(reading),shuntingModeValue);
    private static bool Combined(Signal signal)=>signal?.Definition?.Aspects is {Length:>0} aspects&&aspects[0] is RuAspectDefinition definition&&definition.AutoShunting;
    private static bool White(Signal signal)=>signal?.CurrentAspect?.GetDefinition() is RuAspectDefinition definition&&definition.Indication==Indication.ShuntingWhite;
    private static void CombinedSignal(object __instance,ref Signal __result)
    {
        if(__result!=null||!ShuntingReading(__instance)||(bool)captured.GetValue(__instance))return;
        var owner=controller.GetValue(__instance) as global::Signals.Game.Controllers.BasicSignalController;
        if(owner==null||!owner.Exists||owner.IsOld||!owner.PlacementInfo.HasValue)return;
        var signal=owner.GetControllerSignal();if(Combined(signal)&&signal.Parent==null)__result=signal;
    }
    private static void ReadingBegin(object __instance,out bool __state){__state=shuntingMode;shuntingMode=ShuntingReading(__instance);}
    private static void CabBegin(object[] __args,out bool __state)
    {
        __state=shuntingMode;
        shuntingMode=__args.Length==6?Equals(__args[5],shuntingModeValue):ShuntingReading(__args[2])||ShuntingReading(__args[3]);
    }
    private static void End(bool __state){shuntingMode=__state;}
    private static bool EvaluateLegacy(Signal signal,bool current,bool forbidden,ref CabSignalDetail detail,ref bool known,ref CabAspect __result)=>Read(signal,current||forbidden,ref detail,ref known,ref __result);
    private static bool Evaluate(Signal signal,bool current,ref CabSignalDetail detail,ref bool known,ref CabAspect __result)=>Read(signal,current,ref detail,ref known,ref __result);
    private static bool Read(Signal signal,bool prohibited,ref CabSignalDetail detail,ref bool known,ref CabAspect result)
    {
        if(signal==null||signal.IsOff||!(signal.CurrentAspect?.GetDefinition() is RuAspectDefinition definition))return true;
        bool shunting=signal.IsShunting||(shuntingMode&&definition.AutoShunting);
        // A train indication does not grant a yard movement in shunting mode.
        if(shuntingMode&&definition.AutoShunting&&definition.Indication!=Indication.ShuntingWhite&&definition.Indication!=Indication.Invitation)prohibited=true;
        var value=CabDisplayRules.Select(definition.Indication,prohibited||(signal.CurrentAspect.DisallowPassing&&definition.Indication!=Indication.Stop),signal.Block!=null&&!signal.Block.ShouldBeUpdated,shunting,Reserved(signal),out var note);
        known=value!=CabIndication.Unknown;
        switch(value){case CabIndication.Green:result=CabAspect.Green;break;case CabIndication.Yellow:result=CabAspect.Yellow;break;case CabIndication.YellowRed:result=CabAspect.YellowRed;break;case CabIndication.Red:result=CabAspect.Red;break;default:result=CabAspect.White;break;}
        // RSG's restriction details describe its own green/flashing-green code.
        // CopyTo replaces the selected colours when they are present. Russian
        // yellow indications remain steady yellow in the locomotive display.
        detail=CabSignalDetail.None;
        return false;
    }
    private static void Prohibits(object __instance,ref bool __result)
    {
        var signal=source.GetValue(__instance,null) as Signal;
        if(!(signal?.CurrentAspect?.GetDefinition() is RuAspectDefinition definition))return;
        if(definition.Indication==Indication.Invitation)__result|=!Reserved(signal);
        if(Combined(signal)&&ShuntingReading(__instance))__result|=definition.Indication!=Indication.ShuntingWhite&&definition.Indication!=Indication.Invitation;
        else if(definition.Indication==Indication.ShuntingWhite&&!signal.IsShunting)__result=true;
    }
    private static void Capture(object __instance,object __result)
    {
        var signal=source.GetValue(__instance,null) as Signal;
        if(Combined(signal)&&White(signal)&&!ShuntingReading(__instance))
        {reservedAtPassage.SetValue(__result,false);shuntingAtPassage.SetValue(__result,false);}
        if(Combined(signal)&&ShuntingReading(__instance)&&White(signal)&&!signal.IsOff&&signal.Block!=null&&!signal.Block.ShouldBeUpdated)
            shuntingAtPassage.SetValue(__result,true);
    }
    private static void Contains(object __instance,TrackPosition<RailTrack> position,ref bool __result)
    {
        var signal=source.GetValue(__instance,null) as Signal;
        if(AutomaticBlock.TryContains(signal?.Block,position.Track,position.Span,position.Direction,out bool contains))__result=contains;
    }
}
