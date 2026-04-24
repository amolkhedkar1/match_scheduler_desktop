using ClosedXML.Excel;
using CricketScheduler.App.Models;
using CricketScheduler.App.ViewModels;
using System.Data;

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
            UmpireTwo = m.UmpireOne ?? string.Empty,
            UmpireThree = m.UmpireThree ?? string.Empty,
            UmpireFour = m.UmpireFour ?? string.Empty,
            MatchManager = m.MatchManager ?? string.Empty,
            Scorer1 = m.ScorerOne ?? string.Empty,
            Scorer2 = m.ScorerTwo ?? string.Empty,
            IsFixed = m.IsFixed
        }).ToList();

        return _csvService.WriteAsync(outputPath, rows);
    }

    // Exports scheduled matches, unscheduled matches, and all four statistics pivot tables
    // into a single .xlsx workbook — one sheet per data category.
    public static void ExportAllStatisticsToExcel(
        StatisticsViewModel    vm,
        IEnumerable<Match>     scheduledMatches,
        IEnumerable<Match>     unscheduledMatches,
        string                 path)
    {
        using var workbook = new XLWorkbook();
        AddScheduleSheet(workbook,    scheduledMatches);
        AddUnscheduledSheet(workbook, unscheduledMatches);
        AddStatSheet(workbook, "Matches Scheduled",   vm.MatchesByDivision);
        AddStatSheet(workbook, "Umpiring Schedule",   vm.UmpiringByDivision);
        AddStatSheet(workbook, "Ground Assignment",   vm.GroundByDivision);
        AddStatSheet(workbook, "Weekly Ground Usage", vm.MatchesPerWeekPerGround);
        workbook.SaveAs(path);
    }

    // ── Schedule sheet ────────────────────────────────────────────────────────

    private static readonly string[] ScheduleHeaders =
    [
        "#", "Division", "Match Type", "Date", "Time",
        "Team One", "Team Two", "Ground",
        "Umpire 1", "Umpire 2", "Umpire 3", "Umpire 4",
        "Match Manager", "Scorer 1", "Scorer 2", "Fixed"
    ];

    private static void AddScheduleSheet(XLWorkbook workbook, IEnumerable<Match> matches)
    {
        var ws = workbook.Worksheets.Add("Schedule");
        WriteHeaderRow(ws, 1, ScheduleHeaders, XLColor.FromHtml("#C6EFCE"));

        int row = 2;
        foreach (var m in matches.OrderBy(m => m.Date).ThenBy(m => m.Slot?.Start).ThenBy(m => m.Sequence))
        {
            ws.Cell(row, 1).Value  = m.Sequence;
            ws.Cell(row, 2).Value  = m.DivisionName;
            ws.Cell(row, 3).Value  = m.MatchType;
            ws.Cell(row, 4).Value  = m.Date?.ToString("MM/dd/yyyy") ?? string.Empty;
            ws.Cell(row, 5).Value  = m.Slot?.Start.ToString("h:mm tt") ?? string.Empty;
            ws.Cell(row, 6).Value  = m.TeamOne;
            ws.Cell(row, 7).Value  = m.TeamTwo;
            ws.Cell(row, 8).Value  = m.Ground?.Name ?? string.Empty;
            ws.Cell(row, 9).Value  = m.UmpireOne ?? string.Empty;
            ws.Cell(row, 10).Value = m.UmpireTwo ?? string.Empty;
            ws.Cell(row, 11).Value = m.UmpireThree ?? string.Empty;
            ws.Cell(row, 12).Value = m.UmpireFour ?? string.Empty;
            ws.Cell(row, 13).Value = m.MatchManager ?? string.Empty;
            ws.Cell(row, 14).Value = m.ScorerOne ?? string.Empty;
            ws.Cell(row, 15).Value = m.ScorerTwo ?? string.Empty;
            ws.Cell(row, 16).Value = m.IsFixed ? "Yes" : "No";

            if (m.IsFixed)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");

            row++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 5;  // #
    }

    // ── Unscheduled sheet ─────────────────────────────────────────────────────

    private static readonly string[] UnscheduledHeaders =
        ["Division", "Match Type", "Team One", "Team Two", "Reason"];

    private static void AddUnscheduledSheet(XLWorkbook workbook, IEnumerable<Match> matches)
    {
        var ws = workbook.Worksheets.Add("Unscheduled");
        WriteHeaderRow(ws, 1, UnscheduledHeaders, XLColor.FromHtml("#FFEB9C"));

        int row = 2;
        foreach (var m in matches.OrderBy(m => m.DivisionName).ThenBy(m => m.TeamOne))
        {
            ws.Cell(row, 1).Value = m.DivisionName;
            ws.Cell(row, 2).Value = m.MatchType;
            ws.Cell(row, 3).Value = m.TeamOne;
            ws.Cell(row, 4).Value = m.TeamTwo;
            ws.Cell(row, 5).Value = m.UnscheduledReason ?? string.Empty;
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    // ── Stat pivot sheet ──────────────────────────────────────────────────────

    private static void AddStatSheet(XLWorkbook workbook, string sheetName, List<DivisionStatTable> tables)
    {
        var ws  = workbook.Worksheets.Add(sheetName);
        int row = 1;

        foreach (var div in tables)
        {
            int colCount = div.Table.Columns.Count;

            // Division header row
            var divCell = ws.Cell(row, 1);
            divCell.Value = div.DivisionName;
            divCell.Style.Font.Bold            = true;
            divCell.Style.Font.FontSize        = 12;
            divCell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            if (colCount > 1)
                ws.Range(row, 1, row, colCount).Merge();
            row++;

            // Column header row
            for (int c = 0; c < colCount; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = div.Table.Columns[c].ColumnName;
                cell.Style.Font.Bold            = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            // Data rows
            foreach (DataRow dataRow in div.Table.Rows)
            {
                bool isTotalRow = dataRow[0]?.ToString() == "Total";
                for (int c = 0; c < colCount; c++)
                {
                    var cell = ws.Cell(row, c + 1);
                    cell.Value = dataRow[c]?.ToString() ?? string.Empty;
                    if (isTotalRow)
                    {
                        cell.Style.Font.Bold            = true;
                        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    }
                    if (c > 0)
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                row++;
            }

            row += 5; // 5-row blank separator between divisions
        }

        ws.Columns().AdjustToContents();
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static void WriteHeaderRow(IXLWorksheet ws, int row, string[] headers, XLColor background)
    {
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold            = true;
            cell.Style.Fill.BackgroundColor = background;
        }
    }
}
