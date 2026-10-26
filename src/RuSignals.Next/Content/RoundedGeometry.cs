namespace RuSignals;
// Original rounded RUS heads are imported offline and shared by all instances.
internal static class RoundedGeometry
{
    internal static bool Available(string key)=>key=="shunting_mast"||key=="mast"||key=="entry"||key=="junction"||key=="exit_small_two_yellows";
}
