# Wildlife Survey Data Validator (web)

A browser application for checking and cleaning aerial wildlife survey data files. It is a
Blazor WebAssembly port of the Windows Forms application of the same name.

The data file is read and cleaned entirely in the browser. Nothing is uploaded to a server, and
the cleaned file is saved back to the user's own computer as a download.

## What it does

- Opens a survey data file (`.dat` or `.csv`, comma-separated).
- Keeps the 11 auto-recorded columns on every line: time, time of day, observer id, observer
  position, UTC, latitude, longitude, bearing, altitude, VTOL and HTOL.
- Checks that every record has a species, one distance class and at least one group size.
- Highlights lines to fix or delete:
  - orange: an entry is missing;
  - yellow: more than one distance class was recorded, so one must be chosen;
  - salmon: a transect marker is out of sequence.
- Lets you edit or delete lines, with undo for deletions.
- Downloads the result as a CSV file with the headings
  `Time,TimeOfDay,ObserverID,ObserverPosition,UTC,Latitude,Longitude,Bearing,Altitude,VTOL,HTOL,Species,GroupSize,Distance`.

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

## Templates

The lists of valid species, distance classes and group sizes are not fixed in the code. The app
starts with a built-in template, and you can load your own from the **Template** panel. The
template is remembered in the browser until you reset it.

A template is a text file with one `category,value` entry per line. Lines starting with `#` are
ignored. `samples/default-template.csv` is the built-in one:

```text
category,value
species,red_kangaroo
species,grey_kangaroo
species,goat
distance,yellow(0-50m)
distance,green(50-100m)
distance,blue(100-200m)
distance,black(200-300m)
group_size,1
group_size,2
group_size,3
group_size,4
transect_start,break_on
transect_end,break_off
header_marker,AM_AM_SML
```

| Category | Meaning |
|---|---|
| `species` | A valid species name. Repeat for each species. |
| `distance` | A valid distance class. Repeat for each class. |
| `group_size` | A valid group-size entry. Repeat for each value. |
| `transect_start` | The marker that opens a transect. Optional; defaults to `break_on`. |
| `transect_end` | The marker that closes a transect. Optional; defaults to `break_off`. |
| `header_marker` | The first line of a file is skipped as a header if it has no comma and contains this text. Optional; defaults to `AM_AM_SML`. |

## Project layout

| Folder | Contents |
|---|---|
| `src/SurveyValidator.Core` | Reading, checking, editing and writing survey files. No user-interface code. |
| `src/SurveyValidator.Web` | The Blazor WebAssembly app. `ValidatorPage.razor` is the screen. |
| `tests/SurveyValidator.Tests` | Tests for the Core library, as a console program with no extra packages. |
| `samples` | An example template and a synthetic data file. |
| `.github/workflows/deploy.yml` | Publishes the app to GitHub Pages on each push to `main`. |

## Publish with GitHub Pages

1. Push this repository to GitHub.
2. On github.com open **Settings > Pages** and set **Source** to **GitHub Actions**.
3. Push to `main`. The workflow tests, builds and publishes the app to
   `https://<user>.github.io/<repository>/`.

GitHub Pages on a free account needs a public repository.

## Differences from the Windows Forms version

- Highlighting and the counts update as you edit. The separate Validate Records, Show Multiple
  Distances and Validate Transects buttons are now filters that show only those rows.
- Transect markers are checked as soon as a file is opened and after every edit.
- The species, group size, distance and notes columns are shown first, with the 11 auto-recorded
  columns to their right. The downloaded file keeps the original column order.
- Species and distance are chosen from lists. A line with several recorded distances shows each one
  as a button.
- An entry after the 11 auto-recorded columns that matches nothing in the template (a misspelt
  species, for example) is named in the Notes column.
- When a file has a header line, the first data line after it is kept. The Windows Forms version
  skipped it. Line numbers are the line numbers in the source file.
- A group size typed with spaces (`2, 3`) is stored without them (`2,3`).
- The valid values come from a template file that can be replaced.
