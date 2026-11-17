// <copyright file="ResetProgressionCalculator.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Resets;

/// <summary>
/// Calculates costs and rewards for the next character reset.
/// </summary>
public static class ResetProgressionCalculator
{
    /// <summary>
    /// Calculates the reset progression for the next reset.
    /// </summary>
    /// <param name="currentResetCount">The current reset count.</param>
    /// <param name="pointsPerResetOverride">The player-specific points per reset override (0 means not configured).</param>
    /// <param name="configuration">The reset configuration.</param>
    /// <returns>The calculated progression.</returns>
    public static ResetProgression Calculate(int currentResetCount, int pointsPerResetOverride, ResetConfiguration configuration)
    {
        var nextResetCount = currentResetCount + 1;
        var requiredZen = Math.Max(0, configuration.RequiredMoney);
        if (configuration.MultiplyRequiredMoneyByResetCount)
        {
            requiredZen *= nextResetCount;
        }

        var pointsForReset = GetPointsForReset(configuration, pointsPerResetOverride, nextResetCount);
        var totalPointsAfterReset = GetTotalPointsAfterReset(configuration, pointsPerResetOverride, nextResetCount, pointsForReset);
        var requiredItemAmount = GetRequiredItemAmount(configuration, nextResetCount);

        return new ResetProgression(nextResetCount, requiredZen, requiredItemAmount, pointsForReset, totalPointsAfterReset);
    }

    /// <summary>
    /// Gets the reset limit which is effective at the specified date.
    /// </summary>
    /// <param name="configuration">The reset configuration.</param>
    /// <param name="today">The current date, in the time zone of the server.</param>
    /// <returns>The effective reset limit. Zero means that there is no limit.</returns>
    /// <remarks>
    /// VORTHANIA: el tope de resets de la temporada. Si hay calendario y fecha de apertura, gana la
    /// fila con la semana más alta que ya haya empezado; si no, gana el ResetLimit fijo de siempre.
    /// Se calcula en cada reset, así que la progresión avanza sola. Ver docs/12 y docs/14.
    /// </remarks>
    public static int GetEffectiveResetLimit(ResetConfiguration configuration, DateOnly today)
    {
        if (configuration.ResetLimitSchedule.Count == 0 || configuration.SeasonStart == default)
        {
            return Math.Max(0, configuration.ResetLimit ?? 0);
        }

        var daysSinceStart = today.DayNumber - configuration.SeasonStart.DayNumber;
        var week = daysSinceStart < 0 ? 1 : (daysSinceStart / 7) + 1;

        if (GetMatchingTier(configuration.ResetLimitSchedule, week, tier => tier.FromWeek) is not { } tier)
        {
            return Math.Max(0, configuration.ResetLimit ?? 0);
        }

        return Math.Max(0, tier.ResetLimit);
    }

    private static int GetPointsForReset(ResetConfiguration configuration, int pointsPerResetOverride, int nextResetCount)
    {
        if (GetMatchingTier(configuration.PointsTiers, nextResetCount, tier => tier.MinimumResetCount) is { } tier)
        {
            return Math.Max(0, tier.PointsGranted);
        }

        var pointsPerReset = pointsPerResetOverride == 0 ? configuration.PointsPerReset : pointsPerResetOverride;
        if (configuration.MultiplyPointsByResetCount)
        {
            pointsPerReset *= nextResetCount;
        }

        return Math.Max(0, pointsPerReset);
    }

    private static int GetRequiredItemAmount(ResetConfiguration configuration, int nextResetCount)
    {
        if (configuration.RequiredResetItem is null)
        {
            return 0;
        }

        if (GetMatchingTier(configuration.ItemCostTiers, nextResetCount, tier => tier.MinimumResetCount) is not { } tier)
        {
            return 0;
        }

        return Math.Max(0, tier.RequiredItemAmount);
    }

    private static int GetTotalPointsAfterReset(ResetConfiguration configuration, int pointsPerResetOverride, int nextResetCount, int pointsForReset)
    {
        if (configuration.PointsTiers.Count == 0)
        {
            return pointsForReset;
        }

        long total = 0;
        for (var resetCount = 1; resetCount <= nextResetCount; resetCount++)
        {
            total += GetPointsForReset(configuration, pointsPerResetOverride, resetCount);
            if (total >= int.MaxValue)
            {
                return int.MaxValue;
            }
        }

        return (int)total;
    }

    private static TTier? GetMatchingTier<TTier>(IEnumerable<TTier> tiers, int resetCount, Func<TTier, int> getMinimumResetCount)
        where TTier : class
    {
        return tiers
            .OrderByDescending(getMinimumResetCount)
            .FirstOrDefault(tier => getMinimumResetCount(tier) <= resetCount);
    }
}
