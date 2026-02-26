using SmartOrders.Extensions;
using UI;

namespace SmartOrders.HarmonyPatches;

using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using Model;
using Model.AI;
using RollingStock;
using UI.ContextMenu;
using RRContextMenu = UI.ContextMenu.ContextMenu;

[PublicAPI]
[HarmonyPatch]
public static class CarContextMenuPatches
{
    private static Car? _pendingCar;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CarPickable), "HandleShowContextMenu")]
    private static void HandleShowContextMenu_Prefix(Car car)
    {
        _pendingCar = car;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RRContextMenu), "Show")]
    private static void Show_Prefix(RRContextMenu __instance)
    {
        if (_pendingCar == null)
            return;

        var car = _pendingCar;
        _pendingCar = null;

        if (car is BaseLocomotive loco)
        {
            var cars = loco.EnumerateCoupled().ToList();

            __instance.AddButton(
                ContextMenuQuadrant.General,
                "Oil Train",
                SpriteName.Inspect,
                () => SmartOrdersUtility.OilTheTrain(loco));

            if (cars.Any(c => c.EndAirSystemIssue()))
            {
                __instance.AddButton(
                    ContextMenuQuadrant.General,
                    "Fix Air",
                    SpriteName.Bleed,
                    () => SmartOrdersUtility.ConnectAir(loco));
            }

            if (cars.Any(c => c.air!.handbrakeApplied))
            {
                __instance.AddButton(
                    ContextMenuQuadrant.Brakes,
                    "Release Handbrakes",
                    SpriteName.Handbrake,
                    () => SmartOrdersUtility.ReleaseAllHandbrakes(loco));
            }
        }
    }
}