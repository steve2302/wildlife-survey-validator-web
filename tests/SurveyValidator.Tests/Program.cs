using System.Text;
using SurveyValidator.Core;

// Minimal test runner: each test is a named action; the exit code is the number of failures.

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));

static void Equal<T>(T expected, T actual, string what = "")
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{what} expected <{expected}> but was <{actual}>");
}
static void True(bool condition, string what) { if (!condition) throw new Exception(what); }

const string F = "t,tod,obs,pos,utc,lat,lon,brg,alt,vtol,htol"; // the 11 auto-recorded columns
SurveyTemplate T = SurveyTemplate.Default;
SurveyDocument Doc(params string[] lines) => SurveyDocument.Load(string.Join("\r\n", lines), T);

// ---------------------------------------------------------------- template

Test("default template holds the original lists", () =>
{
    Equal("red_kangaroo,grey_kangaroo,goat", string.Join(",", T.Species));
    Equal("yellow(0-50m),green(50-100m),blue(100-200m),black(200-300m)", string.Join(",", T.DistanceClasses));
    Equal("1,2,3,4", string.Join(",", T.GroupSizes));
    Equal("break_on", T.TransectStart);
    Equal("break_off", T.TransectEnd);
    Equal("AM_AM_SML", T.HeaderMarker);
});

Test("template round-trips through text", () =>
{
    SurveyTemplate again = SurveyTemplate.Parse(T.ToText());
    Equal(T.ToText(), again.ToText());
});

Test("template accepts custom lists, comments, CRLF and a BOM", () =>
{
    var t = SurveyTemplate.Parse("﻿# pigs\r\nSpecies, feral_pig\r\nspecies,deer\r\n\r\ndistance,near\r\ngroup_size,1\r\ngroup_size,5\r\n");
    Equal("feral_pig,deer", string.Join(",", t.Species));
    Equal("near", t.DistanceClasses[0]);
    Equal("1,5", string.Join(",", t.GroupSizes));
    Equal("break_on", t.TransectStart, "default start marker");
});

Test("template errors are reported", () =>
{
    foreach (string bad in new[] { "species,goat", "nonsense", "colour,red\nspecies,a\ndistance,b\ngroup_size,1",
                 "species,a\ndistance,b\ngroup_size,1\ntransect_start,x\ntransect_end,x" })
    {
        bool threw = false;
        try { SurveyTemplate.Parse(bad); } catch (TemplateException) { threw = true; }
        True(threw, $"no error for: {bad}");
    }
});

// ----------------------------------------------------------------- parsing

Test("complete record", () =>
{
    var d = Doc($"{F},red_kangaroo,yellow(0-50m),2");
    var r = d.Records[0];
    Equal("red_kangaroo", r.Species); Equal("yellow(0-50m)", r.Distance); Equal("2", r.GroupSize);
    Equal("t", r.Fixed[0]); Equal("htol", r.Fixed[10]);
    True(r.IsComplete, "complete"); Equal("Complete record", r.Notes); Equal(RowStatus.Ok, r.Status);
});

Test("entries may appear in any order and with spaces", () =>
{
    var r = Doc($"{F}, 3 , goat, blue(100-200m)").Records[0];
    Equal("goat", r.Species); Equal("3", r.GroupSize); Equal("blue(100-200m)", r.Distance);
});

Test("several group sizes are kept comma-separated", () =>
{
    var r = Doc($"{F},red_kangaroo,4,4,2,black(200-300m)").Records[0];
    Equal("4,4,2", r.GroupSize); True(r.IsComplete, "complete");
});

Test("missing fields are identified", () =>
{
    var d = Doc($"{F},red_kangaroo,yellow(0-50m)", $"{F},3,green(50-100m)", $"{F},goat,2", F);
    Equal("Missing: group size", d.Records[0].Notes);
    Equal("Missing: species", d.Records[1].Notes);
    Equal("Missing: distance", d.Records[2].Notes);
    Equal("Missing: species, distance, group size", d.Records[3].Notes);
    Equal(4, d.Summary.Incomplete); Equal(0, d.Summary.Complete);
    Equal(RowStatus.Incomplete, d.Records[0].Status);
});

Test("an unrecognised entry is reported in the note", () =>
{
    var r = Doc($"{F},gray_kangaroo,yellow(0-50m),1").Records[0];
    Equal("Missing: species (unrecognised: gray_kangaroo)", r.Notes);
});

Test("multiple distances: first is used until one is chosen", () =>
{
    var d = Doc($"{F},grey_kangaroo,blue(100-200m),green(50-100m),2");
    var r = d.Records[0];
    Equal("blue(100-200m)", r.Distance); True(r.HasMultipleDistances, "flagged");
    Equal("Multiple distances found: 2", r.Notes);
    Equal("blue(100-200m), green(50-100m)", r.AllDistancesText);
    Equal(RowStatus.MultipleDistances, r.Status); Equal(1, d.Summary.MultipleDistances);

    Equal(null, d.SetDistance(r, "green(50-100m)"));
    True(!r.HasMultipleDistances, "resolved");
    Equal("Complete record (multiple distances resolved)", r.Notes);
    Equal("Selected: green(50-100m) from [blue(100-200m), green(50-100m)]", r.AllDistancesText);
    Equal(0, d.Summary.MultipleDistances);
});

Test("choosing the distance already shown also resolves the row", () =>
{
    var d = Doc($"{F},goat,blue(100-200m),green(50-100m),2");
    d.SetDistance(d.Records[0], "blue(100-200m)");
    True(!d.Records[0].HasMultipleDistances, "resolved");
});

Test("header line is skipped and no data line is lost", () =>
{
    var d = Doc("HEADER AM_AM_SML v1", $"{F},goat,yellow(0-50m),1", "", $"{F},goat,yellow(0-50m),2");
    True(d.HadHeaderLine, "header detected");
    Equal(2, d.Records.Count);
    Equal(2, d.Records[0].Line); Equal(4, d.Records[1].Line);
});

Test("a first line with commas is data, not a header", () =>
{
    var d = Doc($"{F},goat,yellow(0-50m),1");
    True(!d.HadHeaderLine, "no header"); Equal(1, d.Records.Count); Equal(1, d.Records[0].Line);
});

Test("quoted fields keep their commas", () =>
{
    var fields = SurveyDocument.ParseCsvLine("a,\"b,c\",d");
    Equal(3, fields.Count); Equal("b,c", fields[1]);
});

Test("short lines do not fail", () =>
{
    var d = Doc("a,b,c");
    Equal("c", d.Records[0].Fixed[2]); Equal("", d.Records[0].Fixed[10]); True(!d.Records[0].IsComplete, "incomplete");
});

// --------------------------------------------------------------- transects

Test("well-formed transects have no errors", () =>
{
    var d = Doc($"{F},break_on", $"{F},goat,yellow(0-50m),1", $"{F},break_off", $"{F},break_on", $"{F},break_off");
    Equal(0, d.Summary.TransectErrors); Equal(2, d.Summary.TransectStartCount); Equal(2, d.Summary.TransectEndCount);
    True(d.Summary.TransectMarkersMatch, "matched");
    Equal("Transect marker: break_on", d.Records[0].Notes);
    True(d.Records[0].IsComplete, "markers are complete");
});

Test("transect sequence errors are flagged on the same rows as the desktop app", () =>
{
    var d = Doc($"{F},break_off", $"{F},break_on", $"{F},break_on", $"{F},break_off", $"{F},break_on");
    True(d.Records[0].TransectError, "close with none open");
    True(!d.Records[1].TransectError, "first open is fine");
    True(d.Records[2].TransectError, "open while open");
    True(!d.Records[3].TransectError, "close is fine");
    True(d.Records[4].TransectError, "never closed");
    Equal(3, d.Summary.TransectErrors);
    Equal("Transect error: 'break_off' without preceding 'break_on'", d.Records[0].Notes);
    Equal("Transect error: 'break_on' without matching 'break_off'", d.Records[4].Notes);
    Equal(RowStatus.TransectError, d.Records[0].Status);
});

Test("swapping the markers reverses which one opens a transect", () =>
{
    string text = string.Join("\n", $"{F},break_off", $"{F},goat,yellow(0-50m),1", $"{F},break_on");
    Equal(2, SurveyDocument.Load(text, T).Summary.TransectErrors);
    Equal(0, SurveyDocument.Load(text, T.WithTransectMarkersSwapped()).Summary.TransectErrors);
});

Test("editing a marker re-checks the sequence", () =>
{
    var d = Doc($"{F},break_on", $"{F},break_on");
    Equal(1, d.Summary.TransectErrors); // the second marker: opened while open, and never closed
    d.SetSpecies(d.Records[1], "break_off");
    Equal(0, d.Summary.TransectErrors);
});

// ----------------------------------------------------------------- editing

Test("invalid edits are rejected and leave the value unchanged", () =>
{
    var d = Doc($"{F},goat,yellow(0-50m),1");
    var r = d.Records[0];
    True(d.SetSpecies(r, "wombat") != null, "species"); Equal("goat", r.Species);
    True(d.SetDistance(r, "far") != null, "distance"); Equal("yellow(0-50m)", r.Distance);
    True(d.SetGroupSize(r, "2,7") != null, "group size"); Equal("1", r.GroupSize);
    True(!d.IsModified, "not modified by rejected edits");
});

Test("valid edits complete a record", () =>
{
    var d = Doc(F);
    var r = d.Records[0];
    Equal(null, d.SetSpecies(r, "goat"));
    Equal("Missing: distance, group size", r.Notes);
    Equal(null, d.SetDistance(r, "green(50-100m)"));
    Equal(null, d.SetGroupSize(r, "2, 3"));
    Equal("2,3", r.GroupSize, "spaces removed");
    True(r.IsComplete, "complete"); Equal(1, d.Summary.Complete); True(d.IsModified, "modified");
});

Test("clearing a value makes the record incomplete again", () =>
{
    var d = Doc($"{F},goat,yellow(0-50m),1");
    d.SetGroupSize(d.Records[0], "");
    Equal("Missing: group size", d.Records[0].Notes);
});

Test("delete and undo restore the row in place", () =>
{
    var d = Doc($"{F},goat,yellow(0-50m),1", F, $"{F},goat,yellow(0-50m),3");
    var middle = d.Records[1];
    d.Delete(middle);
    Equal(2, d.Records.Count); Equal(0, d.Summary.Incomplete); Equal(1, d.DeletedCount);
    True(ReferenceEquals(middle, d.UndoDelete()), "same row returned");
    Equal(3, d.Records.Count); True(ReferenceEquals(middle, d.Records[1]), "back in place");
    Equal(null, d.UndoDelete());
});

// ------------------------------------------------------------------ export

Test("export has the header, CRLF line ends and three tidy columns", () =>
{
    var d = Doc("HDR AM_AM_SML", $"{F},break_on", $"{F},3,goat,blue(100-200m)", $"{F},red_kangaroo,4,2,black(200-300m),yellow(0-50m)", $"{F},break_off");
    string expected =
        SurveyDocument.CsvHeader + "\r\n" +
        $"{F},break_on,,\r\n" +
        $"{F},goat,3,blue(100-200m)\r\n" +
        $"{F},red_kangaroo,\"4,2\",black(200-300m)\r\n" +
        $"{F},break_off,,\r\n";
    Equal(expected, d.ToCsv());
});

Test("export bytes start with the UTF-8 byte-order mark", () =>
{
    byte[] bytes = Doc($"{F},goat,yellow(0-50m),1").ToCsvBytes();
    True(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM");
    Equal(SurveyDocument.CsvHeader, Encoding.UTF8.GetString(bytes, 3, SurveyDocument.CsvHeader.Length));
});

Test("exported file can be read back with the same values", () =>
{
    var d = Doc($"{F},red_kangaroo,4,2,black(200-300m)");
    string line = d.ToCsv().Split("\r\n")[1];
    var fields = SurveyDocument.ParseCsvLine(line);
    Equal(14, fields.Count); Equal("red_kangaroo", fields[11]); Equal("4,2", fields[12]); Equal("black(200-300m)", fields[13]);
});

Test("fields with quotes are escaped", () =>
{
    Equal("\"a\"\"b\"", SurveyDocument.FormatCsvField("a\"b"));
    Equal("plain", SurveyDocument.FormatCsvField("plain"));
});

// ------------------------------------------------------------------- run

int failed = 0;
foreach (var (name, body) in tests)
{
    try { body(); Console.WriteLine($"  ok    {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"  FAIL  {name}\n        {ex.Message}"); }
}
Console.WriteLine($"\n{tests.Count - failed} of {tests.Count} tests passed.");
return failed;
