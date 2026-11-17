using AppForge.Core.Domain;

namespace AppForge.Core.Approval;

/// <summary>
/// „Audit zuerst“: Eine Supplemental Policy darf erst nach einer Beobachtungszeit ohne neue
/// CodeIntegrity-Events (3076/3077) für das Produkt auf Enforced umgestellt werden – und auch dann
/// nur über eine Freigabe der Stufe 3.
/// </summary>
public static class EnforcementPolicy
{
    public static readonly TimeSpan DefaultObservation = TimeSpan.FromDays(7);

    public static (bool Allowed, string Reason) CanSwitchToEnforced(AppControlPolicyEntity policy, DateTimeOffset now, TimeSpan? observation = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var window = observation ?? DefaultObservation;

        if (policy.Mode == PolicyEnforcementMode.Enforced)
        {
            return (false, "Die Richtlinie ist bereits erzwungen.");
        }

        if (policy.AuditSince is not { } since)
        {
            return (false, "Die Richtlinie wurde noch nicht im Audit-Modus verteilt.");
        }

        if (now - since < window)
        {
            return (false, $"Beobachtungszeit noch nicht erreicht (Audit seit {since:yyyy-MM-dd}, nötig {window.TotalDays:0} Tage).");
        }

        if (policy.LastBlockEventAt is { } last && now - last < window)
        {
            return (false, $"Letztes CodeIntegrity-Event (3076/3077) am {last:yyyy-MM-dd} – Beobachtungszeit beginnt neu.");
        }

        return (true, "Beobachtungszeit ohne neue Events abgelaufen – Freigabe Stufe 3 möglich.");
    }
}

/// <summary>Vorschläge für die Ring-Promotion Test → Pilot → Produktion.</summary>
public static class RingPromotion
{
    public static DeploymentRing? Suggest(DeploymentRing current, DateTimeOffset inRingSince, bool hadFailures, DateTimeOffset now, int daysInTest = 3, int daysInPilot = 7)
    {
        if (hadFailures)
        {
            return null;
        }

        return current switch
        {
            DeploymentRing.Test when (now - inRingSince).TotalDays >= daysInTest => DeploymentRing.Pilot,
            DeploymentRing.Pilot when (now - inRingSince).TotalDays >= daysInPilot => DeploymentRing.Production,
            _ => null,
        };
    }
}
