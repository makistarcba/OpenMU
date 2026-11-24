// <copyright file="ZenDropPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: el zen que tira un bicho sale de su experiencia BASE, sin los rates.
/// </summary>
/// <remarks>
/// Tarjetas #8 y #17 de Trello (24/11/2026): en OpenMU el zen de un bicho es la experiencia que dio
/// el kill + 7 (<c>DefaultDropGenerator</c>). Con experiencia de 80x a 250x el zen sale igual de inflado,
/// el monto del piso coincide con la experiencia y el zen pierde valor.
///
/// Con este plugin prendido el zen es la experiencia base del bicho (la del MU con rate 1x, misma
/// fórmula que <c>AttackableExtensions.CalculateBaseExperience</c>) por <see cref="ZenDropConfiguration.Multiplier"/>.
/// Apagado, todo queda como en OpenMU. Viene apagado: se prende desde el panel o por SQL.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: Zen drop", Description = "Money drops are based on the base experience of the monster (without rates), multiplied by the configured multiplier.")]
[Guid("5B8E2C41-7D3A-4F69-9E12-A4C6D80F3B57")]
public class ZenDropPlugIn
    : IFeaturePlugIn,
      ISupportCustomConfiguration<ZenDropPlugIn.ZenDropConfiguration>,
      ISupportDefaultCustomConfiguration,
      IDisabledByDefault
{
    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public ZenDropConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gets the amount on which the money drop of a kill is based, or <c>null</c> when the plugin is off.
    /// </summary>
    /// <param name="killer">The player which killed the monster.</param>
    /// <param name="killed">The killed monster.</param>
    /// <returns>The base for the money drop; <c>null</c>, if OpenMU's default should be used.</returns>
    public static int? GetMoneyBase(Player killer, IAttackable killed)
    {
        if (killer.GameContext.FeaturePlugIns.GetPlugIn<ZenDropPlugIn>() is not { } plugIn)
        {
            return null;
        }

        var multiplier = (plugIn.Configuration ?? new ZenDropConfiguration()).Multiplier;
        if (multiplier <= 0)
        {
            return 0;
        }

        var baseExperience = killed.CalculateBaseExperience(killer.Attributes?[Stats.TotalLevel] ?? 0);
        return (int)Math.Clamp(baseExperience * multiplier, 0, int.MaxValue / 2);
    }

    /// <inheritdoc />
    public object CreateDefaultConfig()
    {
        return new ZenDropConfiguration();
    }

    /// <summary>
    /// The configuration of the <see cref="ZenDropPlugIn"/>.
    /// </summary>
    public class ZenDropConfiguration
    {
        /// <summary>
        /// Gets or sets the multiplier of the base experience. 1 = like the original MU with rate 1x.
        /// </summary>
        [Display(Name = "Multiplier", Description = "Money drop = base experience of the monster (rate 1x) * multiplier. 1 = original MU with rate 1x.")]
        public double Multiplier { get; set; } = 1.0;
    }
}
