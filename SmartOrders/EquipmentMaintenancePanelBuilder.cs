// EquipmentMaintenancePanelBuilder.cs

using HarmonyLib;
using Model;
using Model.Definition;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Model.Ops;
using UI.Builder;
using UI.CarInspector;
using UI.CompanyWindow;
using UnityEngine;

#nullable disable
namespace UI.CompanyWindow;

[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct EquipmentMaintenancePanelBuilder
{
    public enum EquipmentFilter
    {
        Locomotives,
        RollingStock
    }

    public static void Build(UIPanelBuilder builder, UIState<EquipmentFilter> filterState)
    {
        builder.ButtonStrip((Action<UIPanelBuilder>)(strip =>
        {
            strip.AddButtonSelectable("Locomotives",
                filterState.Value == EquipmentFilter.Locomotives,
                (Action)(() =>
                {
                    filterState.Value = EquipmentFilter.Locomotives;
                    builder.Rebuild();
                }));

            strip.AddButtonSelectable("Rolling Stock",
                filterState.Value == EquipmentFilter.RollingStock,
                (Action)(() =>
                {
                    filterState.Value = EquipmentFilter.RollingStock;
                    builder.Rebuild();
                }));
        }));

        builder.Spacer(4f);

        List<Car> cars = GetFilteredCars(filterState.Value);
        bool isLocomotives = filterState.Value == EquipmentFilter.Locomotives;

        if (cars.Count == 0)
        {
            builder.AddLabel(isLocomotives ? "No locomotives owned." : "No rolling stock owned.");
            return;
        }

        // Header row
        builder.HStack((Action<UIPanelBuilder>)(header =>
        {
            header.AddLabel("<b>Reporting #</b>", t => t.GetComponent<UnityEngine.RectTransform>().Width(140f));
            header.AddLabel("<b>Condition</b>", t => t.GetComponent<UnityEngine.RectTransform>().Width(100f));
            if (isLocomotives)
                header.AddLabel("<b>Fuel / Resources</b>", t => t.GetComponent<UnityEngine.RectTransform>().Width(180f));
            header.AddLabel("<b>Overhaul</b>", t => t.GetComponent<UnityEngine.RectTransform>().Width(100f));
            header.AddLabel("<b></b>", t => t.GetComponent<UnityEngine.RectTransform>().Width(80f)); // Inspect button spacer
            header.AddLabel("<b>Repair Destination</b>", t =>
            {
                var layout = t.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                layout.flexibleWidth = 1f;
            });
        }));

        builder.AddHRule();

        // Scrollable data rows
        builder.VScrollView((Action<UIPanelBuilder>)(scroll =>
        {
            foreach (Car car in cars)
            {
                Car localCar = car;
                scroll.HStack((Action<UIPanelBuilder>)(row =>
                {
                    row.AddLabel(localCar.DisplayName, t => t.GetComponent<UnityEngine.RectTransform>().Width(140f));

                    float condition = localCar.Condition;
                    string conditionStr = $"{condition * 100f:F0}%";
                    Color conditionColor = GetConditionColor(condition);
                    row.AddLabel(
                        $"<color=#{UnityEngine.ColorUtility.ToHtmlStringRGB(conditionColor)}>{conditionStr}</color>",
                        t => t.GetComponent<UnityEngine.RectTransform>().Width(100f));

                    if (isLocomotives)
                    {
                        string resourceStr = GetLocomotiveResourceString(localCar);
                        row.AddLabel(resourceStr, t => t.GetComponent<UnityEngine.RectTransform>().Width(180f));
                        Log.Warning($"car {localCar.DisplayName} keyvalueobject: {localCar.KeyValueObject}");
                    }

                    row.AddLabel(GetOverhaulString(localCar), t => t.GetComponent<UnityEngine.RectTransform>().Width(100f));
                    row.AddButtonCompact("Inspect", (Action)(() => UI.CarInspector.CarInspector.Show(localCar))).Width(70f);
                    var repairLabel = row.AddLabel(
                        (Func<string>)(() => GetRepairDestination(localCar)),
                        UIPanelBuilder.Frequency.Periodic); ;
                    var repairLayout = repairLabel.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    repairLayout.flexibleWidth = 1f;
                    repairLayout.minWidth = 50f;
                }));

                scroll.Spacer(2f);
            }
        }));
    }
    
    private static List<Car> GetFilteredCars(EquipmentFilter filter)
    {
        IEnumerable<Car> ownedCars = TrainController.Shared.Cars
            .Where(c => c.IsOwnedByPlayer && c.Archetype != CarArchetype.Tender);

        if (filter == EquipmentFilter.Locomotives)
        {
            return ownedCars
                .Where(c => c.Archetype == CarArchetype.LocomotiveDiesel ||
                            c.Archetype == CarArchetype.LocomotiveSteam)
                .OrderBy(c => c.Condition)
                .ToList();
        }
        else
        {
            return ownedCars
                .Where(c => c.Archetype != CarArchetype.LocomotiveDiesel &&
                            c.Archetype != CarArchetype.LocomotiveSteam)
                .OrderBy(c => c.Condition)
                .ToList();
        }
    }

    private static Color GetConditionColor(float condition)
    {
        float pct = condition * 100f;

        if (pct >= 95f)
            return Color.green;
        if (pct >= 85f)
            return Color.yellow;

        // Below 85 - lerp from red to orange
        return Color.Lerp(Color.red, new Color(1f, 0.5f, 0f), pct / 85f);
    }
    
    private static string GetOverhaulString(Car car)
    {
        try
        {
            float milesSinceOverhaul = (car.OdometerService - car.LastOverhaulOdometer) * 0.6213712f;
            float milesRemaining = Car.OverhaulMiles - milesSinceOverhaul;
            milesRemaining = Mathf.Max(0f, milesRemaining);

            string hex = ColorUtility.ToHtmlStringRGB(GetConditionColor(milesRemaining / Car.OverhaulMiles));
            return $"<color=#{hex}>{milesRemaining:F0} mi</color>";
        }
        catch (Exception ex)
        {
            Log.Warning("EquipmentMaintenance: could not read overhaul for {car}: {ex}", car.DisplayName, ex.Message);
            return "N/A";
        }
    }
    
    private static string GetRepairDestination(Car car)
    {
        try
        {
            (OpsCarPosition position, string tag)? result;
            if (!car.TryGetOverrideDestination(OverrideDestination.Repair, (IOpsCarPositionResolver)OpsController.Shared, out result))
                return "None";

            OpsCarPosition position = result.Value.position;
            string tag = result.Value.tag;

            // Resolve the position to a displayable name
            OpsCarPositionDisplayable displayable = new OpsCarPositionDisplayable(position);
            string name = displayable.DisplayName;

            if (tag == "overhaul")
                name += " (Overhaul)";

            return name;
        }
        catch (Exception ex)
        {
            Log.Warning("EquipmentMaintenance: could not read repair destination for {car}: {ex}", car.DisplayName, ex.Message);
            return "None";
        }
    }
    
    private static string ColorizeResource(float quantity, float max, string unit)
    {
        if (max <= 0f) return $"{quantity:F0}/{max:F0} {unit}";
    
        float pct = (quantity / max) * 100f;
        string hex;
    
        if (pct <= 30f)
            hex = ColorUtility.ToHtmlStringRGB(Color.red);
        else if (pct <= 59f)
            hex = ColorUtility.ToHtmlStringRGB(Color.yellow);
        else
            hex = ColorUtility.ToHtmlStringRGB(Color.green);

        return $"<color=#{hex}>{quantity:F0}/{max:F0}</color> {unit}";
    }

    private static string GetLocomotiveResourceString(Car car)
{
    try
    {
        if (car.Archetype == CarArchetype.LocomotiveDiesel)
        {
            int fuelSlot = car.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == "diesel-fuel");
            if (fuelSlot < 0) fuelSlot = 0;
            CarLoadInfo? loadInfo = car.GetLoadInfo(fuelSlot);
            float quantity = loadInfo.HasValue ? loadInfo.GetValueOrDefault().Quantity : 0f;
            float fuelMax = car.Definition.LoadSlots[fuelSlot].MaximumCapacity;
            return ColorizeResource(quantity, fuelMax, "gal");
        }

        if (car.Archetype == CarArchetype.LocomotiveSteam)
        {
            // First check if the loco itself has coal/water slots (tank engines)
            int coalSlot = car.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == "coal");
            int waterSlot = car.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == "water");

            // If not found on loco, check the coupled tender
            Car sourcecar = car;
            if (coalSlot < 0 || waterSlot < 0)
            {
                Car tender = car.EnumerateCoupled()
                    .FirstOrDefault(c => c.Archetype == CarArchetype.Tender);

                if (tender == null)
                    return "No tender coupled";

                sourcecar = tender;
                coalSlot = tender.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == "coal");
                waterSlot = tender.Definition.LoadSlots.FindIndex(s => s.RequiredLoadIdentifier == "water");
            }

            float coal = 0f, water = 0f, coalMax = 0f, waterMax = 0f;

            if (coalSlot >= 0)
            {
                CarLoadInfo? coalInfo = sourcecar.GetLoadInfo(coalSlot);
                coal = coalInfo.HasValue ? coalInfo.GetValueOrDefault().Quantity : 0f;
                coalMax = sourcecar.Definition.LoadSlots[coalSlot].MaximumCapacity;
            }

            if (waterSlot >= 0)
            {
                CarLoadInfo? waterInfo = sourcecar.GetLoadInfo(waterSlot);
                water = waterInfo.HasValue ? waterInfo.GetValueOrDefault().Quantity : 0f;
                waterMax = sourcecar.Definition.LoadSlots[waterSlot].MaximumCapacity;
            }

            return $"{ColorizeResource(water, waterMax, "gal (W)")}  {ColorizeResource(coal, coalMax, "T (C)")}";
        }
    }
    catch (Exception ex)
    {
        Log.Warning("EquipmentMaintenance: could not read resources for {car}: {ex}", car.DisplayName, ex.Message);
    }

    return string.Empty;
}
}