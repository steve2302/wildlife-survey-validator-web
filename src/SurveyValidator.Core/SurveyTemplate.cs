using System.Text;

namespace SurveyValidator.Core;

/// <summary>Raised when a template file cannot be read.</summary>
public sealed class TemplateException : Exception
{
    public TemplateException(string message) : base(message) { }
}

/// <summary>
/// The lists of values a survey file is checked against: species, distance classes,
/// group sizes and the two transect markers. Replaces the lists that were hard-coded
/// in the WinForms version. A template is a small text file, one entry per line, in
/// the form <c>category,value</c>.
/// </summary>
public sealed class SurveyTemplate
{
    /// <summary>The values built into the original desktop application.</summary>
    public const string DefaultText =
        "# Wildlife Survey Validator template\n" +
        "# One entry per line: category,value\n" +
        "# Categories: species, distance, group_size, transect_start, transect_end, header_marker\n" +
        "category,value\n" +
        "species,red_kangaroo\n" +
        "species,grey_kangaroo\n" +
        "species,goat\n" +
        "distance,yellow(0-50m)\n" +
        "distance,green(50-100m)\n" +
        "distance,blue(100-200m)\n" +
        "distance,black(200-300m)\n" +
        "group_size,1\n" +
        "group_size,2\n" +
        "group_size,3\n" +
        "group_size,4\n" +
        "transect_start,break_on\n" +
        "transect_end,break_off\n" +
        "header_marker,AM_AM_SML\n";

    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> DistanceClasses { get; }
    public IReadOnlyList<string> GroupSizes { get; }

    /// <summary>Marker that opens a transect.</summary>
    public string TransectStart { get; }

    /// <summary>Marker that closes a transect.</summary>
    public string TransectEnd { get; }

    /// <summary>
    /// Text that identifies the first line of a file as a header line to skip.
    /// The first line is a header only if it contains this text and has no comma.
    /// </summary>
    public string HeaderMarker { get; }

    public SurveyTemplate(
        IEnumerable<string> species,
        IEnumerable<string> distanceClasses,
        IEnumerable<string> groupSizes,
        string transectStart,
        string transectEnd,
        string headerMarker)
    {
        Species = species.Distinct().ToList();
        DistanceClasses = distanceClasses.Distinct().ToList();
        GroupSizes = groupSizes.Distinct().ToList();
        TransectStart = transectStart;
        TransectEnd = transectEnd;
        HeaderMarker = headerMarker;
    }

    public static SurveyTemplate Default { get; } = Parse(DefaultText);

    public bool IsSpecies(string value) => Species.Contains(value);
    public bool IsDistanceClass(string value) => DistanceClasses.Contains(value);
    public bool IsGroupSize(string value) => GroupSizes.Contains(value);
    public bool IsTransectMarker(string value) => value == TransectStart || value == TransectEnd;

    /// <summary>The same template with the meaning of the two transect markers exchanged.</summary>
    public SurveyTemplate WithTransectMarkersSwapped() =>
        new(Species, DistanceClasses, GroupSizes, TransectEnd, TransectStart, HeaderMarker);

    /// <summary>Reads a template from text. Throws <see cref="TemplateException"/> if it is not usable.</summary>
    public static SurveyTemplate Parse(string text)
    {
        var species = new List<string>();
        var distances = new List<string>();
        var groupSizes = new List<string>();
        string? transectStart = null;
        string? transectEnd = null;
        string? headerMarker = null;

        using var reader = new StringReader(text ?? "");
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            string trimmed = line.Trim().TrimStart('﻿');
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            int comma = trimmed.IndexOf(',');
            if (comma < 0)
                throw new TemplateException($"Template line {lineNumber}: expected 'category,value' but found '{trimmed}'.");

            string category = trimmed[..comma].Trim().ToLowerInvariant();
            string value = Unquote(trimmed[(comma + 1)..].Trim());

            if (category == "category" && value.ToLowerInvariant() == "value")
                continue; // optional column heading row

            if (value.Length == 0)
                throw new TemplateException($"Template line {lineNumber}: '{category}' has no value.");

            switch (category)
            {
                case "species": species.Add(value); break;
                case "distance": distances.Add(value); break;
                case "group_size": groupSizes.Add(value); break;
                case "transect_start": transectStart = value; break;
                case "transect_end": transectEnd = value; break;
                case "header_marker": headerMarker = value; break;
                default:
                    throw new TemplateException(
                        $"Template line {lineNumber}: unknown category '{category}'. " +
                        "Use species, distance, group_size, transect_start, transect_end or header_marker.");
            }
        }

        if (species.Count == 0) throw new TemplateException("The template lists no species.");
        if (distances.Count == 0) throw new TemplateException("The template lists no distance classes.");
        if (groupSizes.Count == 0) throw new TemplateException("The template lists no group sizes.");

        transectStart ??= Default.TransectStart;
        transectEnd ??= Default.TransectEnd;
        if (transectStart == transectEnd)
            throw new TemplateException("transect_start and transect_end must be different.");

        foreach (string g in groupSizes)
        {
            if (g.Contains(','))
                throw new TemplateException($"Group size '{g}' must not contain a comma.");
        }

        return new SurveyTemplate(species, distances, groupSizes, transectStart, transectEnd,
            headerMarker ?? Default.HeaderMarker);
    }

    /// <summary>Writes the template in the format <see cref="Parse"/> reads.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append("# Wildlife Survey Validator template\n");
        sb.Append("# One entry per line: category,value\n");
        sb.Append("# Categories: species, distance, group_size, transect_start, transect_end, header_marker\n");
        sb.Append("category,value\n");
        foreach (string s in Species) sb.Append("species,").Append(s).Append('\n');
        foreach (string d in DistanceClasses) sb.Append("distance,").Append(d).Append('\n');
        foreach (string g in GroupSizes) sb.Append("group_size,").Append(g).Append('\n');
        sb.Append("transect_start,").Append(TransectStart).Append('\n');
        sb.Append("transect_end,").Append(TransectEnd).Append('\n');
        sb.Append("header_marker,").Append(HeaderMarker).Append('\n');
        return sb.ToString();
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\"\"", "\"").Trim();
        return value;
    }
}
