﻿namespace SmartOrders.HarmonyPatches;

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
using System.Reflection.Emit;

using UnityEngine;
using UnityEngine.Rendering;
using static Model.Car;

[PublicAPI]
[HarmonyPatch]
public static class CarInspectorPatches
{
    // IMPORTANT: Transpiler attribute and signature must match exactly
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(CarInspector), "PopulatePanel")]
    private static IEnumerable<CodeInstruction> PopulatePanel_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
        var list = new List<CodeInstruction>(instructions);

        // UIPanelBuilder.AddTabbedPanels(UIState<string>, Action<UITabbedPanelBuilder>)
        var addTabbedPanels = AccessTools.Method(
            typeof(UIPanelBuilder),
            "AddTabbedPanels",
            new[] { typeof(UIState<string>), typeof(Action<UITabbedPanelBuilder>) });

        var wrapMethod = AccessTools.Method(typeof(CarInspectorPatches), nameof(WrapTabsAction));

        for (int i = 0; i < list.Count; i++)
        {
            var instr = list[i];

            // Just before calling AddTabbedPanels, the stack is:
            // ... builder, selectedTabState, action
            // We insert ldarg.0 (this) and call WrapTabsAction(action, this) to replace the action
            if (instr.Calls(addTabbedPanels))
            {
                // Inject: ldarg.0; call WrapTabsAction
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, wrapMethod));
                i += 2; // skip over the inserted instructions
            }
        }

        return list;
    }
    
    private static Car? GetInspectedCar(CarInspector inspector)
    {
        // Find the first instance field whose type is (assignable to) Car
        var t = inspector.GetType();
        var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var f in fields)
        {
            if (typeof(Car).IsAssignableFrom(f.FieldType))
            {
                return (Car?)f.GetValue(inspector);
            }
        }
        return null;
    }


    // Wrap the original tab-builder: run it, then append "SmartOrders"
    public static Action<UITabbedPanelBuilder> WrapTabsAction(Action<UITabbedPanelBuilder> original, CarInspector inspector)
    {
        return tabBuilder =>
        {
            original(tabBuilder);

            // Resolve the inspected car without relying on a specific private field name
            var car = GetInspectedCar(inspector);
            if (car == null)
                return;

            if (car.Archetype == CarArchetype.LocomotiveSteam || car.Archetype == CarArchetype.LocomotiveDiesel)
            {
                var persistence = new AutoEngineerPersistence(car.KeyValueObject);

                tabBuilder.AddTab(
                    "Crew",
                    "smartorders",
                    b => BuildMiscTab(b, (BaseLocomotive)car, persistence));
            }
        };
    }

    // Your existing tab content (keep minimal if you like)
    private static void BuildMiscTab(UIPanelBuilder builder, BaseLocomotive loco, AutoEngineerPersistence persistence)
    {
        BuildHandbrakeAndAirHelperButtons(builder, loco);
        BuildDisconnectCarsButtons(builder, loco, persistence);
        builder.AddExpandingVerticalSpacer();
    }

    
    private static void BuildHandbrakeAndAirHelperButtons(UIPanelBuilder builder, BaseLocomotive locomotive)
    {
        builder.ButtonStrip(strip =>
            {
                strip.Spacer(38);
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
        AutoEngineerPersistence persistence)
    {
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