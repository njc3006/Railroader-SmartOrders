namespace SmartOrders.HarmonyPatches;

using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Track.Signals;
using RollingStock.ContinuousControls;
using UnityEngine;

[PublicAPI]
[HarmonyPatch]
public static class CTCPanelKnobPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CTCPanelKnob), "Awake")]
    private static void Awake_Postfix(CTCPanelKnob __instance)
    {
        __instance.gameObject.AddComponent<CTCKnobRightClickHandler>().Knob = __instance;
    }
}

public class CTCKnobRightClickHandler : MonoBehaviour
{
    public CTCPanelKnob Knob { get; set; }

    private static readonly FieldInfo ControlField =
        AccessTools.Field(typeof(CTCPanelKnob), "_control");

    private static readonly FieldInfo OnValueChangedField =
        AccessTools.Field(typeof(ContinuousControl), "OnValueChanged");

    private static readonly int ClickableLayer = 1 << ObjectPicker.LayerClickable;

    private void Update()
    {
        if (!Input.GetMouseButtonDown(1))
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 10f, ClickableLayer))
            return;

        if (hit.collider.gameObject != gameObject)
            return;

        FlipKnob();
    }

    private void FlipKnob()
    {
        var control = (RadialAnimatedControl)ControlField.GetValue(Knob);

        float newValue;
        switch (Knob.purpose)
        {
            case CTCPanelKnob.Purpose.Switch:
                newValue = Knob.CurrentSwitchSetting == SwitchSetting.Normal ? 1f : 0f;
                break;

            case CTCPanelKnob.Purpose.Signal:
                newValue = Knob.CurrentDirection switch
                {
                    SignalDirection.Left  => 0.5f,
                    SignalDirection.None  => 1f,
                    SignalDirection.Right => 0f,
                    _                    => 0f
                };
                break;

            default:
                return;
        }

        control.Value = newValue;

        var onValueChanged = OnValueChangedField.GetValue(control) as System.Action<float>;
        onValueChanged?.Invoke(newValue);
    }
}