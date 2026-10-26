using UnityEngine;
namespace RuSignals;
internal sealed class LensSpec
{
    internal readonly string Name;internal readonly Vector3 Position;
    internal LensSpec(string name,float x,float y,float z){Name=name;Position=new Vector3(x,y,z);}
}
internal sealed class ModelSpec
{
    internal readonly string Key;internal readonly float InstallationDepth;internal readonly LensSpec[] Lenses;
    internal ModelSpec(string key,float depth,params LensSpec[] lenses){Key=key;InstallationDepth=depth;Lenses=lenses;}
}
