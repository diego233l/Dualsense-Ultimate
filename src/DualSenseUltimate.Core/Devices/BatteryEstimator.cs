using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Devices;

/// <summary>Learned drain / charge speed of one controller, persisted between sessions.</summary>
public sealed class BatteryRates
{
    /// <summary>Minutes the controller takes to lose one 10 % step.</summary>
    public double DischargeMinutesPerLevel { get; set; } = 45;
    /// <summary>Minutes the controller takes to gain one 10 % step while charging.</summary>
    public double ChargeMinutesPerLevel { get; set; } = 17;
    /// <summary>Number of observed step transitions used to learn the rates.</summary>
    public int Samples { get; set; }
}

/// <summary>Turns the controller's coarse battery reading into a stable, finer estimate.
/// The hardware only reports 11 steps (0-10) and the raw value jitters between neighbours (voltage
/// sags under vibration and rises on the charger). This class:
///  - debounces the raw step (a change must persist before it is accepted, longer if it goes against
///    the expected direction);
///  - never lets the displayed value rise while discharging or fall while charging;
///  - interpolates inside the 10 % step using the time spent in it and a per-controller rate that is
///    learned from real step transitions, so the estimate self-calibrates with use.
/// Deterministic: time is a parameter.</summary>
public sealed class BatteryEstimator
{
    private const double ExpectedStableSeconds = 4, UnexpectedStableSeconds = 60;
    private int _level = -1, _candidate = -1;
    private BatteryStatus _status = BatteryStatus.Unknown;
    private double _candidateSince, _enteredAt;
    private bool _enteredByTransition;
    private double _display = -1;

    public BatteryRates Rates { get; set; } = new();
    /// <summary>Estimated minutes until empty (discharging) or full (charging); null when unknown.</summary>
    public double? MinutesRemaining { get; private set; }
    /// <summary>True once the position inside the current 10 % step is known from an observed transition.</summary>
    public bool Calibrated => _enteredByTransition;

    public int Update(int rawLevel, BatteryStatus status, double nowSeconds)
    {
        rawLevel = Math.Clamp(rawLevel, 0, 10);
        if (status == BatteryStatus.Full) { Reset(rawLevel, status, nowSeconds); MinutesRemaining = 0; _display = 100; return 100; }
        if (status is BatteryStatus.Unknown or BatteryStatus.Error) { Reset(rawLevel, status, nowSeconds); MinutesRemaining = null; return status == BatteryStatus.Error ? 0 : Math.Min(rawLevel * 10 + 5, 100); }

        bool charging = status == BatteryStatus.Charging;
        if (_level < 0 || status != _status)
        {
            double previous = _display;
            Reset(rawLevel, status, nowSeconds);
            // Keep continuity when only the charger state changed and the step is the same.
            if (previous >= rawLevel * 10 && previous <= rawLevel * 10 + 9.99) _display = previous;
        }
        else if (rawLevel != _level)
        {
            if (rawLevel != _candidate) { _candidate = rawLevel; _candidateSince = nowSeconds; }
            bool expected = charging ? rawLevel > _level : rawLevel < _level;
            if (nowSeconds - _candidateSince >= (expected ? ExpectedStableSeconds : UnexpectedStableSeconds))
            {
                if (expected && Math.Abs(rawLevel - _level) == 1 && _enteredByTransition) Learn(charging, (nowSeconds - _enteredAt) / 60.0);
                bool single = expected && Math.Abs(rawLevel - _level) == 1;
                _level = rawLevel; _candidate = -1; _enteredAt = nowSeconds; _enteredByTransition = single;
                if (!expected) _display = -1; // genuine correction: allow the value to move the "wrong" way once
            }
        }
        else _candidate = -1;

        double rate = Math.Max(1, charging ? Rates.ChargeMinutesPerLevel : Rates.DischargeMinutesPerLevel);
        double frac = Math.Clamp((nowSeconds - _enteredAt) / (rate * 60), 0, 1);
        double low = _level * 10, value;
        if (_level >= 10) value = 100;
        else if (charging) value = _enteredByTransition ? low + frac * 9 : low + 5 + frac * 4;
        else value = _enteredByTransition ? low + 9 - frac * 9 : low + 5 - frac * 5;
        value = Math.Clamp(value, charging ? 0 : 1, charging && _level < 10 ? 99 : 100);

        if (_display >= 0) value = charging ? Math.Max(value, _display) : Math.Min(value, _display);
        _display = value;
        MinutesRemaining = charging ? (100 - value) / 10.0 * Rates.ChargeMinutesPerLevel : value / 10.0 * Rates.DischargeMinutesPerLevel;
        return (int)Math.Round(value);
    }

    private void Reset(int level, BatteryStatus status, double now)
    {
        _level = level; _status = status; _candidate = -1; _enteredAt = now; _enteredByTransition = false; _display = -1;
    }

    private void Learn(bool charging, double minutes)
    {
        // Ignore implausible durations (controller asleep, heavy outliers).
        if (charging) { if (minutes is < 3 or > 90) return; Rates.ChargeMinutesPerLevel = Blend(Rates.ChargeMinutesPerLevel, minutes); }
        else { if (minutes is < 8 or > 240) return; Rates.DischargeMinutesPerLevel = Blend(Rates.DischargeMinutesPerLevel, minutes); }
        Rates.Samples++;
    }

    private double Blend(double current, double sample) => Rates.Samples == 0 ? (current + sample) / 2 : current * 0.7 + sample * 0.3;
}
