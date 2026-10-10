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
static void Throws(Action action, string what)
{
    try { action(); } catch (TemplateException) { return; }
    throw new Exception($"no error for: {what}");
}

// The data logger's button config, as supplied.
const string Config = """
    Button,AudioText,RecordedData,NewRecord
    A,one,1,No
    B,two,2,No
    Y,three,3,No
    X,four,4,No
    LeftShoulder,goat,goat,Yes
    RightShoulder,glare,glare,No
    Start_On,start transect,start_transect,Yes
    Start_Off,end transect,end_transect,Yes
    Back,delete last record,delete_last_record,No
    DPadUp,blue one hundred to two hundred meters,(blue) 100-200,No
    DPadLeft,green fifty to one hundred meters,(green) 50-100,No
    DPadDown,yellow zero to fifty meters,(yellow) 0-50,No
    DPadRight,black two hundred to three hundred meters,(black) 200-300,No
    LeftTrigger,grey kangaroo,grey_kangaroo,Yes
    RightTrigger,red kangaroo,red_kangaroo,Yes
    LeftThumbUp,blue one hundred to two hundred meters,(blue) 100-200,No
    LeftThumbLeft,green fifty to one hundred meters,(green) 50-100,No
    LeftThumbDown,yellow zero to fifty meters,(yellow) 0-50,No
    LeftThumbRight,black two hundred to three hundred meters,(black) 200-300,No
    RightThumbDown,ten,10,No
    RightThumbLeft,not configured,NA,No
    RightThumbUp,mixed species in the same group,mixed_species_group,No
    RightThumbRight,not configured,NA,No
    LeftThumbClick,not configured,NA,No
    RightThumbClick,not configured,NA,No
    """;

const string F = "t,tod,obs,pos,utc,lat,lon,brg,alt,vtol,htol"; // the 11 auto-recorded columns
const string Y = "(yellow) 0-50", G = "(green) 50-100", B = "(blue) 100-200", K = "(black) 200-300";
SurveyTemplate T = SurveyTemplate.Parse(Config);
SurveyDocument Doc(params string[] lines) => SurveyDocument.Load(string.Join("\r\n", lines), T);

// ------------------------------------------------------------- logger config

Test("config: every value gets the right meaning", () =>
{
    Equal("goat,grey_kangaroo,red_kangaroo", string.Join(",", T.Species));
    Equal("1,2,3,4,10", string.Join(",", T.Counts));
    Equal($"{B}|{G}|{Y}|{K}", string.Join("|", T.DistanceClasses));
    Equal("glare,mixed_species_group", string.Join(",", T.OtherValues));
    Equal("start_transect", T.TransectStart);
    Equal("end_transect", T.TransectEnd);
    Equal(ValueRole.DeleteRequest, T.RoleOf("delete_last_record"));
    Equal(null, T.RoleOf("NA"), "unconfigured buttons are left out");
    Equal(17, T.Entries.Count, "values on two buttons are listed once");
});

Test("config: changing the file changes what is accepted", () =>
{
    string changed = Config
        .Replace("LeftShoulder,goat,goat,Yes", "LeftShoulder,feral pig,feral_pig,Yes")
        .Replace("RightThumbLeft,not configured,NA,No", "RightThumbLeft,twenty,20,No")
        .Replace("DPadRight,black two hundred to three hundred meters,(black) 200-300,No", "DPadRight,beyond,(white) 300-400,No")
        .Replace("start_transect", "break_on").Replace("end_transect", "break_off");
    SurveyTemplate t = SurveyTemplate.Parse(changed);
    True(t.IsSpecies("feral_pig") && !t.IsSpecies("goat"), "species");
    True(t.IsCount("20"), "count");
    True(t.IsDistanceClass("(white) 300-400"), "distance");
    Equal("break_on", t.TransectStart); Equal("break_off", t.TransectEnd);

    var d = SurveyDocument.Load($"{F},break_on\n{F},feral_pig,20,10,(white) 300-400\n{F},goat,1,{Y}\n{F},break_off", t);
    True(d.Records[1].IsComplete, "new values accepted"); Equal(30, d.Records[1].GroupSize);
    Equal("Missing: species (unrecognised: goat)", d.Records[2].Notes);
    Equal(0, d.Summary.TransectErrors);
});

Test("config: columns are found by name, in any order, with quotes, CRLF and a BOM", () =>
{
    var t = SurveyTemplate.Parse("﻿NewRecord,RecordedData,Button\r\nYes,emu,A\r\nNo,\"5\",B\r\nNo,near 0-10,C\r\n");
    Equal("emu", t.Species[0]); Equal("5", t.Counts[0]); Equal("near 0-10", t.DistanceClasses[0]);
    Equal("", t.TransectStart, "no Start button in this config");
});

Test("config: unusable files are reported", () =>
{
    Throws(() => SurveyTemplate.Parse(""), "empty");
    Throws(() => SurveyTemplate.Parse("nonsense"), "not a config");
    Throws(() => SurveyTemplate.Parse("Button,AudioText,RecordedData,NewRecord\nA,one,1,No"), "no species or distances");
    Throws(() => SurveyTemplate.Parse("colour,red"), "unknown role");
});

Test("a value's meaning can be overridden", () =>
{
    SurveyTemplate t = T.WithRole("glare", ValueRole.Ignore);
    Equal("mixed_species_group", string.Join(",", t.OtherValues));
    t = T.WithRole("end_transect", ValueRole.TransectStart);
    Equal("end_transect", t.TransectStart); Equal("start_transect", t.TransectEnd, "the two markers swap");
});

Test("template with meanings round-trips through text", () =>
{
    SurveyTemplate changed = T.WithRole("glare", ValueRole.Ignore);
    SurveyTemplate again = SurveyTemplate.Parse(changed.ToText());
    Equal(changed.ToText(), again.ToText());
    Equal(ValueRole.Ignore, again.RoleOf("glare"));
    Equal("grey kangaroo", again.Entries.First(e => e.Value == "grey_kangaroo").Description);
});

// ----------------------------------------------------------------- parsing

Test("complete record", () =>
{
    var r = Doc($"{F},red_kangaroo,{Y},2").Records[0];
    Equal("red_kangaroo", r.Species); Equal(Y, r.Distance); Equal("2", r.Counts); Equal(2, r.GroupSize);
    Equal("t", r.Fixed[0]); Equal("htol", r.Fixed[10]);
    True(r.IsComplete, "complete"); Equal("Complete record", r.Notes); Equal(RowStatus.Ok, r.Status);
});

Test("entries may appear in any order and with spaces", () =>
{
    var r = Doc($"{F}, 3 , goat, {B}").Records[0];
    Equal("goat", r.Species); Equal("3", r.Counts); Equal(B, r.Distance);
});

Test("group size is the sum of the counts", () =>
{
    var d = Doc($"{F},red_kangaroo,4,4,2,{K}", $"{F},goat,10,10,3,{Y}", $"{F},goat,{Y}");
    Equal("4,4,2", d.Records[0].Counts); Equal(10, d.Records[0].GroupSize);
    Equal(23, d.Records[1].GroupSize);
    Equal(null, d.Records[2].GroupSize, "no counts, no group size");
    d.SetCounts(d.Records[2], "1, 2");
    Equal(3, d.Records[2].GroupSize, "recalculated after an edit");
});

Test("other observations are kept", () =>
{
    var r = Doc($"{F},grey_kangaroo,mixed_species_group,3,{B},glare,glare").Records[0];
    Equal("mixed_species_group;glare", r.Other); True(r.IsComplete, "complete");
});

Test("a delete request flags the record until it is deleted or kept", () =>
{
    var d = Doc($"{F},goat,{Y},1,delete_last_record");
    var r = d.Records[0];
    True(r.DeleteRequested, "flagged"); Equal(RowStatus.DeleteRequested, r.Status);
    Equal("Marked for deletion by the observer (delete_last_record)", r.Notes);
    Equal(1, d.Summary.DeleteRequests);
    d.KeepRecord(r);
    Equal(RowStatus.Ok, r.Status); Equal(0, d.Summary.DeleteRequests);
});

Test("missing fields are identified", () =>
{
    var d = Doc($"{F},red_kangaroo,{Y}", $"{F},3,{G}", $"{F},goat,2", F);
    Equal("Missing: count", d.Records[0].Notes);
    Equal("Missing: species", d.Records[1].Notes);
    Equal("Missing: distance", d.Records[2].Notes);
    Equal("Missing: species, distance, count", d.Records[3].Notes);
    Equal(4, d.Summary.Incomplete); Equal(0, d.Summary.Complete);
});

Test("a value the config does not list is reported in the note", () =>
{
    Equal("Missing: species (unrecognised: gray_kangaroo)", Doc($"{F},gray_kangaroo,{Y},1").Records[0].Notes);
});

Test("multiple distances: the last recorded is the default until one is chosen", () =>
{
    var d = Doc($"{F},grey_kangaroo,{B},{G},2", $"{F},goat,{Y},{K},{B},1");
    var r = d.Records[0];
    Equal(G, r.Distance, "last of two"); True(r.HasMultipleDistances, "still flagged");
    Equal(B, d.Records[1].Distance, "last of three");
    Equal("Multiple distances found: 2", r.Notes);
    Equal($"{B}, {G}", r.AllDistancesText);
    Equal(RowStatus.MultipleDistances, r.Status); Equal(2, d.Summary.MultipleDistances);
    True(d.ToCsv().Split("\r\n")[1].EndsWith($"grey_kangaroo,2,2,{G},"), "the default is what gets saved");

    Equal(null, d.SetDistance(r, B));
    True(!r.HasMultipleDistances, "resolved");
    Equal("Complete record (multiple distances resolved)", r.Notes);
    Equal($"Selected: {B} from [{B}, {G}]", r.AllDistancesText);
});

Test("choosing the distance already shown also resolves the row", () =>
{
    var d = Doc($"{F},goat,{B},{G},2");
    d.SetDistance(d.Records[0], G);
    True(!d.Records[0].HasMultipleDistances, "resolved");
    Equal(G, d.Records[0].Distance);
});

Test("header line is skipped and no data line is lost", () =>
{
    var d = Doc("HEADER AM_AM_SML v1", $"{F},goat,{Y},1", "", $"{F},goat,{Y},2");
    True(d.HadHeaderLine, "header detected");
    Equal(2, d.Records.Count); Equal(2, d.Records[0].Line); Equal(4, d.Records[1].Line);
});

Test("a first line with commas is data, not a header", () =>
{
    var d = Doc($"{F},goat,{Y},1");
    True(!d.HadHeaderLine, "no header"); Equal(1, d.Records[0].Line);
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
    var d = Doc($"{F},start_transect", $"{F},goat,{Y},1", $"{F},end_transect", $"{F},start_transect", $"{F},end_transect");
    Equal(0, d.Summary.TransectErrors); Equal(2, d.Summary.TransectStartCount); Equal(2, d.Summary.TransectEndCount);
    Equal("Transect marker: start_transect", d.Records[0].Notes);
    True(d.Records[0].IsComplete, "markers are complete");
});

Test("transect sequence errors are flagged", () =>
{
    var d = Doc($"{F},end_transect", $"{F},start_transect", $"{F},start_transect", $"{F},end_transect", $"{F},start_transect");
    True(d.Records[0].TransectError, "close with none open");
    True(!d.Records[1].TransectError, "first open is fine");
    True(d.Records[2].TransectError, "open while open");
    True(!d.Records[3].TransectError, "close is fine");
    True(d.Records[4].TransectError, "never closed");
    Equal(3, d.Summary.TransectErrors);
    Equal("Transect error: 'end_transect' without preceding 'start_transect'", d.Records[0].Notes);
    Equal("Transect error: 'start_transect' without matching 'end_transect'", d.Records[4].Notes);
});

Test("editing a marker re-checks the sequence", () =>
{
    var d = Doc($"{F},start_transect", $"{F},start_transect");
    Equal(1, d.Summary.TransectErrors);
    d.SetSpecies(d.Records[1], "end_transect");
    Equal(0, d.Summary.TransectErrors);
});

// ----------------------------------------------------------------- editing

Test("invalid edits are rejected and leave the value unchanged", () =>
{
    var d = Doc($"{F},goat,{Y},1,glare");
    var r = d.Records[0];
    True(d.SetSpecies(r, "wombat") != null, "species"); Equal("goat", r.Species);
    True(d.SetDistance(r, "far") != null, "distance"); Equal(Y, r.Distance);
    True(d.SetCounts(r, "2,7") != null, "counts"); Equal("1", r.Counts);
    True(d.SetOther(r, "fog") != null, "other"); Equal("glare", r.Other);
    True(!d.IsModified, "not modified by rejected edits");
});

Test("valid edits complete a record", () =>
{
    var d = Doc(F);
    var r = d.Records[0];
    Equal(null, d.SetSpecies(r, "goat"));
    Equal("Missing: distance, count", r.Notes);
    Equal(null, d.SetDistance(r, G));
    Equal(null, d.SetCounts(r, "2, 10"));
    Equal("2,10", r.Counts, "spaces removed"); Equal(12, r.GroupSize);
    Equal(null, d.SetOther(r, "glare; mixed_species_group"));
    Equal("glare;mixed_species_group", r.Other);
    True(r.IsComplete, "complete"); True(d.IsModified, "modified");
});

Test("delete and undo restore the row in place", () =>
{
    var d = Doc($"{F},goat,{Y},1", F, $"{F},goat,{Y},3");
    var middle = d.Records[1];
    d.Delete(middle);
    Equal(2, d.Records.Count); Equal(0, d.Summary.Incomplete); Equal(1, d.DeletedCount);
    True(ReferenceEquals(middle, d.UndoDelete()), "same row returned");
    True(ReferenceEquals(middle, d.Records[1]), "back in place");
    Equal(null, d.UndoDelete());
});

// ------------------------------------------------------------------ export

Test("export has counts, the summed group size, distance and other", () =>
{
    var d = Doc("HDR AM_AM_SML", $"{F},start_transect", $"{F},3,goat,{B}", $"{F},red_kangaroo,4,10,{K},{Y},glare", $"{F},end_transect");
    string expected =
        "Time,TimeOfDay,ObserverID,ObserverPosition,UTC,Latitude,Longitude,Bearing,Altitude,VTOL,HTOL,Species,Counts,GroupSize,Distance,Other\r\n" +
        $"{F},start_transect,,,,\r\n" +
        $"{F},goat,3,3,{B},\r\n" +
        $"{F},red_kangaroo,\"4,10\",14,{Y},glare\r\n" +
        $"{F},end_transect,,,,\r\n";
    Equal(expected, d.ToCsv());
});

Test("no Other column when the config has no other observations", () =>
{
    SurveyTemplate t = T.WithRole("glare", ValueRole.Ignore).WithRole("mixed_species_group", ValueRole.Ignore);
    var d = SurveyDocument.Load($"{F},goat,2,{Y},glare", t);
    True(d.CsvHeader.EndsWith(",Species,Counts,GroupSize,Distance"), d.CsvHeader);
    Equal($"{F},goat,2,2,{Y}", d.ToCsv().Split("\r\n")[1]);
    True(d.Records[0].IsComplete, "ignored value causes no complaint");
});

Test("export bytes start with the UTF-8 byte-order mark", () =>
{
    var d = Doc($"{F},goat,{Y},1");
    byte[] bytes = d.ToCsvBytes();
    True(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM");
    Equal(d.CsvHeader, Encoding.UTF8.GetString(bytes, 3, d.CsvHeader.Length));
});

Test("exported file can be read back with the same values", () =>
{
    var fields = SurveyDocument.ParseCsvLine(Doc($"{F},red_kangaroo,4,2,{K}").ToCsv().Split("\r\n")[1]);
    Equal(16, fields.Count); Equal("red_kangaroo", fields[11]); Equal("4,2", fields[12]); Equal("6", fields[13]); Equal(K, fields[14]);
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
