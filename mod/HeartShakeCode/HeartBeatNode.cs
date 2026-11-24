using Godot;

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;

namespace HeartShake.HeartShakeCode;

/// <summary>
/// Node that reproduces the StS1 Corrupt Heart heartbeat while attached to a
/// combat room: every 1.3333s a Weak/Short screen shake + the StS1 heartbeat
/// sound. Stops and releases on heart death, combat end, or room teardown
/// (node lifetime is the room's lifetime).
///
/// StS1 evidence (desktop-1.0.jar, images/npcs/heart/skeleton.json):
/// idle anim 2.0s * timeScale 1.5 = 1.3333s cycle, maxbeat event -> ScreenShake
/// (LOW, SHORT) + HEART_SIMPLE ogg. smallbeat events are NOT handled by
/// HeartAnimListener, so only one pulse per cycle.
///
/// Beat scheduling contract (2026-10-02, E01 fix):
/// the beat clock is a fixed-interval pulse, not a queue of owed beats. If a
/// frame (pause, load hitch, debugger break) covers several intervals, the
/// missed beats are MERGED into at most one immediate beat and the remaining
/// debt is DISCARDED: the timer is snapped back to one full BeatInterval
/// instead of carrying the negative remainder. This keeps at most one beat per
/// frame and at most one beat per interval over time, so a long frame can
/// never spawn a storm of ScreenShake calls or AudioStreamPlayers.
/// </summary>
public partial class HeartBeatNode : Node
{
    /// <summary>StS1 idle cycle: 2.0s / 1.5 timeScale.</summary>
    private const double BeatInterval = 2.0 / 1.5; // 1.3333s

    /// <summary>StS1 HEART_SIMPLE pitch variance: random(-0.05, 0.05).</summary>
    private const float PitchJitter = 0.05f;

    /// <summary>StS1 HEART_SIMPLE playback: playAV(name, random(-0.05, 0.05), 0.75).</summary>
    private const float BeatVolume = 0.75f;

    // Sound loaded lazily once; the ogg ships in HeartShake.pck under
    // res://HeartShake/heartbeat.ogg (renamed from SLS_SFX_HeartBeat_Simple_v1).
    private static AudioStream? _beatStream;

    /// <summary>Set after one failed load/log attempt; never retried per beat.</summary>
    private static bool _beatLoadFailed;

    private readonly Creature _heart;

    // The room's state is immutable for this node's lifetime. Creature.CombatState
    // is cleared by host teardown before some asynchronous combat continuations end.
    private readonly ICombatState _combatState;

    /// <summary>Time left until the next beat; see the scheduling contract above.</summary>
    private double _nextBeatAt;

    /// <summary>
    /// One reusable native player, parented to this node for the room's lifetime.
    /// A completed one-shot remains idle until the next beat; stopping the
    /// heartbeat stops and frees it. No per-beat node or Finished delegate is needed.
    /// </summary>
    private AudioStreamPlayer? _beatPlayer;

    private bool _stopped;
    private bool _subscribed;
    private bool _audioFailed;

    private HeartBeatNode(Creature heart, ICombatState combatState)
    {
        _heart = heart;
        _combatState = combatState;
    }

    /// <summary>
    /// Attach a heartbeat to the combat room if the heart creature is alive.
    /// No-op (null) when the master toggle is off. At most one heartbeat node
    /// exists per room even if this is called again for the same room.
    /// </summary>
    public static HeartBeatNode? Attach(Node parent, Creature heart)
    {
        if (!HeartShakeConfig.EnableHeartShake || heart.IsDead)
        {
            return null;
        }
        if (parent is not NCombatRoom room || room.Mode != CombatRoomMode.ActiveCombat)
        {
            MainFile.Log.Warn("Heartbeat attach rejected: parent is not an active NCombatRoom.");
            return null;
        }
        var combatState = heart.CombatState;
        if (combatState == null || parent.IsQueuedForDeletion())
        {
            MainFile.Log.Warn("Heartbeat attach rejected: combat state is absent or the room is exiting.");
            return null;
        }

        foreach (var child in parent.GetChildren())
        {
            if (child is not HeartBeatNode existing || !GodotObject.IsInstanceValid(existing))
            {
                continue;
            }
            if (!existing._stopped && !existing.IsQueuedForDeletion()
                && ReferenceEquals(existing._heart, heart)
                && ReferenceEquals(existing._combatState, combatState))
            {
                return existing;
            }
            existing.StopAndRelease("combat replaced");
            if (!existing.IsQueuedForDeletion())
            {
                existing.QueueFree();
            }
        }

        var node = new HeartBeatNode(heart, combatState);
        try
        {
            parent.AddChild(node);
            MainFile.Log.Info($"Heartbeat attached to {heart.Monster?.Id.Entry} (interval {BeatInterval:F4}s)");
            return node;
        }
        catch (Exception e)
        {
            MainFile.Log.Error($"Heartbeat attach failed: {e}");
            node.StopAndRelease("attach failed");
            try
            {
                if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
                {
                    node.QueueFree();
                }
            }
            catch (Exception cleanupError)
            {
                MainFile.Log.Warn($"Heartbeat attach cleanup failed: {cleanupError}");
            }
            return null;
        }
    }

    public override void _Ready()
    {
        if (_stopped || _subscribed)
        {
            return;
        }

        // Start a fresh full interval on attachment. Godot's process delta is
        // the clock, so pauses/time scaling follow the host rather than wall time.
        _nextBeatAt = BeatInterval;

        // CombatEnded may follow awaited victory hooks; keep the captured state
        // as the owner even if teardown clears the creature's current state.
        CombatManager.Instance.CombatEnded += OnCombatEnded;
        _subscribed = true;
    }

    public override void _Process(double delta)
    {
        if (_stopped)
        {
            return;
        }

        if (_heart.IsDead)
        {
            // StS1 removes the anim listener in die(); mirror by stopping the
            // pulse and releasing the player now, not at room teardown.
            StopAndRelease("heart died");
            return;
        }

        if (!ReferenceEquals(_heart.CombatState, _combatState))
        {
            StopAndRelease("heart left combat");
            return;
        }

        if (!HeartShakeConfig.EnableHeartSound && _beatPlayer != null)
        {
            ReleaseBeatPlayer();
        }

        _nextBeatAt -= delta;
        if (_nextBeatAt > 0)
        {
            return;
        }

        // Fixed-interval pulse with bounded catch-up: emit at most one beat for
        // this frame, then discard all overdue time and schedule the next beat
        // a full interval from the current frame. This prevents a long frame
        // from producing a near-immediate second beat on the next frame.
        _nextBeatAt = BeatInterval;

        NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);

        if (HeartShakeConfig.EnableHeartSound)
        {
            PlayBeat();
        }
    }

    public override void _ExitTree()
    {
        // Room exit / tree exit / game shutdown: stop the pulse and release the
        // player. Idempotent with the death and combat-end paths.
        StopAndRelease("exit tree");
    }

    private void OnCombatEnded(CombatRoom room)
    {
        // Never infer ownership from the current visual room or the creature's
        // mutable CombatState. A late event from A must leave B untouched even
        // after either creature has been detached by host cleanup.
        if (ReferenceEquals(room.CombatState, _combatState))
        {
            StopAndRelease("combat ended");
        }
    }

    /// <summary>
    /// Stop the pulse, unsubscribe from combat events, and free the active
    /// player. Safe to call from _Process, the combat-ended event, and
    /// _ExitTree; repeated calls are no-ops.
    /// </summary>
    private void StopAndRelease(string reason)
    {
        if (_stopped)
        {
            return;
        }
        _stopped = true;

        // Cleanup is deliberately segmented: a Godot teardown exception in one operation
        // must not skip the event unsubscribe or the audio-player release that follows it.
        try
        {
            SetProcess(false);
        }
        catch (System.Exception e)
        {
            MainFile.Log.Warn($"Heartbeat process-stop cleanup failed: {e}");
        }

        try
        {
            if (_subscribed)
            {
                CombatManager.Instance.CombatEnded -= OnCombatEnded;
                _subscribed = false;
            }
        }
        catch (System.Exception e)
        {
            MainFile.Log.Warn($"Heartbeat event-unsubscribe cleanup failed: {e}");
        }

        try
        {
            ReleaseBeatPlayer();
        }
        catch (System.Exception e)
        {
            MainFile.Log.Warn($"Heartbeat player cleanup failed: {e}");
        }

        MainFile.Log.Info($"Heartbeat stopped ({reason})");
    }

    private void ReleaseBeatPlayer()
    {
        var player = _beatPlayer;
        _beatPlayer = null;
        if (player == null || !GodotObject.IsInstanceValid(player))
        {
            return;
        }

        try
        {
            player.Stop();
        }
        catch (Exception e)
        {
            MainFile.Log.Warn($"Heartbeat player stop failed: {e}");
        }
        finally
        {
            try
            {
                if (!player.IsQueuedForDeletion())
                {
                    player.QueueFree();
                }
            }
            catch (Exception e)
            {
                MainFile.Log.Warn($"Heartbeat player release failed: {e}");
            }
        }
    }

    private void PlayBeat()
    {
        if (_audioFailed || _beatLoadFailed)
        {
            return;
        }

        try
        {
            _beatStream ??= LoadBeatStream();
            if (_beatStream == null)
            {
                // LoadBeatStream already logged the error; one shot only.
                _beatLoadFailed = true;
                return;
            }

            var player = _beatPlayer;
            if (player != null
                && (!GodotObject.IsInstanceValid(player) || player.IsQueuedForDeletion()))
            {
                // A native owner may have retired this player since the last
                // beat. Discard it before use and rebuild only in this case.
                ReleaseBeatPlayer();
                player = null;
            }
            if (player == null)
            {
                player = new AudioStreamPlayer();
                // Own the native node before any setter or AddChild can fail.
                _beatPlayer = player;
                player.Stream = _beatStream;
                player.VolumeLinear = BeatVolume;
                player.Bus = "SFX";
                AddChild(player);
            }

            // The one-shot ends naturally; Play restarts this same native player.
            // StS1 playAV random pitch in [-0.05, 0.05] -> scale 1 +/- 0.05.
            player.PitchScale = 1f + (float)GD.RandRange(-PitchJitter, PitchJitter);
            player.Play();
        }
        catch (Exception e)
        {
            MainFile.Log.Error($"Heartbeat audio failed: {e}");
            // A player failure only disables audio for this combat, not every
            // future room that could still use the successfully loaded stream.
            _audioFailed = true;
            try
            {
                ReleaseBeatPlayer();
            }
            catch (Exception cleanupError)
            {
                MainFile.Log.Warn($"Heartbeat audio cleanup failed: {cleanupError}");
            }
        }
    }

    /// <summary>
    /// The ogg ships as a raw file in the mod pck (quick PCK packer does not
    /// run the Godot import step, so ResourceLoader cannot see it - verified
    /// in godot.log: "Missing heartbeat sound at res://HeartShake/heartbeat.ogg").
    /// FileAccess reads raw pck bytes fine; build the stream at runtime.
    /// </summary>
    private static AudioStream? LoadBeatStream()
    {
        var path = $"{MainFile.ResPath}/heartbeat.ogg";
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
        {
            MainFile.Log.Error($"Heartbeat sound unreadable at {path} (error {Godot.FileAccess.GetOpenError()})");
            return null;
        }
        var stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
        if (stream == null)
        {
            MainFile.Log.Error($"heartbeat.ogg is not valid Ogg Vorbis data ({bytes.Length} bytes)");
        }
        return stream;
    }
}