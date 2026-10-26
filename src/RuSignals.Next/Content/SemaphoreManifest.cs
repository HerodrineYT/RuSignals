using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
namespace SovietSemaphore;
internal sealed class SemaphorePart
{
    public string key=null,mesh=null,parent=null;public float[] pivot=null;
}
internal sealed class SemaphoreManifest
{
    public SemaphorePart[] parts=null;
    internal static SemaphorePart[] Read(string source)
    {
        var parsed=JsonConvert.DeserializeObject<SemaphoreManifest>(source,new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None});
        if(parsed?.parts==null||parsed.parts.Length==0||parsed.parts.Length>64)throw new InvalidDataException("Missing semaphore parts");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in parsed.parts)
        {
            if(p==null||string.IsNullOrEmpty(p.key)||string.IsNullOrEmpty(p.mesh)||string.IsNullOrEmpty(p.parent)||p.pivot==null||p.pivot.Length!=3||!names.Add(p.key)||Path.GetFileName(p.key)!=p.key||p.key.IndexOfAny(new[]{'/', '\\', ':'})>=0)throw new InvalidDataException("Invalid semaphore part");
            foreach(float f in p.pivot)if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidDataException("Invalid pivot");
        }
        return parsed.parts;
    }
}
