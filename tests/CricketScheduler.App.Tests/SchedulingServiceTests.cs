using CricketScheduler.App.Models;
using CricketScheduler.App.Services;

namespace CricketScheduler.App.Tests;

public class SchedulingServiceTests
{
    [Fact]
    public void Generate_LeavesMatchUnscheduled_WhenTeamHasFullDayBlock()
    {
        var service = new SchedulingService();
        var league = BuildLeague(
            constraints:
            [
                new SchedulingRequest
                {
                    TeamName = "A",
                    Date = new DateOnly(2026, 5, 2),
                    StartTime = null,
                    EndTime = null
                }
            ]);

        var result = service.Generate(league);

        Assert.Empty(result.ScheduledMatches);
        Assert.Single(result.UnscheduledMatches);
    }

    [Fact]
    public void Generate_SchedulesMatch_WhenConstraintIsPartialAndNonOverlapping()
    {
        var service = new SchedulingService();
        var league = BuildLeague(
            constraints:
            [
                new SchedulingRequest
                {
                    TeamName = "A",
                    Date = new DateOnly(2026, 5, 2),
                    StartTime = new TimeOnly(10, 0),
                    EndTime = new TimeOnly(11, 0)
                }
            ]);

        var result = service.Generate(league);

        Assert.Single(result.ScheduledMatches);
        Assert.Empty(result.UnscheduledMatches);
        Assert.Equal(new DateOnly(2026, 5, 2), result.ScheduledMatches[0].Date);
    }

    private static League BuildLeague(List<SchedulingRequest> constraints)
    {
        var tournament = new Tournament
        {
            Name = "Series",
            StartDate = new DateOnly(2026, 5, 2), // Saturday
            EndDate = new DateOnly(2026, 5, 2),
            Grounds = [new Ground { Name = "G1" }],
            TimeSlots = [new TimeSlot { Start = new TimeOnly(7, 0), End = new TimeOnly(9, 0) }],
            DiscardedDates = []
        };

        var division = new Division
        {
            Name = "DivisionA",
            IsRoundRobin = true,
            Teams =
            [
                new Team { Name = "A", DivisionName = "DivisionA" },
                new Team { Name = "B", DivisionName = "DivisionA" }
            ]
        };

        return new League
        {
            Name = "L1",
            Tournament = tournament,
            Divisions = [division],
            Constraints = constraints,
            Matches = []
        };
    }
}
