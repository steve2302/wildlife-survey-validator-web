namespace SurveyValidator.Core;

public enum RowStatus
{
    Ok,
    MultipleDistances,
    Incomplete,
    DeleteRequested,
    TransectError
}

/// <summary>One line of the survey file.</summary>
public sealed class SurveyRecord
{
    /// <summary>Number of auto-recorded columns at the start of every line.</summary>
    public const int FixedColumnCount = 11;

    public static readonly IReadOnlyList<string> FixedColumnNames = new[]
    {
        "Time", "TimeOfDay", "ObserverID", "ObserverPosition", "UTC", "Latitude",
        "Longitude", "Bearing", "Altitude", "VTOL", "HTOL"
    };

    /// <summary>Line number in the source file, counting from 1 and including any header and blank lines.</summary>
    public int Line { get; init; }

    /// <summary>The line exactly as it was read.</summary>
    public string OriginalLine { get; init; } = "";

    /// <summary>The 11 auto-recorded values (time, time of day, observer id ... HTOL).</summary>
    public string[] Fixed { get; } = Enumerable.Repeat("", FixedColumnCount).ToArray();

    public string Species { get; internal set; } = "";

    /// <summary>The counts recorded for the group, comma-separated, for example "4,4,2".</summary>
    public string Counts { get; internal set; } = "";

    /// <summary>Other observations recorded on the line (for example glare), separated by semicolons.</summary>
    public string Other { get; internal set; } = "";

    /// <summary>The counts added together, or null if there are none or one is not a whole number.</summary>
    public int? GroupSize
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Counts)) return null;
            int total = 0;
            foreach (string part in Counts.Split(','))
            {
                if (!int.TryParse(part.Trim(), out int n)) return null;
                total += n;
            }
            return total;
        }
    }

    /// <summary>True if the observer asked for this record to be deleted.</summary>
    public bool DeleteRequested { get; internal set; }

    /// <summary>The recorded value that asked for deletion.</summary>
    public string DeleteRequestValue { get; internal set; } = "";
    public string Distance { get; internal set; } = "";

    /// <summary>Every distance class found on the line, in the order recorded.</summary>
    public IReadOnlyList<string> RecordedDistances { get; internal set; } = Array.Empty<string>();

    /// <summary>Entries after the 11 auto-recorded columns that the config does not list.</summary>
    public IReadOnlyList<string> UnrecognisedEntries { get; internal set; } = Array.Empty<string>();

    /// <summary>True while a line with more than one distance class has not had one chosen.</summary>
    public bool HasMultipleDistances { get; internal set; }

    /// <summary>True once a distance has been chosen for a line that had more than one.</summary>
    public bool MultipleDistancesResolved { get; internal set; }

    // --- Set by SurveyDocument.Revalidate ---

    public bool IsTransectMarker { get; internal set; }
    public bool IsComplete { get; internal set; }
    public bool TransectError { get; internal set; }
    public IReadOnlyList<string> MissingFields { get; internal set; } = Array.Empty<string>();
    public string Notes { get; internal set; } = "";

    public bool IsMissing(string field) => MissingFields.Contains(field);

    /// <summary>The single most important issue on the row, used for colouring.</summary>
    public RowStatus Status =>
        TransectError ? RowStatus.TransectError
        : DeleteRequested ? RowStatus.DeleteRequested
        : !IsComplete ? RowStatus.Incomplete
        : HasMultipleDistances ? RowStatus.MultipleDistances
        : RowStatus.Ok;

    /// <summary>Text for the "recorded distances" column.</summary>
    public string AllDistancesText
    {
        get
        {
            if (RecordedDistances.Count > 1)
            {
                string all = string.Join(", ", RecordedDistances);
                return MultipleDistancesResolved ? $"Selected: {Distance} from [{all}]" : all;
            }
            return Distance;
        }
    }
}
