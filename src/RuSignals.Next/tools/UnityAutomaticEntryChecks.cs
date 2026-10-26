using System;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static int automaticEntryChecks;
    private static void AutomaticEntryChecks(Assembly sdk,Assembly assembly,object signal,Component track,Component junction,string role,Action<string> nextAspect,Func<object,string> indication)
    {
        if(role!="Entry"&&role!="Exit"&&role!="Route")return;
        int start=passed;
        var settings=sdk.GetType("Signals.Game.SignalsMod",true).GetProperty("Settings",All).GetValue(null,null);
        var option=settings.GetType().GetField("SpecialReservation");bool saved=(bool)option.GetValue(settings);
        var manual=assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ManualOccupancy",All).GetValue(null,null);
        var reserver=sdk.GetType("Signals.Game.Railway.TrackReserver",true);
        Action<string,string> expect=(expected,label)=>{Call(signal,"UpdateAspect",true);Check(indication(signal)==expected,role+" / "+label);};
        try
        {
            option.SetValue(settings,true);track.name="[track through]";nextAspect("Green");
            if(role!="Entry")
            {
                expect("Stop","original reservation requirement is retained for non-entry signals");return;
            }
            expect("Green","straight entry and its open next entry need no own reservations");
            Check(!(bool)reserver.GetMethod("HasReservation",new[]{signal.GetType()}).Invoke(null,new[]{signal}),"Automatic entry does not acquire a reservation as a side effect");
            signal.GetType().GetProperty("IsShunting").GetSetMethod(true).Invoke(signal,new object[]{true});expect("Stop","entry shunting still requires an explicit reservation");signal.GetType().GetProperty("IsShunting").GetSetMethod(true).Invoke(signal,new object[]{false});
            track.name="[track diverging]";expect("DoubleYellowUpperFlashing","unreserved side route opens with upper flashing yellow");
            nextAspect("Stop");expect("DoubleYellow","unreserved side route before a closed home shows two yellows");nextAspect("Green");
            junction.GetType().GetField("selectedBranch").SetValue(junction,(byte)1);expect("Stop","unreserved entry never overrides misaligned points");junction.GetType().GetField("selectedBranch").SetValue(junction,(byte)0);
            Call(manual,"Add",track);expect("Stop","unreserved entry never overrides occupied track");Call(manual,"Remove",track);
            expect("DoubleYellowUpperFlashing","clearing the receiving track restores the unreserved side entry");
        }
        finally
        {
            option.SetValue(settings,saved);Call(manual,"Remove",track);track.name="[track through]";junction.GetType().GetField("selectedBranch").SetValue(junction,(byte)0);
            automaticEntryChecks+=passed-start;
        }
    }
}
