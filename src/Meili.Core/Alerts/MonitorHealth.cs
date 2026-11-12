namespace Meili.Core.Alerts;

public enum MonitorTransition { None, Interrupted, Recovered }

/// <summary>
/// F10: tracks whether alert monitoring is actually getting quotes. After <see cref="Threshold"/> consecutive failed
/// checks it reports <see cref="MonitorTransition.Interrupted"/> exactly once, and <see cref="MonitorTransition.Recovered"/>
/// on the next success — so the UI and tray can tell the user instead of failing silently.
/// </summary>
public sealed class MonitorHealth
{
    public MonitorHealth(int threshold = 3) => Threshold = threshold;

    public int Threshold { get; set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? FailingSince { get; private set; }
    public DateTimeOffset? LastSuccess { get; private set; }
    public string? LastError { get; private set; }
    public bool Interrupted { get; private set; }

    public MonitorTransition RecordFailure(DateTimeOffset at, string? error)
    {
        ConsecutiveFailures++;
        FailingSince ??= at;
        LastError = error;
        if (!Interrupted && ConsecutiveFailures >= Threshold)
        {
            Interrupted = true;
            return MonitorTransition.Interrupted;
        }
        return MonitorTransition.None;
    }

    public MonitorTransition RecordSuccess(DateTimeOffset at)
    {
        var was = Interrupted;
        ConsecutiveFailures = 0;
        FailingSince = null;
        LastError = null;
        LastSuccess = at;
        Interrupted = false;
        return was ? MonitorTransition.Recovered : MonitorTransition.None;
    }
}

/// <summary>What the last alert check did with the quotes it got (F3).</summary>
public sealed record AlertCheckSummary(DateTimeOffset At, int Evaluated, IReadOnlyList<string> SkippedStale, IReadOnlyList<string> DelayUnknown);
