using RuSignals;

internal static class NormativeChecks
{
    internal static void Run(Action<string,object,object> check)
    {
        // Independent examples from ISI RF 14,16,19,23,28,34-36.
        // Extend advance warning to all straight train roles, regardless of
        // next-controller classification. Own turnout permission stays separate.
        var rows=new (SignalRole Role,bool Diverging,bool Side,bool Entry,NextPermission Next,Indication Expected)[]
        {
            (SignalRole.Through,false,false,false,NextPermission.Stop,Indication.Yellow),
            (SignalRole.Through,false,false,false,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Through,false,false,true,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Entry,false,false,false,NextPermission.Stop,Indication.Yellow),
            (SignalRole.Entry,false,false,false,NextPermission.Normal,Indication.Green),
            (SignalRole.Entry,false,false,false,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Entry,true,false,false,NextPermission.Stop,Indication.DoubleYellow),
            (SignalRole.Entry,true,false,false,NextPermission.Normal,Indication.DoubleYellowUpperFlashing),
            (SignalRole.Route,true,false,false,NextPermission.Stop,Indication.DoubleYellow),
            (SignalRole.Route,true,false,false,NextPermission.Normal,Indication.DoubleYellowUpperFlashing),
            (SignalRole.Route,false,false,false,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Exit,false,false,true,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Exit,true,false,false,NextPermission.Stop,Indication.DoubleYellow),
            (SignalRole.Exit,true,false,false,NextPermission.Normal,Indication.DoubleYellowUpperFlashing),
            (SignalRole.Exit,true,true,false,NextPermission.Stop,Indication.Yellow),
            (SignalRole.Exit,true,true,false,NextPermission.Normal,Indication.Green),
            (SignalRole.Exit,true,true,true,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Distant,false,false,false,NextPermission.Stop,Indication.Yellow),
            (SignalRole.Distant,false,false,false,NextPermission.Reduced,Indication.FlashingYellow),
            (SignalRole.Repeater,false,false,false,NextPermission.Stop,Indication.Dark),
            (SignalRole.Repeater,false,false,false,NextPermission.Reduced,Indication.Green),
        };
        for(int i=0;i<rows.Length;i++)
        {
            var r=rows[i];var s=new SignalState{BlockReady=true,ShuntingAllowed=true,Diverging=r.Diverging,DepartureFromSide=r.Side,NextIsEntry=r.Entry,Next=r.Next};
            check("ISI reference case "+i,r.Expected,SignalRules.Select(r.Role,s));
            check("Selected indication has a native model profile "+i,true,r.Expected==Indication.Dark||SignalCatalogue.Profile(r.Role).Contains(r.Expected));
        }
        var upper=SignalCatalogue.Pattern(SignalRole.Entry,Indication.DoubleYellowUpperFlashing);
        check("2Y flashing lower lens remains steady",LampMask.Yellow2,upper.Steady);
        check("2Y flashing upper lens alone blinks",LampMask.Yellow,upper.Blinking);
        var both=SignalCatalogue.Pattern(SignalRole.Entry,Indication.DoubleYellow);
        check("2Y closed-next both steady",LampMask.Yellow|LampMask.Yellow2,both.Steady);
        check("2Y closed-next neither blinks",LampMask.None,both.Blinking);
        check("Single flashing yellow has no steady lens",LampMask.None,SignalCatalogue.Pattern(SignalRole.Distant,Indication.FlashingYellow).Steady);
        check("Manoeuvre stop is blue",LampMask.Blue,SignalCatalogue.Pattern(SignalRole.Shunting,Indication.Stop).Steady);
        check("Combined exit stop stays red",LampMask.Red,SignalCatalogue.Pattern(SignalRole.Exit,Indication.Stop).Steady);
        check("Invitation retains red",LampMask.Red,SignalCatalogue.Pattern(SignalRole.Entry,Indication.Invitation).Steady);
        check("Invitation flashes only white",LampMask.MoonWhite,SignalCatalogue.Pattern(SignalRole.Entry,Indication.Invitation).Blinking);
        foreach(int fps in new[]{20,30,60,144})
        {
            double end=Math.Ceiling(SignalTiming.FadeSeconds*fps)/fps;
            check("Lamp fully lit after bounded fade at "+fps+" FPS",1f,SignalTiming.Fade(0,1,end));
            check("Lamp fully dark after bounded fade at "+fps+" FPS",0f,SignalTiming.Fade(1,0,end));
            var up=Enumerable.Range(0,(int)Math.Ceiling(end*fps)+1).Select(frame=>SignalTiming.Fade(0,1,(double)frame/fps)).ToArray();
            check("Fade is bounded and monotone at "+fps+" FPS",true,up.All(v=>v>=0&&v<=1)&&up.Zip(up.Skip(1),(a,b)=>b>=a).All(v=>v));
        }
        check("2.0.9 native blinking smoothing parameter",.15f,SignalTiming.NativeBlinkLag);
        check("ALS approaching red is yellow-red",CabIndication.YellowRed,CabDisplayRules.Select(Indication.Stop,false,true,false,false,out _));
        check("ALS shunting blue has no train code",CabIndication.White,CabDisplayRules.Select(Indication.Stop,false,true,true,false,out _));
        check("ALS crossed stop is red",CabIndication.Red,CabDisplayRules.Select(Indication.Stop,true,true,false,false,out _));
        check("ALS no reliable block is unknown",CabIndication.Unknown,CabDisplayRules.Select(Indication.Stop,false,false,false,false,out _));
        foreach(var value in new[]{Indication.Yellow,Indication.FlashingYellow,Indication.DoubleYellow,Indication.DoubleYellowUpperFlashing})
        {
            check("ALS single steady yellow for "+value,CabIndication.Yellow,CabDisplayRules.Select(value,false,true,false,false,out var detail));
            check("ALS no foreign flashing detail for "+value,CabDetail.None,detail);
        }
    }
}
