using BaseLib.Config;

namespace HeartShake.HeartShakeCode;

/// <summary>
/// Runtime toggles (Settings -> Mod Settings), PerfectConfig pattern.
/// SimpleModConfig auto-generates the page.
/// </summary>
[ConfigHoverTipsByDefault]
internal class HeartShakeConfig : SimpleModConfig
{
    /// <summary>
    /// Master switch. When false: the heartbeat timer never starts, no shake,
    /// no sound (e.g. for recording or if the vanilla StS2 fight is preferred).
    /// </summary>
    public static bool EnableHeartShake { get; set; } = true;

    /// <summary>
    /// Play the StS1 heartbeat sound (SLS_SFX_HeartBeat_Simple_v1.ogg) with
    /// each beat. The beat is played by a self-built AudioStreamPlayer on the
    /// SFX bus, so its volume follows the game's SFX bus setting.
    /// </summary>
    public static bool EnableHeartSound { get; set; } = true;

    // Shake strength is fixed to StS1 fidelity (Weak/Short) - deliberately not
    // configurable in v0.1.0 to keep the settings page honest; a strength enum
    // can be added if users ask.
}
