// <copyright file="WeeklyScheduleEntry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.PeriodicTasks;

/// <summary>
/// VORTHANIA: One entry of a weekly event schedule - a day, a start time and its own duration.
/// </summary>
/// <remarks>
/// Upstream OpenMU schedules periodic events with <see cref="PeriodicTaskConfiguration.Timetable"/>,
/// which is a list of times of day only: the same times every day, and one shared duration for all
/// of them. That cannot express a normal server event calendar, where weekdays and weekends differ.
/// This entry adds the missing two dimensions - which day, and how long that particular run lasts.
/// </remarks>
public class WeeklyScheduleEntry
{
    /// <summary>
    /// Gets or sets the day of the week on which this run starts.
    /// </summary>
    [Display(Name = "Day of week")]
    public DayOfWeek Day { get; set; }

    /// <summary>
    /// Gets or sets the time of day at which this run starts, in the server's time zone.
    /// </summary>
    [Display(Name = "Start time (server time zone)")]
    public TimeOnly StartTime { get; set; }

    /// <summary>
    /// Gets or sets how long this particular run lasts.
    /// </summary>
    /// <remarks>
    /// A run may cross midnight - a Saturday entry starting at 22:00 with a duration of two hours
    /// ends at 00:00 on Sunday. Only the start is matched against <see cref="Day"/>.
    /// </remarks>
    [Display(Name = "Duration")]
    public TimeSpan Duration { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{this.Day} {this.StartTime} ({this.Duration})";
}
