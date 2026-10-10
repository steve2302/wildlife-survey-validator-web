using System.Text;

namespace SurveyValidator.Core;

/// <summary>Counts shown in the summary bar.</summary>
public sealed record SurveySummary(
    int Total,
    int Complete,
    int Incomplete,
    int MultipleDistances,
    int TransectErrors,
    int DeleteRequests,
    int TransectStartCount,
    int TransectEndCount)
{
    public bool TransectMarkersMatch => TransectStartCount == TransectEndCount;
    public int NeedingAttention => Incomplete + MultipleDistances + TransectErrors + DeleteRequests;
}

/// <summary>
/// A survey data file loaded into memory: parsing, validation, editing and export.
/// This is the logic from the WinForms MainForm with the user-interface code removed.
/// </summary>
public sealed class SurveyDocument
{
    /// <summary>
    /// Column headings of the cleaned file. An "Other" column is added only when the config
    /// lists other observations.
    /// </summary>
    public string CsvHeader =>
        string.Join(",", SurveyRecord.FixedColumnNames) + ",Species,Counts,GroupSize,Distance" +
        (Template.OtherValues.Count > 0 ? ",Other" : "");

    private readonly List<SurveyRecord> _records = new();
    private readonly Stack<(int Index, SurveyRecord Record)> _deleted = new();

    public SurveyTemplate Template { get; }
    public IReadOnlyList<SurveyRecord> Records => _records;

    /// <summary>True if the first line of the file was recognised as a header and skipped.</summary>
    public bool HadHeaderLine { get; private set; }

    /// <summary>True once any value has been edited or any row deleted.</summary>
    public bool IsModified { get; private set; }

    public int DeletedCount => _deleted.Count;

    public SurveySummary Summary { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    private SurveyDocument(SurveyTemplate template) => Template = template;

    // ------------------------------------------------------------------ loading

    /// <summary>Reads the text of a survey file.</summary>
    public static SurveyDocument Load(string text, SurveyTemplate template)
    {
        var doc = new SurveyDocument(template);

        using var reader = new StringReader(text ?? "");
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;

            if (lineNumber == 1)
            {
                line = line.TrimStart('﻿');
                // The first line is a header if it has no comma and contains the header marker.
                if (line.Length > 0 && !line.Contains(',') && line.Contains(template.HeaderMarker))
                {
                    doc.HadHeaderLine = true;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            doc._records.Add(doc.ParseRecord(line, lineNumber));
        }

        doc.Revalidate();
        return doc;
    }

    private SurveyRecord ParseRecord(string line, int lineNumber)
    {
        List<string> fields = ParseCsvLine(line);
        var record = new SurveyRecord { Line = lineNumber, OriginalLine = line };

        int fixedCount = Math.Min(fields.Count, SurveyRecord.FixedColumnCount);
        for (int i = 0; i < fixedCount; i++)
            record.Fixed[i] = fields[i];

        // A transect marker anywhere on the line makes this a marker row.
        foreach (string field in fields)
        {
            string trimmed = field.Trim();
            if (Template.IsTransectMarker(trimmed))
            {
                record.Species = trimmed;
                return record;
            }
        }

        string species = "";
        var distances = new List<string>();
        var counts = new List<string>();
        var others = new List<string>();
        var unrecognised = new List<string>();

        for (int i = SurveyRecord.FixedColumnCount; i < fields.Count; i++)
        {
            string field = fields[i].Trim();
            switch (Template.RoleOf(field))
            {
                case ValueRole.Species: species = field; break;
                case ValueRole.Distance: distances.Add(field); break;
                case ValueRole.Count: counts.Add(field); break;
                case ValueRole.Other: if (!others.Contains(field)) others.Add(field); break;
                case ValueRole.DeleteRequest:
                    record.DeleteRequested = true;
                    record.DeleteRequestValue = field;
                    break;
                case ValueRole.Ignore: break;
                default: if (field.Length > 0) unrecognised.Add(field); break;
            }
        }

        record.Species = species;
        // When several distances were recorded, the last one is the default: it is the observer's latest entry.
        record.Distance = distances.Count > 0 ? distances[^1] : "";
        record.Counts = string.Join(",", counts);
        record.Other = string.Join(";", others);
        record.RecordedDistances = distances;
        record.UnrecognisedEntries = unrecognised;
        record.HasMultipleDistances = distances.Count > 1;
        return record;
    }

    /// <summary>Splits one CSV line into fields. Quotes group text containing commas and are removed.</summary>
    public static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var field = new StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        result.Add(field.ToString());
        return result;
    }

    // --------------------------------------------------------------- validation

    /// <summary>Recomputes completeness, transect sequence errors, notes and the summary for every row.</summary>
    public void Revalidate()
    {
        int startCount = 0, endCount = 0;
        bool transectOpen = false;
        SurveyRecord? lastStart = null;
        var transectMessages = new Dictionary<SurveyRecord, string>();

        foreach (SurveyRecord r in _records)
        {
            r.TransectError = false;
            r.IsTransectMarker = Template.IsTransectMarker(r.Species.Trim());

            var missing = new List<string>();
            if (!r.IsTransectMarker)
            {
                if (string.IsNullOrWhiteSpace(r.Species)) missing.Add("species");
                if (string.IsNullOrWhiteSpace(r.Distance)) missing.Add("distance");
                if (string.IsNullOrWhiteSpace(r.Counts)) missing.Add("count");
            }
            r.MissingFields = missing;
            r.IsComplete = missing.Count == 0;

            if (!r.IsTransectMarker)
                continue;

            string marker = r.Species.Trim();
            if (marker == Template.TransectStart)
            {
                startCount++;
                if (transectOpen)
                {
                    // Opening a transect when one is already open.
                    r.TransectError = true;
                    transectMessages[r] =
                        $"Transect error: '{Template.TransectStart}' while the transect opened at line {lastStart!.Line} is still open";
                }
                lastStart = r;
                transectOpen = true;
            }
            else
            {
                endCount++;
                if (!transectOpen)
                {
                    // Closing a transect when none is open.
                    r.TransectError = true;
                    transectMessages[r] =
                        $"Transect error: '{Template.TransectEnd}' without preceding '{Template.TransectStart}'";
                }
                transectOpen = false;
            }
        }

        // The last start marker was never closed.
        if (transectOpen && lastStart != null)
        {
            lastStart.TransectError = true;
            transectMessages[lastStart] =
                $"Transect error: '{Template.TransectStart}' without matching '{Template.TransectEnd}'";
        }

        int incomplete = 0, multiple = 0, transectErrors = 0, deleteRequests = 0;
        foreach (SurveyRecord r in _records)
        {
            r.Notes = BuildNotes(r, transectMessages);
            if (!r.IsComplete) incomplete++;
            if (r.HasMultipleDistances) multiple++;
            if (r.TransectError) transectErrors++;
            if (r.DeleteRequested) deleteRequests++;
        }

        Summary = new SurveySummary(
            _records.Count, _records.Count - incomplete, incomplete, multiple, transectErrors, deleteRequests,
            startCount, endCount);
    }

    private static string BuildNotes(SurveyRecord r, Dictionary<SurveyRecord, string> transectMessages)
    {
        if (r.IsTransectMarker)
            return r.TransectError ? transectMessages[r] : $"Transect marker: {r.Species.Trim()}";

        if (r.DeleteRequested)
            return $"Marked for deletion by the observer ({r.DeleteRequestValue})";

        if (!r.IsComplete)
        {
            string notes = "Missing: " + string.Join(", ", r.MissingFields);
            if (r.UnrecognisedEntries.Count > 0)
                notes += " (unrecognised: " + string.Join(", ", r.UnrecognisedEntries) + ")";
            return notes;
        }

        if (r.HasMultipleDistances)
            return $"Multiple distances found: {r.RecordedDistances.Count}";

        return r.MultipleDistancesResolved
            ? "Complete record (multiple distances resolved)"
            : "Complete record";
    }

    // ------------------------------------------------------------------ editing
    // Each setter returns null on success, or a message explaining why the value was rejected.

    public string? SetSpecies(SurveyRecord record, string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0 && !Template.IsSpecies(value) && !Template.IsTransectMarker(value))
        {
            return "Invalid species. Valid values are: " + string.Join(", ", Template.Species) +
                   $" or transect markers: {Template.TransectStart}, {Template.TransectEnd}";
        }

        record.Species = value;
        Changed();
        return null;
    }

    public string? SetDistance(SurveyRecord record, string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0 && !Template.IsDistanceClass(value))
            return "Invalid distance class. Valid values are: " + string.Join(", ", Template.DistanceClasses);

        record.Distance = value;
        if (value.Length > 0 && record.RecordedDistances.Count > 1)
        {
            // Choosing a distance settles a line that had more than one.
            record.HasMultipleDistances = false;
            record.MultipleDistancesResolved = true;
        }
        Changed();
        return null;
    }

    public string? SetCounts(SurveyRecord record, string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0)
        {
            string[] counts = value.Split(',').Select(c => c.Trim()).ToArray();
            foreach (string count in counts)
            {
                if (!Template.IsCount(count))
                {
                    return "Invalid count. Valid values are: " + string.Join(", ", Template.Counts) +
                           " (comma-separated for several)";
                }
            }

            value = string.Join(",", counts); // stored without spaces, the same as when read from a file
        }

        record.Counts = value;
        Changed();
        return null;
    }

    /// <summary>Sets the other observations on a record, separated by semicolons.</summary>
    public string? SetOther(SurveyRecord record, string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 0)
        {
            string[] others = value.Split(';').Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToArray();
            foreach (string other in others)
            {
                if (!Template.IsOther(other))
                {
                    return "Invalid entry. Valid values are: " + string.Join(", ", Template.OtherValues) +
                           " (separate several with a semicolon)";
                }
            }

            value = string.Join(";", others);
        }

        record.Other = value;
        Changed();
        return null;
    }

    /// <summary>Clears the observer's delete request so the record is kept.</summary>
    public void KeepRecord(SurveyRecord record)
    {
        record.DeleteRequested = false;
        Changed();
    }

    /// <summary>Edits one of the 11 auto-recorded values. Any text is accepted.</summary>
    public void SetFixed(SurveyRecord record, int column, string? value)
    {
        record.Fixed[column] = value ?? "";
        Changed();
    }

    public void Delete(SurveyRecord record)
    {
        int index = _records.IndexOf(record);
        if (index < 0) return;
        _records.RemoveAt(index);
        _deleted.Push((index, record));
        Changed();
    }

    /// <summary>Puts back the most recently deleted row. Returns it, or null if there is nothing to undo.</summary>
    public SurveyRecord? UndoDelete()
    {
        if (_deleted.Count == 0) return null;
        var (index, record) = _deleted.Pop();
        _records.Insert(Math.Min(index, _records.Count), record);
        Changed();
        return record;
    }

    private void Changed()
    {
        IsModified = true;
        Revalidate();
    }

    /// <summary>Call after the cleaned file has been saved.</summary>
    public void MarkSaved() => IsModified = false;

    // ------------------------------------------------------------------- export

    /// <summary>
    /// The cleaned file as CSV text: a header row, then one line per record with the 11
    /// auto-recorded columns followed by species, counts, group size (the counts added together),
    /// distance and, if the config has any, other observations. Lines end with CR LF.
    /// </summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.Append(CsvHeader).Append("\r\n");

        foreach (SurveyRecord r in _records)
        {
            var fields = new List<string>(SurveyRecord.FixedColumnCount + 5);
            foreach (string f in r.Fixed)
                fields.Add(FormatCsvField(f));

            fields.Add(FormatCsvField(r.Species));
            if (r.IsTransectMarker)
            {
                fields.Add("");
                fields.Add("");
                fields.Add("");
            }
            else
            {
                fields.Add(FormatCsvField(r.Counts));
                fields.Add(r.GroupSize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
                fields.Add(FormatCsvField(r.Distance));
            }

            if (Template.OtherValues.Count > 0)
                fields.Add(FormatCsvField(r.Other));

            sb.Append(string.Join(",", fields)).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>The CSV as UTF-8 bytes with a byte-order mark, as the desktop application wrote it.</summary>
    public byte[] ToCsvBytes()
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] body = Encoding.UTF8.GetBytes(ToCsv());
        var bytes = new byte[bom.Length + body.Length];
        bom.CopyTo(bytes, 0);
        body.CopyTo(bytes, bom.Length);
        return bytes;
    }

    public static string FormatCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
