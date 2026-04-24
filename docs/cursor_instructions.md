# Cricket Tournament Scheduler — Requirements & Implementation Reference

---

## 1. Objective

A **C# WPF desktop application (MVVM pattern)** to create, manage, and schedule cricket tournament matches with strong constraint handling, CSV persistence, and an interactive scheduling workspace.

---

## 2. Technology Stack

| Layer | Technology |
|---|---|
| Language | C# (.NET 8+, nullable enabled) |
| UI | WPF — MVVM via `CommunityToolkit.Mvvm` |
| Storage | CSV files — one folder per league |
| CSV parsing | `CsvHelper` |
| Excel export | `ClosedXML` — single `.xlsx` workbook with multiple sheets |
| Converter | `InverseBoolConverter` (custom, in root namespace) |

---

## 3. Project Structure

```
CricketScheduler/
├── CricketScheduler.sln
├── src/CricketScheduler.App/
│   ├── App.xaml / .cs
│   ├── MainWindow.xaml / .cs          ← league toolbar + TabControl host (5 tabs)
│   ├── InputDialog.cs                 ← modal text-input dialog
│   ├── InverseBoolConverter.cs        ← bool→!bool for RadioButton bindings
│   ├── Views/
│   │   ├── TournamentView.xaml/.cs
│   │   ├── DivisionView.xaml/.cs
│   │   ├── SchedulingRequestView.xaml/.cs
│   │   ├── SchedulerView.xaml/.cs
│   │   ├── StatisticsView.xaml/.cs    ← read-only pivot grids; dynamic columns from DataTable
│   │   └── LeagueSelectionView.xaml/.cs
│   ├── ViewModels/
│   │   ├── MainViewModel.cs           ← single unified ViewModel; stubs for others
│   │   └── StatisticsViewModel.cs     ← builds 4 DataTable pivots; exposed via MainViewModel.StatisticsVM
│   ├── Models/
│   │   ├── League.cs
│   │   ├── Tournament.cs
│   │   ├── Division.cs                ← mutable IsRoundRobin, MatchesPerTeam, ModeSummary, FixedPairings
│   │   ├── Team.cs
│   │   ├── Match.cs                   ← IsFixed, Date?, Slot?, Ground?, UnscheduledReason?
│   │   ├── Ground.cs
│   │   ├── TimeSlot.cs
│   │   ├── SchedulingRequest.cs       ← IsFullDayBlock computed property
│   │   └── ForbiddenSlot.cs           ← Date?, GroundName?, TimeSlot?, Division?
│   ├── Services/
│   │   ├── CsvService.cs
│   │   ├── LeagueService.cs           ← Load/Save all CSVs including schedule.csv + unscheduled.csv
│   │   ├── SchedulingService.cs       ← Generate (2 overloads), SuggestMoves, RescheduleUmpiring, RescheduleGroundAndUmpiring
│   │   ├── ExportService.cs
│   │   ├── ConstraintService.cs
│   │   ├── FairnessService.cs
│   │   └── SuggestionService.cs
│   └── SchedulingEngine/              ← internal static classes
│       ├── MatchGenerator.cs          ← Round Robin + Fixed pairing algorithms
│       ├── SchedulingMatrixBuilder.cs
│       ├── ConstraintEvaluator.cs
│       ├── SlotScorer.cs
│       ├── Scheduler.cs
│       └── ConflictResolver.cs
├── data/leagues/{leagueName}/
│   ├── tournament.csv
│   ├── divisions.csv
│   ├── constraints.csv
│   ├── schedule.csv
│   └── unscheduled.csv
├── docs/cursor_instructions.md
└── tests/CricketScheduler.App.Tests/
```

---

## 4. Data Files (CSV)

### tournament.csv
```csv
TournamentName,StartDate,EndDate,DiscardedDates,Grounds,TimeSlots
My League,2026-04-18,2026-06-28,2026-05-10,OCG;Central Park,07:00-09:00;09:30-11:30;15:00-17:00
```
- `DiscardedDates` — semicolon-separated `yyyy-MM-dd`
- `Grounds` — semicolon-separated names
- `TimeSlots` — semicolon-separated `HH:mm-HH:mm`

### divisions.csv
```csv
DivisionName,TeamName,IsRoundRobin,MatchesPerTeam
DivisionA,Team1,True,
DivisionA,Team2,True,
DivisionB,Team3,False,4
DivisionB,Team4,False,4
```
- `IsRoundRobin=True` → all unique pairs scheduled
- `IsRoundRobin=False` → `MatchesPerTeam`  match pairings are generated using a **balanced skip-based fixed pairing algorithm**.


### constraints.csv
```csv
TeamName,Date,StartTime,EndTime
Team1,2026-05-01,,
Team2,2026-05-02,09:30,11:30
```
- Empty `StartTime`/`EndTime` → full day block

### schedule.csv (output — strict format)
```csv
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,Umpire One,Umpire Two,Umpire Three,Umpire Four,Match Manager,Scorer 1,Scorer 2,IsFixed
1,2026 TAGKC T-10,DivisionA,League,04/19/2026,7:00 AM,Team1,Team2,OCG,,,,,,,,False
```
- `Date` → `MM/dd/yyyy`
- `Time` → `h:mm tt`
- `IsFixed` — persisted so fixed matches survive app restart

### unscheduled.csv (written on every save — empty file clears stale data)
```csv
Series,Division,MatchType,TeamOne,TeamTwo,Reason
2026 TAGKC T-10,DivisionA,League,Team3,Team4,No available weekend slot
```
- Loaded on `OpenLeague`; absence handled gracefully

---

## 5. Models

### Division
```csharp
public sealed class Division
{
    public required string Name   { get; init; }
    public List<Team> Teams       { get; init; } = [];
    public bool IsRoundRobin      { get; set; } = true;   // mutable for UI editing
    public int? MatchesPerTeam    { get; set; }            // mutable for UI editing
    public string ModeSummary     { get; }                 // computed display string
    public List<(Team, Team)> FixedPairings { get; set; } = []; // pre-computed pairs for fixed mode
}
```

### ForbiddenSlot
```csharp
public sealed class ForbiddenSlot
{
    public string? GroundName  { get; init; }   // null = any ground
    public TimeSlot? TimeSlot  { get; init; }   // null = any slot
    public DateOnly? Date      { get; init; }   // null = any date
    public string? Division    { get; init; }   // null = all divisions
    public string Display      { get; }         // human-readable summary
}
```

### Match
```csharp
public sealed class Match
{
    public int Sequence          { get; set; }
    public string TournamentName { get; init; }
    public string DivisionName   { get; init; }
    public string MatchType      { get; init; } = "League";
    public string TeamOne        { get; init; }
    public string TeamTwo        { get; init; }
    public DateOnly? Date        { get; set; }
    public TimeSlot? Slot        { get; set; }
    public Ground? Ground        { get; set; }
    public bool IsFixed          { get; set; }   // fixed matches cannot be rescheduled
    public string? UnscheduledReason { get; set; } // populated when match cannot be scheduled
    public string? UmpireOne, UmpireTwo, UmpireThree, UmpireFour { get; set; }
    public string? MatchManager, ScorerOne, ScorerTwo { get; set; }
}
```

### League
```csharp
public sealed class League
{
    public string Name { get; init; }
    public Tournament Tournament { get; set; }
    public List<Division> Divisions { get; set; } = [];
    public List<Match> Matches { get; set; } = [];
    public List<Match> UnscheduledMatches { get; set; } = []; // persisted to unscheduled.csv
    public List<SchedulingRequest> Requests { get; set; } = [];
}
```

---

## 6. Application Tabs

The main window hosts five tabs via a `TabControl`: **Tournament**, **Divisions**, **Requests**, **Scheduler**, **Statistics**. All five bind to the same `MainViewModel` instance. The Statistics tab additionally accesses `MainViewModel.StatisticsVM` (a `StatisticsViewModel` child instance) for its pivot data.

### 6.1 Tournament Tab (`TournamentView`)

**Left panel:**
- Tournament Name — `TextBox`
- Start Date / End Date — side-by-side `DatePicker`s
- Blackout Dates — `DatePicker` input + "Add" → list; multi-select remove
- **Save Tournament** button

**Right panel:**
- Grounds — text input + Add/Remove; multi-select `ListBox`
- Match Time Slots — HH:mm start + HH:mm end + "Add Slot"; remove selected
- **Append Divisions CSV** — shortcut to import additional division/team data

---

### 6.2 Divisions Tab (`DivisionView`) — 3-column layout

**Column 1 — Division list:**
- Search box (filters `FilteredDivisions`)
- `ListBox` with custom `DataTemplate` showing Name + `ModeSummary`
- Add New Division form:
  - Division Name textbox
  - Scheduling Mode radio buttons: **Round Robin** | **Fixed matches per team**
  - Matches per team textbox (fixed mode only)
  - Add / Remove buttons
- Save, Import CSV, and **📎 Append CSV** buttons (Append merges without overwriting existing data)

**Column 2 — Teams for selected division:**
- Header shows selected division name
- Multi-select `DataGrid` (Team Name, Division columns) — `CanUserSortColumns="True"`
- Toolbar: Delete Selected (multi-row), Import Teams CSV
- Add team row with textbox + Add + Remove buttons

**Column 3 — Scheduling Mode Editor (right panel):**
- Shows selected division name + current `ModeSummary`
- **Radio buttons:** Round Robin | Fixed matches per team
- Matches per team textbox (enabled in fixed mode)
- **Apply to Division** button — calls `UpdateDivisionModeCommand`
  - Captures `var div = SelectedDivision` before remove, re-assigns `SelectedDivision = div` after insert to avoid NullReferenceException from ListBox selection event
  - Refreshes the list so `ModeSummary` updates immediately
- Match count reference table (quick guide for Round Robin vs Fixed)
- **Pairings DataGrid** — shows `Division.FixedPairings` for fixed-mode divisions (`CanUserSortColumns="True"`)

**Match generation rules per division:**
- `IsRoundRobin=true` → all N×(N-1)/2 unique pairs
- `IsRoundRobin=false` → 
**Fixed matches per team (`IsRoundRobin=false`):**

- `GenerateSchedule` calls `GeneratePairingsForDivision(div)` for any fixed-mode division with empty `FixedPairings`

#### Algorithm Overview

  - Teams are **sorted alphabetically (case-insensitive)**.
  - Each team plays exactly `MatchesPerTeam` opponents.
  - A **skip value** determines which opponents a team will NOT play.

  **Skip formula:**
  ```
  skip = n - MatchesPerTeam - 1
  ```

---

#### Pairing Logic

  - For each team index `i`, opponents are chosen excluding:
    - Itself
    - `skip` teams starting from mirror position `(n - 1 - i)` moving inward

  - Only valid pairs `(i < j)` are selected where:
    - Not forbidden
    - Both teams under match quota
    - Pair not already used

---

#### Guarantees

  - Exact `MatchesPerTeam` matches per team
  - No duplicate matches (in base case)
  - Balanced distribution
  - Deterministic output

---

#### 🔁 Handling MatchesPerTeam > (n - 1)

When matches requested exceed unique opponents:

```
MatchesPerTeam > (n - 1)
```

##### Behavior

1. Generate all **unique pairings first**
2. Then **repeat matches** using same balanced pairing logic
3. Previously used pairs are allowed in second pass

---

##### Example: 8 Teams, 8 Matches Per Team

- Each team has 7 unique opponents
- Needs 1 extra match

**Repeat pairing (mirror-based):**

| Team | Repeat Opponent |
|------|---------------|
| T1   | T8            |
| T2   | T7            |
| T3   | T6            |
| T4   | T5            |

---

##### Result

- Each team plays:
  - 7 unique matches
  - 1 repeated match
- Repeats are:
  - Symmetric
  - Evenly distributed

---

##### Implementation Note

- Requires relaxing `usedPairs` constraint after first pass
- Same `GeneratePairingsForDivision` function can support multi-pass logic

---


### 6.3 Requests Tab (`SchedulingRequestView`)

**Input form (3 fields in one row):**
- Team — `ComboBox` (editable, populated from all teams)
- Date — `DatePicker`
- Time Slot — `ComboBox` from tournament time slots (blank = full day block)
- **Add Request** button

**Multi-slot block (Expander):**
- `ListBox` with `SelectionMode="Extended"` showing all time slots
- **Block Selected Slots** — combines selected slots into one `start–end` range constraint
- Example: selecting 07:00–09:00 and 09:30–11:30 creates one request blocking 07:00–11:30

**Toolbar:**
- Search box (filters by team name or date)
- **Export Filtered** — saves `FilteredRequests` to CSV
- **Delete Selected** (multi-row)

**Grid:** Team (✎ inline editable), Date, Start Time, End Time, Block Type — `IsReadOnly="False"`, `CanUserSortColumns="True"`
- Full day rows show "(full day)" in time columns
- `SelectionMode="Extended"` for multi-row operations
- `SchedulingRequest` properties use `set` (not `init`); `SchedulingRequestRow` extends `ObservableObject`

**Actions:** Import CSV, Save All

---

### 6.4 Scheduler Tab (`SchedulerView`)

**Toolbar row 1 — Schedule actions:**
- **💾 Save Schedule** — syncs in-memory state to `schedule.csv` + `unscheduled.csv` without regenerating
- **Generate** — fresh schedule from scratch (all matches)
- **Reschedule** — re-runs engine preserving fixed matches and forbidden slots
- **Export All** — exports full schedule to `schedule.csv`
- **Export Filtered** — exports only currently visible rows
- **Import Matches** — overlay existing match data
- **Toggle Fixed** — marks/unmarks all selected rows as fixed (green row = fixed)
- **📤 Unschedule** — removes selected scheduled matches back to the Unscheduled grid (clears Date/Slot/Ground, sets `UnscheduledReason = "Manually unscheduled"`)
- **Copy** — copies selected rows to clipboard
- **Paste** — duplicates clipboard matches as new entries
- **Delete Selected** — removes multiple matches at once
- **Analyze Move** — opens Move Analyzer window for selected match
- **🧑‍⚖️ Reschedule Umpiring** — clears and reassigns umpires on all non-fixed matches using priority rules
- **🏟 Reschedule Ground & Umpiring** — reshuffles ground assignments for all non-fixed matches to balance usage, then re-runs umpiring; fixed matches fully preserved
- Schedule stats label (e.g. "23 of 28 matches shown")

**Filter / search bar:**
- Division `ComboBox`, Team `ComboBox`, Ground `ComboBox`
- Free-text Search box (matches team names, ground, division, date)
- Clear Filters button

**Forbidden Slots panel (Expander — collapsed by default):**
- 4 optional inputs: Date (`DatePicker`), Ground (`ComboBox`), Time Slot (`ComboBox`), Division (`ComboBox`)
- Add Forbidden Slot / Remove Selected
- List shows `Display` property of each slot
- Applied during both Generate and Reschedule; division-specific slots are enforced per-match (see §9)

**Schedule DataGrid (`SelectionMode="Extended"`, `CanUserSortColumns="True"`):**
- Columns: #, Division, Date, Time, Team One, Team Two, Ground (✎), Umpire 1 (✎), Umpire 2, Fixed
- Fixed column: `IsReadOnly="False"` checkbox — setter propagates directly to `SourceMatch.IsFixed`
- Ground / Umpire columns: setters propagate to `SourceMatch`
- Row styles:
  - 🔴 `#FFEEEE` — conflict detected (`HasConflict=true`)
  - 🟢 `#E8F5E9` — fixed match (`IsFixed=true`)

**Unscheduled Matches section (Unscheduled tab):**
- Persisted to/from `unscheduled.csv` on every save/load
- **Action bar**: Move Analyzer, Relax All, Clear Relaxations, Backtrack Reschedule, Export, Remove buttons
- **Move Analyzer button** — opens `MoveAnalyzerWindow` for the selected unscheduled match; on commit the match is moved to the schedule and the unscheduled grid refreshes via `SyncUnscheduledGrid()`
- **Constraint Relaxation** per-row checkboxes (see §8.3) — affect Backtrack Reschedule only
- `OpenRescheduleAnalyzerCommand` — creates `MoveAnalyzerViewModel` for the selected match, opens the window; on `DialogResult=true` calls `FinaliseMove()` + `SyncUnscheduledGrid()`

**Move Analyzer (separate window — `MoveAnalyzerWindow`):**

Redesigned with two grids and recursive depth support:

**Constraint Relaxation Panel** (top of window):
- 8 individual checkboxes, one per `RelaxedConstraints` flag, matching the Unscheduled grid labels:
  - **Ground Fairness** (`RelaxGroundFairness`) — allow over-used ground
  - **Umpire Fairness** (`RelaxUmpireFairness`) — ignore umpire-duty load
  - **Slot Fairness** (`RelaxTimeSlotFairness`) — allow over-used time slot
  - **2 Week Break** (`RelaxMaxGapRule`) — allow >2 consecutive no-match weekends
  - **1 Match / Weekend** (`RelaxOneMatchPerWeekend`) — allow >1 match per team per weekend
  - **Slot Restriction** (`RelaxTimeSlotRestriction`) — ignore partial-time unavailability
  - **Date Restriction** (`RelaxDateRestriction`) — ignore full-day date blocks
  - **Blackout Dates** (`RelaxDiscardedDates`) — allow scheduling on discarded dates
- Changing any checkbox immediately reloads the Potential Slots Grid via `BuildRelaxed()` → `SuggestMoves`

**Potential Slots Grid** (primary):
- Generated by `SuggestMoves` with the current `RelaxedConstraints`
- Includes ALL valid slots (free and occupied) excluding fixed-occupied and forbidden
- Columns: Date, Time Slot, Ground, Conflicts, Fairness Score
- 🟢 Green rows = `ConflictCount == 0` (free slots, immediately commitable)
- `additionalContext` — parent's tentative placement passed down the recursion chain so child analyzers correctly exclude the parent's target slot

**Affected Matches Grid** (secondary — populated on slot selection):
- Shows all matches that would be displaced by the selected slot
- Columns: Match, Current Assignment, [↔ Analyze] button, [📤 Bump] button
- **↔ Analyze** — opens a child `MoveAnalyzerWindow` for that match; parent always refreshes after child closes
- **📤 Bump** — real-state mutation: removes the match from `League.Matches`, adds to `League.UnscheduledMatches`, reloads Potential Slots Grid; hidden for fixed matches

**Commit Move** — only enabled when `ConflictCount == 0`; assigns the match to the slot in real state; for unscheduled matches moves from `League.UnscheduledMatches` to `League.Matches`; closes window with `DialogResult=true`

**Recursive chain**: child Move Analyzer receives parent's match tentatively placed at the parent's selected slot as `additionalContext`, so it correctly excludes that slot and respects weekend constraints for the parent's teams. Parent always calls `RefreshAfterChildAction()` when child closes (regardless of commit/cancel) so any real-state changes (bumps) are reflected.

### 6.5 Statistics Tab (`StatisticsView`)

Read-only pivot grids summarising the current schedule. The tab contains four sub-tabs.

**Data source:** `MainViewModel.StatisticsVM` — a `StatisticsViewModel` instance. `StatisticsVM.RefreshStatistics(matches)` is called at the end of every `RenderSchedule()` call in `MainViewModel`. `StatisticsVM.Clear()` is called from `ClearAllForms()` when no league is loaded.

**Dynamic column generation:** each grid uses `AutoGenerateColumns="True"` with `ItemsSource` bound to a `DataTable.DefaultView`. The code-behind (`StatisticsView.xaml.cs`) subscribes to `StatisticsViewModel.PropertyChanged` and updates the three `DataGrid.ItemsSource` references whenever the corresponding `DataTable` property changes. Column widths and header styles are applied in the `AutoGeneratingColumn` event handler.

**Total row styling:** any row whose `"Team"` cell equals `"Total"` is rendered bold with a light-grey background via the `LoadingRow` event handler. The `"Total"` column header is rendered bold via a named `Style` applied in `AutoGeneratingColumn`.

#### Sub-tab 1 — Matches Scheduled

**Purpose:** shows on which dates each team has a match scheduled.

**Grid layout:**

| Column | Content |
|---|---|
| `Team` (first column, fixed) | Team name; last row = `"Total"` |
| One column per unique match date, ordered ascending | `1` if the team plays on that date, `0` otherwise |
| `Total` (last column) | Sum of 1s in the row = number of match-dates for the team |
| **Total row (last row)** | Number of matches actually scheduled on each date (not just teams present — counts matches) |

- Date columns are labelled `MM/dd` (e.g. `"04/19"`).
- A team-date cell is `1` if the team appears as `TeamOne` or `TeamTwo` in any match on that date.
- The Total-row cell for a date = `matches.Count(m => m.Date == date)` (number of matches, not unique teams).
- The Total-row Total cell = total scheduled match count across all dates.

#### Sub-tab 2 — Umpiring Schedule

**Purpose:** shows on which dates each team has umpiring duty.

**Grid layout:** identical structure to Matches Scheduled.

| Column | Content |
|---|---|
| `Team` (first column) | Team name; last row = `"Total"` |
| One column per unique match date, ordered ascending | `1` if the team has at least one umpiring assignment on that date, `0` otherwise |
| `Total` (last column) | Number of dates on which the team has umpiring duty |
| **Total row (last row)** | Number of distinct teams with umpiring duty on each date |

- A team-date cell is `1` if the team appears in `UmpireOne`, `UmpireTwo`, `UmpireThree`, or `UmpireFour` in any match on that date.
- The Total-row Total cell = sum of all team-date umpiring-duty counts across all dates.

#### Sub-tab 3 — Ground Assignment

**Purpose:** shows how many matches each team plays at each ground.

**Grid layout:**

| Column | Content |
|---|---|
| `Team` (first column, fixed) | Team name; last row = `"Total"` |
| One column per unique ground name, ordered ascending | Count of matches where the team is `TeamOne` or `TeamTwo` **and** `Match.Ground.Name == this ground` |
| `Total` (last column) | Total matches for the team across all grounds |
| **Total row (last row)** | Total matches played at each ground (across all teams); last cell = overall total |

- Only matches with a non-null `Ground` are included.
- A cell shows a raw integer count (not 0/1 — a team can play multiple matches at the same ground).

#### Sub-tab 4 — Weekly Ground Usage

**Purpose:** shows how many matches are played at each ground per calendar week.

**Grid layout:**

| Column | Content |
|---|---|
| `Ground` (first column, fixed) | Ground name; last row = `"Total"` |
| One column per week (Mon–Sun), ordered ascending | Count of matches at that ground within the week; `·` if zero |
| `Total` (last column) | Total matches at the ground across all weeks |
| **Total row (last row)** | Total matches across all grounds per week; last cell = overall total |

- Week columns are labelled by the **Monday** start date of each week in `MM/dd` format (e.g. `"04/07"`).
- `GetWeekStart(date)` returns the Monday of the week: `date.AddDays(-((date.DayOfWeek - DayOfWeek.Monday + 7) % 7))`.
- A week spans Monday–Sunday (`weekStart` to `weekStart.AddDays(6)`). Cricket matches fall on Saturday/Sunday so they always land within a single week window.
- Only matches with both `Date` and `Ground` assigned are included.
- Zero cells display `·` (middle dot) rather than `0` for readability; the Total row always shows a numeric count.
- Data source: `StatisticsViewModel.MatchesPerWeekPerGround` — a `List<DivisionStatTable>` with one entry (`DivisionName = "All Divisions"`) containing the single cross-division pivot table.

---

## 7. ViewModel (`MainViewModel.cs`)

Single `ObservableObject`-derived class handling all tabs.

### Key property groups

```csharp
// Statistics
StatisticsViewModel StatisticsVM { get; }   // child VM for Statistics tab pivot data

// Tournament
DateTime? TournamentStartDate, TournamentEndDate, NewDiscardedDate

// Divisions
Division? SelectedDivision
bool EditDivisionIsRoundRobin          // bound to mode editor radio buttons
string EditDivisionMatchesPerTeam      // bound to mode editor textbox
ObservableCollection<Division> Divisions
ObservableCollection<Division> FilteredDivisions

// Requests
DateTime? NewRequestDate
string? NewRequestTimeSlot             // selected from slot dropdown
ObservableCollection<SchedulingRequestRow> SchedulingRequests
ObservableCollection<SchedulingRequestRow> FilteredRequests

// Scheduler — scheduled matches
ObservableCollection<ScheduleRowViewModel> ScheduledMatches
ObservableCollection<ScheduleRowViewModel> FilteredScheduledMatches
ObservableCollection<ForbiddenSlot> ForbiddenSlots
ObservableCollection<MoveOptionViewModel> MoveOptions
string ScheduleSearchText, RequestSearchText, DivisionSearchText
bool ShowMovePanel
string MoveAnalysis

// Scheduler — unscheduled matches
ObservableCollection<UnscheduledMatchViewModel> UnscheduledMatches
ObservableCollection<MoveOptionViewModel> UnscheduledMoveOptions  // available slot analysis
string ManualScheduleDate
string? ManualScheduleTimeSlot, ManualScheduleGround, ManualScheduleUmpire
```

### Commands (all `[RelayCommand]`)

| Command | Description |
|---|---|
| `CreateLeague` | Prompts for name via `InputDialog`, creates league folder |
| `OpenLeague` | Loads all CSVs, populates all ViewModel collections including unscheduled matches |
| `DeleteLeague` | Deletes league folder after confirmation |
| `SaveTournament` | Persists tournament fields to `tournament.csv` |
| `AddDiscardedDate / Remove` | Manages blackout date list |
| `AddGround / Remove` | Manages ground list |
| `AddTimeSlot / Remove` | Manages time slot list |
| `AddDivision / Remove` | Adds/removes division |
| `UpdateDivisionMode` | Applies `EditDivisionIsRoundRobin` + `EditDivisionMatchesPerTeam` to selected division |
| `SaveDivisions` | Persists to `divisions.csv` |
| `ImportDivisions` | Bulk CSV import (DivisionName,TeamName) |
| `AppendDivisions` | Merge CSV without overwriting existing divisions/teams |
| `AddTeam / Remove` | Adds/removes team from selected division |
| `DeleteSelectedDivisionTeams` | Multi-row team delete (takes `DataGrid` parameter) |
| `AddRequest / Remove` | Single request add/remove |
| `AddCombinedSlotConstraint` | Multi-slot combined range block (takes `ListBox` parameter) |
| `SaveRequests` | Persists to `constraints.csv` |
| `ImportRequests` | Bulk CSV import |
| `ExportFilteredRequests` | Exports `FilteredRequests` to user-chosen CSV |
| `DeleteSelectedRequests` | Multi-row delete (takes `DataGrid` parameter) |
| `SaveSchedule` | Syncs `CurrentLeague.Matches` from `ScheduledMatches`, saves without regenerating |
| `GenerateSchedule` | Fresh schedule; pre-generates fixed-mode pairings if absent; passes `CurrentLeague.Matches.Where(m => m.IsFixed)` so fixed matches survive unchanged. Saves relaxation flags before generate using a `(TeamOne, TeamTwo, Division, int occurrenceIndex)` 4-tuple key so duplicate match pairs (same teams in a fixed-pairing division) each get a distinct entry. Runs `RunScheduleVerificationAndRetry` + `RunUmpiringVerificationAndRetry` after generate. |
| `Reschedule` | Non-destructive: calls `ReschedulePreservingExisting` — keeps all currently scheduled matches as context, only places `UnscheduledMatches`; fixed match umpires/slots preserved |
| `RescheduleUmpiring` | Clears umpires on non-fixed matches, reassigns using priority rules, saves |
| `ExportSchedule / ExportFilteredSchedule` | CSV export (all / filtered) |
| `ImportMatches` | Overlay existing matches |
| `ToggleFixedSelected` | Toggle IsFixed on all selected rows |
| `UnscheduleSelectedMatches` | Move selected scheduled matches to unscheduled grid (takes `DataGrid` parameter) |
| `CopySelectedMatches / PasteMatches` | In-memory clipboard |
| `DeleteSelectedMatches` | Multi-row delete (takes `DataGrid` parameter) |
| `AddForbiddenSlot / Remove` | Manages forbidden slot list |
| `AnalyzeMove` | Opens `MoveAnalyzerWindow`; after `DialogResult==true`, awaits `FinaliseMove()` |
| `OpenRescheduleAnalyzer` | Open Move Analyzer for the selected unscheduled match; on commit calls `FinaliseMove` + `SyncUnscheduledGrid` |
| `RescheduleGroundAndUmpiring` | Balance ground assignments across all non-fixed matches (greedy per date+slot group), then re-run umpiring; fixed matches untouched |
| `ClearFilters` | Resets all three filter dropdowns |

### WPF Binding rules
- `<Run Text="{Binding Prop, Mode=OneWay}"/>` — always explicit `Mode=OneWay` for computed properties inside `TextBlock`
- `<DataGridCheckBoxColumn Binding="{Binding IsFixed, Mode=OneWay}"/>` — `Mode=OneWay` for init-only or read-only row properties
- `DataGridTextColumn` — defaults to `OneWay`, safe for display-only row models
- `InverseBoolConverter.Instance` — used for "Fixed mode" radio button (`IsRoundRobin=false` → radio checked)

---

## 8. Scheduling Engine

### 8.1 Match Generation (per division)

**Round Robin (`IsRoundRobin=true`):**
- Generates all N×(N-1)/2 unique pairs

**Fixed matches per team (`IsRoundRobin=false`):**

#### Algorithm Overview

  - Teams are **sorted alphabetically (case-insensitive)**.
  - Each team plays exactly `MatchesPerTeam` opponents.
  - A **skip value** determines which opponents a team will NOT play.

  **Skip formula:**
  ```
  skip = n - MatchesPerTeam - 1
  ```

---

#### Pairing Logic

  - For each team index `i`, opponents are chosen excluding:
    - Itself
    - `skip` teams starting from mirror position `(n - 1 - i)` moving inward

  - Only valid pairs `(i < j)` are selected where:
    - Not forbidden
    - Both teams under match quota
    - Pair not already used

---

#### Guarantees

  - Exact `MatchesPerTeam` matches per team
  - No duplicate matches (in base case)
  - Balanced distribution
  - Deterministic output

---

#### 🔁 Handling MatchesPerTeam > (n - 1)

When matches requested exceed unique opponents:

```
MatchesPerTeam > (n - 1)
```

##### Behavior

1. Generate all **unique pairings first**
2. Then **repeat matches** using same balanced pairing logic
3. Previously used pairs are allowed in second pass

---

##### Example: 8 Teams, 8 Matches Per Team

- Each team has 7 unique opponents
- Needs 1 extra match

**Repeat pairing (mirror-based):**

| Team | Repeat Opponent |
|------|---------------|
| T1   | T8            |
| T2   | T7            |
| T3   | T6            |
| T4   | T5            |

---

##### Result

- Each team plays:
  - 7 unique matches
  - 1 repeated match
- Repeats are:
  - Symmetric
  - Evenly distributed

---

##### Implementation Note
- `GenerateSchedule` calls `GeneratePairingsForDivision(div)` for any fixed-mode division with empty `FixedPairings`
- Requires relaxing `usedPairs` constraint after first pass
- Same `GeneratePairingsForDivision` function can support multi-pass logic

---


### 8.2 Modular Scheduling Pipeline

The scheduling system is split into **8 independent, loosely coupled routines** (implemented in `SchedulingService.cs` + `SchedulingService.Pipeline.cs`). Each routine is callable independently.

#### Routine 1 — Match Pair Generation (`MatchGenerator.GenerateMatches`)
Generates all expected match pairs from division config (round-robin or fixed-pairing). See §6.2.

#### Routine 2 — Slot Universe Generation (`SchedulingMatrixBuilder.BuildSlots`)
Generates all valid `(date × ground × timeslot)` slots:
- Iterates every weekend (Sat+Sun) between `StartDate` and `EndDate`
- Skips discarded/blackout dates
- Crosses every **Ground** with every **TimeSlot** defined in tournament setup
- **N grounds × M timeslots per date = N×M distinct identifiable slots per date**
  - Example: 2 grounds × 3 timeslots = 6 slots on a given date; each slot independently bookable
- Output: `List<SchedulableSlot>` — each entry uniquely identified by `(Date, Ground, TimeSlot)`
- Globally-forbidden slots (no ground/division qualifier) stripped immediately; division- and ground-specific forbidden slots enforced per-match in Phase 3

#### Routine 3 — Match Scheduling (`ScheduleMatchesToSlots`) — Phase 3
Assigns a `(Date, Ground, TimeSlot)` to every match. Ground fairness is NOT enforced here — that is Phase 5.
1. Three orderings tried (picks the one scheduling the most matches):
   - Most-constrained-team first
   - Division-balanced
   - Fewest-available-slots first (hardest match placed first)
2. `TrySlotOrdering` — greedy pass; each match picks the highest-scoring valid `SchedulableSlot`
3. `BacktrackImproveSlots` — for unscheduled remainders:
   - Direct placement first (constraints may have relaxed after earlier placements)
   - If still no slot: displace one non-fixed match, place the current match, re-place the displaced one
4. `IsForbiddenForMatch` called per match for division-aware forbidden-slot enforcement
5. Fixed matches are immovable context — included in the result unchanged

#### Routine 4 — Pair Verification (`RestoreMissingPairs`)
Compares expected pairs (from division config) against `scheduled + unscheduled`. Any missing pair is created and added to `UnscheduledMatches`. Called from `MainViewModel.RunPairCompletionCheck()` after generate. See §16.6.

#### Routine 5 — Hard Scheduling Rule Validation (`VerifySchedule`)
Checks the finalised schedule for:
1. Two matches sharing the same `(ground, date, timeslot)`
2. A team with >1 match on the same weekend
3. A match on a forbidden slot
Violations are moved to `UnscheduledMatches` and a `ReschedulePreservingExisting` pass is retried (up to 2×). Called from `RunScheduleVerificationAndRetry()`.

#### Routine 6 — Ground Assignment (`AssignGrounds`) — Phase 5
Rebalances ground assignments while **keeping date and timeslot fixed**.
- Clears all non-fixed grounds; fixed match grounds are preserved
- Sorts matches most-constrained-first (fewest valid candidate grounds)
- Recursive backtracking: tries grounds in order of lowest combined team-usage (fairness)
- On failure at any node: backtracks to try the next candidate; if all candidates fail, leaves Ground = null and continues
- Respects ground-specific forbidden slots
- Called after Phase 3 (initial generate) and by `RescheduleGroundAndUmpiring`

#### Routine 7 — Umpiring Assignment (`RescheduleUmpiring` / `AssignUmpires`)
Assigns umpiring teams to non-fixed matches. See §11 for full rules.

#### Routine 8 — Umpiring Rule Validation (`VerifyUmpiring`)
Checks umpiring assignments against 5 hard rules. Violations cleared and `RescheduleUmpiring` re-run (up to 2×). Called from `RunUmpiringVerificationAndRetry()`.

---

### 8.3 `Generate` Orchestrator (full pipeline)

```
Phase 1  MatchGenerator.GenerateMatches        → match pairs
Phase 2  SchedulingMatrixBuilder.BuildSlots     → slot universe (N grounds × M slots × D dates)
         + IsForbidden global filter
Phase 3  ScheduleMatchesToSlots                 → date + ground + timeslot assigned
Phase 4  RestoreMissingPairs                    → any missing pair added to unscheduled
Phase 5  AssignGrounds                          → ground rebalanced for fairness
Phase 5v VerifySchedule / retry                 → slot conflict check + auto-correct
Phase 6  AssignUmpires                          → umpiring assigned
Phase 6v VerifyUmpiring / retry                 → umpiring rule check + auto-correct
```

**`ReschedulePreservingExisting`** (called by `Reschedule` button — non-destructive):
1. Uses all currently scheduled matches (fixed + non-fixed) as starting context
2. Attempts to place only the `UnscheduledMatches` list into remaining open slots
3. Runs `BacktrackImproveSlots` if any still unplaceable
4. Reassigns umpires for non-fixed matches only; fixed match umpires are preserved unchanged

### 8.3 Core Scheduling Constraints

- Matches only on **weekends (Saturday + Sunday)**
- Each team: **max 1 match per weekend**
- Each team: **max 2 consecutive no-match weekends**
- Respect team availability constraints (full-day and partial-day blocks)
- **Fixed matches are never moved or overwritten by any scheduling operation** — this applies to Generate, Reschedule, Backtrack Reschedule, Move Analyzer, and manual scheduling panels
- Forbidden slots excluded from slot matrix

### 8.4 Fairness Rules
- Even distribution across grounds
- Even distribution across time slots

### 8.5 Constraint Relaxation System

Unscheduled matches appear in the **Unscheduled Matches** grid. Each row has 8 independent constraint flags:

| Column | Constraint relaxed |
|---|---|
| **Ground Fairness** | Allow assigning to an over-used ground |
| **Umpire Fairness** | Assign umpiring team regardless of duty load |
| **Time Slot Fairness** | Allow over-represented time slots |
| **≤2 Week Break** | Allow team to sit out >2 consecutive weekends |
| **1 Match/Weekend** | Allow >1 match per team per weekend |
| **Time Slot Restriction** | Ignore partial-time unavailability blocks |
| **Date Restriction** | Ignore full-day unavailability blocks |
| **Blackout Dates** | Allow scheduling on discarded/blackout dates |

**Hard constraint (never relaxed):** two matches cannot share the same ground + date + time slot.

**Toolbar shortcuts:**
- **Relax All Selected** — ticks all 8 flags for selected rows
- **Clear Relaxations** — unticks all flags for selected rows
- **Backtrack Reschedule** — retries unscheduled matches using their individual relaxation settings; also calls `IsForbiddenForMatch` per match

---

## 9. Forbidden Slots — Division-Aware Enforcement

- Optional filters on the scheduling matrix
- Any combination of: Date, Ground, TimeSlot, Division (all nullable)
- **`IsForbidden`** (Phase 2 global pre-filter): strips slots where the forbidden entry has no `Division` qualifier; division-specific and ground-specific entries are deferred to per-match checks
- **`IsForbiddenForMatch(slot, divisionName, forbidden)`** (Phase 3 per-match check): checks all four fields — `Date`, `Ground`, `TimeSlot`, `Division` (null = wildcard for any)
- Ground-specific forbidden slots (`f.GroundName != null`) are respected in both `IsForbiddenForMatch` (Phase 3) and `GroundAssignmentState.CandidateGrounds` (Phase 5/6)
- `IsForbiddenForMatch` is called in `TrySlotOrdering`, `BacktrackImproveSlots`, `BacktrackReschedule`, and `SuggestMoves`
- Stored in persistant CSV  `ForbiddenSlots` collection (save with torunament.csv ) loat with tournament open
- Each slot shows a `Display` string: `Date | Ground | TimeSlot | Division`

---


## 10. Move Analysis (`SuggestMoves`)

When user clicks **Analyze Move** on a selected match:
1. `SchedulingService.SuggestMoves(league, match, forbiddenSlots)` is called
2. All valid alternative slots are evaluated — **including occupied slots** (overwrite-aware):
   - `ConstraintEvaluator.IsSlotAllowedForMove` — checks team-availability constraints only (does NOT reject occupied slots)
   - `IsForbiddenForMatch` — respects division-specific forbidden slots
   - Slots occupied by **fixed matches** are excluded entirely (can never be overwritten)
   - Fixed matches are included in the constraint context so team-busy checks see all commitments
   - Slot must differ from current assignment
   - before suggesting a slot virtually place the match there and check if it breaks any hard scheduling rule.
3. For occupied slots: identifies non-fixed displaced match(es), temporarily removes them from constraint context, evaluates team availability as if vacated; `AffectedMatchList` populated for UI display
4. Results sorted: fewest affected matches first, then highest fairness score
5. Slots with 0 affected and fairness > 80 marked `IsRecommended=true` (shown in green)
6. User selects a slot → **Commit Move** → `FinaliseMove()` saves + re-renders grid
7. If a child-level sub-analyzer commits a move, the parent analyzer **refreshes its slot options** so conflicts resolved in the child are reflected in the parent grid

Selecting an unscheduled match also triggers `SuggestMoves` → populates `UnscheduledMoveOptions`; double-clicking a row fills the manual schedule panel inputs.

**`additionalFixed` parameter (context-aware suggestions for displaced matches):**
When the Move Analyzer is opened for a *displaced* (affected) match from the unscheduled slot panel, a virtual copy of the unscheduled match is created with its target date/slot/ground and `IsFixed=true`. This is passed as `additionalFixed` to `MoveAnalyzerViewModel` → `SuggestMoves`. Effect:
- The target slot (where the unscheduled match will be placed) is added to `fixedSlotKeys` and excluded from candidate slots for the displaced match.
- The unscheduled match's teams participate in the same-weekend conflict check, so the displaced match's suggested slots correctly avoid weekends where the unscheduled match's teams are already committed.
- When `additionalFixed` is non-null (strict/displaced-match mode): any slot that would cause a non-fixed same-weekend conflict is also hard-filtered out (only constraint-clean slots are suggested).
- Child-level analyzers (resolving displaced-of-displaced) do NOT receive `additionalFixed` — they only need to find any free slot for the next-level match.

**Hard constraint filter in `SuggestMoves`:**
- Always: slots where a **fixed** match already occupies the team's weekend are filtered out (fixed matches cannot be moved — unresolvable violation).
- Strict mode (`additionalFixed` non-null): slots where any non-fixed same-weekend conflict exists are also filtered out (so only constraint-clean options are suggested to the user).

**`RescheduleGroundAndUmpiring(league, forbidden?)` algorithm (tree-like, most-constrained first):**
1. Build `occupied` dict (`"date|HH:mm|groundname" → Match`) seeded from fixed matches; build `(team, ground) → count` usage table seeded from fixed matches.
2. Sort non-fixed matches by fewest valid candidate slots in their weekend (most constrained first).
3. For each match:
   a. Temporarily remove it from `occupied` and decrement its ground-usage counts.
   b. Scan all tournament slots in the **same weekend** (Sat+Sun pair): skip occupied, forbidden, date-blocked, time-blocked, and slots that would violate 1-per-weekend for either team.
   c. Score each candidate: `usageTeamOne[ground] + usageTeamTwo[ground] + (5 if different day)`. The day-change penalty discourages moving Sat↔Sun unless it gives meaningfully better balance.
   d. Assign the lowest-scoring slot. If none found, restore the original assignment.
4. Call `RescheduleUmpiring` to re-assign umpires on the updated layout.

---

## 11. Umpiring

Priority order for `AssignUmpires` (all subject to hard rules, soft rule applied within each tier):

| Priority | Criterion |
|---|---|
| 1 | Team with a match in **adjacent slot on same date + ground** (physically at the ground) |
| 2 | Team with **no match on the same WEEKEND** (Sat+Sun pair) — least travel burden |
| 3 | Any eligible team with the **lowest umpire load** (fairness fallback) |

Within each priority tier: teams that umpired within the last 7 days are deprioritized (soft gap rule), then ties broken by lowest cumulative umpire load.

Hard rules (always enforced):
- A team **never** umpires its **own division**
- A team **never** umpires a match it is **playing** (same date + slot, any ground)
- A team **never** umpires at a ground where it is **not playing that day**
- A team **never** umpires more than **once per weekend** (Sat+Sun pair)
- A team's total umpiring load **never exceeds `ceil(matchesPlaying / 2)`** (e.g. 9 matches → max 5 duties; 8 matches → max 4 duties)
- Fixed matches' existing umpire assignments are pre-seeded into the load/weekend tracking so they count toward these limits

Export: both `UmpireOne` and `UmpireTwo` CSV columns are set to `match.UmpireOne` (same team name in both boxes).

**`RescheduleUmpiring(League league)`** — public method: clears umpire assignments on non-fixed matches, then calls `AssignUmpires` with the new rules above.

---

## 12. Multi-select & Clipboard (all grids)

| Operation | How |
|---|---|
| Multi-select rows | `SelectionMode="Extended"` + Ctrl/Shift+click |
| Delete selected | Toolbar button passes `DataGrid` reference via `CommandParameter` |
| Copy matches | Stores selected `ScheduleRowViewModel` list in `_clipboard` |
| Paste matches | Clones each `SourceMatch`, assigns new sequence number |
| Export filtered | Saves only `FilteredRequests` or `FilteredScheduledMatches` |

---

## 13. Search & Filter (all screens)

| Screen | Filter mechanism |
|---|---|
| Scheduler | Division/Team/Ground dropdowns + free-text `ScheduleSearchText` |
| Requests | Free-text `RequestSearchText` (team name or date) |
| Divisions | Free-text `DivisionSearchText` (division name) |

Searches use `StringComparison.OrdinalIgnoreCase` and update in real-time via `UpdateSourceTrigger=PropertyChanged`.

---

## 14. Export

### 14.1 CSV exports

All CSV exports via `ExportService.ExportScheduleAsync`:
- Full schedule → `data/leagues/{name}/schedule.csv`
- Filtered schedule → user-chosen path via `SaveFileDialog`
- Filtered requests → user-chosen path via `SaveFileDialog`

**`IsFixed` persistence:** `ExportService` always writes `IsFixed = m.IsFixed` in the `ScheduleCsv` DTO. `LeagueService` also reads and writes `IsFixed`. Both paths are required — omitting either causes the flag to be silently reset to `false` on the next save.

#### Schedule CSV strict format
```
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,
Umpire One,Umpire Two,Umpire Three,Umpire Four,Match Manager,Scorer 1,Scorer 2,IsFixed
```
- `Date` = `MM/dd/yyyy`
- `Time` = `h:mm tt`
- `IsFixed` = `True`/`False`

### 14.2 Combined Excel export (`ExportAllStatisticsToExcel`)

**Triggered by:** "📊 Export All Stats (Excel)" button in the Statistics view header — opens a `SaveFileDialog` (defaults to `outputs/` folder, suggests a timestamped `.xlsx` filename).

**Method:** `ExportService.ExportAllStatisticsToExcel(StatisticsViewModel vm, IEnumerable<Match> scheduledMatches, IEnumerable<Match> unscheduledMatches, string path)` — static, uses `ClosedXML`.

**Workbook structure — 6 sheets in order:**

| Sheet | Header colour | Content |
|---|---|---|
| **Schedule** | Green (`#C6EFCE`) | All scheduled matches; columns: `#, Division, Match Type, Date, Time, Team One, Team Two, Ground, Umpire 1–4, Match Manager, Scorer 1–2, Fixed`; sorted by Date → Time → Sequence; fixed-match rows shaded light green |
| **Unscheduled** | Amber (`#FFEB9C`) | Unscheduled matches; columns: `Division, Match Type, Team One, Team Two, Reason`; sorted by Division → Team One |
| **Matches Scheduled** | — | Pivot table (all divisions on one sheet, 5 blank rows between divisions) |
| **Umpiring Schedule** | — | Pivot table (same layout) |
| **Ground Assignment** | — | Pivot table (same layout) |
| **Weekly Ground Usage** | — | Pivot table (same layout) |

**Stat sheet layout (per division block):**
1. Division name row — bold, `LightSteelBlue` background, merged across all columns
2. Column header row — bold, `LightGray` background, centred
3. Data rows — `"Total"` row is bold + `LightGray`; non-first columns are centred
4. 5 blank rows before the next division block

**Code-behind wiring (`StatisticsView.xaml.cs`):**
- `_mainVm` (`MainViewModel`) cached alongside `_vm` in `OnDataContextChanged`
- `OnExportAllStatsExcel` extracts `SourceMatch` from `_mainVm.ScheduledMatches` and `Match` from `_mainVm.UnscheduledMatches`, then calls `ExportService.ExportAllStatisticsToExcel`

---

## 15. Multi-League

- Each league stored in `data/leagues/{leagueName}/`
- Operations: Create (name via dialog), Open, Delete
- League toolbar always visible at top of `MainWindow`
- On Open: all CSVs loaded, all ViewModel collections populated including unscheduled matches
- On Save (any tab): full league written back to all CSV files including `schedule.csv` and `unscheduled.csv`

---

## 16. Statistics View — Implementation Reference

### 16.1 Architecture

| Class | File | Role |
|---|---|---|
| `StatisticsViewModel` | `ViewModels/StatisticsViewModel.cs` | Owns the 4 `List<DivisionStatTable>` properties; all pivot computation lives here |
| `StatisticsView` | `Views/StatisticsView.xaml` | 4-sub-tab layout; panels built dynamically in code-behind from `DivisionStatTable` lists |
| `StatisticsView` code-behind | `Views/StatisticsView.xaml.cs` | Subscribes to `StatisticsViewModel.PropertyChanged`; calls `BuildPanels` to create one `DataGrid` per division/table entry; handles column widths, header styles, and total-row styling |

`StatisticsViewModel` is a plain `ObservableObject`; it is **not** registered in the DI container. `MainViewModel` owns the single instance:

```csharp
public StatisticsViewModel StatisticsVM { get; } = new();
```

### 16.2 Lifecycle

| Event | `MainViewModel` action |
|---|---|
| `RenderSchedule()` completes | Calls `StatisticsVM.RefreshStatistics(CurrentLeague?.Matches ?? [])` |
| `ClearAllForms()` called | Calls `StatisticsVM.Clear()` — sets all three DataTable properties to `null` |

`RenderSchedule()` is invoked after every operation that changes the schedule: `GenerateSchedule`, `Reschedule`, `BacktrackReschedule`, `RescheduleUmpiring`, `FinaliseMove`, `ManuallyScheduleMatch`, `PasteMatches`, `UnscheduleSelectedMatches`, `OpenLeague`, and `PopulateFormsFromLeague`.

### 16.3 `StatisticsViewModel` API

```csharp
public partial class StatisticsViewModel : ObservableObject
{
    [ObservableProperty] List<DivisionStatTable> matchesByDivision       = [];
    [ObservableProperty] List<DivisionStatTable> umpiringByDivision      = [];
    [ObservableProperty] List<DivisionStatTable> groundByDivision        = [];
    [ObservableProperty] List<DivisionStatTable> matchesPerWeekPerGround = [];  // single "All Divisions" entry

    public void RefreshStatistics(IEnumerable<Match> matches);
    public void Clear();  // resets all four lists to []
    public static string ExportTableToCsv(DataTable table, string prefix, string divisionName);
}

public sealed class DivisionStatTable
{
    public string    DivisionName { get; init; }
    public DataTable Table        { get; init; }
}
```

`RefreshStatistics` filters out unscheduled matches (`!m.Date.HasValue`) before building any pivot. Each of the four list properties is assigned as a whole new list (fires `PropertyChanged` once per property). `matchesPerWeekPerGround` always contains exactly one `DivisionStatTable` entry with `DivisionName = "All Divisions"` aggregating all divisions.

### 16.4 DataTable Schema

Tables 1–3 use `"Team"` as the first column; table 4 uses `"Ground"`. All tables end with a `"Total"` column and a `"Total"` last row. The code-behind detects which variant a table is by checking `table.Columns.Contains("Team")` vs `"Ground"`.

**Matches Scheduled table** — columns: `Team`, one `MM/dd` per unique date (ascending), `Total`
- Team-date cell = `"1"` if team plays on that date, `"·"` otherwise
- Total column = count of dates the team has a match
- Total row = `matches.Count(m => m.Date == date)` per date; Total-row Total = overall scheduled-match count

**Umpiring Schedule table** — same date columns as Matches Scheduled (all match dates, not just division dates)
- Team-date cell = `"1"` if team appears in any of `UmpireOne`–`UmpireFour` on that date, `"·"` otherwise
- Total column = number of dates the team has umpiring duty
- Total row = number of teams with at least one umpiring assignment on each date; Total-row Total = sum across all dates

**Ground Assignment table** — columns: `Team`, one column per unique ground name (ascending), `Total`
- Cell = count of matches where team is `TeamOne` or `TeamTwo` at that ground (integer string, not binary)
- Total column = total matches for the team across all grounds
- Total row = `matches.Count(m => m.Ground?.Name == ground)` per ground; Total-row Total = overall count

**Weekly Ground Usage table** — columns: `Ground`, one `MM/dd` week-start (Monday) per week (ascending), `Total`
- Week-start computed by `GetWeekStart(date)`: `date.AddDays(-((date.DayOfWeek - DayOfWeek.Monday + 7) % 7))`
- Ground-week cell = count of matches at that ground in the Mon–Sun window; `"·"` if zero
- Total column = total matches at the ground across all weeks
- Total row = matches across all grounds per week (numeric string, never `"·"`); Total-row Total = overall count
- Single `DivisionStatTable` entry with `DivisionName = "All Divisions"` (cross-division aggregate)

### 16.5 View and Code-Behind

**`StatisticsView.xaml`** — header bar + four `TabItem`s (`Matches Scheduled`, `Umpiring Schedule`, `Ground Assignment`, `Weekly Ground Usage`):
- **Header bar** (`Border`, `#F5F5F5`) — contains a `DockPanel` with "Schedule Statistics" label and a **"📊 Export All Stats (Excel)"** button (`ExportAllStatsExcelBtn`, right-docked) that exports the combined `.xlsx` workbook
- Each tab contains:
  - A description `TextBlock` explaining the grid semantics
  - A named `StackPanel` (`MatchesPanel` / `UmpiringPanel` / `GroundPanel` / `WeeklyGroundPanel`) inside a `ScrollViewer`
  - An **Export All** button (`ExportAllMatchesBtn` / `ExportAllUmpiringBtn` / `ExportAllGroundBtn` / `ExportAllWeeklyGroundBtn`) that exports that category to individual CSVs via `StatisticsViewModel.ExportTableToCsv`

**`StatisticsView.xaml.cs`** — key responsibilities:
1. `DataContextChanged` → caches `MainViewModel` as `_mainVm` and `MainViewModel.StatisticsVM` as `_vm`; subscribes to `_vm.PropertyChanged`; calls `RefreshAll()`
2. `OnVmPropertyChanged` → dispatches to `BuildPanels(panel, tables, prefix)` for the changed property
3. `RefreshAll()` → calls `BuildPanels` for all four panels
4. `BuildPanels(StackPanel, List<DivisionStatTable>, string prefix)` — clears the panel and rebuilds: one `TextBlock` header + one `DataGrid` per `DivisionStatTable` entry. DataGrids use `AutoGenerateColumns=false`; columns built by `BuildColumns`.
5. `BuildColumns(DataGrid, DataTable)` — creates one `DataGridTemplateColumn` per DataTable column using `FrameworkElementFactory`:
   - First column (`"Team"` or `"Ground"`): width 150, left-aligned, `TeamHeaderStyle`
   - `"Total"` column: width 55, center-aligned, `TotalHeaderStyle` (bold, grey background)
   - All other columns: width 50, center-aligned
   - `Foreground` set as a **local value** via `factory.SetValue` so it beats all WPF theme overrides
   - Binding: `new Binding($"[{columnName}]")` (DataRowView bracket indexer)
6. `OnLoadingRow` — checks `drv["Team"]` or `drv["Ground"]` for `"Total"`: sets `FontWeight=Bold`, `Background=#DCDCDC`; otherwise resets to `Normal` / `Transparent` (reset required — WPF virtualises and reuses row containers)
7. Per-tab export buttons wired in constructor: `ExportAll(_vm?.MatchesByDivision, "matches")` etc.; per-division `Export` button added inline in `BuildPanels`
8. `OnExportAllStatsExcel` — opens `SaveFileDialog`, extracts `SourceMatch` from `_mainVm.ScheduledMatches` and `Match` from `_mainVm.UnscheduledMatches`, calls `ExportService.ExportAllStatisticsToExcel`

### 16.6 Constraints and Edge Cases

- **No matches loaded:** all four `List<DivisionStatTable>` properties are `[]`; `BuildPanels` returns immediately leaving the panel empty.
- **Date column uniqueness:** `MM/dd` format is used for column names. For single-season tournaments this is collision-free. Two matches on the same calendar date produce one column (same date — no collision). Multi-year leagues could theoretically collide; out of scope for single-season use.
- **Week column uniqueness:** `GetWeekStart` returns the Monday `MM/dd`; within one season each Monday is unique so no DataTable `DuplicateNameException` arises.
- **Ground column uniqueness:** ground names are used directly as DataTable column names. Ground names must be unique (enforced at tournament setup level).
- **`DataTable` is not thread-safe:** `RefreshStatistics` is always called from the UI thread (invoked at the end of `RenderSchedule`, which runs on the dispatcher thread). No locking needed.
- **Row container recycling:** WPF DataGrid virtualises rows. The `OnLoadingRow` handler always sets both the "Total" style **and** the normal-row reset path to avoid stale bold/grey on recycled containers.
- **First-column detection:** `BuildColumns` checks `name == "Team" || name == "Ground"` to identify the row-label column, allowing the same method to handle both team-pivot tables and the ground-pivot table.
- **Duplicate match pairs in fixed-pairing divisions:** `GenerateSchedule` saves per-match relaxation flags using a 4-tuple key `(TeamOne, TeamTwo, Division, int occurrenceIndex)`. The occurrence index increments each time the same triple appears in `UnscheduledMatches`, so two KC Rockers vs Pirates matches in Div A get keys `(..., 0)` and `(..., 1)` respectively. The same index-counting logic is applied when looking up relaxations in the backtrack step and when restoring them after generate.
