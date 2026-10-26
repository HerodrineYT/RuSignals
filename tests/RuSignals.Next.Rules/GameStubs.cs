// Test doubles for the DV adapter. The release build references the real game DLLs.
namespace Signals.Common { public enum CrossingCheckMode { Ignore, WholeTrack, IntersectionOnly } }
namespace Signals.Common.Aspects
{
    public class AspectBaseDefinition { public bool DisallowPassing, UsePassingSpeed; }
}
namespace Signals.Game
{
    public enum PrefabType { NotSet, Mainline, Diverging, JunctionLeft, JunctionRight, Entry, Exit, ExitPax, ExitMainline, Shunting, ShuntingMajor, ShuntingJunction, Spacing }
    public enum SignalType { Mainline, Entry, Exit, Shunting }
    public enum TrackDirection { Out, In }
    public enum SignalOperationMode { Automatic, TempOverride, SemiManual, FullManual }
    public class Signal
    {
        public bool IsShunting, IsOff, ShuntingAllowed = true, ReservedByOther, HasReservation;
        public Railway.TrackBlock Block;
        public Controllers.BasicSignalController Controller = new();
        public Aspects.IAspect CurrentAspect;
        public Aspects.IAspect[] AllAspects = Array.Empty<Aspects.IAspect>();
        public SignalDefinition Definition=new();
        public SignalOperationMode Operation;
        public int ManualOverrideAspect;
        public Controllers.BasicSignalController GetNextController() => Block?.NextController;
    }
    public class SignalDefinition { public Common.Aspects.AspectBaseDefinition[] Aspects=new[]{new Common.Aspects.AspectBaseDefinition()}; }
    public static class SignalsMod { public static Settings Settings = new(); }
    public class Settings { public bool SpecialReservation; }
    public class SignalManager
    {
        public static SignalManager Instance = new();
        public Dictionary<Railway.Junction,JunctionSignalGroup> Groups = new();
        public bool TryGetJunctionGroup(Railway.Junction junction, out JunctionSignalGroup group) => Groups.TryGetValue(junction,out group);
    }
    public class JunctionSignalGroup
    {
        public Controllers.BasicSignalController JunctionSignal, ReverseJunctionSignal;
        public Dictionary<Railway.RailTrack,Controllers.BasicSignalController> Branches = new();
        public bool TryGetControllerForTrack(Railway.RailTrack track, out Controllers.BasicSignalController controller) => Branches.TryGetValue(track,out controller);
    }
}
namespace Signals.Game.Aspects
{
    public interface IAspect
    {
        bool DisallowPassing { get; }
        Common.Aspects.AspectBaseDefinition GetDefinition();
    }
    public abstract class AspectBase<T> : IAspect where T : Common.Aspects.AspectBaseDefinition
    {
        protected AspectBase(Common.Aspects.AspectBaseDefinition def, Signal signal) { Definition=(T)def;Signal=signal; }
        public T Definition;
        public Signal Signal;
        public string Station;
        public Railway.TrackBlock Block => Signal.Block;
        public Controllers.BasicSignalController Controller => Signal.Controller;
        public bool DisallowPassing => Definition.DisallowPassing;
        public Common.Aspects.AspectBaseDefinition GetDefinition() => Definition;
        public abstract bool MeetsConditions();
    }
}
namespace Signals.Game.Controllers
{
    public class BasicSignalController
    {
        public string Station;
        public SignalType Type;
        public PrefabType PrefabType;
        public int? RequiredJunctionBranch;
        public Railway.Junction GroupJunction;
        public Signal Signal;
        public Signal GetControllerSignal() => Signal;
    }
    public class TurntableSignalController : BasicSignalController { public bool IsConnected; }
}
namespace Signals.Game.Railway
{
    public class TrackBlock
    {
        public TrackInfo[] Tracks = new[] { new TrackInfo() };
        public bool IsSelfLoop, IsDeadEnd, Occupied, ShouldBeUpdated;
        public string Station;public float Length;
        public Controllers.BasicSignalController NextController;
        public TrackBlock FullRoute;
        public bool IsOccupied(Common.CrossingCheckMode mode) => Occupied;
    }
    public class TrackInfo
    {
        public RailTrack Track = new();
        public bool IsJunctionTrack;
        public TrackDirection Direction;
    }
    public class RailTrack { public Junction inJunction; public string name; }
    public class Junction { public int selectedBranch; public List<Branch> outBranches = new(); }
    public class Branch { public RailTrack track; }
    public static class TrackReserver
    {
        public static bool IsSignalReservedByAnother(Signal s) => s.ReservedByOther;
        public static bool HasReservation(Signal s) => s.HasReservation;
    }
}
namespace RuSignals
{
    internal static class SignalDesignations { internal static string Station(Signals.Game.Controllers.BasicSignalController c)=>c.Station; }
    // The gameplay implementation is Unity/DVSignals-specific; pure rule
    // tests use the native-block fallback path.
    internal static class AutomaticBlock
    {
        public static Signals.Game.Railway.TrackBlock FullRoute(Signals.Game.Railway.TrackBlock block)=>block.FullRoute??block;
        public static bool IsSignal(Signals.Game.Signal signal) => false;
        public static bool DirectionAllowed(Signals.Game.Signal signal) => true;
    }
}
