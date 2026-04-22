using CommunityToolkit.Mvvm.ComponentModel;
using CricketScheduler.App.Models;
using CricketScheduler.App.Services;
using System.Data;
using System.IO;
using System.Text;

namespace CricketScheduler.App.ViewModels;

public sealed class DivisionStatTable
{
    public string    DivisionName { get; init; } = string.Empty;
    public DataTable Table        { get; init; } = new();
}

public partial class StatisticsViewModel : ObservableObject
{
    [ObservableProperty] private List<DivisionStatTable> matchesByDivision  = [];
    [ObservableProperty] private List<DivisionStatTable> umpiringByDivision = [];
    [ObservableProperty] private List<DivisionStatTable> groundByDivision   = [];

    public void RefreshStatistics(IEnumerable<Match> matches)
    {
        var matchList = matches.Where(m => m.Date.HasValue).ToList();
        var divisions = matchList.Select(m => m.DivisionName).Distinct().OrderBy(d => d).ToList();

        var matchesTables  = new List<DivisionStatTable>();
        var umpiringTables = new List<DivisionStatTable>();
        var groundTables   = new List<DivisionStatTable>();

        foreach (var div in divisions)
        {
            var divMatches = matchList.Where(m => m.DivisionName == div).ToList();
            var divTeams   = divMatches
                .SelectMany(m => new[] { m.TeamOne, m.TeamTwo })
                .Distinct().OrderBy(t => t).ToList();

            matchesTables.Add(new DivisionStatTable
                { DivisionName = div, Table = BuildMatchesTable(divMatches, divTeams) });
            umpiringTables.Add(new DivisionStatTable
                { DivisionName = div, Table = BuildUmpiringTable(matchList, divTeams) });
            groundTables.Add(new DivisionStatTable
                { DivisionName = div, Table = BuildGroundTable(divMatches, divTeams) });
        }

        MatchesByDivision  = matchesTables;
        UmpiringByDivision = umpiringTables;
        GroundByDivision   = groundTables;
    }

    public void Clear()
    {
        MatchesByDivision  = [];
        UmpiringByDivision = [];
        GroundByDivision   = [];
    }

    // ── CSV export ────────────────────────────────────────────────────────────

    public static string ExportTableToCsv(DataTable table, string prefix, string divisionName)
    {
        var safe   = string.Concat(divisionName.Split(Path.GetInvalidFileNameChars()));
        var path   = AppPaths.TimestampedExportPath($"stats_{prefix}_{safe}");
        var sb     = new StringBuilder();
        sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>()
            .Select(c => CsvQuote(c.ColumnName))));
        foreach (DataRow row in table.Rows)
            sb.AppendLine(string.Join(",", row.ItemArray
                .Select(v => CsvQuote(v?.ToString() ?? string.Empty))));
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string CsvQuote(string v) =>
        v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    // ── Table builders ────────────────────────────────────────────────────────

    // Matches: "1" = played, "·" = not played; Total column = match count per team.
    private static DataTable BuildMatchesTable(List<Match> matches, List<string> teams)
    {
        var dates = matches.Select(m => m.Date!.Value).Distinct().OrderBy(d => d).ToList();
        var t = NewTable(dates.Select(d => d.ToString("MM/dd")).ToList());

        foreach (var team in teams)
        {
            var row = t.NewRow();
            row["Team"] = team;
            int total = 0;
            foreach (var date in dates)
            {
                bool played = matches.Any(m => m.Date == date && (m.TeamOne == team || m.TeamTwo == team));
                row[date.ToString("MM/dd")] = played ? "1" : "·";
                if (played) total++;
            }
            row["Total"] = total.ToString();
            t.Rows.Add(row);
        }

        var totRow = t.NewRow();
        totRow["Team"] = "Total";
        int grand = 0;
        foreach (var date in dates)
        {
            int cnt = matches.Count(m => m.Date == date);
            totRow[date.ToString("MM/dd")] = cnt.ToString();
            grand += cnt;
        }
        totRow["Total"] = grand.ToString();
        t.Rows.Add(totRow);
        return t;
    }

    // Umpiring: "1" = has umpiring duty that date, "·" = none.
    private static DataTable BuildUmpiringTable(List<Match> matches, List<string> teams)
    {
        var dates = matches.Select(m => m.Date!.Value).Distinct().OrderBy(d => d).ToList();
        var t = NewTable(dates.Select(d => d.ToString("MM/dd")).ToList());

        foreach (var team in teams)
        {
            var row = t.NewRow();
            row["Team"] = team;
            int total = 0;
            foreach (var date in dates)
            {
                bool umpires = matches.Any(m => m.Date == date &&
                    (m.UmpireOne == team || m.UmpireTwo == team ||
                     m.UmpireThree == team || m.UmpireFour == team));
                row[date.ToString("MM/dd")] = umpires ? "1" : "·";
                if (umpires) total++;
            }
            row["Total"] = total.ToString();
            t.Rows.Add(row);
        }

        var totRow = t.NewRow();
        totRow["Team"] = "Total";
        int grand = 0;
        foreach (var date in dates)
        {
            int cnt = teams.Count(team => matches.Any(m => m.Date == date &&
                (m.UmpireOne == team || m.UmpireTwo == team ||
                 m.UmpireThree == team || m.UmpireFour == team)));
            totRow[date.ToString("MM/dd")] = cnt.ToString();
            grand += cnt;
        }
        totRow["Total"] = grand.ToString();
        t.Rows.Add(totRow);
        return t;
    }

    // Ground: numeric count of matches at each ground per team.
    private static DataTable BuildGroundTable(List<Match> matches, List<string> teams)
    {
        var grounds = matches.Where(m => m.Ground != null)
            .Select(m => m.Ground!.Name).Distinct().OrderBy(g => g).ToList();
        var t = NewTable(grounds);

        foreach (var team in teams)
        {
            var row = t.NewRow();
            row["Team"] = team;
            int total = 0;
            foreach (var ground in grounds)
            {
                int cnt = matches.Count(m => m.Ground?.Name == ground &&
                    (m.TeamOne == team || m.TeamTwo == team));
                row[ground] = cnt.ToString();
                total += cnt;
            }
            row["Total"] = total.ToString();
            t.Rows.Add(row);
        }

        var totRow = t.NewRow();
        totRow["Team"] = "Total";
        int grand = 0;
        foreach (var ground in grounds)
        {
            int cnt = matches.Count(m => m.Ground?.Name == ground);
            totRow[ground] = cnt.ToString();
            grand += cnt;
        }
        totRow["Total"] = grand.ToString();
        t.Rows.Add(totRow);
        return t;
    }

    private static DataTable NewTable(List<string> midColumns)
    {
        var t = new DataTable();
        t.Columns.Add("Team", typeof(string));
        foreach (var col in midColumns)
            t.Columns.Add(col, typeof(string));
        t.Columns.Add("Total", typeof(string));
        return t;
    }
}
