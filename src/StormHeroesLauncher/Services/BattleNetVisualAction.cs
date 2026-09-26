namespace StormHeroesLauncher.Services;

// Battle.net only: no confirmation, delay or logging on the Hide -> Close path.
public sealed record BattleNetVisualResult(double? HideRequested, double? CloseRequested,
    bool HideAccepted, bool ClosePosted, bool ProcessAlive, string Result);

public static class BattleNetVisualAction
{
    public static BattleNetVisualResult Run(bool transient, Func<bool, bool> validate,
        Func<bool> hide, Func<bool> close, Func<bool> alive, Func<double> milliseconds)
    {
        double? hidden = null, closed = null;
        bool hideAccepted = false, posted = false;
        try
        {
            if (!validate(true)) return new(null, null, false, false, false, "IdentityChanged");
            hidden = milliseconds();
            try { hideAccepted = hide(); } catch { /* Retain safe native tray action. */ }
            if (!transient && validate(false))
            {
                closed = milliseconds();
                try { posted = close(); } catch { /* Window failures remain nonfatal. */ }
            }
            return new(hidden, closed, hideAccepted, posted, alive(),
                !hideAccepted || !transient && !posted ? "RejectedOrChanged" : "Requested");
        }
        catch { return new(hidden, closed, hideAccepted, posted, false, "Unavailable"); }
    }
}
