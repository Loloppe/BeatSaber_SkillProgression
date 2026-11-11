namespace SkillProgression.Plugin.Gameplay;

/// <summary>
/// What happened during the current plugin-started play that the level results don't tell: a No Fail soft fail and the
/// pause count. Written by <see cref="GameplayTracker"/> in the game scene, read by <see cref="LevelLauncher"/>.
/// </summary>
internal class PlaySession
{
    /// <summary>True between a plugin start and its result: plays from the normal level select are not tracked.</summary>
    public bool Active { get; private set; }
    /// <summary>The play was started with the No Fail modifier.</summary>
    public bool NoFail { get; private set; }
    public double? SoftFailSongTime { get; set; }
    public int Pauses { get; set; }

    public void Begin(bool noFail)
    {
        Active = true;
        NoFail = noFail;
        SoftFailSongTime = null;
        Pauses = 0;
    }

    public void End() => Active = false;
}
