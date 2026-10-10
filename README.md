# Wildlife Survey Data Validator (web)

A browser application for checking and cleaning aerial wildlife survey data files. It is a
Blazor WebAssembly port of the Windows Forms application of the same name.

The data file is read and cleaned entirely in the browser. Nothing is uploaded to a server, and
the cleaned file is saved back to the user's own computer as a download.

## What it does

- Opens a survey data file (`.dat` or `.csv`, comma-separated).
- Reads what counts as a valid entry from the data logger's button config file
  (`GamepadAudioConfig.csv`). Nothing about species, counts or distance classes is fixed in the code.
- Keeps the 11 auto-recorded columns on every line: time, time of day, observer id, observer
  position, UTC, latitude, longitude, bearing, altitude, VTOL and HTOL.
- Checks that every record has a species, one distance class and at least one count.
- Calculates **GroupSize** for each record by adding its counts together (counts of 4, 4 and 2
  give a group size of 10).
- Highlights lines to fix or delete:
  - orange: an entry is missing;
  - yellow: more than one distance class was recorded, so one must be chosen;
  - salmon: a transect marker is out of sequence;
  - grey: the observer marked the record for deletion.
- Lets you edit or delete lines, with undo for deletions.
- Downloads the result as a CSV file with the headings
  `Time,TimeOfDay,ObserverID,ObserverPosition,UTC,Latitude,Longitude,Bearing,Altitude,VTOL,HTOL,Species,Counts,GroupSize,Distance,Other`.
  `Counts` holds the counts as recorded (`4,4,2`), `GroupSize` their sum, and `Other` any other
  observations such as glare, separated by semicolons. `Other` is left out when the config has no
  such values.

## Run it on your computer

Requires the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/).

```bash
dotnet watch --project src/SurveyValidator.Web
```

This builds the app and opens it in your browser. It reloads when you save a file. Stop it with
Ctrl+C. `samples/synthetic-example.dat` is a made-up file with one of each kind of problem.

## Run the tests

```bash
dotnet run --project tests/SurveyValidator.Tests
```

## The config file

The app is driven by the logger's config file, which has one row per gamepad button:

```text
Button,AudioText,RecordedData,NewRecord
A,one,1,No
LeftShoulder,goat,goat,Yes
Start_On,start transect,start_transect,Yes
DPadUp,blue one hundred to two hundred meters,(blue) 100-200,No
RightShoulder,glare,glare,No
```

`src/SurveyValidator.Web/wwwroot/config/GamepadAudioConfig.csv` is the built-in copy, read each
time the app starts. To change what the app accepts, either replace that file and push, or load a
different config from the **Config** panel, which is remembered in that browser. No code changes
are needed in either case.

The meaning of each `RecordedData` value is worked out from its row, in this order:

| Rule | Meaning |
|---|---|
| `RecordedData` is empty or `NA` | Button not configured; left out |
| `Button` is `Start_On` | Transect start marker |
| `Button` is `Start_Off` | Transect end marker |
| `RecordedData` is a whole number | Count (added into GroupSize) |
| `NewRecord` is `Yes` | Species |
| `RecordedData` contains a number range such as `100-200` | Distance class |
| `RecordedData` starts with `delete` | Delete request: the record is flagged for deletion |
| anything else | Other observation, kept in the `Other` column |

The Config panel lists every value with the meaning it was given, and each one can be changed
there. **Download as template** saves the values with their meanings as a `role,value,description`
file that can be loaded again. `samples/legacy-template.csv` is one of these: it holds the values
the Windows Forms version had built in (`break_on`, `yellow(0-50m)` and so on), for cleaning files
recorded with those values.

## Project layout

| Folder | Contents |
|---|---|
| `src/SurveyValidator.Core` | Reading, checking, editing and writing survey files. No user-interface code. |
| `src/SurveyValidator.Web` | The Blazor WebAssembly app. `ValidatorPage.razor` is the screen. |
| `tests/SurveyValidator.Tests` | Tests for the Core library, as a console program with no extra packages. |
| `samples` | The logger config, a legacy template and a synthetic data file. |
| `github-pages-workflow.yml` | A GitHub Actions workflow that publishes the app to GitHub Pages. Not active until moved (see below). |

## Publish with GitHub Pages

1. Push this repository to GitHub.
2. Put the workflow where GitHub looks for it, then commit and push:

   ```bash
   mkdir -p .github/workflows
   git mv github-pages-workflow.yml .github/workflows/deploy.yml
   git commit -m "Enable GitHub Pages deployment"
   git push
   ```

3. On github.com open **Settings > Pages** and set **Source** to **GitHub Actions**.
4. Each push to `main` then tests, builds and publishes the app to
   `https://<user>.github.io/<repository>/`.

GitHub Pages on a free account needs a public repository.

## Differences from the Windows Forms version

- The valid species, counts, distance classes and transect markers come from the logger's config
  file instead of being fixed in the code.
- The saved file has a `Counts` column (the counts as recorded) and a calculated `GroupSize` column
  (their sum). The Windows Forms version saved the counts under the name `GroupSize`.
- Other observations in the config, such as glare, are kept in an `Other` column.
- A record containing a delete request is flagged so it can be deleted or kept.
- Highlighting and the counts update as you edit. The separate Validate Records, Show Multiple
  Distances and Validate Transects buttons are now filters that show only those rows.
- Transect markers are checked as soon as a file is opened and after every edit.
- The species, counts, group size, distance and notes columns are shown first, with the 11
  auto-recorded columns to their right. The downloaded file keeps the original column order.
- Species and distance are chosen from lists. A line with several recorded distances shows each one
  as a button.
- An entry after the 11 auto-recorded columns that the config does not list (a misspelt species,
  for example) is named in the Notes column.
- When a file has a header line, the first data line after it is kept. The Windows Forms version
  skipped it. Line numbers are the line numbers in the source file.
