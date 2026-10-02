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

    private readonly Node _room;

    /// <summary>Time left until the next beat; see the scheduling contract above.</summary>
    private double _nextBeatAt;

    /// <summary>
    /// At most one live beat player: it is parented to this node (the room's
    /// lifetime), so room teardown frees it; it is also stopped and freed
    /// explicitly on death/combat-end/exit. The ogg is 1.25s, shorter than the
    /// 1.3333s interval, so the next beat never needs to overlap the previous
    /// one.
    /// </summary>
    private AudioStreamPlayer? _activePlayer;

    private bool _stopped;

    private HeartBeatNode(Creature heart, Node room)
    {
        _heart = heart;
        _room = room;
    }

    /// <summary>
    /// Attach a heartbeat to the combat room if the heart creature is alive.
    /// No-op (null) when the master toggle is off. At most one heartbeat node
    /// exists per room even if this is called again for the same room.
    /// </summary>
    public static HeartBeatNode? Attach(Node parent, Creature heart)
    {
        if (!HeartShakeConfig.EnableHeartShake)
        {
            return null;
        }
        if (parent is not NCombatRoom)
        {
            MainFile.Log.Warn("Heartbeat attach rejected: parent is not an NCombatRoom.");
            return null;
        }

        foreach (var child in parent.GetChildren())
        {
            if (child is HeartBeatNode existing && GodotObject.IsInstanceValid(existing))
            {
                MainFile.Log.Info("Heartbeat already attached to this room; reusing existing node.");
                return existing;
            }
        }

        var node = new HeartBeatNode(heart, parent);
        try
        {
            parent.AddChild(node);
            MainFile.Log.Info($"Heartbeat attached to {heart.Monster?.Id.Entry} (interval {BeatInterval:F4}s)");
            return node;
        }
        catch (Exception e)
        {
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion())
            {
                node.QueueFree();
            }
            MainFile.Log.Error($"Heartbeat attach failed: {e.Message}");
            return null;
        }
    }

    public override void _Ready()
    {
        // First beat lands one full interval after room entry, matching StS1
        // where the first maxbeat fires at 0.3666*... into the idle loop and the
        // fight intro plays before the pulse is noticeable.
        _nextBeatAt = BeatInterval;

        // Combat end (win or loss) fires on the room even while this node is
        // still kept alive by the scene tree until the next room loads. Stop
        // the heartbeat immediately instead of waiting for _ExitTree.
        CombatManager.Instance.CombatEnded += OnCombatEnded;
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
        // Prefer the model identity carried by the event: the heart's CombatState
        // belongs to the ended CombatRoom even while NRun is transitioning its
        // current visual node. Keep the visual-room fallback for engine paths
        // that clear the creature state before dispatching the event.
        if (ReferenceEquals(room.CombatState, _heart.CombatState)
            || ReferenceEquals(NCombatRoom.Instance, _room))
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
            MainFile.Log.Warn($"Heartbeat process-stop cleanup failed: {e.Message}");
        }

        try
        {
            CombatManager.Instance.CombatEnded -= OnCombatEnded;
        }
        catch (System.Exception e)
        {
            MainFile.Log.Warn($"Heartbeat event-unsubscribe cleanup failed: {e.Message}");
        }

        try
        {
            ReleaseActivePlayer();
        }
        catch (System.Exception e)
        {
            MainFile.Log.Warn($"Heartbeat player cleanup failed: {e.Message}");
        }

        MainFile.Log.Info($"Heartbeat stopped ({reason})");
    }

    private void ReleaseActivePlayer()
    {
        var player = _activePlayer;
        _activePlayer = null;
        if (player == null || !GodotObject.IsInstanceValid(player) || player.IsQueuedForDeletion())
        {
            return;
        }

        try
        {
            player.Stop();
        }
        catch (System.Exception e)
        {
            MainFile.Log.Warn($"Heartbeat player stop failed: {e.Message}");
        }
        finally
        {
            try
            {
                player.QueueFree();
            }
            catch (System.Exception e)
            {
                MainFile.Log.Warn($"Heartbeat player release failed: {e.Message}");
            }
        }
    }

    private void OnPlayerFinished(AudioStreamPlayer player)
    {
        if (ReferenceEquals(_activePlayer, player))
        {
            _activePlayer = null;
        }
        if (GodotObject.IsInstanceValid(player) && !player.IsQueuedForDeletion())
        {
            player.QueueFree();
        }
    }

    private void PlayBeat()
    {
        if (_beatLoadFailed)
        {
            return;
        }

        AudioStreamPlayer? player = null;
        try
        {
            _beatStream ??= LoadBeatStream();
            if (_beatStream == null)
            {
                // LoadBeatStream already logged the error; one shot only.
                _beatLoadFailed = true;
                return;
            }

            // One player at a time: stop and free the previous beat before
            // starting this one. The ogg is 1.25s < 1.3333s interval, so this
            // does not cut a still-playing beat.
            ReleaseActivePlayer();

            // StS1: playAV random pitch in [-0.05, 0.05] -> Godot pitch scale
            // 1 +/- 0.05.
            var newPlayer = new AudioStreamPlayer
            {
                Stream = _beatStream,
                VolumeLinear = BeatVolume,
                PitchScale = 1f + (float)GD.RandRange(-PitchJitter, PitchJitter),
                Bus = new StringName("SFX"),
            };
            player = newPlayer;
            newPlayer.Finished += () => OnPlayerFinished(newPlayer);

            // Parent to this node (a child of the combat room), not NGame:
            // the player then lives and dies with the room and can be
            // released deterministically by StopAndRelease.
            AddChild(newPlayer);
            _activePlayer = newPlayer;
            newPlayer.Play();
        }
        catch (Exception e)
        {
            if (player != null && GodotObject.IsInstanceValid(player) && !player.IsQueuedForDeletion())
            {
                player.Stop();
                player.QueueFree();
            }
            if (ReferenceEquals(_activePlayer, player))
            {
                _activePlayer = null;
            }
            MainFile.Log.Error($"Heartbeat audio failed: {e.Message}");
            // Don't let a broken stream break the shake loop; stop trying.
            _beatLoadFailed = true;
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