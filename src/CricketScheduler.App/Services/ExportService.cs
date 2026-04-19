using CricketScheduler.App.Models;

namespace CricketScheduler.App.Services;

public sealed class ExportService
{
    private readonly CsvService _csvService;

    public ExportService(CsvService csvService)
    {
        _csvService = csvService;
    }

    public Task ExportScheduleAsync(IEnumerable<Match> matches, string outputPath)
    {
        var rows = matches.Select(m => new ScheduleCsv
        {
            Number = m.Sequence,
            Series = m.TournamentName,
            Division = m.DivisionName,
            MatchType = m.MatchType,
            Date = m.Date?.ToString("MM/dd/yyyy") ?? string.Empty,
            Time = m.Slot?.Start.ToString("h:mm tt") ?? string.Empty,
            TeamOne = m.TeamOne,
            TeamTwo = m.TeamTwo,
            Ground = m.Ground?.Name ?? string.Empty,
            UmpireOne = m.UmpireOne ?? string.Empty,
            UmpireTwo = m.UmpireTwo ?? string.Empty,
            UmpireThree = m.UmpireThree ?? string.Empty,
            UmpireFour = m.UmpireFour ?? string.Empty,
            MatchManager = m.MatchManager ?? string.Empty,
            Scorer1 = m.ScorerOne ?? string.Empty,
            Scorer2 = m.ScorerTwo ?? string.Empty
        }).ToList();

        return _csvService.WriteAsync(outputPath, rows);
    }
}
