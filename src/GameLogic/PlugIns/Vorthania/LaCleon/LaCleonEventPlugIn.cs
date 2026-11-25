// <copyright file="LaCleonEventPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania.LaCleon;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlayerActions;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: Evento LaCleon. Un mapa de bonus al que se entra con tiempo ganado.
/// </summary>
/// <remarks>
/// Diseño del 13/11/2026 (doc 28 del proyecto):
/// - El tiempo es por cuenta. Se gana matando jefes de evento (el del último golpe), entregando el
///   arma en Blood Castle, llegando vivo al final de Devil Square y ganando Chaos Castle.
/// - Cada cobro da 6 minutos, con tope de 90 minutos ganados por día. A las 00:00 vuelve a 0.
/// - Se entra hablando con la Fireworks Girl (379, Devias), con nivel 250 y saldo. Ninguna otra
///   forma de entrar al mapa funciona (portal, /move, invocación de party).
/// - Adentro el saldo baja cada segundo; en 0 te manda a Lorencia. Sin PvP.
/// El x1,5 de experiencia y el +25 % de joyas son datos del mapa (script SQL), no de este plugin.
///
/// Enganches fuera de este archivo, todos marcados VORTHANIA:
/// <c>PlayerMapTransitions.WarpToAsync</c> (entrada), <c>Player.AttackByAsync</c> (PvP),
/// <c>BloodCastleContext</c>, <c>DevilSquareContext</c> y <c>ChaosCastleContext</c> (cobros).
/// Desactivar el plugin en el panel apaga todo y el mapa vuelve a quedar cerrado por la compuerta.
/// </remarks>
[PlugIn]
[Display(Name = "Vorthania: Evento LaCleon", Description = "Mapa de bonus al que se entra con tiempo ganado matando jefes de evento y ganando Blood Castle, Devil Square y Chaos Castle.")]
[Guid("A1F4C7D2-63B9-4E58-9C0A-5D2E8B7F4A16")]
public class LaCleonEventPlugIn
    : IFeaturePlugIn,
      IPeriodicTaskPlugIn,
      IAttackableGotKilledPlugIn,
      IPlayerTalkToNpcPlugIn,
      IWarpGateEnteringPlugIn,
      ISupportCustomConfiguration<LaCleonEventConfiguration>,
      ISupportDefaultCustomConfiguration
{
    /// <summary>
    /// Los jugadores que la Fireworks Girl está mandando al mapa en este instante.
    /// </summary>
    private static readonly ConcurrentDictionary<Player, byte> AuthorizedEntries = new();

    private static (string Source, HashSet<int> Numbers) _bossNumbersCache = (string.Empty, new HashSet<int>());

    /// <inheritdoc />
    public LaCleonEventConfiguration? Configuration { get; set; }

    private LaCleonEventConfiguration Config => this.Configuration ??= new LaCleonEventConfiguration();

    /// <summary>
    /// Gives the time reward of the specified source to the player, if the event is active.
    /// Never throws, so it's safe to call from the mini games.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="source">The source of the reward.</param>
    public static async ValueTask RewardAsync(Player player, LaCleonRewardSource source)
    {
        // VORTHANIA 24/11/2026: los mismos logros dan Goblin Points para el Cash Shop (si ese plugin esta prendido).
        await GoblinPointsForEventsPlugIn.RewardAsync(player, source).ConfigureAwait(false);

        try
        {
            if (player.GameContext.FeaturePlugIns.GetPlugIn<LaCleonEventPlugIn>() is { } plugIn)
            {
                await plugIn.GiveRewardAsync(player, source).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            player.Logger.LogWarning(ex, "LaCleon: no se pudo dar el tiempo ({Source}).", source);
        }
    }

    /// <inheritdoc />
    public object CreateDefaultConfig() => new LaCleonEventConfiguration();

    /// <inheritdoc />
    public void ForceStart()
    {
        // do nothing.
    }

    /// <summary>
    /// Determines whether the specified map is the map of the event.
    /// </summary>
    /// <param name="mapNumber">The number of the map.</param>
    /// <returns><c>true</c> when it's the map of the event.</returns>
    public bool IsEventMap(short mapNumber) => mapNumber == this.Config.MapNumber;

    /// <summary>
    /// Determines whether attacks between players are blocked on the specified map.
    /// </summary>
    /// <param name="mapNumber">The number of the map.</param>
    /// <returns><c>true</c> when players can't attack each other there.</returns>
    public bool IsPvpBlocked(short mapNumber) => this.Config.BlockPvp && this.IsEventMap(mapNumber);

    /// <summary>
    /// VORTHANIA 18/11: corta los portales (como el de Devias 2) y la lista de warp hacia el mapa del evento.
    /// Cancelado acá, el OpenMU nuevo deja al jugador donde está y le cierra la pantalla de carga; el
    /// corte de <c>PlayerMapTransitions.WarpToAsync</c> llega tarde para un portal y la pantalla quedaba negra.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="targetGate">The target gate.</param>
    /// <param name="eventArgs">The event args.</param>
    public async ValueTask WarpGateEnteringAsync(Player player, ExitGate targetGate, CancelEventArgs eventArgs)
    {
        if (eventArgs.Cancel
            || targetGate.Map is not { } map
            || this.MayEnterMap(player, map.Number, out var message))
        {
            return;
        }

        eventArgs.Cancel = true;
        if (message is { })
        {
            await player.ShowBlueMessageAsync(message).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether the player is allowed to be moved to the specified map.
    /// Called from <c>PlayerMapTransitions.WarpToAsync</c>.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="mapNumber">The number of the target map.</param>
    /// <param name="message">The message which should be shown to the player when he's not allowed.</param>
    /// <returns><c>true</c> when the player may enter the map.</returns>
    public bool MayEnterMap(Player player, short mapNumber, out string? message)
    {
        message = null;
        if (!this.IsEventMap(mapNumber) || IsGameMaster(player))
        {
            return true;
        }

        if (player.CurrentMap?.Definition.Number == mapNumber)
        {
            // Ya está adentro (por ejemplo vuelve a la zona segura del mapa): el reloj decide.
            return true;
        }

        if (AuthorizedEntries.ContainsKey(player))
        {
            return true;
        }

        message = this.Config.OnlyByNpcMessage;
        return false;
    }

    /// <inheritdoc />
    public async ValueTask AttackableGotKilledAsync(IAttackable killed, IAttacker? killer)
    {
        if (killed is not Monster monster)
        {
            return;
        }

        var player = killer as Player ?? (killer as Monster)?.SummonedBy;
        if (player is null || !this.GetBossNumbers().Contains(monster.Definition.Number))
        {
            return;
        }

        await RewardAsync(player, LaCleonRewardSource.Boss).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask PlayerTalksToNpcAsync(Player player, NonPlayerCharacter npc, NpcTalkEventArgs eventArgs)
    {
        if (npc.Definition.Number != this.Config.NpcNumber)
        {
            return ValueTask.CompletedTask;
        }

        eventArgs.HasBeenHandled = true;

        // VORTHANIA 17/11: la entrada corre aparte. TalkNpcAction deja al jugador en "diálogo abierto"
        // y recién lo devuelve al estado normal DESPUÉS de este método (el OpenMU nuevo lo espera con
        // await; el viejo no). Moverlo de mapa antes rompe el estado, y antes de este arreglo la entrada
        // se cortaba sin ningún mensaje ("clic y no pasa nada").
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 30 && player.PlayerState.CurrentState == PlayerState.NpcDialogOpened; i++)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }

                await this.TryEnterAsync(player).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                player.Logger.LogWarning(ex, "LaCleon: falló la entrada por el NPC.");
            }
        });

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var config = this.Config;
        var inside = new ConcurrentBag<Player>();
        await gameContext.ForEachPlayerAsync(player =>
        {
            if (player.CurrentMap?.Definition.Number == config.MapNumber)
            {
                inside.Add(player);
            }

            return Task.CompletedTask;
        }).ConfigureAwait(false);

        foreach (var player in inside)
        {
            try
            {
                await this.ChargePlayerAsync(gameContext, player).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                player.Logger.LogWarning(ex, "LaCleon: falló el descuento de tiempo.");
            }
        }

        LaCleonTimeService.SaveIfDue(gameContext, config.StateFile);
    }

    /// <summary>
    /// Writes the time of the account of the player into his chat.
    /// </summary>
    /// <param name="player">The player.</param>
    public async ValueTask ShowTimeAsync(Player player)
    {
        var config = this.Config;
        if (player.Account?.LoginName is not { } loginName)
        {
            return;
        }

        var (earned, remaining) = LaCleonTimeService.GetTime(player.GameContext, config.StateFile, loginName);
        await player.ShowBlueMessageAsync($"LaCleon: tenés {FormatTime(remaining)}. Ganado hoy: {earned / 60} de {config.DailyCapMinutes} min.").ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"Se entra con la Fireworks Girl en Devias, desde nivel {config.MinimumLevel}. El tiempo se pierde a las 00:00.").ConfigureAwait(false);
    }

    private static bool IsGameMaster(Player player) => player.SelectedCharacter?.CharacterStatus >= CharacterStatus.GameMaster;

    private static string FormatTime(double seconds)
    {
        var total = (int)Math.Ceiling(Math.Max(0, seconds));
        if (total >= 60)
        {
            return total % 60 == 0 ? $"{total / 60} min" : $"{total / 60} min {total % 60} s";
        }

        return $"{total} s";
    }

    // Avisos: en cada minuto entero que queda, a los 30 y a los 10 segundos.
    private static bool CrossedNoticeMark(double previous, double current, out int mark)
    {
        mark = 0;
        if (current <= 0 || previous <= current)
        {
            return false;
        }

        int[] shortMarks = [30, 10];
        foreach (var shortMark in shortMarks)
        {
            if (previous > shortMark && current <= shortMark)
            {
                mark = shortMark;
                return true;
            }
        }

        var minuteMark = (int)Math.Ceiling(current / 60) * 60;
        if (previous > minuteMark && current <= minuteMark)
        {
            mark = minuteMark;
            return true;
        }

        return false;
    }

    private HashSet<int> GetBossNumbers()
    {
        var source = this.Config.BossMonsterNumbers ?? string.Empty;
        var cache = _bossNumbersCache;
        if (cache.Source == source)
        {
            return cache.Numbers;
        }

        var numbers = new HashSet<int>();
        foreach (var part in source.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var number))
            {
                numbers.Add(number);
            }
        }

        _bossNumbersCache = (source, numbers);
        return numbers;
    }

    private async ValueTask GiveRewardAsync(Player player, LaCleonRewardSource source)
    {
        var config = this.Config;
        var isEnabled = source switch
        {
            LaCleonRewardSource.BloodCastle => config.RewardBloodCastle,
            LaCleonRewardSource.DevilSquare => config.RewardDevilSquare,
            LaCleonRewardSource.ChaosCastle => config.RewardChaosCastle,
            _ => true,
        };

        if (!isEnabled || player.Account is not { IsBot: false, LoginName: { Length: > 0 } loginName })
        {
            return;
        }

        var (added, earned, remaining) = LaCleonTimeService.AddEarnedTime(
            player.GameContext,
            config.StateFile,
            loginName,
            config.MinutesPerReward * 60,
            config.DailyCapMinutes * 60);

        var sourceName = source switch
        {
            LaCleonRewardSource.BloodCastle => "Blood Castle",
            LaCleonRewardSource.DevilSquare => "Devil Square",
            LaCleonRewardSource.ChaosCastle => "Chaos Castle",
            _ => "jefe de evento",
        };

        if (added <= 0)
        {
            await player.ShowBlueMessageAsync($"LaCleon: ya ganaste el tope de {config.DailyCapMinutes} min de hoy.").ConfigureAwait(false);
            return;
        }

        await player.ShowBlueMessageAsync($"LaCleon: +{FormatTime(added)} por {sourceName}. Tenés {FormatTime(remaining)} (ganado hoy: {earned / 60} de {config.DailyCapMinutes} min).").ConfigureAwait(false);
    }

    private async ValueTask TryEnterAsync(Player player)
    {
        var config = this.Config;
        if (player.SelectedCharacter is null
            || !player.IsAlive
            || player.Account?.LoginName is not { } loginName)
        {
            return;
        }

        if (player.PlayerState.CurrentState != PlayerState.EnteredWorld)
        {
            // Antes se cortaba en silencio; si vuelve a pasar, que quede a la vista.
            player.Logger.LogInformation("LaCleon: entrada cancelada, el jugador está en el estado {State}.", player.PlayerState.CurrentState);
            await player.ShowBlueMessageAsync("LaCleon: no se pudo entrar ahora, hablale de nuevo a la Fireworks Girl.").ConfigureAwait(false);
            return;
        }

        if (player.CurrentMap?.Definition.Number == config.MapNumber)
        {
            return;
        }

        var isGameMaster = IsGameMaster(player);
        if (!isGameMaster)
        {
            var level = (int)(player.Attributes?[Stats.Level] ?? 0);
            if (level < config.MinimumLevel)
            {
                await player.ShowBlueMessageAsync($"LaCleon: necesitás nivel {config.MinimumLevel} para entrar.").ConfigureAwait(false);
                return;
            }

            var (earned, remaining) = LaCleonTimeService.GetTime(player.GameContext, config.StateFile, loginName);
            if (remaining < 1)
            {
                await player.ShowBlueMessageAsync($"LaCleon: no tenés tiempo. Ganado hoy: {earned / 60} de {config.DailyCapMinutes} min.").ConfigureAwait(false);
                await player.ShowBlueMessageAsync($"Cada jefe de evento, Blood Castle, Devil Square o Chaos Castle ganado te da {config.MinutesPerReward} min.").ConfigureAwait(false);
                return;
            }
        }

        var mapDefinition = player.GameContext.Configuration.Maps.FirstOrDefault(m => m.Number == config.MapNumber);
        var gate = player.GameContext.Configuration.WarpList.FirstOrDefault(w => w.Gate?.Map?.Number == config.MapNumber)?.Gate
                   ?? mapDefinition?.GetSafezoneGate()
                   ?? mapDefinition?.ExitGates.FirstOrDefault();
        if (gate is null)
        {
            player.Logger.LogWarning("LaCleon: el mapa {Map} no tiene ninguna puerta de entrada.", config.MapNumber);
            await player.ShowBlueMessageAsync("LaCleon: el mapa no está disponible.").ConfigureAwait(false);
            return;
        }

        AuthorizedEntries[player] = 0;
        try
        {
            await player.WarpToAsync(gate).ConfigureAwait(false);
        }
        finally
        {
            AuthorizedEntries.TryRemove(player, out _);
        }
    }

    private async ValueTask ChargePlayerAsync(GameContext gameContext, Player player)
    {
        var config = this.Config;
        if (player.SelectedCharacter is null
            || player.PlayerState.CurrentState != PlayerState.EnteredWorld
            || IsGameMaster(player)
            || player.Account is not { IsBot: false, LoginName: { Length: > 0 } loginName })
        {
            return;
        }

        var (previous, remaining, isFirstCharge) = LaCleonTimeService.Charge(gameContext, config.StateFile, loginName);
        if (remaining <= 0)
        {
            if (!player.IsAlive)
            {
                // Se lo saca cuando reaparezca.
                return;
            }

            var exitGate = gameContext.Configuration.Maps.FirstOrDefault(m => m.Number == config.ExitMapNumber)?.GetSafezoneGate();
            if (exitGate is null)
            {
                player.Logger.LogWarning("LaCleon: el mapa de salida {Map} no tiene zona segura.", config.ExitMapNumber);
                return;
            }

            await player.ShowBlueMessageAsync("LaCleon: se te acabó el tiempo.").ConfigureAwait(false);
            await player.WarpToAsync(exitGate).ConfigureAwait(false);
            return;
        }

        if (isFirstCharge)
        {
            await player.ShowBlueMessageAsync($"LaCleon: entraste con {FormatTime(remaining)}. El reloj corre solo mientras estás acá.").ConfigureAwait(false);
        }
        else if (CrossedNoticeMark(previous, remaining, out var mark))
        {
            await player.ShowBlueMessageAsync($"LaCleon: te quedan {FormatTime(mark)}.").ConfigureAwait(false);
        }
    }
}

/// <summary>
/// VORTHANIA: What gave time for LaCleon.
/// </summary>
public enum LaCleonRewardSource
{
    /// <summary>
    /// The last hit on a boss of an event.
    /// </summary>
    Boss,

    /// <summary>
    /// Delivered the weapon to the archangel of the Blood Castle.
    /// </summary>
    BloodCastle,

    /// <summary>
    /// Was alive at the end of the Devil Square.
    /// </summary>
    DevilSquare,

    /// <summary>
    /// Won the Chaos Castle.
    /// </summary>
    ChaosCastle,
}

/// <summary>
/// VORTHANIA: The configuration of the <see cref="LaCleonEventPlugIn"/>.
/// </summary>
public class LaCleonEventConfiguration
{
    /// <summary>
    /// Gets or sets the number of the map of the event. LaCleon is 57.
    /// </summary>
    [Display(Name = "Map number")]
    public short MapNumber { get; set; } = 57;

    /// <summary>
    /// Gets or sets the number of the NPC which lets the players in. The Fireworks Girl is 379.
    /// </summary>
    [Display(Name = "Entrance NPC number")]
    public short NpcNumber { get; set; } = 379;

    /// <summary>
    /// Gets or sets the minimum level to enter.
    /// </summary>
    [Display(Name = "Minimum level")]
    public int MinimumLevel { get; set; } = 250;

    /// <summary>
    /// Gets or sets the minutes which every reward gives.
    /// </summary>
    [Display(Name = "Minutes per reward")]
    public int MinutesPerReward { get; set; } = 6;

    /// <summary>
    /// Gets or sets the maximum of minutes which an account can earn per day.
    /// </summary>
    [Display(Name = "Daily cap in minutes")]
    public int DailyCapMinutes { get; set; } = 90;

    /// <summary>
    /// Gets or sets the number of the map to which the players are moved when their time is over. Lorencia is 0.
    /// </summary>
    [Display(Name = "Exit map number")]
    public short ExitMapNumber { get; set; }

    /// <summary>
    /// Gets or sets the numbers of the monsters which give time to the player who did the last hit, separated by commas.
    /// Default: the nine golden monsters, Red Dragon (44), White Wizard (135), Death King (55) and Kundun (275).
    /// </summary>
    [Display(Name = "Boss monster numbers (comma separated)")]
    public string BossMonsterNumbers { get; set; } = "43,53,54,78,79,80,81,82,83,44,135,55,275";

    /// <summary>
    /// Gets or sets a value indicating whether delivering the weapon in Blood Castle gives time.
    /// </summary>
    [Display(Name = "Reward: Blood Castle (weapon delivered)")]
    public bool RewardBloodCastle { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether being alive at the end of Devil Square gives time.
    /// </summary>
    [Display(Name = "Reward: Devil Square (alive at the end)")]
    public bool RewardDevilSquare { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether winning Chaos Castle gives time.
    /// </summary>
    [Display(Name = "Reward: Chaos Castle (winner)")]
    public bool RewardChaosCastle { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether attacks between players are blocked on the map.
    /// </summary>
    [Display(Name = "Block PvP on the map")]
    public bool BlockPvp { get; set; } = true;

    /// <summary>
    /// Gets or sets the message for players who try to enter the map without the NPC.
    /// </summary>
    [Display(Name = "Message: only by NPC")]
    public string OnlyByNpcMessage { get; set; } = "A LaCleon se entra hablando con la Fireworks Girl en Devias.";

    /// <summary>
    /// Gets or sets the path of the file where the time of the day is kept between restarts.
    /// </summary>
    [Display(Name = "State file")]
    public string StateFile { get; set; } = "vorthania-state/lacleon-tiempo.json";
}
