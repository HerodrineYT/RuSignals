namespace SovietSemaphore { public enum SemaphoreState { Stop,Main,Diverging,Dark } }
namespace RuSignals
{
    public static class SemaphoreRules
    {
        public static SovietSemaphore.SemaphoreState Pose(SignalRole role,Indication value,bool shunting,bool twoArms)
        {
            if(value==Indication.Dark)return SovietSemaphore.SemaphoreState.Dark;
            if(role==SignalRole.Shunting||role==SignalRole.Turntable)
                return value==Indication.ShuntingWhite?SovietSemaphore.SemaphoreState.Main:SovietSemaphore.SemaphoreState.Stop;
            if(shunting||role>SignalRole.Route)return SovietSemaphore.SemaphoreState.Stop;
            var permission=SignalRules.Permission(value);
            if(permission==NextPermission.Normal)return SovietSemaphore.SemaphoreState.Main;
            return permission==NextPermission.Reduced&&twoArms?SovietSemaphore.SemaphoreState.Diverging:SovietSemaphore.SemaphoreState.Stop;
        }
    }
}
