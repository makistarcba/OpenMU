// <copyright file="KundunEventPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.Kundun;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: Kundun (monstruo 275, "Illusion of Kundun 7") en Kalima 7 como evento visible.
/// </summary>
/// <remarks>
/// Kundun no es un evento con horario: reaparece su RespawnDelay (3 h) despues de que lo matan.
/// Este plugin no cambia eso; solo lo hace visible (22/11/2026, pedido de Nicolas):
/// - Aviso global cuando aparece y cuando lo matan (quien y a que hora vuelve).
/// - Entrada "Kundun" en el panel de eventos (H): vivo, o la hora a la que reaparece.
/// El momento de la muerte se guarda en memoria: despues de reiniciar el server, Kundun
/// arranca vivo (asi lo crea OpenMU), asi que no hace falta persistirlo.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: Kundun (avisos y panel)", Description = "Avisa a todo el servidor cuando Kundun aparece en Kalima 7 y cuando lo matan, y lo muestra en el panel de eventos (H).")]
[Guid("5B7E2C19-8D43-4F6A-A1E0-3C9D72F48B65")]
public class KundunEventPlugIn : IPeriodicTaskPlugIn, IAttackableGotKilledPlugIn, IEventPanelSource
{
    /// <summary>
    /// El numero de monstruo de Kundun en Kalima 7.
    /// </summary>
    public const short KundunNumber = 275;

    /// <summary>
    /// El numero del mapa Kalima 7.
    /// </summary>
    public const ushort KalimaSevenMapNumber = 36;

    private const string MapName = "Kalima 7";

    private static readonly ConcurrentDictionary<IGameContext, KundunState> States = new();

    // "Empezar ahora" desde el editor: se atiende en la proxima vuelta de ExecuteTaskAsync,
    // que es la que tiene el contexto del juego.
    private volatile bool _forceRequested;

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var state = States.GetOrAdd(gameContext, _ => new KundunState());

        if (this._forceRequested)
        {
            this._forceRequested = false;
            await SpawnNowAsync(gameContext, state).ConfigureAwait(false);
        }

        // El mapa se crea cuando entra el primer jugador; sin mapa, Kundun todavia no existe
        // pero va a estar vivo cuando alguien entre.
        if (await gameContext.GetMapAsync(KalimaSevenMapNumber, false).ConfigureAwait(false) is not { } map)
        {
            return;
        }

        var kundun = FindKundun(map);
        var alive = kundun.Any(m => m.IsAlive);
        var wasAlive = state.IsAlive;
        state.IsAlive = alive;

        if (alive && wasAlive == false)
        {
            state.RespawnUtc = null;
            state.AliveSinceUtc = DateTime.UtcNow;
            await gameContext.SendGlobalNotificationAsync($"Kundun aparecio en {MapName}!").ConfigureAwait(false);
            EventPanelService.NotifyChanged(gameContext);
        }
        else if (alive && wasAlive is null)
        {
            state.AliveSinceUtc ??= DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public void ForceStart()
    {
        // VORTHANIA (23/11): "Empezar ahora" del editor. Si Kundun esta vivo no hace nada. Si esta
        // muerto esperando su reaparicion, pone UN Kundun extra que no reaparece; el de siempre sigue
        // con su reloj (si vuelve mientras el extra esta vivo, hay dos).
        this._forceRequested = true;
    }

    /// <inheritdoc />
    public async ValueTask AttackableGotKilledAsync(IAttackable killed, IAttacker? killer)
    {
        if (killed is not Monster { Definition.Number: KundunNumber } kundun)
        {
            return;
        }

        IGameContext? gameContext = killer switch
        {
            Player player => player.GameContext,
            IPlayerSurrogate surrogate => surrogate.Owner.GameContext,
            _ => null,
        };
        if (gameContext is null)
        {
            return;
        }

        var state = States.GetOrAdd(gameContext, _ => new KundunState());
        var killerNameOfForced = killer switch
        {
            Player p => p.Name,
            IPlayerSurrogate s => s.Owner.Name,
            _ => null,
        };
        if (state.Forced.TryRemove(kundun, out _))
        {
            // El extra no reaparece: no cambia la hora del Kundun de siempre.
            await gameContext.SendGlobalNotificationAsync(string.IsNullOrEmpty(killerNameOfForced)
                ? $"Murio el Kundun extra de {MapName}."
                : $"{killerNameOfForced} derroto al Kundun extra de {MapName}.").ConfigureAwait(false);
            EventPanelService.NotifyChanged(gameContext);
            return;
        }

        var respawnUtc = DateTime.UtcNow.Add(kundun.Definition.RespawnDelay);
        state.IsAlive = false;
        state.RespawnUtc = respawnUtc;
        state.AliveSinceUtc = null;

        var killerName = killer switch
        {
            Player player => player.Name,
            IPlayerSurrogate surrogate => surrogate.Owner.Name,
            _ => null,
        };
        var respawnLocal = TimeZoneInfo.ConvertTimeFromUtc(respawnUtc, gameContext.ServerTimeZone);
        var message = string.IsNullOrEmpty(killerName)
            ? $"Kundun murio en {MapName}. Vuelve a las {respawnLocal:HH:mm}."
            : $"{killerName} derroto a Kundun en {MapName}. Vuelve a las {respawnLocal:HH:mm}.";
        await gameContext.SendGlobalNotificationAsync(message).ConfigureAwait(false);
        EventPanelService.NotifyChanged(gameContext);
    }

    /// <inheritdoc />
    public EventPanelEntry? GetEventPanelEntry(IGameContext gameContext)
    {
        var state = States.GetOrAdd(gameContext, _ => new KundunState());
        if (state.RespawnUtc is { } respawn && respawn > DateTime.UtcNow)
        {
            return new EventPanelEntry("Kundun", EventPanelEntryKind.Other, EventPanelEntryState.Scheduled, respawn, null, Array.Empty<EventPanelMonsterGroup>());
        }

        // Vivo (o por aparecer apenas alguien entre a Kalima 7). Sin hora de fin: el cliente
        // muestra "En curso" sin cuenta regresiva.
        return new EventPanelEntry(
            "Kundun",
            EventPanelEntryKind.Other,
            EventPanelEntryState.Running,
            state.AliveSinceUtc,
            null,
            [new EventPanelMonsterGroup("Kundun", MapName, 1, 1)]);
    }

    private static async ValueTask SpawnNowAsync(GameContext gameContext, KundunState state)
    {
        try
        {
            // Si el mapa no existia, crearlo ya pone al Kundun de siempre (vivo): con eso alcanza.
            if (await gameContext.GetMapAsync(KalimaSevenMapNumber, true).ConfigureAwait(false) is not { } map
                || FindKundun(map).Any(m => m.IsAlive))
            {
                return;
            }

            if (gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == KundunNumber) is not { } definition)
            {
                return;
            }

            var spawn = map.Definition.MonsterSpawns.FirstOrDefault(s => s.MonsterDefinition?.Number == KundunNumber);
            var x = spawn is { } ? (byte)((spawn.X1 + spawn.X2) / 2) : (byte)26;
            var y = spawn is { } ? (byte)((spawn.Y1 + spawn.Y2) / 2) : (byte)76;
            var area = new MonsterSpawnArea
            {
                GameMap = map.Definition,
                MonsterDefinition = definition,
                SpawnTrigger = SpawnTrigger.OnceAtEventStart, // no reaparece
                Quantity = 1,
                X1 = (byte)Math.Max(x - 2, 0),
                X2 = (byte)Math.Min(x + 2, 255),
                Y1 = (byte)Math.Max(y - 2, 0),
                Y2 = (byte)Math.Min(y + 2, 255),
            };
            var intelligence = new BasicMonsterIntelligence();
            var monster = new Monster(area, definition, map, gameContext.DropGenerator, intelligence, gameContext.PlugInManager, gameContext.PathFinderPool);
            monster.Initialize();
            await map.AddAsync(monster).ConfigureAwait(false);
            monster.OnSpawn();
            state.Forced[monster] = 0;
        }
        catch (Exception ex)
        {
            gameContext.LoggerFactory.CreateLogger<KundunEventPlugIn>().LogError(ex, "No se pudo poner el Kundun extra.");
        }
    }

    private static List<Monster> FindKundun(GameMap map)
    {
        var spawn = map.Definition.MonsterSpawns.FirstOrDefault(s => s.MonsterDefinition?.Number == KundunNumber);
        var center = spawn is { }
            ? new Point((byte)((spawn.X1 + spawn.X2) / 2), (byte)((spawn.Y1 + spawn.Y2) / 2))
            : new Point(26, 76);
        return map.GetNpcsInRange(center, 60)
            .OfType<Monster>()
            .Where(m => m.Definition.Number == KundunNumber)
            .ToList();
    }

    private sealed class KundunState
    {
        public bool? IsAlive { get; set; }

        public DateTime? AliveSinceUtc { get; set; }

        public DateTime? RespawnUtc { get; set; }

        public ConcurrentDictionary<Monster, byte> Forced { get; } = new();
    }
}
