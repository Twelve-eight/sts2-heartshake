using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace HeartShake.HeartShakeCode.Patches;

/// <summary>
/// Redirects Act4Heart's BeatOfDeath damage to Osty when the user enables the option.
///
/// Root cause: BeatOfDeathPower.AfterCardPlayed deals (ValueProp)4 = Unpowered damage.
/// Osty's DieForYouPower only intercepts powered attacks (Move &amp; !Unpowered), so
/// the card-play self-damage bypasses Osty in vanilla. This patch changes the target
/// to Osty while keeping the same ValueProp, preserving block/strength semantics.
///
/// Implementation constraint: no compile-time reference to Act4Heart.dll.
/// The type is looked up by name at patch time; if absent the patch is silently skipped.
/// </summary>
internal static class BeatOfDeathRedirectPatch
{
    internal static void Apply(HarmonyLib.Harmony harmony)
    {
        // TYPE IDENTITY (astra-advice item 8, 2026-09-12; evidence
        // astra-advice-evidence/2026-09-12/heart-type-identities.txt): the
        // shipped Act4Heart dll declares the class in the Powers SUB-namespace -
        // "Act4Heart.Powers.BeatOfDeathPower". The old lookup used the flat
        // name, resolved null, and the new feature silently "skipped" forever.
        // The lookup now tries the verified name first and keeps the legacy
        // name as a fallback so an older Act4Heart build still binds.
        var targetType = AccessTools.TypeByName("Act4Heart.Powers.BeatOfDeathPower")
                         ?? AccessTools.TypeByName("Act4Heart.BeatOfDeathPower");
        if (targetType == null)
        {
            MainFile.Log.Info("[HeartShake] Act4Heart (Powers.BeatOfDeathPower) not found; BeatOfDeath redirect patch skipped.");
            return;
        }

        var method = AccessTools.Method(targetType, "AfterCardPlayed");
        if (method == null)
        {
            MainFile.Log.Warn("[HeartShake] AfterCardPlayed not found on BeatOfDeathPower; redirect patch skipped.");
            return;
        }

        harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(BeatOfDeathRedirectPatch), nameof(Prefix)));
        MainFile.Log.Info("[HeartShake] BeatOfDeath redirect patch applied.");
    }

    private static bool Prefix(PowerModel __instance, PlayerChoiceContext context, CardPlay cardPlay, ref Task __result)
    {
        if (!HeartShakeConfig.BeatOfDeathTargetsOsty)
        {
            return true;
        }

        try
        {
            Player owner = cardPlay.Card.Owner;
            if (!owner.IsOstyAlive || owner.Osty == null)
            {
                // No live Osty: vanilla fallback.
                return true;
            }

            // Reproduce original feedback exactly.
            __instance.Flash();
            NDebugAudioManager.Instance?.Play("SOTE_SFX_FastBlunt_v2.mp3", 0.4f, PitchVariance.None);

            // Redirect the same damage to Osty, preserving Unpowered props.
            __result = CreatureCmd.Damage(
                context,
                owner.Osty,
                (decimal)__instance.Amount,
                (ValueProp)4,
                __instance.Owner);

            return false;
        }
        catch (Exception e)
        {
            MainFile.Log.Error($"[HeartShake] BeatOfDeath redirect failed, falling back to vanilla: {e.Message}");
            return true;
        }
    }
}
