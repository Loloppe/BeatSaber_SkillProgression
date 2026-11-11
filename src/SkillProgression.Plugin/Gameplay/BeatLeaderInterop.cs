using System;
using System.Reflection;
using IPA.Loader;

namespace SkillProgression.Plugin.Gameplay;

/// <summary>
/// BeatLeader only records/uploads a replay when its own level-select Play button was pressed
/// (RecorderUtils.shouldRecord, set by Harmony patches). Levels started directly (D-006) must set it themselves,
/// as BeatSaberPlus does. Internal API → reflection; failure only means the play is not uploaded.
/// </summary>
internal static class BeatLeaderInterop
{
    private static MethodInfo? _onActionButtonWasPressed;
    private static bool _resolved;

    /// <returns>false when BeatLeader is missing or its internals changed.</returns>
    public static bool PrepareRecording()
    {
        if (!_resolved)
        {
            _resolved = true;
            var plugin = PluginManager.GetPluginFromId("BeatLeader");
            _onActionButtonWasPressed = plugin?.Assembly
                .GetType("BeatLeader.Utils.RecorderUtils")
                ?.GetMethod("OnActionButtonWasPressed", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (_onActionButtonWasPressed == null)
                global::SkillProgression.Plugin.Plugin.Log.Warn("BeatLeader recorder hook not found: plays started from Skill Progression will not be uploaded to BeatLeader.");
        }

        if (_onActionButtonWasPressed == null) return false;
        try
        {
            _onActionButtonWasPressed.Invoke(null, null);
            return true;
        }
        catch (Exception e)
        {
            global::SkillProgression.Plugin.Plugin.Log.Warn($"BeatLeader recorder hook failed: {e.Message}");
            return false;
        }
    }
}
