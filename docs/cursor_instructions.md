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
│   │   └── StatisticsViewModel.cs     ← builds 3 DataTable pivots; exposed via MainViewModel.StatisticsVM
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
│   │   ├── SchedulingService.cs       ← Generate (2 overloads), SuggestMoves, RescheduleUmpiring
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
- `IsRoundRobin=False` → `MatchesPerTeam` opponents per team using balanced pairing

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
- `IsRoundRobin=false` → balanced pairing algorithm:
  - Teams sorted alphabetically; round-robin rotation wheel (round 0: Team[0]↔Team[N-1], Team[1]↔Team[N-2], …; subsequent rounds rotate wheel)
  - Each team gets exactly `MatchesPerTeam` opponents
  - No team plays any opponent more than once
  - Teams at quota are skipped in subsequent iterations
  - Result stored in `Division.FixedPairings`

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

**Unscheduled Matches section (below scheduled grid):**
- Persisted to/from `unscheduled.csv` on every save/load
- **Manual schedule panel** (above unscheduled grid): Date, Time Slot, Ground, Umpiring Team inputs
- **Schedule Selected** button — assigns panel inputs to selected unscheduled match, moves it to scheduled grid
- **Available Slots Expander** — populated by `SuggestMoves` when an unscheduled row is selected; double-clicking a slot row fills the panel inputs automatically
- **Constraint Relaxation** per-row checkboxes (see §8.3)

**Move Analyzer (separate window — `MoveAnalyzerWindow`):**
- Shows match being moved + affected teams
- `SlotOptions` DataGrid: Date, Time, Ground, Affected Matches, Fairness Score, Recommendation
  - Shows **both free slots** (0 affected) and **occupied slots** (≥1 displaced non-fixed matches)
  - 🟢 Recommended rows = 0 collisions + high fairness score
- **Commit Move** — calls `FinaliseMove()` which saves (`SaveLeagueAsync`) and re-renders the grid
- **Cancel** — closes window without saving

### 6.5 Statistics Tab (`StatisticsView`)

Read-only pivot grids summarising the current schedule. The tab contains three sub-tabs.

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
| `GenerateSchedule` | Fresh schedule; pre-generates fixed-mode pairings if absent; passes `CurrentLeague.Matches.Where(m => m.IsFixed)` so fixed matches survive unchanged |
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
| `ScheduleSelectedUnscheduled` | Schedule selected unscheduled match using panel inputs |
| `ApplyUnscheduledSlot` | Fill panel inputs from analysis row double-click |
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
- Teams sorted alphabetically; results stored in `Division.FixedPairings`
- Round 0: Team[0]↔Team[N-1], Team[1]↔Team[N-2], …, Team[N/2-1]↔Team[N/2]
- Subsequent rounds rotate the wheel (last → position 1)
- Stops when every team reaches `MatchesPerTeam`; no duplicate pairs
- `GenerateSchedule` calls `GeneratePairingsForDivision(div)` for any fixed-mode division with empty `FixedPairings`

### 8.2 Optimised Scheduling Algorithm

**`RunOptimisedSchedule`** (called by `Generate`):
1. **Build slot matrix once** — strips globally forbidden slots up front; division-specific forbidden slots applied per-match (see §9)
2. **Three orderings tried:**
   - Most-constrained-team first
   - Division-balanced
   - Fewest-available-slots first (hardest match scheduled first)
3. **Best result kept** — ordering that schedules the most matches wins
4. **Backtrack pass** (`BacktrackImprove`) — for each remaining unscheduled match:
   - Tries a direct slot first
   - If no direct slot: finds a non-fixed already-scheduled match whose removal frees a valid slot; displaces it, places the current match, then re-places the displaced match
5. Both `TryScheduleOrdering` and `BacktrackImprove` call `IsForbiddenForMatch` per match for division-aware enforcement
6. Fixed matches passed in to `Generate` are kept unchanged (slots, umpires, grounds all preserved); only non-fixed matches are regenerated

**`ReschedulePreservingExisting`** (called by `Reschedule` button — non-destructive):
1. Uses all currently scheduled matches (fixed + non-fixed) as starting context
2. Attempts to place only the `UnscheduledMatches` list into remaining open slots
3. Runs `BacktrackImprove` if any still unplaceable
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
- **`IsForbidden`** (global pre-filter, slot matrix build): **skips** entries that have a non-null `Division` field
- **`IsForbiddenForMatch(slot, divisionName, forbidden)`** (per-match check): checks all four fields including `Division` (null = all divisions wildcard)
- `IsForbiddenForMatch` is called in `TryScheduleOrdering`, `BacktrackImprove`, `BacktrackReschedule`, and `SuggestMoves`
- Stored in-memory in `ForbiddenSlots` collection (not persisted to CSV)
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
3. For occupied slots: identifies non-fixed displaced match(es), temporarily removes them from constraint context, evaluates team availability as if vacated; `AffectedMatchList` populated for UI display
4. Results sorted: fewest affected matches first, then highest fairness score
5. Slots with 0 affected and fairness > 80 marked `IsRecommended=true` (shown in green)
6. User selects a slot → **Commit Move** → `FinaliseMove()` saves + re-renders grid
7. If a child-level sub-analyzer commits a move, the parent analyzer **refreshes its slot options** so conflicts resolved in the child are reflected in the parent grid

Selecting an unscheduled match also triggers `SuggestMoves` → populates `UnscheduledMoveOptions`; double-clicking a row fills the manual schedule panel inputs.

---

## 11. Umpiring

Priority order for `AssignUmpires` (all subject to hard rules):

| Priority | Criterion |
|---|---|
| 1 | Team with a match in **adjacent slot on same date + ground** (physically at the ground) |
| 2 | Team with **no match in the same calendar week** (ISO week) — least travel burden |
| 3 | Any eligible team with the **lowest umpire load** (fairness fallback) |


Hard rules (always enforced):
- A team **never** umpires its **own division**
- A team **never** umpires a match it is **playing** (same date + slot, any ground)
- A team **never** umpires at a ground where it is **not playing that day**

Ties within each priority tier broken by lowest cumulative umpire load.

**`RescheduleUmpiring(League league)`** — public method: clears umpire assignments on non-fixed matches, then calls `AssignUmpires`.

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

All exports via `ExportService.ExportScheduleAsync`:
- Full schedule → `data/leagues/{name}/schedule.csv`
- Filtered schedule → user-chosen path via `SaveFileDialog`
- Filtered requests → user-chosen path via `SaveFileDialog`

**`IsFixed` persistence:** `ExportService` always writes `IsFixed = m.IsFixed` in the `ScheduleCsv` DTO. `LeagueService` also reads and writes `IsFixed`. Both paths are required — omitting either causes the flag to be silently reset to `false` on the next save.

### Schedule CSV strict format
```
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,
Umpire One,Umpire Two,Umpire Three,Umpire Four,Match Manager,Scorer 1,Scorer 2,IsFixed
```
- `Date` = `MM/dd/yyyy`
- `Time` = `h:mm tt`
- `IsFixed` = `True`/`False`

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
| `StatisticsViewModel` | `ViewModels/StatisticsViewModel.cs` | Owns the 3 `DataTable` properties; all pivot computation lives here |
| `StatisticsView` | `Views/StatisticsView.xaml` | 3-sub-tab layout; all DataGrids are `IsReadOnly="True"` with `AutoGenerateColumns="True"` |
| `StatisticsView` code-behind | `Views/StatisticsView.xaml.cs` | Wires `DataGrid.ItemsSource` to `DataTable.DefaultView`; handles column/row styling events |

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
    [ObservableProperty] DataTable? matchesScheduledTable;
    [ObservableProperty] DataTable? umpiringScheduleTable;
    [ObservableProperty] DataTable? groundAssignmentTable;

    public void RefreshStatistics(IEnumerable<Match> matches);
    public void Clear();  // sets all three tables to null
}
```

`RefreshStatistics` filters out unscheduled matches (`m.Date == null`) before building any pivot. All three `DataTable` properties are assigned atomically (separate assignments that each fire `PropertyChanged`).

### 16.4 DataTable Schema

All three tables follow the same column-naming convention:

| Position | Column name | Type |
|---|---|---|
| First | `"Team"` | `string` |
| Middle (1…N) | Dynamic — date `"MM/dd"` or ground name | `int` |
| Last | `"Total"` | `int` |

The last row in every table has `"Team"` = `"Total"`.

**Matches Scheduled table** — columns: `Team`, one `MM/dd` per unique date (ascending), `Total`
- Team-date cell = `1` if team plays on that date, `0` otherwise
- Total column = sum of 1s in the row (number of dates the team has a match)
- Total row = `matches.Count(m => m.Date == date)` per date (match count, not team count); Total-row Total = overall scheduled-match count

**Umpiring Schedule table** — same date columns
- Team-date cell = `1` if team appears in any of `UmpireOne`–`UmpireFour` on that date, `0` otherwise
- Total column = number of dates on which the team has umpiring duty
- Total row = number of distinct teams with at least one umpiring assignment on each date; Total-row Total = sum across all dates

**Ground Assignment table** — columns: `Team`, one column per unique ground name (ascending), `Total`
- Cell = count of matches where team is `TeamOne` or `TeamTwo` at that ground (integer, not binary)
- Total column = total matches for the team across all grounds
- Total row = `matches.Count(m => m.Ground?.Name == ground)` per ground; Total-row Total = overall count

### 16.5 View and Code-Behind

**`StatisticsView.xaml`** — three `TabItem`s (`Matches Scheduled`, `Umpiring Schedule`, `Ground Assignment`), each containing:
- A single-line description `TextBlock` explaining the grid semantics
- A `DataGrid` named `MatchesGrid` / `UmpiringGrid` / `GroundGrid` with `AutoGenerateColumns="True"`, `IsReadOnly="True"`, `CanUserAddRows="False"`, `CanUserDeleteRows="False"`, `CanUserReorderColumns="False"`, both scroll bars on `Auto`
- All three grids share the same `AutoGeneratingColumn` and `LoadingRow` event handlers

**`StatisticsView.xaml.cs`** — key responsibilities:
1. `DataContextChanged` → caches `MainViewModel.StatisticsVM` as `_vm`; subscribes to `_vm.PropertyChanged`; calls `RefreshAllGrids()`
2. `OnVmPropertyChanged` → maps property name to the matching `DataGrid.ItemsSource = table?.DefaultView`
3. `Grid_AutoGeneratingColumn` — sets column widths: `"Team"` = 130 px, `"Total"` = 55 px, all date/ground columns = 52 px; applies `TotalHeaderStyle` (bold, grey background) to `"Total"` column
4. `Grid_LoadingRow` — if `drv["Team"] == "Total"`: sets `FontWeight=Bold`, `Background=#DCDCDC`; otherwise resets to `Normal` / `Transparent` (reset required because WPF virtualises and reuses row containers)

### 16.6 Constraints and Edge Cases

- **No matches loaded:** all three DataTables are `null`; grids show empty (no columns, no rows)
- **Date column uniqueness:** `MM/dd` format is used for column names. For single-season tournaments this is collision-free. If two matches fall on the same calendar date, there is only one column (they are the same date — no collision).
- **Ground column uniqueness:** ground names are used directly as DataTable column names. Ground names must be unique (enforced at tournament setup level).
- **`DataTable` is not thread-safe:** `RefreshStatistics` is always called from the UI thread (it is invoked at the end of `RenderSchedule`, which runs on the dispatcher thread). No locking is needed.
- **Row container recycling:** WPF DataGrid virtualises rows. The `LoadingRow` handler must always set both the "Total" style and the normal-row reset path to avoid stale bold/grey rendering on recycled containers.
