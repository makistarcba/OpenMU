// <copyright file="RequestAutoChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Vorthania;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// VORTHANIA: <c>/request auto</c> acepta solo todas las invitaciones de party; <c>/request off</c> vuelve a preguntar.
/// </summary>
/// <remarks>
/// Tarjeta #6 de Trello (24/11/2026). Vale hasta salir del juego (no se guarda). Las reglas de siempre
/// siguen: diferencia de nivel, eventos sin party, tope de la party.
/// </remarks>
[Guid("D41B7E93-6A2C-4F85-B1E7-0C9A3F5D2E68")]
[PlugIn]
[Display(Name = "Vorthania: /request auto", Description = "'/request auto' accepts every party invitation automatically, '/request off' asks again.")]
[ChatCommandHelp(Command, "Accept every party invitation automatically: /request auto | /request off", typeof(RequestAutoChatCommandPlugIn.Arguments))]
public class RequestAutoChatCommandPlugIn : ChatCommandPlugInBase<RequestAutoChatCommandPlugIn.Arguments>
{
    private const string Command = "/request";

    private static readonly ConditionalWeakTable<Player, object> AutoAccepting = new();

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc/>
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Determines whether the player accepts every party invitation automatically.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns><c>True</c>, if the player turned on <c>/request auto</c>.</returns>
    public static bool IsAutoAccepting(Player player) => AutoAccepting.TryGetValue(player, out _);

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, Arguments arguments)
    {
        switch (arguments.Mode?.ToLowerInvariant())
        {
            case Arguments.AutoMode:
                AutoAccepting.AddOrUpdate(player, new object());
                await player.ShowBlueMessageAsync("Party: aceptas todas las invitaciones automaticamente. Para volver: /request off").ConfigureAwait(false);
                break;
            case Arguments.OffMode:
                AutoAccepting.Remove(player);
                await player.ShowBlueMessageAsync("Party: las invitaciones se vuelven a preguntar.").ConfigureAwait(false);
                break;
            default:
                await player.ShowBlueMessageAsync(IsAutoAccepting(player)
                    ? "Party: aceptacion automatica PRENDIDA (/request off para apagarla)."
                    : "Party: aceptacion automatica apagada (/request auto para prenderla).").ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// The arguments of <c>/request</c>.
    /// </summary>
    public class Arguments : ArgumentsBase
    {
        /// <summary>
        /// Accept every invitation.
        /// </summary>
        public const string AutoMode = "auto";

        /// <summary>
        /// Ask again.
        /// </summary>
        public const string OffMode = "off";

        /// <summary>
        /// Gets or sets the mode.
        /// </summary>
        [Argument("mode", false)]
        [ValidValues(AutoMode, OffMode)]
        public string? Mode { get; set; }
    }
}
