// <copyright file="QuestPeriod.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Configuration.Quests;

/// <summary>
/// VORTHANIA: Cada cuánto se vuelve a habilitar una quest ya completada.
/// </summary>
/// <remarks>
/// Upstream no tiene ninguna noción de tiempo en las quests: una quest es repetible al instante o
/// no es repetible nunca. Esto agrega el medio, que es donde viven las diarias y las semanales.
///
/// El corte es de CALENDARIO, no de duración: "diaria" no significa 24 horas después de la última,
/// significa hasta la medianoche de la zona horaria del servidor. Un cooldown rodante castiga al
/// que un día juega a las 22 y al otro a las 20, y le va corriendo el horario solo.
/// </remarks>
public enum QuestPeriod
{
    /// <summary>
    /// Sin período. Vale <see cref="QuestDefinition.Repeatable"/>: repetible al instante, o nunca.
    /// </summary>
    None,

    /// <summary>
    /// Se vuelve a habilitar al cambiar el día en la zona horaria del servidor.
    /// </summary>
    Daily,

    /// <summary>
    /// Se vuelve a habilitar al empezar una nueva semana (lunes) en la zona horaria del servidor.
    /// </summary>
    Weekly,
}
