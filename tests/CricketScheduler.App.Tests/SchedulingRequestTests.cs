using CricketScheduler.App.Models;

namespace CricketScheduler.App.Tests;

public class SchedulingRequestTests
{
    [Fact]
    public void IsFullDayBlock_IsTrue_WhenTimesAreMissing()
    {
        var request = new SchedulingRequest
        {
            TeamName = "TeamA",
            Date = new DateOnly(2026, 5, 1),
            StartTime = null,
            EndTime = null
        };

        Assert.True(request.IsFullDayBlock);
    }

    [Fact]
    public void IsFullDayBlock_IsFalse_WhenBothTimesPresent()
    {
        var request = new SchedulingRequest
        {
            TeamName = "TeamA",
            Date = new DateOnly(2026, 5, 1),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0)
        };

        Assert.False(request.IsFullDayBlock);
    }
}
