// <copyright file="PeriodicTaskConfiguration.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.PeriodicTasks;

using System.ComponentModel;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Configuration for the <see cref="PeriodicTaskBasePlugIn{TConfiguration,TState}"/>.
/// </summary>
/// <remarks>
/// Display order values are spaced by inheritance level, so adding a property never
/// requires renumbering another level: this class uses 1–99, direct subclasses
/// 100–199, their subclasses 200–299, and so on.
/// </remarks>
public class PeriodicTaskConfiguration
{
    /// <summary>
    /// Gets or sets a time delay.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PeriodicTaskConfiguration_PreStartMessageDelay_Name), Order = 1)]
    public TimeSpan PreStartMessageDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets the minimum time of the task duration.
    /// </summary>
    /// <remarks>
    /// VORTHANIA: when <see cref="WeeklySchedule"/> is used, this value is only the fallback and the
    /// guard against a new run starting while the previous one is still going. Set it to the longest
    /// duration in the weekly schedule so that guard stays conservative.
    /// </remarks>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PeriodicTaskConfiguration_TaskDuration_Name), Order = 2)]
    public TimeSpan TaskDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the text which prints as a golden message in the game when task starts.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PeriodicTaskConfiguration_StartMessage_Name), Order = 3)]
    public LocalizedString StartMessage { get; set; }

    /// <summary>
    /// Gets or sets the text which prints as a golden message in the game when task ends.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PeriodicTaskConfiguration_EndMessage_Name), Order = 4)]
    public LocalizedString EndMessage { get; set; }

    /// <summary>
    /// Gets or sets a timetable for the event.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PeriodicTaskConfiguration_Timetable_Name), Order = 5)]
    public IList<TimeOnly> Timetable { get; set; } = new List<TimeOnly>();

    /// <summary>
    /// Gets or sets the identifiers of the game servers on which the event runs.
    /// An empty list means the event runs on all game servers.
    /// </summary>
    [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PeriodicTaskConfiguration_AllowedServerIds_Name), Description = nameof(PlugInResources.PeriodicTaskConfiguration_AllowedServerIds_Description), Order = 6)]
    public IList<byte> AllowedServerIds { get; set; } = new List<byte>();

    /// <summary>
    /// Gets or sets a weekly schedule, which takes precedence over <see cref="Timetable"/>.
    /// </summary>
    /// <remarks>
    /// VORTHANIA: when this list has any entry, <see cref="Timetable"/> is ignored entirely. Leave it
    /// empty to keep the original upstream behaviour.
    ///
    /// It is deliberately hidden from the admin panel with <see cref="BrowsableAttribute"/>. The panel
    /// has no editor for a collection of complex configuration objects: <c>ObjectCollectionFieldBuilder</c>
    /// renders an <c>ItemTable</c>, which is a picker for already persisted entities, so the field shows
    /// up as an empty "Select WeeklyScheduleEntry" dialog and saving the form would wipe the schedule.
    /// The schedule therefore lives in code (see <c>HappyHourConfiguration.Default</c>); hiding it keeps
    /// the rest of the plugin's configuration editable in the panel without that risk.
    /// </remarks>
    [Browsable(false)]
    [Display(Name = "Weekly schedule (overrides the timetable)", Order = 7)]
    public ICollection<WeeklyScheduleEntry> WeeklySchedule { get; set; } = new List<WeeklyScheduleEntry>();

    /// <summary>
    /// Generate a sequence of time points like [00:00, 00:01, ...].
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <param name="startLimit">The start limit, inclusive.</param>
    /// <param name="endLimit">The end limit, exclusive.</param>
    public static IEnumerable<TimeOnly> GenerateTimeSequence(TimeSpan duration, TimeOnly? startLimit = null, TimeOnly? endLimit = null)
    {
        var limit = endLimit?.ToTimeSpan() ?? TimeSpan.FromDays(1);
        var current = startLimit?.ToTimeSpan() ?? TimeSpan.Zero;

        while (current < limit)
        {
            yield return TimeOnly.FromTimeSpan(current);
            current = current.Add(duration);
        }
    }

    /// <summary>
    /// Determines whether the event runs on the game server with the specified identifier.
    /// A missing (<c>null</c>) or empty list means the event runs on all servers.
    /// </summary>
    /// <param name="serverId">The identifier of the game server.</param>
    /// <returns><c>true</c> if the event runs on the server; otherwise, <c>false</c>.</returns>
    public bool IsEnabledOnServer(byte serverId)
    {
        return this.AllowedServerIds is null or { Count: 0 } || this.AllowedServerIds.Contains(serverId);
    }

    /// <summary>
    /// Check if current time is OK for starting an invasion.
    /// </summary>
    /// <param name="serverTimeZone">
    /// The server time zone used to interpret the <see cref="Timetable"/> times of day.
    /// Pass <see cref="TimeZoneInfo.Utc"/> to keep the previous UTC-based behavior.
    /// </param>
    /// <returns>Returns true if the invasion can be started.</returns>
    public virtual bool IsItTimeToStart(TimeZoneInfo serverTimeZone)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, serverTimeZone);
        var nowTime = TimeOnly.FromDateTime(nowLocal);
        var earlier = nowTime.Add(TimeSpan.FromSeconds(-5));

        // VORTHANIA: the weekly schedule wins when it is configured.
        if (this.WeeklySchedule.Count > 0)
        {
            return this.WeeklySchedule.Any(entry =>
                entry.Day == nowLocal.DayOfWeek && entry.StartTime.IsBetween(earlier, nowTime));
        }

        if (this.Timetable.Count == 0)
        {
            return false;
        }

        // For example, p = 00:00. Check that time between 00:00:00 and 00:00:05
        return this.Timetable.Any(p => p.IsBetween(earlier, nowTime));
    }

    /// <summary>
    /// VORTHANIA: Gets how long the run which is starting right now should last.
    /// </summary>
    /// <param name="serverTimeZone">The server time zone.</param>
    /// <returns>The duration of the current run.</returns>
    /// <remarks>
    /// Deliberately does not depend on the five second start window: it picks the latest entry of
    /// today whose start time has already passed. The duration is therefore still correct when this
    /// is called a few seconds after the event was matched, which is what the base plugin does.
    /// Falls back to <see cref="TaskDuration"/> when no weekly schedule is configured.
    /// </remarks>
    public virtual TimeSpan GetEffectiveTaskDuration(TimeZoneInfo serverTimeZone)
    {
        if (this.WeeklySchedule.Count == 0)
        {
            return this.TaskDuration;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, serverTimeZone);
        var nowTime = TimeOnly.FromDateTime(nowLocal);

        var current = this.WeeklySchedule
            .Where(entry => entry.Day == nowLocal.DayOfWeek && entry.StartTime <= nowTime)
            .OrderByDescending(entry => entry.StartTime)
            .FirstOrDefault();

        return current?.Duration ?? this.TaskDuration;
    }

    /// <summary>
    /// VORTHANIA: Gets the next time this task starts on its own, for the event panel.
    /// </summary>
    /// <param name="serverTimeZone">The server time zone, in which the start times are defined.</param>
    /// <param name="nowUtc">The current time; <c>null</c> for <see cref="DateTime.UtcNow"/>.</param>
    /// <returns>The next start in UTC, or <c>null</c> if no start time is configured.</returns>
    /// <remarks>
    /// Mirrors <see cref="IsItTimeToStart"/>: a weekly schedule, when present, replaces the timetable.
    /// </remarks>
    public virtual DateTime? GetNextStartUtc(TimeZoneInfo serverTimeZone, DateTime? nowUtc = null)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc ?? DateTime.UtcNow, serverTimeZone);
        nowLocal = DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified);
        DateTime? next = null;

        for (var dayOffset = 0; dayOffset <= 7 && next is null; dayOffset++)
        {
            var day = nowLocal.Date.AddDays(dayOffset);
            IEnumerable<TimeOnly> startTimes = this.WeeklySchedule.Count > 0
                ? this.WeeklySchedule.Where(entry => entry.Day == day.DayOfWeek).Select(entry => entry.StartTime)
                : this.Timetable;

            foreach (var startTime in startTimes)
            {
                var candidate = day.Add(startTime.ToTimeSpan());
                if (candidate > nowLocal && (next is null || candidate < next))
                {
                    next = candidate;
                }
            }
        }

        if (next is not { } nextLocal)
        {
            return null;
        }

        if (serverTimeZone.IsInvalidTime(nextLocal))
        {
            nextLocal = nextLocal.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(nextLocal, serverTimeZone);
    }

    /// <summary>
    /// Gets the next start time of the task, according to the <see cref="Timetable"/>.
    /// </summary>
    /// <param name="utcNow">The current time, in UTC.</param>
    /// <param name="serverTimeZone">The time zone of the server, in which the times of the <see cref="Timetable"/> are defined.</param>
    /// <returns>The next start time, in UTC; or <see langword="null"/>, if the timetable is empty.</returns>
    /// <remarks>
    /// VORTHANIA: upstream solo miraba el <see cref="Timetable"/>; delega en nuestra version, que respeta
    /// el <see cref="WeeklySchedule"/> (Happy Hour) y las clases que la sobreescriben.
    /// </remarks>
    public DateTime? GetNextStartUtc(DateTime utcNow, TimeZoneInfo serverTimeZone)
        => this.GetNextStartUtc(serverTimeZone, utcNow);
}
