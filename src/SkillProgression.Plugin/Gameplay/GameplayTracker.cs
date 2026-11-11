using System;
using UnityEngine;
using Zenject;

namespace SkillProgression.Plugin.Gameplay;

/// <summary>
/// Game-scene listener for plugin-started plays: energy reaching 0 with No Fail (a soft fail the results report as a
/// clear) and pauses. Does nothing for plays started from the normal level select.
/// The energy is read every frame: on 1.40.8 the energy counter's "reached 0" event and noFail flag don't reflect the
/// No Fail modifier (FPFC tests 2026-11-10), so No Fail comes from the modifiers the plugin started the level with.
/// </summary>
internal class GameplayTracker : IInitializable, ITickable, IDisposable
{
    [Inject] private readonly PlaySession _session = null!;
    [InjectOptional] private readonly GameEnergyCounter? _injectedEnergy = null;
    [InjectOptional] private readonly PauseController? _pause = null;
    [InjectOptional] private readonly IAudioTimeSource? _time = null;

    private GameEnergyCounter? _energy;
    private bool _loggedEnergy;

    public void Initialize()
    {
        if (!_session.Active) return;
        if (_pause != null) _pause.didPauseEvent += HandlePaused;
        else Plugin.Log.Warn("Pause tracking unavailable: no PauseController in this scene.");
    }

    public void Dispose()
    {
        if (_pause != null) _pause.didPauseEvent -= HandlePaused;
    }

    public void Tick()
    {
        if (!_session.Active || !_session.NoFail || _session.SoftFailSongTime != null) return;
        _energy ??= _injectedEnergy != null ? _injectedEnergy : UnityEngine.Object.FindObjectOfType<GameEnergyCounter>();
        if (_energy == null || !_energy.isInitialized) return;
        if (!_loggedEnergy)
        {
            _loggedEnergy = true;
            Plugin.Log.Debug($"Energy tracking: energy={_energy.energy:F2}");
        }
        // Without No Fail the level fails and the results say so; only No Fail plays need the flag.
        if (_energy.energy <= 1e-5f)
        {
            _session.SoftFailSongTime = _time?.songTime ?? 0;
            Plugin.Log.Info($"No Fail soft fail at {_session.SoftFailSongTime:F1}s");
        }
    }

    private void HandlePaused() => _session.Pauses++;
}
