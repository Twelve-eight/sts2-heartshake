using Godot;

using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Rooms;

namespace HeartShake.HeartShakeCode;

/// <summary>
/// Node that reproduces the StS1 Corrupt Heart heartbeat while attached to a
/// combat room: every 1.3333s a Weak/Short screen shake + the StS1 heartbeat
/// sound. Detaches itself (and stops) when the heart dies or the room is torn
/// down (node lifetime is the room's lifetime).
///
/// StS1 evidence (desktop-1.0.jar, images/npcs/heart/skeleton.json):
/// idle anim 2.0s * timeScale 1.5 = 1.3333s cycle, maxbeat event -> ScreenShake
/// (LOW, SHORT) + HEART_SIMPLE ogg. smallbeat events are NOT handled by
/// HeartAnimListener, so only one pulse per cycle.
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

    private readonly Creature _heart;

    private double _nextBeatAt;

    private HeartBeatNode(Creature heart)
    {
        _heart = heart;
    }

    /// <summary>
    /// Attach a heartbeat to the combat room if the heart creature is alive.
    /// No-op (null) when the master toggle is off.
    /// </summary>
    public static HeartBeatNode? Attach(Node parent, Creature heart)
    {
        if (!HeartShakeConfig.EnableHeartShake)
        {
            return null;
        }
        var node = new HeartBeatNode(heart);
        parent.AddChild(node);
        MainFile.Log.Info($"Heartbeat attached to {heart.Monster?.Id.Entry} (interval {BeatInterval:F4}s)");
        return node;
    }

    public override void _Ready()
    {
        // First beat lands one full interval after room entry, matching StS1
        // where the first maxbeat fires at 0.3666*... into the idle loop and the
        // fight intro plays before the pulse is noticeable.
        _nextBeatAt = BeatInterval;
    }

    public override void _Process(double delta)
    {
        if (_heart.IsDead)
        {
            // StS1 removes the anim listener in die(); mirror by going idle.
            // The node dies with the room.
            SetProcess(false);
            return;
        }

        _nextBeatAt -= delta;
        if (_nextBeatAt > 0)
        {
            return;
        }
        _nextBeatAt += BeatInterval;

        NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);

        if (HeartShakeConfig.EnableHeartSound)
        {
            PlayBeat();
        }
    }

    private static void PlayBeat()
    {
        try
        {
            _beatStream ??= LoadBeatStream();
            if (_beatStream == null)
            {
                return;
            }
            // StS1: playAV random pitch in [-0.05, 0.05] -> Godot pitch scale
            // 1 +/- 0.05.
            var player = new AudioStreamPlayer
            {
                Stream = _beatStream,
                VolumeLinear = BeatVolume,
                PitchScale = 1f + (float)GD.RandRange(-PitchJitter, PitchJitter),
                Bus = new StringName("SFX"),
            };
            player.Finished += player.QueueFree;
            NGame.Instance?.AddChild(player);
            player.Play();
        }
        catch (Exception e)
        {
            MainFile.Log.Error($"Heartbeat audio failed: {e.Message}");
            // Don't let a broken stream break the shake loop; stop trying.
            _beatStream = null;
        }
    }

    private static AudioStream? LoadBeatStream()
    {
        var path = $"{MainFile.ResPath}/heartbeat.ogg";
        if (!ResourceLoader.Exists(path))
        {
            MainFile.Log.Error($"Missing heartbeat sound at {path} (pck not packed?)");
            return null;
        }
        return ResourceLoader.Load<AudioStream>(path);
    }
}
