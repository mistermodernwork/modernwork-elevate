using AppForge.Core.Domain;

namespace AppForge.Core.Approval;

public enum ApprovalState
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Cancelled,
}

/// <summary>Eine Person, die eine Entscheidung trifft.</summary>
public sealed record Approver(string Id, IReadOnlyCollection<string> Roles, bool IsServicePrincipal = false)
{
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public sealed record ApprovalDecisionRecord(
    string ApproverId,
    IReadOnlyCollection<string> Roles,
    bool Approved,
    string Comment,
    DateTimeOffset At,
    bool IsEmergencyConfirmation = false);

/// <summary>Konfigurierbare Fristen. Standardwerte laut Plan.</summary>
public sealed class ApprovalOptions
{
    public int Level1BusinessDays { get; set; } = 2;

    public int Level2BusinessDays { get; set; } = 3;

    /// <summary>Fallback, wenn kein CAB-Termin konfiguriert ist.</summary>
    public int Level3FallbackBusinessDays { get; set; } = 5;

    /// <summary>Wochentage der CAB-Sitzungen (leer = Fallback-Frist).</summary>
    public List<DayOfWeek> CabDays { get; set; } = [];

    public int EmergencyConfirmationBusinessDays { get; set; } = 2;

    /// <summary>Optionaler automatischer Ablauf nach Fälligkeit + N Werktagen (null = nie, nur Eskalation).</summary>
    public int? ExpireAfterOverdueBusinessDays { get; set; }

    public List<DateOnly> Holidays { get; set; } = [];
}

/// <summary>Ergebnis einer Aktion auf der Freigabe.</summary>
public sealed record ApprovalActionResult(bool Accepted, string Message)
{
    public static ApprovalActionResult Ok(string message) => new(true, message);

    public static ApprovalActionResult Denied(string message) => new(false, message);
}

/// <summary>
/// Zustandsmaschine einer Freigabeanfrage mit den festen Regeln aus Abschnitt 4.5:
/// Einreicher gibt nie selbst frei, Service Principals geben nie frei, Vier-Augen je Stufe,
/// Notfall-Weg für Stufe 2 mit nachträglicher Bestätigung.
/// </summary>
public sealed class ApprovalWorkflow
{
    private readonly List<ApprovalDecisionRecord> _decisions = [];
    private readonly ApprovalOptions _options;

    public ApprovalWorkflow(int level, string submittedBy, DateTimeOffset createdAt, ApprovalOptions? options = null, IEnumerable<ApprovalDecisionRecord>? existing = null)
    {
        if (level is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Freigabestufen sind 0 bis 3.");
        }

        Level = level;
        SubmittedBy = submittedBy;
        CreatedAt = createdAt;
        _options = options ?? new ApprovalOptions();
        DueAt = ComputeDueAt(level, createdAt, _options);
        if (level == 0)
        {
            State = ApprovalState.Approved;
        }

        foreach (var d in existing ?? [])
        {
            _decisions.Add(d);
        }

        if (_decisions.Count > 0)
        {
            Recompute();
        }
    }

    public int Level { get; }

    public string SubmittedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset DueAt { get; }

    public ApprovalState State { get; private set; } = ApprovalState.Pending;

    public bool Emergency { get; private set; }

    public bool EmergencyConfirmed { get; private set; }

    public DateTimeOffset? EmergencyConfirmDueAt { get; private set; }

    public IReadOnlyList<ApprovalDecisionRecord> Decisions => _decisions;

    /// <summary>Rollen-Slots, die für die Stufe erfüllt sein müssen (jeder Slot durch eine andere Person).</summary>
    public static IReadOnlyList<string[]> RequiredSlots(int level) => level switch
    {
        0 => [],
        1 => [[AppRoles.Approver, AppRoles.Packager]],
        2 => [[AppRoles.Packager], [AppRoles.Security]],
        3 => [[AppRoles.Security], [AppRoles.ChangeBoard, AppRoles.PlatformAdmin]],
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static DateTimeOffset ComputeDueAt(int level, DateTimeOffset from, ApprovalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return level switch
        {
            0 => from,
            1 => BusinessDays.Add(from, options.Level1BusinessDays, options.Holidays),
            2 => BusinessDays.Add(from, options.Level2BusinessDays, options.Holidays),
            _ => options.CabDays.Count > 0
                ? BusinessDays.NextOccurrence(from, options.CabDays, options.Holidays)
                : BusinessDays.Add(from, options.Level3FallbackBusinessDays, options.Holidays),
        };
    }

    /// <summary>Darf diese Person bei dieser Anfrage überhaupt entscheiden?</summary>
    public ApprovalActionResult CanDecide(Approver approver)
    {
        ArgumentNullException.ThrowIfNull(approver);
        if (approver.IsServicePrincipal || approver.HasRole(AppRoles.Automation))
        {
            return ApprovalActionResult.Denied("Service Principals dürfen einreichen, aber nie freigeben.");
        }

        if (string.Equals(approver.Id, SubmittedBy, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalActionResult.Denied("Der Einreicher darf nie selbst freigeben (Vier-Augen-Prinzip).");
        }

        if (!RequiredSlots(Level).Any(slot => slot.Any(approver.HasRole)))
        {
            return ApprovalActionResult.Denied($"Für Stufe {Level} fehlt die nötige Rolle.");
        }

        return ApprovalActionResult.Ok("ok");
    }

    public ApprovalActionResult Approve(Approver approver, string comment, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(approver);
        if (State == ApprovalState.Approved && Emergency && !EmergencyConfirmed)
        {
            return ConfirmEmergency(approver, comment, at);
        }

        if (State != ApprovalState.Pending)
        {
            return ApprovalActionResult.Denied($"Die Anfrage ist nicht mehr offen ({State}).");
        }

        var check = CanDecide(approver);
        if (!check.Accepted)
        {
            return check;
        }

        if (_decisions.Any(d => d.Approved && string.Equals(d.ApproverId, approver.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return ApprovalActionResult.Denied("Diese Person hat bereits freigegeben; die nächste Freigabe muss von einer anderen Person kommen.");
        }

        if (!WouldFillOpenSlot(approver))
        {
            return ApprovalActionResult.Denied("Die Rolle dieser Person ist bereits durch eine andere Freigabe abgedeckt.");
        }

        _decisions.Add(new ApprovalDecisionRecord(approver.Id, approver.Roles, true, comment, at));
        Recompute();
        return ApprovalActionResult.Ok(State == ApprovalState.Approved ? "Freigegeben." : "Freigabe erfasst, weitere Freigabe nötig.");
    }

    public ApprovalActionResult Reject(Approver approver, string comment, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(approver);
        if (State != ApprovalState.Pending)
        {
            return ApprovalActionResult.Denied($"Die Anfrage ist nicht mehr offen ({State}).");
        }

        var check = CanDecide(approver);
        if (!check.Accepted)
        {
            return check;
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            return ApprovalActionResult.Denied("Eine Ablehnung braucht eine Begründung.");
        }

        _decisions.Add(new ApprovalDecisionRecord(approver.Id, approver.Roles, false, comment, at));
        State = ApprovalState.Rejected;
        return ApprovalActionResult.Ok("Abgelehnt.");
    }

    /// <summary>
    /// Notfall-Weg: Stufe 2 darf durch einen Security-Verantwortlichen allein freigegeben werden.
    /// Die fehlende Freigabe muss innerhalb der Frist nachträglich bestätigt werden.
    /// </summary>
    public ApprovalActionResult ApproveEmergency(Approver approver, string justification, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(approver);
        if (Level != 2)
        {
            return ApprovalActionResult.Denied("Der Notfall-Weg gilt nur für Stufe 2.");
        }

        if (State != ApprovalState.Pending)
        {
            return ApprovalActionResult.Denied($"Die Anfrage ist nicht mehr offen ({State}).");
        }

        var check = CanDecide(approver);
        if (!check.Accepted)
        {
            return check;
        }

        if (!approver.HasRole(AppRoles.Security))
        {
            return ApprovalActionResult.Denied("Nur ein Security-Verantwortlicher darf den Notfall-Weg nutzen.");
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            return ApprovalActionResult.Denied("Der Notfall-Weg braucht eine Begründung.");
        }

        _decisions.Add(new ApprovalDecisionRecord(approver.Id, approver.Roles, true, justification, at));
        Emergency = true;
        State = ApprovalState.Approved;
        EmergencyConfirmDueAt = BusinessDays.Add(at, _options.EmergencyConfirmationBusinessDays, _options.Holidays);
        return ApprovalActionResult.Ok("Notfall-Freigabe erteilt; nachträgliche Bestätigung erforderlich.");
    }

    public ApprovalActionResult Cancel(string by)
    {
        if (State != ApprovalState.Pending)
        {
            return ApprovalActionResult.Denied($"Die Anfrage ist nicht mehr offen ({State}).");
        }

        if (!string.Equals(by, SubmittedBy, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalActionResult.Denied("Nur der Einreicher kann zurückziehen.");
        }

        State = ApprovalState.Cancelled;
        return ApprovalActionResult.Ok("Zurückgezogen.");
    }

    /// <summary>Ist die Anfrage überfällig und muss eskaliert werden?</summary>
    public bool IsOverdue(DateTimeOffset now) => State == ApprovalState.Pending && now > DueAt;

    public bool IsEmergencyConfirmationOverdue(DateTimeOffset now) =>
        Emergency && !EmergencyConfirmed && EmergencyConfirmDueAt is { } due && now > due;

    /// <summary>Optionaler Ablauf nach Überfälligkeit.</summary>
    public bool ExpireIfDue(DateTimeOffset now)
    {
        if (State != ApprovalState.Pending || _options.ExpireAfterOverdueBusinessDays is not { } extra)
        {
            return false;
        }

        if (now > BusinessDays.Add(DueAt, extra, _options.Holidays))
        {
            State = ApprovalState.Expired;
            return true;
        }

        return false;
    }

    /// <summary>Offene Rollen-Slots (für die Anzeige „wartet auf …“).</summary>
    public IReadOnlyList<string[]> OpenSlots()
    {
        var slots = RequiredSlots(Level);
        var assignment = AssignSlots(_decisions.Where(d => d.Approved && !d.IsEmergencyConfirmation).ToList(), slots);
        return slots.Where((_, i) => assignment[i] is null).ToList();
    }

    private ApprovalActionResult ConfirmEmergency(Approver approver, string comment, DateTimeOffset at)
    {
        var check = CanDecide(approver);
        if (!check.Accepted)
        {
            return check;
        }

        if (_decisions.Any(d => string.Equals(d.ApproverId, approver.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return ApprovalActionResult.Denied("Die Notfall-Freigabe muss von einer anderen Person bestätigt werden.");
        }

        if (!approver.HasRole(AppRoles.Packager))
        {
            return ApprovalActionResult.Denied("Die nachträgliche Bestätigung muss vom Packager kommen (fehlende Rolle der Stufe 2).");
        }

        _decisions.Add(new ApprovalDecisionRecord(approver.Id, approver.Roles, true, comment, at, IsEmergencyConfirmation: true));
        EmergencyConfirmed = true;
        return ApprovalActionResult.Ok("Notfall-Freigabe nachträglich bestätigt.");
    }

    private bool WouldFillOpenSlot(Approver approver)
    {
        var slots = RequiredSlots(Level);
        var before = AssignSlots(_decisions.Where(d => d.Approved).ToList(), slots).Count(a => a is not null);
        var after = AssignSlots(
            [.. _decisions.Where(d => d.Approved), new ApprovalDecisionRecord(approver.Id, approver.Roles, true, string.Empty, DateTimeOffset.MinValue)],
            slots).Count(a => a is not null);
        return after > before;
    }

    private void Recompute()
    {
        if (_decisions.Any(d => !d.Approved))
        {
            State = ApprovalState.Rejected;
            return;
        }

        if (Level == 0)
        {
            State = ApprovalState.Approved;
            return;
        }

        var slots = RequiredSlots(Level);
        var assignment = AssignSlots(_decisions.Where(d => d.Approved && !d.IsEmergencyConfirmation).ToList(), slots);
        if (assignment.All(a => a is not null))
        {
            State = ApprovalState.Approved;
        }
    }

    /// <summary>
    /// Ordnet Freigaben den Rollen-Slots zu, jede Person höchstens einem Slot (bipartites Matching,
    /// bei max. zwei Slots per Backtracking ausreichend).
    /// </summary>
    private static string?[] AssignSlots(IReadOnlyList<ApprovalDecisionRecord> approvals, IReadOnlyList<string[]> slots)
    {
        var people = approvals.GroupBy(a => a.ApproverId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Id: g.Key, Roles: g.SelectMany(x => x.Roles).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var best = new string?[slots.Count];
        var current = new string?[slots.Count];
        var bestCount = -1;

        void Search(int slot, HashSet<string> used)
        {
            if (slot == slots.Count)
            {
                var count = current.Count(c => c is not null);
                if (count > bestCount)
                {
                    bestCount = count;
                    Array.Copy(current, best, current.Length);
                }

                return;
            }

            foreach (var p in people)
            {
                if (!used.Contains(p.Id) && slots[slot].Any(p.Roles.Contains))
                {
                    used.Add(p.Id);
                    current[slot] = p.Id;
                    Search(slot + 1, used);
                    used.Remove(p.Id);
                    current[slot] = null;
                }
            }

            Search(slot + 1, used);
        }

        Search(0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return best;
    }
}

/// <summary>Werktagsrechnung (Sa/So und Feiertage sind keine Werktage).</summary>
public static class BusinessDays
{
    public static DateTimeOffset Add(DateTimeOffset from, int days, IReadOnlyCollection<DateOnly>? holidays = null)
    {
        var result = from;
        var added = 0;
        while (added < days)
        {
            result = result.AddDays(1);
            if (IsBusinessDay(result, holidays))
            {
                added++;
            }
        }

        return result;
    }

    public static DateTimeOffset NextOccurrence(DateTimeOffset from, IReadOnlyCollection<DayOfWeek> days, IReadOnlyCollection<DateOnly>? holidays = null)
    {
        var result = from;
        for (var i = 0; i < 60; i++)
        {
            result = result.AddDays(1);
            if (days.Contains(result.DayOfWeek) && IsBusinessDay(result, holidays))
            {
                return result;
            }
        }

        return Add(from, 5, holidays);
    }

    public static bool IsBusinessDay(DateTimeOffset date, IReadOnlyCollection<DateOnly>? holidays = null) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && (holidays is null || !holidays.Contains(DateOnly.FromDateTime(date.Date)));
}
