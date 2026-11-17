// <copyright file="QuestAvailabilityExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Quests;

using MUnique.OpenMU.DataModel.Configuration.Quests;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// VORTHANIA: Reglas de disponibilidad de quests que upstream no tiene.
/// </summary>
/// <remarks>
/// Están acá y no adentro de <see cref="QuestStartAction"/> porque las usan dos lugares: el que
/// arranca la quest y el que arma la lista que muestra el NPC. Si se duplican, tarde o temprano
/// el NPC ofrece algo que después el server rechaza, que es la peor versión del bug.
/// </remarks>
public static class QuestAvailabilityExtensions
{
    /// <summary>
    /// Determines whether the character's reset count is inside the quest's range.
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="player">The player.</param>
    /// <returns><c>true</c> if the reset count qualifies.</returns>
    public static bool IsResetRangeFulfilled(this QuestDefinition quest, Player player)
    {
        if (quest.MinimumResets <= 0 && quest.MaximumResets <= 0)
        {
            return true;
        }

        if (player.Attributes is not { } attributes)
        {
            // Mismo criterio que el chequeo de nivel para party: un set de atributos ausente no es
            // culpa del jugador, así que no bloquea.
            return true;
        }

        var resets = (int)attributes[Stats.Resets];
        if (resets < quest.MinimumResets)
        {
            return false;
        }

        return quest.MaximumResets <= 0 || resets <= quest.MaximumResets;
    }

    /// <summary>
    /// Determines whether the quest's period has elapsed since it was last finished.
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="questState">The state of the quest's group.</param>
    /// <param name="serverTimeZone">The server time zone.</param>
    /// <returns><c>true</c> if the quest can be taken again.</returns>
    /// <remarks>
    /// El corte es de calendario. "Diaria" significa hasta la medianoche de la zona horaria del
    /// servidor, no 24 horas contadas desde la última vez.
    ///
    /// Como <see cref="CharacterQuestState"/> es uno por grupo y guarda una sola fecha, esto asume
    /// que **cada quest con período tiene su propio grupo**. Dos diarias en el mismo grupo
    /// comparten el reloj.
    /// </remarks>
    public static bool IsPeriodElapsed(this QuestDefinition quest, CharacterQuestState? questState, TimeZoneInfo serverTimeZone)
    {
        if (quest.Period == QuestPeriod.None)
        {
            return true;
        }

        if (questState?.LastFinishedUtc is not { } lastFinishedUtc)
        {
            return true;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, serverTimeZone);
        var lastLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(lastFinishedUtc, DateTimeKind.Utc), serverTimeZone);

        return quest.Period switch
        {
            QuestPeriod.Daily => lastLocal.Date < nowLocal.Date,
            QuestPeriod.Weekly => GetStartOfWeek(lastLocal) < GetStartOfWeek(nowLocal),
            _ => true,
        };
    }

    private static DateTime GetStartOfWeek(DateTime value)
    {
        var daysSinceMonday = ((int)value.DayOfWeek + 6) % 7;
        return value.Date.AddDays(-daysSinceMonday);
    }
}
