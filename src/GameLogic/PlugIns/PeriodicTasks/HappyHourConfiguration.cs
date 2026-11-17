// <copyright file="HappyHourConfiguration.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.PeriodicTasks;

using System;

/// <summary>
/// The Happy Hour event configuration.
/// </summary>
public class HappyHourConfiguration : PeriodicTaskConfiguration
{
    /// <summary>
    /// Gets the default configuration.
    /// </summary>
    /// <remarks>
    /// VORTHANIA: the calendar is content (see docs/03). The hours below target the LATAM prime time
    /// of 20-23h: one hour on weeknights, at an hour that does not punish someone who works the next
    /// morning, and two hours later on the weekend, when people can stay up.
    ///
    /// The times are read in the **server time zone**, which has to be set in the admin panel under
    /// Game configuration -> System -> Server time zone. For Argentina that is
    /// "America/Argentina/Buenos_Aires". Leave it empty and everything below silently runs in UTC.
    ///
    /// <see cref="PeriodicTaskConfiguration.TaskDuration"/> is set to the longest weekly duration on
    /// purpose: it is no longer the run length (the weekly entry decides that), only the guard that
    /// stops a new run from starting while the previous one is still going.
    /// </remarks>
    public static HappyHourConfiguration Default => new()
    {
        TaskDuration = TimeSpan.FromHours(2),
        PreStartMessageDelay = TimeSpan.FromSeconds(0),
        StartMessage = "Happy Hour event has been started!",
        EndMessage = "Happy Hour event has ended!",
        Timetable = new List<TimeOnly>(),
        WeeklySchedule = CreateVorthaniaSchedule(),
        ExperienceMultiplier = 1.5f,
    };

    /// <summary>
    /// Gets or sets the experience multiplier.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.HappyHourConfiguration_ExperienceMultiplier_Name), Order = 100)]
    public float ExperienceMultiplier { get; set; } = 1.5f;

    /// <summary>
    /// VORTHANIA: Creates the launch schedule - weeknights 21:00-22:00, weekends 22:00-00:00.
    /// </summary>
    /// <returns>The weekly schedule.</returns>
    private static ICollection<WeeklyScheduleEntry> CreateVorthaniaSchedule()
    {
        var weekNights = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
        };

        var schedule = weekNights
            .Select(day => new WeeklyScheduleEntry
            {
                Day = day,
                StartTime = new TimeOnly(21, 0),
                Duration = TimeSpan.FromHours(1),
            })
            .ToList();

        foreach (var day in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            schedule.Add(new WeeklyScheduleEntry
            {
                Day = day,
                StartTime = new TimeOnly(22, 0),
                Duration = TimeSpan.FromHours(2),
            });
        }

        return schedule;
    }
}
