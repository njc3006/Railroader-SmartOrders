using UI.CompanyWindow;

namespace SmartOrders.HarmonyPatches;

using HarmonyLib;
using JetBrains.Annotations;
using System;
using UI.Builder;
using UI.TabView;

[PublicAPI]
[HarmonyPatch(typeof(TabView), nameof(TabView.FinishedAddingTabs))]
public static class TabView_AddMaintenanceTab_Patch
{
    static void Prefix(TabView __instance)
    {
        // Only inject into the CompanyWindow's TabView, not any other window
        // We check by looking for the "equipment" tab which is unique to CompanyWindow
        var tabIdsField = AccessTools.Field(typeof(TabView), "_tabIds");
        var tabIds = (System.Collections.Generic.List<string>)tabIdsField.GetValue(__instance);

        if (!tabIds.Contains("equipment"))
            return;

        if (tabIds.Contains("maintenance"))
            return;

        __instance.AddTab(
            "Maintenance",
            "maintenance",
            (Action<UIPanelBuilder>)(b =>
                EquipmentMaintenancePanelBuilder.Build(b, MaintenanceTabState.FilterState)));
    }
}

public static class MaintenanceTabState
{
    public static readonly UIState<EquipmentMaintenancePanelBuilder.EquipmentFilter> FilterState =
        new UIState<EquipmentMaintenancePanelBuilder.EquipmentFilter>(
            EquipmentMaintenancePanelBuilder.EquipmentFilter.Locomotives);
}