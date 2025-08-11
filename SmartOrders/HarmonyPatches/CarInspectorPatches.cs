namespace SmartOrders.HarmonyPatches;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Messages;
using HarmonyLib;
using JetBrains.Annotations;
using Model;
using Model.AI;
using Model.Definition;
using Model.Ops;
using Network.Messages;
using SmartOrders.Extensions;
using UI.Builder;
using UI.CarInspector;
using UI.Common;
using UI.EngineControls;
using UnityEngine;
using UnityEngine.Rendering;
using static Model.Car;

[PublicAPI]
[HarmonyPatch]
public static class CarInspectorPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CarInspector), "PopulatePanel")]
    private static bool PopulatePanel(UIPanelBuilder builder, CarInspector __instance, Car ____car,
        UIState<string> ____selectedTabState, HashSet<IDisposable> ____observers)
    {
        if (!SmartOrdersPlugin.Shared!.IsEnabled)
        {
            return true;
        }

        MethodInfo TitleForCar =
            typeof(CarInspector).GetMethod("TitleForCar", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo SubtitleForCar =
            typeof(CarInspector).GetMethod("SubtitleForCar", BindingFlags.NonPublic | BindingFlags.Static);

        MethodInfo PopulateCarPanel =
            typeof(CarInspector).GetMethod("PopulateCarPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo PopulateEquipmentPanel =
            typeof(CarInspector).GetMethod("PopulateEquipmentPanel", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo PopulatePassengerCarPanel = typeof(CarInspector).GetMethod("PopulatePassengerCarPanel",
            BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo PopulateOperationsPanel = typeof(CarInspector).GetMethod("PopulateOperationsPanel",
            BindingFlags.NonPublic | BindingFlags.Instance);

        MethodInfo Rebuild = typeof(CarInspector).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance);

        AutoEngineerPersistence persistence = new AutoEngineerPersistence(____car.KeyValueObject);
        var helper = new AutoEngineerOrdersHelper(____car, persistence);

        builder.AddTitle((string)TitleForCar.Invoke(null, [____car]), (string)SubtitleForCar.Invoke(null, [____car]));
        builder.AddTabbedPanels(____selectedTabState, delegate(UITabbedPanelBuilder tabBuilder)
        {
            tabBuilder.AddTab("Car", "car", builder => PopulateCarPanel.Invoke(__instance, [builder]));
            tabBuilder.AddTab("Equipment", "equipment",
                (builder) => PopulateEquipmentPanel.Invoke(__instance, [builder]));
            if (____car.IsPassengerCar())
            {
                tabBuilder.AddTab("Passenger", "pass",
                    (builder) => PopulatePassengerCarPanel.Invoke(__instance, [builder]));
            }

            if (____car.Archetype != CarArchetype.Tender)
            {
                tabBuilder.AddTab("Operations", "ops",
                    (builder) => PopulateOperationsPanel.Invoke(__instance, [builder]));
                ____observers.Add(____car.KeyValueObject.Observe("ops.waybill",
                    delegate { Rebuild.Invoke(__instance, null); }, callInitial: false));
            }

            if (____car.Archetype == CarArchetype.LocomotiveSteam || ____car.Archetype == CarArchetype.LocomotiveDiesel)
            {
                tabBuilder.AddTab("SmartOrders", "smartorders",
                    builder => BuildMiscTab(builder, (BaseLocomotive)____car, persistence, helper));
            }
        });

        return false;
    }

    private static void BuildMiscTab(UIPanelBuilder builder, BaseLocomotive _car, AutoEngineerPersistence persistence,
        AutoEngineerOrdersHelper helper)
    {
        BuildHandbrakeAndAirHelperButtons(builder, _car);
        BuildDisconnectCarsButtons(builder, (BaseLocomotive)_car, persistence, helper);
    }

    private static void BuildHandbrakeAndAirHelperButtons(UIPanelBuilder builder, BaseLocomotive locomotive)
    {
        builder.ButtonStrip(strip =>
            {
                var cars = locomotive.EnumerateCoupled().ToList();

                if (cars.Any(c => c.air!.handbrakeApplied))
                {
                    strip.AddButton($"{TextSprites.HandbrakeWheel}", () =>
                        {
                            SmartOrdersUtility.ReleaseAllHandbrakes(locomotive);
                            strip.Rebuild();
                        })
                        .Tooltip("Release handbrakes",
                            $"Iterates over cars in this consist and releases {TextSprites.HandbrakeWheel}.");
                }

                if (cars.Any(c => c.EndAirSystemIssue()))
                {
                    strip.AddButton("Fix Air", () =>
                        {
                            SmartOrdersUtility.ConnectAir(locomotive);
                            strip.Rebuild();
                        })
                        .Tooltip("Connect Consist Air",
                            "Iterates over each car in this consist and connects gladhands and opens anglecocks.");
                }

                strip.RebuildOnInterval(5f);
            }
        );
    }

    static void BuildDisconnectCarsButtons(UIPanelBuilder builder, BaseLocomotive locomotive,
        AutoEngineerPersistence persistence, AutoEngineerOrdersHelper helper)
    {
        AutoEngineerMode mode2 = helper.Mode;
        builder.FieldLabelWidth = new float?(100f);
        builder.AddField("Uncouple groups",
            builder.ButtonStrip(delegate(UIPanelBuilder builder)
            {
                builder.AddButtonCompact("All",
                    delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, -999, persistence); }).Tooltip(
                    "Disconnect all cars with waybills from the back",
                    "Disconnect all cars with waybills from the back");

                builder
                    .AddButtonCompact("-3",
                        delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, -3, persistence); })
                    .Tooltip("Disconnect 3 Car Groups From Back",
                        "Disconnect 3 groups of cars from the back that are headed to 3 different locations");

                builder
                    .AddButtonCompact("-2",
                        delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, -2, persistence); })
                    .Tooltip("Disconnect 2 Car Groups From Back",
                        "Disconnect 2 groups of cars from the back that are headed to 2 different locations");

                builder
                    .AddButtonCompact("-1",
                        delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, -1, persistence); })
                    .Tooltip("Disconnect 1 Car Group From Back",
                        "Disconnect all cars from the back of the train headed to the same location");

                builder
                    .AddButtonCompact("1",
                        delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, 1, persistence); })
                    .Tooltip("Disconnect 1 Car Group From Front",
                        "Disconnect all cars from the front of the train headed to the same location");

                builder
                    .AddButtonCompact("2",
                        delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, 2, persistence); })
                    .Tooltip("Disconnect 2 Car Groups From Front",
                        "Disconnect 2 groups of cars from the front that are headed to 2 different locations");

                builder
                    .AddButtonCompact("3",
                        delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, 3, persistence); })
                    .Tooltip("Disconnect 3 Car Groups From Front",
                        "Disconnect 3 groups of cars from the front that are headed to 3 different locations");

                builder.AddButtonCompact("All",
                    delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, 999, persistence); }).Tooltip(
                    "Disconnect all cars with waybills from the front",
                    "Disconnect all cars with waybills from the front");
                builder.AddButtonCompact("RA",
                    delegate { SmartOrdersUtility.DisconnectCarGroups(locomotive, 998, persistence); }).Tooltip(
                    "Disconnect all cars with waybills from the front",
                    "Disconnect all cars with waybills from the front");
            }, 4).Tooltip("Disconnect Car Groups",
                "Disconnect groups of cars headed for the same location from the front (positive numbers) or the back (negative numbers) in the direction of travel"));
    }
}