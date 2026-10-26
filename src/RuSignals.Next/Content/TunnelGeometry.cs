using System.Collections.Generic;
using UnityEngine;
namespace RuSignals;

// The imported head includes disconnected pole clamps and cables. Remove whole
// connected pieces, never clip triangles through a housing or its texture.
// This runs once per shared model, before releasing its CPU mesh buffers.
internal static class TunnelGeometry
{
    internal static Mesh Create(string key,string name,Mesh source,float lower,float lowestLens)
    {
        var data=new MeshPieces(source);
        bool Keep(Bounds b)
        {
            if(key=="route_pointer")return b.min.z>=-.20f;
            if(key=="distant"||key=="repeater")return b.min.z>=-.25f&&(b.min.y>=lowestLens-.18f||b.size.x>.5f);
            return b.min.z>=.30f&&b.size.x>.20f;
        }
        var kept=new List<MeshPieces.Piece>();foreach(var piece in data.Pieces)if(Keep(piece.Bounds))kept.Add(piece);
        return data.Build(key+"/"+name+" tunnel",kept,lower);
    }
}
