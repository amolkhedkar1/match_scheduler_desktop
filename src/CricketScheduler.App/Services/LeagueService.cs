using CricketScheduler.App.Models;
using System.IO;

namespace CricketScheduler.App.Services;

public sealed class LeagueService
{
    private readonly string _root;
    private readonly CsvService _csv;

    public LeagueService(string root, CsvService csv)
    {
        _root = root;
        _csv = csv;
    }

    public IReadOnlyList<string> GetLeagueNames()
    {
        Directory.CreateDirectory(_root);
        return Directory.GetDirectories(_root).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().OrderBy(x => x).ToList();
    }

    public void CreateLeague(string name)
    {
        Directory.CreateDirectory(GetLeaguePath(name));
    }

    public void DeleteLeague(string name)
    {
        var path = GetLeaguePath(name);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public async Task<League?> LoadLeagueAsync(string name)
    {
        var path = GetLeaguePath(name);
        if (!Directory.Exists(path))
        {
            return null;
        }

        var tournamentRows = await _csv.ReadAsync<TournamentCsv>(Path.Combine(path, "tournament.csv"));
        var tournamentRow = tournamentRows.FirstOrDefault();
        if (tournamentRow is null)
        {
            return null;
        }

        var tournament = new Tournament
        {
            Name = tournamentRow.TournamentName,
            StartDate = DateOnly.Parse(tournamentRow.StartDate),
            EndDate = DateOnly.Parse(tournamentRow.EndDate),
            DiscardedDates = tournamentRow.DiscardedDates.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(DateOnly.Parse).ToList(),
            Grounds = tournamentRow.Grounds.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(g => new Ground { Name = g }).ToList(),
            TimeSlots = tournamentRow.TimeSlots.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseSlot).ToList()
        };

        var divisions = await _csv.ReadAsync<DivisionCsv>(Path.Combine(path, "divisions.csv"));
        var grouped = divisions.GroupBy(d => d.DivisionName);
        var divisionModels = grouped.Select(group => new Division
        {
            Name = group.Key,
            IsRoundRobin = group.First().IsRoundRobin,
            MatchesPerTeam = group.First().MatchesPerTeam,
            Teams = group.Select(x => new Team { Name = x.TeamName, DivisionName = x.DivisionName }).ToList()
        }).ToList();

        var constraints = await _csv.ReadAsync<ConstraintCsv>(Path.Combine(path, "constraints.csv"));
        var requests = constraints.Select(c => new SchedulingRequest
        {
            TeamName = c.TeamName,
            Date = DateOnly.Parse(c.Date),
            StartTime = string.IsNullOrWhiteSpace(c.StartTime) ? null : TimeOnly.Parse(c.StartTime),
            EndTime = string.IsNullOrWhiteSpace(c.EndTime) ? null : TimeOnly.Parse(c.EndTime)
        }).ToList();

        var matches = await _csv.ReadAsync<ScheduleCsv>(Path.Combine(path, "schedule.csv"));
        var modelMatches = matches.Select(m => new Match
        {
            Sequence = m.Number,
            TournamentName = m.Series,
            DivisionName = m.Division,
            MatchType = m.MatchType,
            TeamOne = m.TeamOne,
            TeamTwo = m.TeamTwo,
            Date = DateOnly.TryParseExact(m.Date, "MM/dd/yyyy", out var d) ? d : null,
            Slot = TimeOnly.TryParse(m.Time, out var startTime) ? new TimeSlot { Start = startTime, End = startTime.AddHours(2) } : null,
            Ground = string.IsNullOrWhiteSpace(m.Ground) ? null : new Ground { Name = m.Ground },
            UmpireOne = m.UmpireOne,
            UmpireTwo = m.UmpireTwo,
            UmpireThree = m.UmpireThree,
            UmpireFour = m.UmpireFour,
            MatchManager = m.MatchManager,
            ScorerOne = m.Scorer1,
            ScorerTwo = m.Scorer2
        }).ToList();

        return new League
        {
            Name = name,
            Tournament = tournament,
            Divisions = divisionModels,
            Constraints = requests,
            Matches = modelMatches
        };
    }

    public async Task SaveLeagueAsync(League league)
    {
        var path = GetLeaguePath(league.Name);
        Directory.CreateDirectory(path);

        var tournament = new TournamentCsv
        {
            TournamentName = league.Tournament.Name,
            StartDate = league.Tournament.StartDate.ToString("yyyy-MM-dd"),
            EndDate = league.Tournament.EndDate.ToString("yyyy-MM-dd"),
            DiscardedDates = string.Join(';', league.Tournament.DiscardedDates.Select(d => d.ToString("yyyy-MM-dd"))),
            Grounds = string.Join(';', league.Tournament.Grounds.Select(g => g.Name)),
            TimeSlots = string.Join(';', league.Tournament.TimeSlots.Select(s => $"{s.Start:HH\\:mm}-{s.End:HH\\:mm}"))
        };
        await _csv.WriteAsync(Path.Combine(path, "tournament.csv"), [tournament]);

        var divisions = league.Divisions.SelectMany(d => d.Teams.Select(t => new DivisionCsv
        {
            DivisionName = d.Name,
            TeamName = t.Name,
            IsRoundRobin = d.IsRoundRobin,
            MatchesPerTeam = d.MatchesPerTeam
        }));
        await _csv.WriteAsync(Path.Combine(path, "divisions.csv"), divisions);

        var constraints = league.Constraints.Select(c => new ConstraintCsv
        {
            TeamName = c.TeamName,
            Date = c.Date.ToString("yyyy-MM-dd"),
            StartTime = c.StartTime?.ToString("HH:mm") ?? string.Empty,
            EndTime = c.EndTime?.ToString("HH:mm") ?? string.Empty
        });
        await _csv.WriteAsync(Path.Combine(path, "constraints.csv"), constraints);
    }

    private string GetLeaguePath(string name) => Path.Combine(_root, name);

    private static TimeSlot ParseSlot(string raw)
    {
        var parts = raw.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new TimeSlot
        {
            Start = TimeOnly.Parse(parts[0]),
            End = TimeOnly.Parse(parts[1])
        };
    }
}

public sealed class TournamentCsv
{
    public string TournamentName { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string DiscardedDates { get; set; } = string.Empty;
    public string Grounds { get; set; } = string.Empty;
    public string TimeSlots { get; set; } = string.Empty;
}

public sealed class DivisionCsv
{
    public string DivisionName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public bool IsRoundRobin { get; set; } = true;
    public int? MatchesPerTeam { get; set; }
}

public sealed class ConstraintCsv
{
    public string TeamName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

public sealed class ScheduleCsv
{
    public int Number { get; set; }
    public string Series { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public string MatchType { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string TeamOne { get; set; } = string.Empty;
    public string TeamTwo { get; set; } = string.Empty;
    public string Ground { get; set; } = string.Empty;
    public string UmpireOne { get; set; } = string.Empty;
    public string UmpireTwo { get; set; } = string.Empty;
    public string UmpireThree { get; set; } = string.Empty;
    public string UmpireFour { get; set; } = string.Empty;
    public string MatchManager { get; set; } = string.Empty;
    public string Scorer1 { get; set; } = string.Empty;
    public string Scorer2 { get; set; } = string.Empty;
}
