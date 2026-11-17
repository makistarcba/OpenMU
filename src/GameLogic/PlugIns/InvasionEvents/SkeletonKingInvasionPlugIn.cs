// <copyright file="SkeletonKingInvasionPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// Vorthania: invasión del Skeleton King ("Underground Forces Attack Event" del MU original).
// Death King (55) con escolta de Death Bone (56), en Lorencia o Noria al azar, 30 minutos,
// anuncio global de quién lo mató. Cada 3 horas a las :30 (hora del servidor).
// Va en: src/GameLogic/PlugIns/InvasionEvents/SkeletonKingInvasionPlugIn.cs
// Requiere que existan en la base los MonsterDefinition 55 y 56 (vorthania-skeleton-king.sql).

namespace MUnique.OpenMU.GameLogic.PlugIns.InvasionEvents;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Enables the Skeleton King (Death King) Invasion feature.
/// </summary>
[PlugIn]
[Display(Name = "Skeleton King Invasion", Description = "Vorthania: Death King con escolta de Death Bone en Lorencia o Noria.")]
[Guid("50D53A15-1308-4A65-839A-4C5B0E7CCA27")]
public sealed class SkeletonKingInvasionPlugIn : SimpleInvasionPlugIn
{
    /// <summary>
    /// Número del Death King (Skeleton King). En el cliente: MONSTER_DEATH_KING = 55.
    /// </summary>
    public const ushort DeathKing = 55;

    /// <summary>
    /// Número del Death Bone (escolta). En el cliente: MONSTER_DEATH_BONE = 56.
    /// </summary>
    public const ushort DeathBone = 56;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkeletonKingInvasionPlugIn"/> class.
    /// </summary>
    public SkeletonKingInvasionPlugIn()
        : base(CreateDefaultConfiguration)
    {
    }

    /// <inheritdoc />
    protected override ushort? AnnouncedMonsterId => DeathKing;

    private static PeriodicInvasionConfiguration CreateDefaultConfiguration() => new()
    {
        TaskDuration = TimeSpan.FromMinutes(30),
        PreStartMessageDelay = TimeSpan.FromSeconds(3),
        StartMessage = "[{mapName}] ¡El Skeleton King invade con su ejército de Death Bones!",
        EndMessage = "[{mapName}] La invasión del Skeleton King terminó.",
        ForceSingleMap = true,
        Timetable =
        [
            new TimeOnly(0, 30),
            new TimeOnly(3, 30),
            new TimeOnly(6, 30),
            new TimeOnly(9, 30),
            new TimeOnly(12, 30),
            new TimeOnly(15, 30),
            new TimeOnly(18, 30),
            new TimeOnly(21, 30),
        ],
        Mobs =
        [
            new(DeathKing, 1, [InvasionMaps.Lorencia, InvasionMaps.Noria], SpawnMapStrategy.RandomMap, announceDeath: true),
            new(DeathBone, 10, [], SpawnMapStrategy.RandomMap),
        ],
    };
}
