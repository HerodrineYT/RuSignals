using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Signals.Game;
using Signals.Game.Controllers;
using UnityEngine;
namespace RuSignals;
using Signal=global::Signals.Game.Signal;

// Physical labels read DVSignals identities, including its own overrides.
// Table service keys get short captions from nearby ordinary signals at world
// registration. Name lookup never scans the world; save/phone keys stay intact.
internal static class SignalDesignations
{
    private static readonly Dictionary<Signal,(string Source,string Latin)> custom=new Dictionary<Signal,(string,string)>();
    private static readonly Dictionary<Signal,(string Source,string Label)> tableNames=new Dictionary<Signal,(string,string)>();
    private static readonly Regex tableKey=new Regex(@"^T[_-]([A-Za-z0-9]+)[_-](\d+)-(\d+)$",RegexOptions.Compiled);
    private static readonly Regex numbered=new Regex(@"^[A-Za-z]+(\d+)$",RegexOptions.Compiled);
    private sealed class Reference{internal Signal Signal;internal string Station,Number;internal Vector3 Position;}
    internal static void Reset(){custom.Clear();tableNames.Clear();}
    internal static string Get(Signal signal)
    {
        string source=signal.Name;
        if(tableNames.TryGetValue(signal,out var shortened)&&shortened.Source==source)return shortened.Label;
        if(custom.TryGetValue(signal,out var cached)&&cached.Source==source)return cached.Latin;
        string latin=DesignationRules.Latin(source);custom[signal]=(source,latin);return latin;
    }
    internal static string Station(BasicSignalController controller)
    {
        if(!string.IsNullOrEmpty(controller.Group?.Station))return controller.Group.Station;
        if(StationControllerCache.TryGetStationInfo(controller,out var station))return station.Station;
        string track=controller.PlacementInfo?.Track?.name;
        if(track!=null&&track.StartsWith("[Y]",StringComparison.Ordinal))
        {int end=track.IndexOf('-',3);if(end>3)return track.Substring(3,end-3);}
        return string.Empty;
    }
    internal static void Refresh()
    {
        Reset();if(SignalManager.CurrentPack?.ModId!=Mod.Id||SignalManager.Instance==null)return;
        var signals=new List<Signal>();var references=new List<Reference>();var targets=new List<Signal>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var controller in SignalManager.Instance.AllControllers)
        {
            foreach(var signal in controller.AllSignals)
            {
                if(signal.Definition==null||signal.Definition.Aspects.Length==0||!(signal.Definition.Aspects[0] is RuAspectDefinition))continue;
                signals.Add(signal);string name=DesignationRules.Latin(signal.Name);
                if(controller is TurntableSignalController&&tableKey.IsMatch(name)){targets.Add(signal);continue;}
                used.Add(name);
                var number=numbered.Match(name);
                if(!(controller is TurntableSignalController)&&!(controller is DistantSignalController)&&number.Success)
                    references.Add(new Reference{Signal=signal,Station=Station(controller),Number=number.Groups[1].Value,Position=signal.Definition.transform.position});
            }
        }
        targets.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Name,b.Name));
        foreach(var signal in targets)
        {
            var key=tableKey.Match(signal.Name);string station=key.Groups[1].Value;Reference closest=null;int bestTier=int.MaxValue;float bestDistance=float.PositiveInfinity;
            foreach(var candidate in references)
            {
                int tier=StringComparer.OrdinalIgnoreCase.Equals(candidate.Station,station)?0:string.IsNullOrEmpty(candidate.Station)?1:2;
                if(tier==2)continue;float distance=(candidate.Position-signal.Definition.transform.position).sqrMagnitude;
                if(distance>1000000f)continue;
                if(tier<bestTier||tier==bestTier&&(distance<bestDistance||distance==bestDistance&&StringComparer.Ordinal.Compare(candidate.Signal.Name,closest?.Signal.Name)<0))
                {closest=candidate;bestTier=tier;bestDistance=distance;}
            }
            string stem=closest!=null?"T"+closest.Number:"T"+key.Groups[2].Value+"-"+key.Groups[3].Value,label=stem;int suffix=0;
            while(!used.Add(label))label=stem+DesignationRules.Suffix(suffix++);
            tableNames[signal]=(signal.Name,label);
        }
        foreach(var signal in signals)signal.UpdateDisplays(false);
    }
}
