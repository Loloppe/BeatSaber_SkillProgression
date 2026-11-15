using SkillProgression.Core.Maps;

namespace SkillProgression.Plugin.Gameplay;

/// <summary>A map handed to the game's Solo menu with "To Solo" (D-057): its plays there count as plugin plays.</summary>
internal sealed record SoloHandoff(MapKey Map, double? TargetAccuracy);

/// <summary>
/// What happened during the current plugin-started play that the level results don't tell: a No Fail soft fail and the
/// pause count. Written by <see cref="GameplayTracker"/> in the game scene, read by <see cref="LevelLauncher"/> (or, for
/// "To Solo" plays, by the Solo handoff's recorder).
/// </summary>
internal class PlaySession
{
    /// <summary>True between a plugin start and its result: plays from the normal level select are not tracked.</summary>
    public bool Active { get; private set; }
    /// <summary>The play was started with the No Fail modifier.</summary>
    public bool NoFail { get; private set; }
    /// <summary>The play was started from the Solo menu on the handed-off map (D-057), not by <see cref="LevelLauncher"/>.</summary>
    public bool FromSolo { get; private set; }
    public double? SoftFailSongTime { get; set; }
    public int Pauses { get; set; }

    /// <summary>The map last handed to Solo with "To Solo"; null once the plugin is opened again (D-057).</summary>
    public SoloHandoff? Handoff { get; set; }

    public void Begin(bool noFail, bool fromSolo = false)
    {
        Active = true;
        NoFail = noFail;
        FromSolo = fromSolo;
        SoftFailSongTime = null;
        Pauses = 0;
    }

    public void End() => Active = false;
}
