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

    /// <summary>
    /// When enabled, the Corrupt Heart's Beat of Death (HP loss on playing a
    /// card) is redirected to Osty instead of the player. Only takes effect if
    /// Osty is alive; falls back to vanilla behaviour when Osty is dead or not
    /// present. Default false preserves the original fight balance.
    /// </summary>
    public static bool BeatOfDeathTargetsOsty { get; set; } = false;

    // Shake strength is fixed to StS1 fidelity (Weak/Short) - deliberately not
    // configurable in v0.1.0 to keep the settings page honest; a strength enum
    // can be added if users ask.
}
