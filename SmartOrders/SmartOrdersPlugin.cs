namespace SmartOrders;

using System;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using Railloader;
using Serilog;
using UI.Builder;

[UsedImplicitly]
public sealed class SmartOrdersPlugin : SingletonPluginBase<SmartOrdersPlugin>, IModTabHandler
{

    public static IModdingContext Context { get; private set; } = null!;
    public static IUIHelper UiHelper { get; private set; } = null!;
    public static Settings Settings { get; private set; }  = null!;

    private readonly ILogger _Logger = Log.ForContext<SmartOrdersPlugin>()!;

    public SmartOrdersPlugin(IModdingContext context, IUIHelper uiHelper)
    {
        Context = context;
        UiHelper = uiHelper;

        Settings = Context.LoadSettingsData<Settings>("SmartOrders") ?? new Settings();
    }

    public override void OnEnable()
    {
        _Logger.Information("OnEnable");
        var harmony = new Harmony("SmartOrders");
        harmony.PatchAll();
    }

    public override void OnDisable()
    {
        _Logger.Information("OnDisable");
        var harmony = new Harmony("SmartOrders");
        harmony.UnpatchAll();
    }

    public void ModTabDidOpen(UIPanelBuilder builder) {
        var lengths = Enum.GetNames(typeof(MeasureType)).ToList();
        builder.AddField("Measure distance in", builder.AddDropdown(lengths, (int)Settings.MeasureType, o => {
            Settings.MeasureType = (MeasureType)o;
            builder.Rebuild();
        })!);

        builder.AddField("Apply handbrakes", builder.AddToggle(() => Settings.AutoApplyHandbrake, o => Settings.AutoApplyHandbrake = o)!)
                .Tooltip("Apply handbrakes", "When decoupling stationary cars, set the handbrake in the first car");

        builder.AddField("Release handbrakes", builder.AddToggle(() => Settings.AutoReleaseHandbrake, o => Settings.AutoReleaseHandbrake = o)!)
                .Tooltip("Release handbrakes", "In Yard mode, automatically release the handbrakes for any cars in the train before moving");

        builder.AddField("Couple air", builder.AddToggle(() => Settings.AutoCoupleAir, o => Settings.AutoCoupleAir = o)!)
                .Tooltip("Couple air", "In Yard mode, automatically couple air and open anglecocks for any cars in the train before moving");

        builder.AddField("Remove Yard Mode Speed Limit", builder.AddToggle(() => Settings.NoYardSpeedLimit, o => Settings.NoYardSpeedLimit = o)!)
                .Tooltip("Remove Yard Mode Speed Limit", "Remove 15mph speed limit in yard mode");

        builder.AddField("Send debug logs to console", builder.AddToggle(() => Settings.EnableDebug, o => Settings.EnableDebug = o)!);
    }

    public void ModTabDidClose()
    {
        Context.SaveSettingsData("SmartOrders", Settings);
    }

}