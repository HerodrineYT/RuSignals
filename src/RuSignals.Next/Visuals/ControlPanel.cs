using Signals.Game;
using UnityEngine;
using UnityModManagerNet;
namespace RuSignals;
internal static class ControlPanel
{
    private static string id="";
    internal static void Draw(UnityModManager.ModEntry _)
    {
        GUILayout.Label("RuSignals — автоматическая сигнализация");
        if(!SignalManager.Running){GUILayout.Label("Загрузите карту. Набор выбирается в DVSignals: ru.signals.");return;}
        GUILayout.Label("Проходных АБ: "+AutomaticBlock.SignalCount+". Интервалы 400–1000 м.");
        GUILayout.Label(SignalsMod.Settings.SpecialReservation?"DVSignals требует запроса маршрута телефоном или рацией.":"Обычные поездные маршруты открываются автоматически; телефон позволяет резервировать маршрут.");
        if(MultiplayerIntegration.IsMpActive&&!MultiplayerIntegration.IsHost){GUILayout.Label("Ручное управление доступно ведущему сессии.");return;}
        GUILayout.BeginHorizontal();GUILayout.Label("ID светофора:",GUILayout.Width(125));id=GUILayout.TextField(id,GUILayout.Width(100));GUILayout.EndHorizontal();
        if(!int.TryParse(id,out int number)||!SignalManager.Instance.TryGetSignal(number,out var signal)||signal.AllAspects.Length==0||!(signal.AllAspects[0].GetDefinition() is RuAspectDefinition definition))return;
        GUILayout.Label(signal.Name+" / "+(signal.CurrentAspect?.Id??"погашен"));
        var shape=signal.Controller.Definition.GetComponent<ShieldView>();
        if(shape!=null){GUILayout.BeginHorizontal();if(GUILayout.Button("Щит: исходный RuSignals"))shape.SetOval(false);if(GUILayout.Button("Щит: округлый"))shape.SetOval(true);GUILayout.EndHorizontal();}
        var fit=signal.Controller.Definition.GetComponent<ClearanceView>();
        if(fit!=null){GUILayout.BeginHorizontal();if(GUILayout.Button("Крепление: авто"))fit.SetMode(MastPlacementMode.Automatic);if(GUILayout.Button("Укороченный вариант"))fit.SetMode(MastPlacementMode.Compact);if(GUILayout.Button("Обычная опора"))fit.SetMode(MastPlacementMode.Original);GUILayout.EndHorizontal();if(fit.IsWallMounted)GUILayout.Label("Туннель: консоль на стене.");}
        GUILayout.BeginHorizontal();
        int invitation=-1;for(int i=0;i<signal.AllAspects.Length;i++)if(signal.AllAspects[i].GetDefinition() is RuAspectDefinition a&&a.Indication==Indication.Invitation)invitation=i;
        if(invitation>=0&&GUILayout.Button("Пригласительный · 20 км/ч")){signal.SetAspectOverride(invitation);signal.ChangeOperationMode(SignalOperationMode.FullManual);signal.Controller.Update(true,true);}
        if((definition.Role==SignalRole.Exit||definition.Role==SignalRole.Route)&&GUILayout.Button("Маневры · 25 км/ч")){signal.SetAspectOverride(-1);signal.ChangeOperationMode(SignalOperationMode.SemiManual);signal.Controller.Update(true,true);}
        if(GUILayout.Button("Автоматически")){signal.ChangeOperationMode(SignalOperationMode.Automatic);signal.Controller.Update(true,true);}
        GUILayout.EndHorizontal();GUILayout.Label("Пригласительный принудительный; после проследования верните автоматический режим. Настройка опоры действует до перезагрузки карты.");
    }
}
