using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static int hoverSlots,hoverChecks;
    private static Sprite sharedHoverNameSprite;
    private static void ForeignHoverNameChecks(Assembly sdk,Assembly common)
    {
        var type=sdk.GetType("Signals.Game.Signal",true);
        var signal=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        type.GetField("NameOverride",All).SetValue(signal,"Foreign123");
        Check((string)type.GetProperty("DisplayText").GetValue(signal,null)==" ","Foreign or incomplete signals retain the native hover caption");
        var go=new GameObject("Foreign hover definition");
        try
        {
            var definition=go.AddComponent(common.GetType("Signals.Common.SignalDefinition",true));
            type.GetProperty("Definition").GetSetMethod(true).Invoke(signal,new object[]{definition});
            Check((string)type.GetProperty("DisplayText").GetValue(signal,null)==" ","Other packs' hover text is unchanged");
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
    private static void WriteHoverNameReport(string output)
    {
        Check(hoverSlots==35,"All 35 native slots export their names to Alt hover");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"hover-name-verification.json"),"{\"native_slots\":"+hoverSlots+",\"checks\":"+hoverChecks+",\"foreign_packs_unchanged\":true,\"all_native_aspects_checked\":true,\"off_state_name_checked\":true,\"custom_names_and_latin_labels_checked\":true,\"native_sprites_preserved\":true,\"separate_name_card\":true,\"aspect_caption_blank\":true,\"shared_name_sprite\":true,\"original_name_sprite\":true,\"original_name_colour_and_order\":true,\"no_new_frame_callback\":true,\"scope\":\"Actual SDK HUD elements and original name HUD resources loaded from DVSignals signal_bundle in isolated Unity; no game map or interactive Alt cursor session\"}");
        Check(automaticEntryChecks==13,"Automatic entry and retained non-entry reservation requirements are verified in the native SDK");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"automatic-entry-verification.json"),"{\"native_route_checks\":"+automaticEntryChecks+",\"native_terminal_profiles\":2,\"entry_without_reservation\":true,\"side_tracks_and_terminals\":true,\"points_and_occupation_guards\":true,\"conflicting_reservations_rejected\":true,\"other_roles_retain_requirement\":true,\"no_implicit_reservation\":true,\"scope\":\"Actual SDK route and terminal fixtures in Unity plus pure adapter checks; no game save, SM map or cab ride loaded\"}");
    }
    private static void HoverNameChecks(Assembly assembly,object controller,string slot)
    {
        int start=passed;
        var signals=(IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null);
        foreach(var signal in signals)
        {
            var type=signal.GetType();var name=type.GetField("NameOverride",All);
            var original=(string)name.GetValue(signal);int aspect=(int)type.GetProperty("CurrentAspectIndex").GetValue(signal,null);
            var names=assembly.GetType("RuSignals.SignalDesignations",true);
            Func<object[]> hud=()=>((IEnumerable)Call(signal,"GetAllHudElements")).Cast<object>().ToArray();
            Func<object,string> text=e=>(string)e.GetType().GetProperty("DisplayText").GetValue(e,null);
            Func<object,Sprite> sprite=e=>(Sprite)e.GetType().GetProperty("Sprite").GetValue(e,null);
            Func<object> nameCard=()=>hud().Single(e=>e.GetType().Name=="NameplateDisplay");
            Func<string> caption=()=>text(nameCard());
            try
            {
                name.SetValue(signal,"S715");
                Call(signal,"UpdateDisplays",false);
                Check(sprite(nameCard())!=null&&(bool)nameCard().GetType().GetProperty("ShouldDisplay").GetValue(nameCard(),null),slot+" name has its own visible native HUD card");
                Check(caption()=="S715",slot+" native Alt-hover name card includes its signal name");
                var cardSprite=sprite(nameCard());
                if(sharedHoverNameSprite==null)sharedHoverNameSprite=cardSprite;
                Check(ReferenceEquals(sharedHoverNameSprite,cardSprite),slot+" every signal shares one name-card sprite");
                Check(ReferenceEquals(cardSprite,Field(originalHud,"HUDSprite")),slot+" name card reuses the original DVSignals sprite");
                Check((Color)nameCard().GetType().GetProperty("TextColour").GetValue(nameCard(),null)==(Color)Field(originalHud,"HUDTextColour"),slot+" name colour matches original DVSignals");
                Check((int)nameCard().GetType().GetProperty("DisplayOrder").GetValue(nameCard(),null)==(int)Field(originalHud,"HUDDisplayOrder"),slot+" name order matches original DVSignals");
                var aspects=(Array)type.GetProperty("AllAspects").GetValue(signal,null);
                for(int i=0;i<aspects.Length;i++)
                {
                    Call(signal,"ChangeAspect",i);var elements=hud();var current=type.GetProperty("CurrentAspect").GetValue(signal,null);
                    Check(ReferenceEquals(elements[0],current),slot+" named hover retains the native current aspect");
                    Check(string.IsNullOrWhiteSpace(text(elements[0]))&&caption()=="S715",slot+" name never overlays the native aspect icon");
                    Check(!ReferenceEquals(sprite(elements[0]),cardSprite),slot+" native HUD uses distinct prefabs for aspect and name");
                    var definition=Call(current,"GetDefinition");
                    Check(ReferenceEquals(elements[0].GetType().GetProperty("Sprite").GetValue(elements[0],null),Field(definition,"HUDSprite")),slot+" native aspect icon is unchanged");
                }
                foreach(var pair in new[]{new[]{"S716","S716"},new[]{"T_MF_1-8","T_MF_1-8"},new[]{"ЧМ3","ChM3"}})
                {
                    name.SetValue(signal,pair[0]);
                    Call(signal,"UpdateDisplays",false);
                    Check(caption()==pair[1]&&caption()==(string)names.GetMethod("Get",All).Invoke(null,new[]{signal}),slot+" hover follows current names and physical Latin designations");
                }
                Call(signal,"ChangeAspect",-1);var dark=hud();
                Check(ReferenceEquals(dark[0],signal)&&string.IsNullOrWhiteSpace(text(dark[0]))&&caption()=="ChM3",slot+" a switched-off signal retains its separate name card");
                Check(ReferenceEquals(type.GetProperty("Sprite").GetValue(signal,null),Field(type.GetProperty("Definition").GetValue(signal,null),"OffStateHUDSprite")),slot+" switched-off hover icon is unchanged");
                hoverSlots++;
            }
            finally{name.SetValue(signal,original);Call(signal,"ChangeAspect",aspect);}
        }
        hoverChecks+=passed-start;
        Debug.Log("RUS_HOVER_NAME_PASS "+slot+" / "+(passed-start)+" native caption, aspect and off-state checks");
    }
}
