using System.Collections.Generic;
using SheetMan.Models;

namespace SheetMan;

/// <summary>
/// Collects problems found while checking a cooked model, so a run can report
/// everything wrong with a workbook at once.
///
/// Throwing on the first bad cell makes fixing a sheet a serial process: correct
/// one value, re-run, discover the next. Since the checks here are independent of
/// each other, there is no reason to stop at the first.
///
/// SheetManException has carried a Details list all along and Program prints it;
/// nothing ever filled it in.
/// </summary>
public sealed class Diagnostics
{
    private readonly List<SheetManException.Detail> _errors = new List<SheetManException.Detail>();

    /// <summary>Number of problems recorded so far.</summary>
    public int Count => _errors.Count;

    /// <summary>Records a problem and carries on.</summary>
    public void Error(Location location, string message)
    {
        _errors.Add(new SheetManException.Detail { Location = location, Message = message });
    }

    /// <summary>
    /// Throws a single exception carrying every recorded problem, or returns
    /// quietly if there were none.
    /// </summary>
    /// <param name="summary">
    /// Headline shown above the list. Should say what was being checked, since the
    /// individual entries carry their own locations.
    /// </param>
    public void ThrowIfAny(string summary)
    {
        if (_errors.Count == 0)
            return;

        string headline = _errors.Count == 1
            ? summary
            : $"{summary} ({_errors.Count} problems)";

        throw new SheetManException(headline) { Details = _errors };
    }
}
