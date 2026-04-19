# Cricket Tournament Scheduler - Complete Cursor Instruction File

## 1. Objective

Build a **C# desktop application (WPF, MVVM)** to create, manage, and manipulate cricket tournament schedules with strong constraint handling, CSV-based persistence, and interactive scheduling.

## 2. Technology Stack

- Language: **C#**
- Framework: **.NET 8+**
- UI: **WPF (MVVM pattern)**
- Storage: **CSV (primary)** with file-based league separation
- Libraries:
  - `CsvHelper`
  - `CommunityToolkit.Mvvm`

## 3. Core Concept

### Multi-League Support

- Support **multiple independent leagues**
- Each league:
  - Has its own tournament, divisions, teams, constraints, and schedule
  - Stored independently
  - Can be loaded later for editing

### Features

- Create League
- Open League
- Delete League

## 4. Application Screens

### 4.1 Tournament Creation Screen

**Inputs:**

- Tournament Name
- Start Date
- End Date
- Discarded Dates (blackout dates)
- Available Grounds
- Match Time Slots:
  - Start Time
  - End Time

**Features:**

- Add/edit/delete entries
- Auto-generate time slots
- CSV save/load
- Bulk upload

### 4.2 Division / Team Screen

**Inputs:**

- Division Name
- Teams
- Matches per team OR Round Robin

**CSV Format:**

```csv
DivisionName,TeamName
DivisionA,Team1
DivisionA,Team2
DivisionB,Team3
```

**Rules:**

- Multiple divisions
- All teams accommodated
- Matches only within division

### 4.3 Scheduling Requests Screen (UPDATED)

**Purpose:**  
Capture team unavailability

**Core Rule (IMPORTANT):**

- **Default behavior = FULL DAY NOT AVAILABLE**
- If only date is provided -> team unavailable entire day
- If time slot is provided -> team unavailable only for that time range

**Inputs:**

- Team Name
- Date (mandatory)
- Start Time (optional)
- End Time (optional)

**CSV Format:**

```csv
TeamName,Date,StartTime,EndTime
Team1,2026-05-01,,
Team2,2026-05-02,09:00,12:00
```

**Interpretation Rules:**

- `StartTime + EndTime empty` -> block full day
- `StartTime + EndTime present` -> block only that slot
- Mixed entries allowed

**Features:**

- Bulk upload
- Add/remove entries
- Save per league
- Validate overlapping constraints

### 4.4 Scheduling / Manipulation Screen

**Inputs:**

- Upload existing matches
- Mark matches as fixed
- Define avoid slots

**Features:**

- Generate schedule
- Grid view (Date x Ground x Time)
- Filters:
  - Division
  - Team
  - Ground
- Drag & drop movement
- Suggested moves
- Conflict highlighting

## 5. Data Persistence

```text
/data/leagues/{leagueName}/
```

**Files:**

- tournament.csv
- divisions.csv
- constraints.csv
- schedule.csv

## 6. Scheduling Rules

### 6.1 Core Constraints

- Matches only on **weekends (Saturday & Sunday)**
- Each team plays **max 1 match per weekend**
- **Max 2 consecutive no-match weekends per team**
- Respect:
  - Discarded dates
  - Scheduling requests (full-day or partial)
  - Fixed matches
  - Avoid slots

### 6.2 Fairness Rules

- Even distribution across:
  - Grounds
  - Time slots

### 6.3 Match Pairing

#### Round Robin

- Generate all pairs

#### Custom Logic

- Sort teams alphabetically
- Apply skip logic

##### Example:

- 10 teams, 8 matches:
- Team1 skips Team10
- Team2 skips Team9
- Team3 skips Team8
- Team4 skips Team7
- Team5 skips Team6

## 7. Scheduling Engine

### Steps

1. Generate match pairs
2. Build scheduling matrix (weekend only)
3. Compute constraint weights
4. Sort matches (highest constraint first)
5. Assign best slot

## 8. Scheduling Completion Requirement

### Goal

**Schedule 100% of matches**

### If Not Possible

- Identify unscheduled matches
- Show reasons:
  - Constraint conflicts
  - Full-day vs partial-day blocks
  - Slot limitations

### Ask User

**"Which constraint should be relaxed?"**

Options:

- Team availability
- One match per weekend
- Max 2 consecutive no-match weekends per team
- Ground fairness
- Time slot fairness

## 9. Manual Adjustment

### Move Match

- Drag & drop OR manual selection

### System Should

- Show valid alternative slots
- Highlight:
  - Best options
  - Conflicts

### Suggestion Engine

- Provide:
  - Direct moves
  - Swap options
- Show:
  - Fairness impact
  - Constraint violations

## 10. Umpiring

- Every match must have umpire(s)
- Umpire must be from **different division**
- Even distribution across teams
- Prefer assigning umpiring duty to a team's **next match or previous match** whenever possible to reduce waiting time and improve flow

## 11. Data Models

```csharp
League
Tournament
Division
Team
Match
Ground
TimeSlot
Constraint
```

## 12. Schedule Export Requirement (CSV)

### Output Format (STRICT)

```text
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,
Umpire One,Umpire Two,Umpire Three,Umpire Four,
Match Manager,Scorer 1,Scorer 2
```

### Sample Output

```csv
#,Series,Division,Match Type,Date,Time,Team One,Team Two,Ground,Umpire One,Umpire Two,Umpire Three,Umpire Four,Match Manager,Scorer 1,Scorer 2
1,2026 TAGKC T-10 SAVAALU,,League,04/19/2026,7:00 AM,Bezawada Bullets,KarimNagar Khatarnaks,OCG,,,,,,
```

### Rules

- `#` = Auto increment
- `Series` = Tournament Name
- `Date format` = MM/dd/yyyy
- `Time format` = h:mm tt
- Empty values allowed

## 13. Project Scaffold

```text
CricketScheduler/
|
|-- CricketScheduler.sln
|
|-- src/
|   |-- CricketScheduler.App/
|   |
|   |   |-- App.xaml
|   |   |-- MainWindow.xaml
|   |
|   |   |-- Views/
|   |   |   |-- LeagueSelectionView.xaml
|   |   |   |-- TournamentView.xaml
|   |   |   |-- DivisionView.xaml
|   |   |   |-- SchedulingRequestView.xaml
|   |   |   |-- SchedulerView.xaml
|   |
|   |   |-- ViewModels/
|   |   |   |-- MainViewModel.cs
|   |   |   |-- LeagueViewModel.cs
|   |   |   |-- TournamentViewModel.cs
|   |   |   |-- DivisionViewModel.cs
|   |   |   |-- SchedulingRequestViewModel.cs
|   |   |   |-- SchedulerViewModel.cs
|   |
|   |   |-- Models/
|   |   |   |-- League.cs
|   |   |   |-- Tournament.cs
|   |   |   |-- Division.cs
|   |   |   |-- Team.cs
|   |   |   |-- Match.cs
|   |   |   |-- Ground.cs
|   |   |   |-- TimeSlot.cs
|   |   |   |-- Constraint.cs
|   |
|   |   |-- Services/
|   |   |   |-- CsvService.cs
|   |   |   |-- LeagueService.cs
|   |   |   |-- SchedulingService.cs
|   |   |   |-- ConstraintService.cs
|   |   |   |-- FairnessService.cs
|   |   |   |-- SuggestionService.cs
|   |   |   |-- ExportService.cs
|   |
|   |   |-- SchedulingEngine/
|   |   |   |-- MatchGenerator.cs
|   |   |   |-- SchedulingMatrixBuilder.cs
|   |   |   |-- ConstraintEvaluator.cs
|   |   |   |-- SlotScorer.cs
|   |   |   |-- Scheduler.cs
|   |   |   |-- ConflictResolver.cs
|
|-- data/
|   |-- leagues/
|
|-- docs/
|   |-- cursor_instructions.md
```

## 14. Export Service (Pseudo)

```csharp
ExportSchedule(matches):
    write header

    for each match:
        write row with formatted date/time
```

## 15. Key Focus Areas

- Full-day vs partial-day constraints (CRITICAL)
- 100% scheduling goal
- Constraint relaxation workflow
- Manual adjustments
- Suggestion engine
- Max 2 consecutive no-match weekends per team
- Umpiring continuity (next/previous match preference)
- Multi-league design
- CSV compatibility
