# Cricket Tournament Scheduler — Full Requirements & Implementation Reference

> **Last updated:** reflects all features through v5 including per-division scheduling mode, forbidden slots, move analysis, multi-select, search/filter/export on all screens.

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
│   ├── MainWindow.xaml / .cs          ← league toolbar + TabControl host
│   ├── InputDialog.cs                 ← modal text-input dialog
│   ├── InverseBoolConverter.cs        ← bool→!bool for RadioButton bindings
│   ├── Views/
│   │   ├── TournamentView.xaml/.cs
│   │   ├── DivisionView.xaml/.cs
│   │   ├── SchedulingRequestView.xaml/.cs
│   │   ├── SchedulerView.xaml/.cs
│   │   └── LeagueSelectionView.xaml/.cs
│   ├── ViewModels/
│   │   └── MainViewModel.cs           ← single unified ViewModel; stubs for others
│   ├── Models/
│   │   ├── League.cs
│   │   ├── Tournament.cs
│   │   ├── Division.cs                ← mutable IsRoundRobin, MatchesPerTeam, ModeSummary
│   │   ├── Team.cs
│   │   ├── Match.cs                   ← IsFixed, Date?, Slot?, Ground?
│   │   ├── Ground.cs
│   │   ├── TimeSlot.cs
│   │   ├── SchedulingRequest.cs       ← IsFullDayBlock computed property
│   │   └── ForbiddenSlot.cs           ← Date?, GroundName?, TimeSlot?, Division?
│   ├── Services/
│   │   ├── CsvService.cs
│   │   ├── LeagueService.cs           ← Load/Save all CSVs including schedule.csv
│   │   ├── SchedulingService.cs       ← Generate (2 overloads), SuggestMoves
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
│   └── schedule.csv
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
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,Umpire One,Umpire Two,Umpire Three,Umpire Four,Match Manager,Scorer 1,Scorer 2
1,2026 TAGKC T-10,DivisionA,League,04/19/2026,7:00 AM,Team1,Team2,OCG,,,,,,
```
- `Date` → `MM/dd/yyyy`
- `Time` → `h:mm tt`

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
    public string? UmpireOne ... UmpireTwo ... UmpireThree ... UmpireFour { get; set; }
    public string? MatchManager, ScorerOne, ScorerTwo { get; set; }
}
```

---

## 6. Application Tabs

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
- Save + Import CSV buttons

**Column 2 — Teams for selected division:**
- Header shows selected division name
- Multi-select `DataGrid` (Team Name, Division columns)
- Toolbar: Delete Selected (multi-row), Import Teams CSV
- Add team row with textbox + Add + Remove buttons

**Column 3 — Scheduling Mode Editor (right panel):**
- Shows selected division name + current `ModeSummary`
- **Radio buttons:** Round Robin | Fixed matches per team
- Matches per team textbox (enabled in fixed mode)
- **Apply to Division** button — calls `UpdateDivisionModeCommand`
  - Updates `division.IsRoundRobin` and `division.MatchesPerTeam` in place
  - Refreshes the list so `ModeSummary` updates immediately
- Match count reference table (quick guide for Round Robin vs Fixed)

**Match generation rules per division:**
- `IsRoundRobin=true` → all N×(N-1)/2 unique pairs
- `IsRoundRobin=false` → balanced pairing algorithm:
  - Each team gets exactly `MatchesPerTeam` opponents
  - Pairs drawn from a round-robin rotation wheel, trimmed by per-team quota
  - No team plays any opponent more than once
  - Teams at the quota are skipped in subsequent iterations

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

**Grid:** Team, Date, Start Time, End Time, Block Type
- Full day rows show "(full day)" in time columns
- `SelectionMode="Extended"` for multi-row operations

**Actions:** Import CSV, Save All

---

### 6.4 Scheduler Tab (`SchedulerView`)

**Toolbar row 1 — Schedule actions:**
- **Generate** — fresh schedule from scratch (all matches)
- **Reschedule** — re-runs engine preserving fixed matches and forbidden slots
- **Export All** — exports full schedule to `schedule.csv`
- **Export Filtered** — exports only currently visible rows
- **Import Matches** — overlay existing match data
- **Toggle Fixed** — marks/unmarks all selected rows as fixed (green row = fixed)
- **Copy** — copies selected rows to clipboard
- **Paste** — duplicates clipboard matches as new entries
- **Delete Selected** — removes multiple matches at once
- **Analyze Move** — opens Move Panel for selected match
- Schedule stats label (e.g. "23 of 28 matches shown")

**Filter / search bar:**
- Division `ComboBox`, Team `ComboBox`, Ground `ComboBox`
- Free-text Search box (matches team names, ground, division, date)
- Clear Filters button

**Forbidden Slots panel (Expander — collapsed by default):**
- 4 optional inputs: Date (`DatePicker`), Ground (`ComboBox`), Time Slot (`ComboBox`), Division (`ComboBox`)
- Add Forbidden Slot / Remove Selected
- List shows `Display` property of each slot
- Applied during Reschedule and Generate (overload with forbidden list)

**Schedule DataGrid (`SelectionMode="Extended"`):**
- Columns: #, Division, Date, Time, Team One, Team Two, Ground, Umpire 1, Umpire 2, Fixed
- Row styles:
  - 🔴 `#FFEEEE` — conflict detected (`HasConflict=true`)
  - 🟢 `#E8F5E9` — fixed match (`IsFixed=true`)

**Move Panel (visible when `ShowMovePanel=true`):**
- Shows match being moved + affected teams
- `MoveOptions` DataGrid: Date, Time, Ground, Affected Matches, Fairness Score, Recommendation
  - 🟢 Recommended rows = 0 collisions + high fairness score
- **Apply Selected Move** — commits move, saves, re-renders grid
- **Cancel** — hides panel

---

## 7. ViewModel (`MainViewModel.cs`)

Single `ObservableObject`-derived class handling all tabs. Views inherit `DataContext` from `MainWindow`.

### Key property groups

```csharp
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

// Scheduler
ObservableCollection<ScheduleRowViewModel> ScheduledMatches
ObservableCollection<ScheduleRowViewModel> FilteredScheduledMatches
ObservableCollection<ForbiddenSlot> ForbiddenSlots
ObservableCollection<MoveOptionViewModel> MoveOptions
string ScheduleSearchText, RequestSearchText, DivisionSearchText
bool ShowMovePanel
string MoveAnalysis
```

### Commands (all `[RelayCommand]`)

| Command | Description |
|---|---|
| `CreateLeague` | Prompts for name via `InputDialog`, creates league folder |
| `OpenLeague` | Loads all CSVs, populates all ViewModel collections |
| `DeleteLeague` | Deletes league folder after confirmation |
| `SaveTournament` | Persists tournament fields to `tournament.csv` |
| `AddDiscardedDate / Remove` | Manages blackout date list |
| `AddGround / Remove` | Manages ground list |
| `AddTimeSlot / Remove` | Manages time slot list |
| `AddDivision / Remove` | Adds/removes division |
| `UpdateDivisionMode` | Applies `EditDivisionIsRoundRobin` + `EditDivisionMatchesPerTeam` to selected division |
| `SaveDivisions` | Persists to `divisions.csv` |
| `ImportDivisions` | Bulk CSV import (DivisionName,TeamName) |
| `AddTeam / Remove` | Adds/removes team from selected division |
| `DeleteSelectedDivisionTeams` | Multi-row team delete (takes `DataGrid` parameter) |
| `AddRequest / Remove` | Single request add/remove |
| `AddCombinedSlotConstraint` | Multi-slot combined range block (takes `ListBox` parameter) |
| `SaveRequests` | Persists to `constraints.csv` |
| `ImportRequests` | Bulk CSV import |
| `ExportFilteredRequests` | Exports `FilteredRequests` to user-chosen CSV |
| `DeleteSelectedRequests` | Multi-row delete (takes `DataGrid` parameter) |
| `GenerateSchedule` | Fresh schedule; saves `schedule.csv` |
| `Reschedule` | Re-generates preserving fixed matches + forbidden slots |
| `ExportSchedule / ExportFilteredSchedule` | CSV export (all / filtered) |
| `ImportMatches` | Overlay existing matches |
| `ToggleFixedSelected` | Toggle IsFixed on all selected rows |
| `CopySelectedMatches / PasteMatches` | In-memory clipboard |
| `DeleteSelectedMatches` | Multi-row delete (takes `DataGrid` parameter) |
| `AddForbiddenSlot / Remove` | Manages forbidden slot list |
| `AnalyzeMove` | Computes alternative slots for selected match |
| `ApplyMove` | Commits selected move option |
| `CancelMove` | Hides move panel |
| `ClearFilters` | Resets all three filter dropdowns |

### WPF Binding rules
- `<Run Text="{Binding Prop, Mode=OneWay}"/>` — always explicit `Mode=OneWay` for computed properties inside `TextBlock`
- `<DataGridCheckBoxColumn Binding="{Binding IsFixed, Mode=OneWay}"/>` — `Mode=OneWay` for init-only or read-only row properties
- `DataGridTextColumn` — defaults to `OneWay`, safe for display-only row models
- `InverseBoolConverter.Instance` — used for "Fixed mode" radio button (`IsRoundRobin=false` → radio checked)

---

## 8. Scheduling Engine

### Match Generation (per division)

**Round Robin (`IsRoundRobin=true`):**
- Generates all N×(N-1)/2 unique pairs
- Every team plays every other team exactly once

**Fixed matches per team (`IsRoundRobin=false`):**
- Target: each team plays exactly `MatchesPerTeam` opponents
- Algorithm: draw from round-robin rotation wheel (interleaved from both ends for balance), reject pairs where either team is already at quota
- No duplicate pairs; stops when all teams reach target or all pairs exhausted

### Scheduling Steps
1. Generate match pairs per division (mode-aware)
2. Build weekend-only slot matrix (Sat + Sun, `StartDate`→`EndDate`, excluding discarded dates and forbidden slots)
3. For each match (ordered by constraint density — most constrained first):
   - Find best available slot via `ConstraintEvaluator.IsSlotAllowed` + `SlotScorer.Score`
   - Assign date, slot, ground
4. Assign umpires (from different division, continuity preference)
5. Sequence matches chronologically
6. If 100% not achievable → report unscheduled matches with reasons + offer constraint relaxation

### Core Scheduling Constraints
- Matches only on **weekends (Saturday + Sunday)**
- Each team: **max 1 match per weekend**
- Each team: **max 2 consecutive no-match weekends**
- Respect team availability constraints (full-day and partial-day blocks)
- Fixed matches are never moved (preserved as-is in Reschedule)
- Forbidden slots are excluded from the slot matrix

### Fairness Rules
- Even distribution across grounds
- Even distribution across time slots

### Constraint Relaxation System

Each unscheduled match pair appears in the **Unscheduled Matches** grid (below the schedule).
The user can independently tick **any combination** of the following 8 constraint flags per row
before clicking **Backtrack Reschedule**:

| Column | Constraint relaxed | Effect |
|---|---|---|
| **Ground Fairness** | `RelaxGroundFairness` | Allow assigning to an over-used ground |
| **Umpire Fairness** | `RelaxUmpireFairness` | Assign umpiring team regardless of duty load |
| **Time Slot Fairness** | `RelaxTimeSlotFairness` | Allow over-represented time slots |
| **≤2 Week Break** | `RelaxMaxGapRule` | Allow team to sit out >2 consecutive weekends |
| **1 Match/Weekend** | `RelaxOneMatchPerWeekend` | Allow >1 match per team per weekend |
| **Time Slot Restriction** | `RelaxTimeSlotRestriction` | Ignore partial-time unavailability blocks |
| **Date Restriction** | `RelaxDateRestriction` | Ignore full-day unavailability blocks |
| **Blackout Dates** | `RelaxDiscardedDates` | Allow scheduling on discarded/blackout dates |

**Hard constraint (never relaxed):** two matches cannot share the same ground + date + time slot.

**Toolbar shortcuts:**
- **Relax All Selected** — ticks all 8 flags for currently selected rows
- **Clear Relaxations** — unticks all flags for selected rows
- **Backtrack Reschedule** — retries all unscheduled matches using their individual relaxation settings

---

## 9. Forbidden Slots

- Optional filters on the scheduling matrix
- Any combination of: Date, Ground, TimeSlot, Division (all nullable)
- Applied in both `Generate` overloads
- Stored in-memory in `ForbiddenSlots` collection (not persisted to CSV currently)
- Each slot shows a `Display` string: `Date | Ground | TimeSlot | Division`

---

## 10. Move Analysis (`SuggestMoves`)

When user clicks **Analyze Move** on a selected match:
1. `SchedulingService.SuggestMoves(league, match, forbiddenSlots)` is called
2. All valid alternative slots are evaluated:
   - Slot must pass `ConstraintEvaluator.IsSlotAllowed`
   - Slot must not be in the forbidden list
   - Slot must differ from current assignment
3. For each valid slot: count displaced matches + compute fairness score
4. Results sorted: fewest affected matches first, then highest fairness score
5. Slots with 0 affected and fairness > 80 marked `IsRecommended=true` (shown in green)
6. User selects a slot and clicks **Apply Selected Move** to commit

---

## 11. Umpiring

- Every scheduled match gets umpires
- Umpires must come from a **different division** than the match
- Even distribution of umpiring duties across all teams
- **Continuity preference:** prefer assigning umpiring to a team whose adjacent match (previous or next chronologically) is on the same day — reduces waiting time

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

### Schedule CSV strict format
```
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,
Umpire One,Umpire Two,Umpire Three,Umpire Four,Match Manager,Scorer 1,Scorer 2
```
- `Date` = `MM/dd/yyyy`
- `Time` = `h:mm tt`

---

## 15. Multi-League

- Each league stored in `data/leagues/{leagueName}/`
- Operations: Create (name via dialog), Open, Delete
- League toolbar always visible at top of `MainWindow`
- On Open: all CSVs loaded, all ViewModel collections populated
- On Save (any tab): full league written back to all CSV files including `schedule.csv`
