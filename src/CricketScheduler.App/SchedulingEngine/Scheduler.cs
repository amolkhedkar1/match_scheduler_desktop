using CricketScheduler.App.Models;
using CricketScheduler.App.Services;

namespace CricketScheduler.App.SchedulingEngine;

public sealed class Scheduler
{
    private readonly SchedulingService _schedulingService = new();

    public SchedulingResult Run(League league) => _schedulingService.Generate(league);
}
