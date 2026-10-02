using System.Reflection;

using BaseLib.Config;

using Godot;

using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace HeartShake.HeartShakeCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "HeartShake"; // res://HeartShake and id prefix HEARTSHAKE-
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Log { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        // Settings -> Mod Settings page (BaseLib auto-UI): master toggle + sound toggle.
        // Config registration stays outside the master-switch gate so the settings
        // page remains reachable to turn the mod back on.
        ModConfigRegistry.Register(ModId, new HeartShakeConfig());
        // BaseLib SimpleLoc for settings labels (eng + zhs).
        BaseLib.Patches.Localization.SimpleLoc.EnableSimpleLoc(ModId);
        // Register C# scripts referenced by scenes shipped in the .pck (none yet,
        // but the heartbeat ogg is loaded through ResPath).
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());

        // Master-switch contract (F08, 2026-10-02): when EnableHeartShake is off at
        // mod-init time, install NO HeartShake behavior Harmony patch - neither the
        // heartbeat room patch nor the Beat of Death redirect. (The BaseLib SimpleLoc
        // registration above is loc plumbing, not a gameplay behavior patch.) Runtime entries
        // (HeartBeatNode.Attach and BeatOfDeathRedirectPatch.Prefix) still re-check
        // the live property: turning the switch off mid-session stops new heartbeat
        // attachments and stops the redirect immediately, while an already attached
        // heartbeat finishes the current combat. Turning the switch back on needs a
        // game restart because patch installation only happens here.
        if (!HeartShakeConfig.EnableHeartShake)
        {
            Log.Info($"{ModId} master switch is OFF: no HeartShake behavior patches installed (heartbeat and Beat of Death redirect both disabled). Takes effect at game start.");
            return;
        }

        try
        {
            var harmony = new HarmonyLib.Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Patches.BeatOfDeathRedirectPatch.Apply(harmony);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to apply Harmony patches: {e}");
        }

        Log.Info($"{ModId} initialized (StS1 heartbeat shake in the Act4Heart Corrupt Heart fight)");
    }
}
