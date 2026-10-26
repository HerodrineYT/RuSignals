namespace RuSignals;
public enum CabIndication { Unknown,White,Green,Yellow,YellowRed,Red }
public enum CabDetail { None,ExpectRestricted,RestrictedClear,RestrictedStop,RestrictedEntry }
public static class CabDisplayRules
{
    public static CabIndication Select(Indication value,bool prohibited,bool ready,bool shunting,bool reserved,out CabDetail detail)
    {
        detail=CabDetail.None;
        if(prohibited)return CabIndication.Red;
        // ISI RF 36: yellow-red on approach to red; red after an unauthorised
        // passage. Shunting signals do not transmit a train ALS code.
        if(!ready)return CabIndication.Unknown;
        if(value==Indication.Stop)return shunting?CabIndication.White:CabIndication.YellowRed;
        if(value==Indication.Invitation)return reserved?CabIndication.YellowRed:CabIndication.Red;
        if(value==Indication.ShuntingWhite)return shunting?CabIndication.White:CabIndication.Red;
        if(value==Indication.Green)return CabIndication.Green;
        switch(value)
        {
            case Indication.Yellow:case Indication.FlashingYellow:
            case Indication.DoubleYellow:case Indication.DoubleYellowUpperFlashing:return CabIndication.Yellow;
            default:return CabIndication.Unknown;
        }
    }
}
