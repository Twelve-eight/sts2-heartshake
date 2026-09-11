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
        ModConfigRegistry.Register(ModId, new HeartShakeConfig());
        // BaseLib SimpleLoc for settings labels (eng + zhs).
        BaseLib.Patches.Localization.SimpleLoc.EnableSimpleLoc(ModId);
        // Register C# scripts referenced by scenes shipped in the .pck (none yet,
        // but the heartbeat ogg is loaded through ResPath).
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());

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
