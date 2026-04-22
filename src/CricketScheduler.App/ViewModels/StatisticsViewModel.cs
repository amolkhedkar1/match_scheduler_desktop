using CommunityToolkit.Mvvm.ComponentModel;
using CricketScheduler.App.Models;
using System.Data;

namespace CricketScheduler.App.ViewModels;

public partial class StatisticsViewModel : ObservableObject
{
    [ObservableProperty] private DataTable? matchesScheduledTable;
    [ObservableProperty] private DataTable? umpiringScheduleTable;
    [ObservableProperty] private DataTable? groundAssignmentTable;

    public void RefreshStatistics(IEnumerable<Match> matches)
    {
        var matchList = matches.Where(m => m.Date.HasValue).ToList();

        var allTeams = matchList
            .SelectMany(m => new[] { m.TeamOne, m.TeamTwo })
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        MatchesScheduledTable = BuildMatchesScheduledTable(matchList, allTeams);
        UmpiringScheduleTable = BuildUmpiringScheduleTable(matchList, allTeams);
        GroundAssignmentTable = BuildGroundAssignmentTable(matchList, allTeams);
    }

    public void Clear()
    {
        MatchesScheduledTable = null;
        UmpiringScheduleTable = null;
        GroundAssignmentTable = null;
    }

    private static DataTable BuildMatchesScheduledTable(List<Match> matches, List<string> allTeams)
    {
        var dates = matches.Select(m => m.Date!.Value).Distinct().OrderBy(d => d).ToList();

        var table = new DataTable();
        table.Columns.Add("Team", typeof(string));
        foreach (var date in dates)
            table.Columns.Add(date.ToString("MM/dd"), typeof(int));
        table.Columns.Add("Total", typeof(int));

        foreach (var team in allTeams)
        {
            var row = table.NewRow();
            row["Team"] = team;
            int teamTotal = 0;
            foreach (var date in dates)
            {
                int val = matches.Any(m => m.Date == date && (m.TeamOne == team || m.TeamTwo == team)) ? 1 : 0;
                row[date.ToString("MM/dd")] = val;
                teamTotal += val;
            }
            row["Total"] = teamTotal;
            table.Rows.Add(row);
        }

        // Total row: count of matches per date
        var totalRow = table.NewRow();
        totalRow["Team"] = "Total";
        int grandTotal = 0;
        foreach (var date in dates)
        {
            int dateTotal = matches.Count(m => m.Date == date);
            totalRow[date.ToString("MM/dd")] = dateTotal;
            grandTotal += dateTotal;
        }
        totalRow["Total"] = grandTotal;
        table.Rows.Add(totalRow);

        return table;
    }

    private static DataTable BuildUmpiringScheduleTable(List<Match> matches, List<string> allTeams)
    {
        var dates = matches.Select(m => m.Date!.Value).Distinct().OrderBy(d => d).ToList();

        var table = new DataTable();
        table.Columns.Add("Team", typeof(string));
        foreach (var date in dates)
            table.Columns.Add(date.ToString("MM/dd"), typeof(int));
        table.Columns.Add("Total", typeof(int));

        foreach (var team in allTeams)
        {
            var row = table.NewRow();
            row["Team"] = team;
            int teamTotal = 0;
            foreach (var date in dates)
            {
                int val = matches.Any(m => m.Date == date &&
                    (m.UmpireOne == team || m.UmpireTwo == team ||
                     m.UmpireThree == team || m.UmpireFour == team)) ? 1 : 0;
                row[date.ToString("MM/dd")] = val;
                teamTotal += val;
            }
            row["Total"] = teamTotal;
            table.Rows.Add(row);
        }

        // Total row: count of teams umpiring per date
        var totalRow = table.NewRow();
        totalRow["Team"] = "Total";
        int grandTotal = 0;
        foreach (var date in dates)
        {
            int dateTotal = allTeams.Count(team =>
                matches.Any(m => m.Date == date &&
                    (m.UmpireOne == team || m.UmpireTwo == team ||
                     m.UmpireThree == team || m.UmpireFour == team)));
            totalRow[date.ToString("MM/dd")] = dateTotal;
            grandTotal += dateTotal;
        }
        totalRow["Total"] = grandTotal;
        table.Rows.Add(totalRow);

        return table;
    }

    private static DataTable BuildGroundAssignmentTable(List<Match> matches, List<string> allTeams)
    {
        var grounds = matches.Where(m => m.Ground != null)
            .Select(m => m.Ground!.Name)
            .Distinct()
            .OrderBy(g => g)
            .ToList();

        var table = new DataTable();
        table.Columns.Add("Team", typeof(string));
        foreach (var ground in grounds)
            table.Columns.Add(ground, typeof(int));
        table.Columns.Add("Total", typeof(int));

        foreach (var team in allTeams)
        {
            var row = table.NewRow();
            row["Team"] = team;
            int teamTotal = 0;
            foreach (var ground in grounds)
            {
                int count = matches.Count(m => m.Ground?.Name == ground &&
                    (m.TeamOne == team || m.TeamTwo == team));
                row[ground] = count;
                teamTotal += count;
            }
            row["Total"] = teamTotal;
            table.Rows.Add(row);
        }

        // Total row: count of matches per ground
        var totalRow = table.NewRow();
        totalRow["Team"] = "Total";
        int grandTotal = 0;
        foreach (var ground in grounds)
        {
            int groundTotal = matches.Count(m => m.Ground?.Name == ground);
            totalRow[ground] = groundTotal;
            grandTotal += groundTotal;
        }
        totalRow["Total"] = grandTotal;
        table.Rows.Add(totalRow);

        return table;
    }
}
