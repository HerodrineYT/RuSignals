using System.Text.Json;
using System.Text;
using RuSignals;

internal static class MastChecks
{
    internal static Dictionary<string,object> Run(string assets, Action<string,bool> check)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(assets,"conversion-report.json")));
        var profiles=new Dictionary<string,object>();
        foreach(var model in json.RootElement.GetProperty("models").EnumerateObject())
        {
            var m=model.Value;float depth=-m.GetProperty("placement_offset_y").GetSingle();
            if(depth<.5f)continue;
            float top=m.GetProperty("max")[1].GetSingle();
            var lenses=m.GetProperty("lenses").EnumerateArray().ToArray();
            float lowest=lenses.Min(l=>l.GetProperty("position")[1].GetSingle());
            var lensNames=lenses.Select(l=>l.GetProperty("name").GetString()).ToHashSet();
            bool board=RouteBoardRules.ModelHasBoard(model.Name);
            float lower=ClearanceRules.Lowering(top,lowest,depth,board), bottom=lowest-.55f;
            check(model.Name+" compact pole stays upright",ClearanceRules.ShaftScale(bottom,lower)>0);
            check(model.Name+" lowest compact lens accessible",lowest-depth-lower>=.84999f);
            check(model.Name+" compact top below normal",top-depth-lower<top-depth);
            bool foundation=true, rigidLenses=true, ordered=true;
            using var r=new BinaryReader(File.OpenRead(Path.Combine(assets,model.Name+".rsm")));
            if(Encoding.ASCII.GetString(r.ReadBytes(8))!="RUSMESH2")throw new Exception("Invalid mesh header");
            int parts=r.ReadInt32();
            for(int p=0;p<parts;p++)
            {
                string name=Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                r.ReadBytes(r.ReadInt32());r.ReadInt32();int vertices=r.ReadInt32(),indices=r.ReadInt32();
                for(int v=0;v<vertices;v++)
                {
                    r.ReadSingle();float y=r.ReadSingle();r.ReadSingle();
                    for(int i=0;i<5;i++)r.ReadSingle();
                    float compact=ClearanceRules.Height(y,bottom,lower);
                    foundation &= y>ClearanceRules.FoundationTop || compact==y;
                    rigidLenses &= !lensNames.Contains(name) || Math.Abs(compact-(y-lower))<.00001f;
                    ordered &= float.IsFinite(compact) && compact>=-.00001f && compact<=y+.00001f;
                }
                r.ReadBytes(indices*4);
            }
            check(model.Name+" foundation unchanged",foundation);
            check(model.Name+" all lens vertices translate rigidly",rigidLenses);
            check(model.Name+" no vertex folds below ground",ordered);
            if(board)
            {
                using var rBoard=new BinaryReader(File.OpenRead(Path.Combine(assets,"route_pointer.rsm")));
                rBoard.ReadBytes(8);int boardParts=rBoard.ReadInt32();
                float minY=float.PositiveInfinity,maxY=float.NegativeInfinity,maxX=0;
                for(int p=0;p<boardParts;p++)
                {
                    rBoard.ReadBytes(rBoard.ReadInt32());rBoard.ReadBytes(rBoard.ReadInt32());rBoard.ReadInt32();
                    int vertices=rBoard.ReadInt32(),indices=rBoard.ReadInt32();
                    for(int v=0;v<vertices;v++)
                    {
                        maxX=Math.Max(maxX,Math.Abs(rBoard.ReadSingle()));float y=rBoard.ReadSingle();
                        minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                        for(int n=0;n<6;n++)rBoard.ReadSingle();
                    }
                    rBoard.ReadBytes(indices*4);
                }
                float s=RouteBoardRules.CompactScale,dy=RouteBoardRules.OffsetY(lowest,lower,s);
                check(model.Name+" full compact board above rail",minY*s+dy-depth>=.199f);
                check(model.Name+" compact board below all heads",maxY*s+dy<=bottom-lower-RouteBoardRules.HeadGap+.0001f);
                check(model.Name+" compact board stays at pole instead of train envelope",maxX*s<.25f);
                check(model.Name+" normal board below all heads",maxY+RouteBoardRules.OffsetY(lowest,0,1)<=bottom-RouteBoardRules.HeadGap+.0001f);
            }
            profiles[model.Name]=new {lowering=lower,head_bottom=bottom,depth,normal_top=top-depth,compact_top=top-depth-lower,
                board_fitted=board,board_scale=RouteBoardRules.CompactScale,
                board_normal_y=RouteBoardRules.OffsetY(lowest,0,1),board_normal_z=RouteBoardRules.OffsetZ(1),
                board_compact_y=RouteBoardRules.OffsetY(lowest,lower,RouteBoardRules.CompactScale),board_compact_z=RouteBoardRules.OffsetZ(RouteBoardRules.CompactScale)};
        }
        return profiles;
    }
}
