using HarmonyLib;

using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using HeartShake.HeartShakeCode;

/// <summary>
/// Start the heartbeat when the Corrupt Heart combat room becomes ready.
///
/// The heart is identified by its model id string ("CORRUPT_HEART", the
/// Slugify of Act4Heart's CorruptHeart class) so this mod has no compile-time
/// dependency on Act4Heart and stays completely inert in every other fight.
///
/// SINGLE-TARGET patch class (dual-target classes patch only the last target).
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), "_Ready")]
internal static class CombatRoomReadyPatch
{
    private const string HeartMonsterId = "CORRUPT_HEART";

    private static void Postfix(NCombatRoom __instance)
    {
        try
        {
            // ActiveCombat only: no heartbeat in post-victory replays of the
            // room (FinishedCombat) or visual-only event previews (VisualOnly),
            // matching StS1 where the listener lives on the fighting monster.
            if (__instance.Mode != CombatRoomMode.ActiveCombat)
            {
                return;
            }

            foreach (var creatureNode in __instance.CreatureNodes)
            {
                var entity = creatureNode.Entity;
                if (entity?.Monster?.Id.Entry == HeartMonsterId && !entity.IsDead)
                {
                    HeartBeatNode.Attach(__instance, entity);
                    return;
                }
            }
        }
        catch (Exception e)
        {
            MainFile.Log.Error($"CombatRoomReadyPatch failed: {e}");
        }
    }
}
