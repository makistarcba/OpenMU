// <copyright file="DynamicExperienceRatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Applies an experience rate which depends on the amount of resets of the character.
/// </summary>
/// <remarks>
/// This is the core mechanic of Vorthania: the experience rate starts very high and decays as the
/// character resets, so a player who joins in month six catches up with the day-one players
/// within weeks instead of never.
///
/// OpenMU only has a single global <see cref="IGameContext.ExperienceRate"/>. This plugin turns
/// that single value into a curve: the configured tiers express the *absolute* rate wanted at a
/// given reset count, and the plugin divides by the global rate to get the factor it has to
/// apply. That way the numbers configured in the admin panel are the ones a player would
/// recognise ("450x", "35x"), with no mental arithmetic.
///
/// The plugin runs after the configured rates and the map multiplier, and before the random
/// multiplier - see the remarks on <see cref="IExperienceCalculationPlugIn"/>.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: Dynamic experience rate", Description = "Experience rate which decays with the reset count of the character.")]
[Guid("7A1C4E52-9D3B-4F86-8C21-5E0B7A94D1F3")]
public class DynamicExperienceRatePlugIn
    : IExperienceCalculationPlugIn,
      ISupportCustomConfiguration<DynamicExperienceRatePlugIn.DynamicExperienceRateConfiguration>,
      ISupportDefaultCustomConfiguration,
      IDisabledByDefault
{
    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public DynamicExperienceRateConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public object CreateDefaultConfig() => DynamicExperienceRateConfiguration.CreateDefault();

    /// <inheritdoc />
    public ValueTask CalculateExperienceAsync(Player player, ExperienceCalculationArgs args)
    {
        if (args.IsMasterExperience)
        {
            // The master level curve is a separate progression with its own rate. Leaving it
            // alone keeps this plugin to one job.
            return ValueTask.CompletedTask;
        }

        if (this.Configuration is not { Tiers.Count: > 0 } configuration)
        {
            return ValueTask.CompletedTask;
        }

        if (player.Attributes is not { } attributes)
        {
            return ValueTask.CompletedTask;
        }

        var globalRate = player.GameContext.ExperienceRate;
        if (globalRate <= 0)
        {
            // Nothing sane to divide by; leave the experience untouched rather than
            // silently zeroing everyone's progression.
            return ValueTask.CompletedTask;
        }

        var resets = (int)attributes[Stats.Resets];
        var targetRate = GetTargetRateForResets(configuration, resets);
        if (targetRate <= 0)
        {
            return ValueTask.CompletedTask;
        }

        args.Experience *= targetRate / globalRate;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets the absolute experience rate configured for the given reset count.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="resets">The reset count of the character.</param>
    /// <returns>The target rate, or 0 when no tier applies.</returns>
    /// <remarks>
    /// The tier with the highest <see cref="ExperienceRateTier.MinimumResetCount"/> that is still
    /// less than or equal to the reset count wins, so the tiers do not have to be stored in order
    /// and gaps between them are harmless.
    /// </remarks>
    internal static float GetTargetRateForResets(DynamicExperienceRateConfiguration configuration, int resets)
    {
        var bestRate = 0f;
        var bestMinimum = -1;
        foreach (var tier in configuration.Tiers)
        {
            if (tier.MinimumResetCount <= resets && tier.MinimumResetCount > bestMinimum)
            {
                bestMinimum = tier.MinimumResetCount;
                bestRate = tier.ExperienceRate;
            }
        }

        return bestRate;
    }

    /// <summary>
    /// The configuration of the <see cref="DynamicExperienceRatePlugIn"/>.
    /// </summary>
    public class DynamicExperienceRateConfiguration
    {
        /// <summary>
        /// Gets or sets the tiers of the experience curve.
        /// </summary>
        [Display(Name = "Experience rate tiers")]
        public ICollection<ExperienceRateTier> Tiers { get; set; } = [];

        /// <summary>
        /// Creates the default configuration, which is the launch curve of Vorthania.
        /// </summary>
        /// <returns>The default configuration.</returns>
        /// <remarks>
        /// These numbers come from docs/economia.xlsx and are a starting point, not a decision.
        /// Adjust them there first - the spreadsheet turns a rate into hours of play per tier,
        /// which is the number that actually matters.
        /// </remarks>
        public static DynamicExperienceRateConfiguration CreateDefault() => new()
        {
            Tiers =
            [
                new ExperienceRateTier { MinimumResetCount = 0, ExperienceRate = 450f },
                new ExperienceRateTier { MinimumResetCount = 20, ExperienceRate = 250f },
                new ExperienceRateTier { MinimumResetCount = 60, ExperienceRate = 120f },
                new ExperienceRateTier { MinimumResetCount = 120, ExperienceRate = 35f },
            ],
        };
    }

    /// <summary>
    /// One step of the experience rate curve.
    /// </summary>
    public class ExperienceRateTier
    {
        /// <summary>
        /// Gets or sets the minimum reset count at which this tier applies.
        /// </summary>
        [Display(Name = "Minimum reset count")]
        public int MinimumResetCount { get; set; }

        /// <summary>
        /// Gets or sets the absolute experience rate for this tier, e.g. 450 for 450x.
        /// </summary>
        [Display(Name = "Experience rate (absolute, e.g. 450 for 450x)")]
        public float ExperienceRate { get; set; }
    }
}
