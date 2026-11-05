namespace EveLocalMon.Core.Settings;

public sealed record UiSettings
{
    /// <summary>Keep the main window above other windows (the title-bar pin).</summary>
    public bool KeepOnTop { get; init; }

    /// <summary>Zoom of the Calibration tab's guide preview: 1, 2 or 4. Anything else reads as 2.</summary>
    public int CalibrationZoom { get; init; } = 2;

    public int EffectiveCalibrationZoom => CalibrationZoom is 1 or 2 or 4 ? CalibrationZoom : 2;
}
