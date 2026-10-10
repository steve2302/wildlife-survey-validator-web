using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SurveyValidator.Core;

/// <summary>Raised when a config or template file cannot be read.</summary>
public sealed class TemplateException : Exception
{
    public TemplateException(string message) : base(message) { }
}

/// <summary>What a recorded value means when it appears on a line of a survey file.</summary>
public enum ValueRole
{
    Species,
    Count,
    Distance,
    TransectStart,
    TransectEnd,
    /// <summary>Any other observation attached to a record, such as glare.</summary>
    Other,
    /// <summary>The observer asked for the record to be deleted.</summary>
    DeleteRequest,
    /// <summary>Recognised but of no interest; dropped without comment.</summary>
    Ignore
}

/// <summary>One recorded value and its meaning.</summary>
public sealed record TemplateEntry(string Value, ValueRole Role, string Description);

/// <summary>
/// The values a survey file is checked against. Nothing is fixed in the program: the list is
/// built from the data logger's button config file (Button, AudioText, RecordedData, NewRecord),
/// so changing that file changes what the cleaner accepts.
/// </summary>
public sealed class SurveyTemplate
{
    public const string DefaultHeaderMarker = "AM_AM_SML";

    private static readonly Regex NumberRange = new(@"\d+\s*-\s*\d+", RegexOptions.Compiled);

    private readonly Dictionary<string, ValueRole> _roles;

    /// <summary>Every recorded value, in config-file order.</summary>
    public IReadOnlyList<TemplateEntry> Entries { get; }

    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> Counts { get; }
    public IReadOnlyList<string> DistanceClasses { get; }
    public IReadOnlyList<string> OtherValues { get; }

    /// <summary>Marker that opens a transect, or an empty string if the config has none.</summary>
    public string TransectStart { get; }

    /// <summary>Marker that closes a transect, or an empty string if the config has none.</summary>
    public string TransectEnd { get; }

    /// <summary>
    /// The first line of a data file is skipped as a header if it has no comma and contains this text.
    /// </summary>
    public string HeaderMarker { get; }

    public SurveyTemplate(IEnumerable<TemplateEntry> entries, string headerMarker = DefaultHeaderMarker)
    {
        var list = new List<TemplateEntry>();
        _roles = new Dictionary<string, ValueRole>();
        foreach (TemplateEntry e in entries)
        {
            if (_roles.ContainsKey(e.Value)) continue; // the same value on two buttons counts once
            _roles[e.Value] = e.Role;
            list.Add(e);
        }

        Entries = list;
        Species = ValuesWith(ValueRole.Species);
        Counts = ValuesWith(ValueRole.Count);
        DistanceClasses = ValuesWith(ValueRole.Distance);
        OtherValues = ValuesWith(ValueRole.Other);
        TransectStart = ValuesWith(ValueRole.TransectStart).FirstOrDefault() ?? "";
        TransectEnd = ValuesWith(ValueRole.TransectEnd).FirstOrDefault() ?? "";
        HeaderMarker = string.IsNullOrWhiteSpace(headerMarker) ? DefaultHeaderMarker : headerMarker;
    }

    private List<string> ValuesWith(ValueRole role) =>
        Entries.Where(e => e.Role == role).Select(e => e.Value).ToList();

    /// <summary>The meaning of a recorded value, or null if the config does not list it.</summary>
    public ValueRole? RoleOf(string value) => _roles.TryGetValue(value, out ValueRole role) ? role : null;

    public bool IsSpecies(string value) => RoleOf(value) == ValueRole.Species;
    public bool IsDistanceClass(string value) => RoleOf(value) == ValueRole.Distance;
    public bool IsCount(string value) => RoleOf(value) == ValueRole.Count;
    public bool IsOther(string value) => RoleOf(value) == ValueRole.Other;
    public bool IsTransectMarker(string value) =>
        value.Length > 0 && (value == TransectStart || value == TransectEnd);

    /// <summary>Problems that stop the template being useful, or an empty list.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Species.Count == 0) problems.Add("no species");
        if (Counts.Count == 0) problems.Add("no counts");
        if (DistanceClasses.Count == 0) problems.Add("no distance classes");
        return problems;
    }

    /// <summary>
    /// The same template with one value given a different meaning. There is one start marker and
    /// one end marker, so a value that held the new role takes over the changed value's old role.
    /// </summary>
    public SurveyTemplate WithRole(string value, ValueRole newRole)
    {
        ValueRole? oldRole = RoleOf(value);
        if (oldRole is null || oldRole == newRole) return this;
        bool single = newRole is ValueRole.TransectStart or ValueRole.TransectEnd;

        return new SurveyTemplate(Entries.Select(e =>
            e.Value == value ? e with { Role = newRole }
            : single && e.Role == newRole
                ? e with { Role = oldRole is ValueRole.TransectStart or ValueRole.TransectEnd ? oldRole.Value : ValueRole.Other }
                : e), HeaderMarker);
    }

    // ------------------------------------------------------------------ reading

    /// <summary>
    /// Reads either the data logger's button config file or a template saved by this program.
    /// Throws <see cref="TemplateException"/> if the text is neither.
    /// </summary>
    public static SurveyTemplate Parse(string text)
    {
        var lines = new List<(int Number, string Text)>();
        using (var reader = new StringReader(text ?? ""))
        {
            string? line;
            int n = 0;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                string trimmed = line.Trim().TrimStart('﻿');
                if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                    lines.Add((n, trimmed));
            }
        }

        if (lines.Count == 0)
            throw new TemplateException("The file is empty.");

        List<string> header = SplitCsv(lines[0].Text).Select(h => h.Trim().ToLowerInvariant()).ToList();
        SurveyTemplate template = header.Contains("recordeddata")
            ? ParseLoggerConfig(header, lines.Skip(1))
            : ParseSavedTemplate(lines);

        IReadOnlyList<string> problems = template.Problems();
        if (problems.Count > 0)
            throw new TemplateException("The file lists " + string.Join(", ", problems) + ".");
        return template;
    }

    /// <summary>
    /// The logger's config: one row per gamepad button. The meaning of each recorded value is
    /// worked out from the row (see <see cref="InferRole"/>).
    /// </summary>
    private static SurveyTemplate ParseLoggerConfig(List<string> header, IEnumerable<(int Number, string Text)> rows)
    {
        int valueColumn = header.IndexOf("recordeddata");
        int buttonColumn = header.IndexOf("button");
        int audioColumn = header.IndexOf("audiotext");
        int newRecordColumn = header.IndexOf("newrecord");

        var entries = new List<TemplateEntry>();
        foreach (var (number, text) in rows)
        {
            List<string> fields = SplitCsv(text);
            string Field(int column) => column >= 0 && column < fields.Count ? fields[column].Trim() : "";

            string value = Field(valueColumn);
            if (value.Length == 0 || value.Equals("NA", StringComparison.OrdinalIgnoreCase))
                continue; // button not configured

            if (value.Contains(','))
                throw new TemplateException($"Config line {number}: the recorded value '{value}' must not contain a comma.");

            bool newRecord = Field(newRecordColumn).Equals("yes", StringComparison.OrdinalIgnoreCase);
            entries.Add(new TemplateEntry(value, InferRole(Field(buttonColumn), value, newRecord), Field(audioColumn)));
        }

        return new SurveyTemplate(entries);
    }

    /// <summary>
    /// Works out what a config row's recorded value means:
    /// the Start button's two states are the transect markers; a whole number is a count;
    /// any other value that starts a new record is a species; a value containing a number
    /// range such as 100-200 is a distance class; a value starting with "delete" is a delete
    /// request; anything else is another observation.
    /// </summary>
    public static ValueRole InferRole(string button, string value, bool startsNewRecord)
    {
        if (button.Equals("Start_On", StringComparison.OrdinalIgnoreCase)) return ValueRole.TransectStart;
        if (button.Equals("Start_Off", StringComparison.OrdinalIgnoreCase)) return ValueRole.TransectEnd;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return ValueRole.Count;
        if (startsNewRecord) return ValueRole.Species;
        if (NumberRange.IsMatch(value)) return ValueRole.Distance;
        if (value.StartsWith("delete", StringComparison.OrdinalIgnoreCase)) return ValueRole.DeleteRequest;
        return ValueRole.Other;
    }

    private static readonly (string Name, ValueRole Role)[] RoleNames =
    {
        ("species", ValueRole.Species),
        ("count", ValueRole.Count),
        ("distance", ValueRole.Distance),
        ("transect_start", ValueRole.TransectStart),
        ("transect_end", ValueRole.TransectEnd),
        ("other", ValueRole.Other),
        ("delete_request", ValueRole.DeleteRequest),
        ("ignore", ValueRole.Ignore),
    };

    /// <summary>A template saved by this program: one <c>role,value</c> entry per line.</summary>
    private static SurveyTemplate ParseSavedTemplate(List<(int Number, string Text)> lines)
    {
        var entries = new List<TemplateEntry>();
        string headerMarker = DefaultHeaderMarker;

        foreach (var (number, text) in lines)
        {
            List<string> fields = SplitCsv(text);
            if (fields.Count < 2)
                throw new TemplateException(
                    $"Line {number}: expected the logger's config file (with a RecordedData column) or 'role,value' lines, but found '{text}'.");

            string roleName = fields[0].Trim().ToLowerInvariant();
            string value = fields[1].Trim();
            if (roleName == "role" && value.ToLowerInvariant() == "value")
                continue; // column heading row
            if (value.Length == 0)
                throw new TemplateException($"Line {number}: '{roleName}' has no value.");

            if (roleName == "header_marker")
            {
                headerMarker = value;
                continue;
            }

            int index = Array.FindIndex(RoleNames, r => r.Name == roleName);
            if (index < 0)
                throw new TemplateException(
                    $"Line {number}: unknown role '{roleName}'. Use " + string.Join(", ", RoleNames.Select(r => r.Name)) + " or header_marker.");
            if (value.Contains(','))
                throw new TemplateException($"Line {number}: the value '{value}' must not contain a comma.");

            entries.Add(new TemplateEntry(value, RoleNames[index].Role, fields.Count > 2 ? fields[2].Trim() : ""));
        }

        return new SurveyTemplate(entries, headerMarker);
    }

    /// <summary>Writes the template, with the meaning of every value stated, in a form <see cref="Parse"/> reads.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append("# Wildlife Survey Validator template\n");
        sb.Append("# One entry per line: role,value,description\n");
        sb.Append("# Roles: ").Append(string.Join(", ", RoleNames.Select(r => r.Name))).Append(", header_marker\n");
        sb.Append("role,value,description\n");
        foreach (TemplateEntry e in Entries)
        {
            sb.Append(RoleNames.First(r => r.Role == e.Role).Name).Append(',')
              .Append(e.Value).Append(',')
              .Append(SurveyDocument.FormatCsvField(e.Description)).Append('\n');
        }
        sb.Append("header_marker,").Append(HeaderMarker).Append(",\n");
        return sb.ToString();
    }

    public static string RoleLabel(ValueRole role) => role switch
    {
        ValueRole.Species => "Species",
        ValueRole.Count => "Count",
        ValueRole.Distance => "Distance class",
        ValueRole.TransectStart => "Transect start",
        ValueRole.TransectEnd => "Transect end",
        ValueRole.Other => "Other observation",
        ValueRole.DeleteRequest => "Delete request",
        _ => "Ignore"
    };

    private static List<string> SplitCsv(string line) => SurveyDocument.ParseCsvLine(line);
}
