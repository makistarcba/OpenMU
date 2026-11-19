// <copyright file="EditorBridgeController.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.API
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using MUnique.OpenMU.DataModel.Configuration;
    using MUnique.OpenMU.DataModel.Entities;
    using MUnique.OpenMU.GameLogic;
    using MUnique.OpenMU.GameLogic.Attributes;
    using MUnique.OpenMU.GameLogic.PlugIns;
    using MUnique.OpenMU.GameLogic.PlugIns.PeriodicTasks;
    using MUnique.OpenMU.GameLogic.PlugIns.Vorthania.EventPanel;
    using MUnique.OpenMU.GameLogic.PlugIns.Vorthania.LaCleon;
    using MUnique.OpenMU.GameLogic.Views.Character;
    using MUnique.OpenMU.GameLogic.Views.Inventory;
    using MUnique.OpenMU.GameServer;
    using MUnique.OpenMU.Interfaces;
    using MUnique.OpenMU.Persistence;
    using MUnique.OpenMU.PlugIns;
    using MUnique.OpenMU.Web.AdminPanel.Auth;

    /// <summary>
    /// Vorthania: el puente entre el MuEditor (la herramienta web aparte) y el servidor que esta corriendo.
    /// </summary>
    /// <remarks>
    /// Para que existe: el editor escribe en la base, pero OpenMU lee la configuracion una sola vez y
    /// tiene a los personajes conectados en memoria. Un UPDATE por SQL no se ve hasta reiniciar, y sobre
    /// un personaje conectado directamente se pierde. Este controller hace los mismos cambios DESDE
    /// ADENTRO del proceso, que es lo unico que los vuelve inmediatos:
    ///  - spots: se guardan con el mismo contexto que usa el editor de mapas del panel, y eso dispara
    ///    el aviso de cambio de configuracion que hace aparecer o desaparecer a los monstruos;
    ///  - tiendas: los items viven en el esquema data, que no avisa cambios, asi que el editor manda la lista;
    ///  - jugadores conectados: se tocan en memoria, igual que los comandos /set, /setmoney y /item.
    /// Pide una API key con rol Operator, igual que el endpoint que manda mensajes globales.
    /// </remarks>
    [Route("api/editor/")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.ApiSchemes, Policy = AdminPolicies.Operator)]
    public class EditorBridgeController : Controller
    {
        /// <summary>
        /// La version del puente. El editor la muestra, y sirve para saber que build corre.
        /// </summary>
        private const string BridgeVersion = "vorthania-bridge-6";

        private readonly IDictionary<int, IGameServer> _gameServers;
        private readonly IDataSource<GameConfiguration> _configDataSource;
        private readonly IPersistenceContextProvider _contextProvider;
        private readonly IConfigurationChangeMediatorListener _changeMediator;
        private readonly ILogger<EditorBridgeController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditorBridgeController"/> class.
        /// </summary>
        /// <param name="gameServers">The game servers.</param>
        /// <param name="configDataSource">The data source of the game configuration, the same which the admin panel edits.</param>
        /// <param name="contextProvider">The persistence context provider.</param>
        /// <param name="changeMediator">The mediator which informs the game about changed configuration.</param>
        /// <param name="logger">The logger.</param>
        public EditorBridgeController(
            IDictionary<int, IGameServer> gameServers,
            IDataSource<GameConfiguration> configDataSource,
            IPersistenceContextProvider contextProvider,
            IConfigurationChangeMediatorListener changeMediator,
            ILogger<EditorBridgeController> logger)
        {
            this._gameServers = gameServers;
            this._configDataSource = configDataSource;
            this._contextProvider = contextProvider;
            this._changeMediator = changeMediator;
            this._logger = logger;
        }

        /// <summary>
        /// Tells the editor that the bridge is there.
        /// </summary>
        /// <returns>The version of the bridge.</returns>
        [HttpGet]
        [Route("ping")]
        public IActionResult Ping()
        {
            return this.Ok(new { bridge = BridgeVersion });
        }

        /// <summary>
        /// Gets the connected players with their map and position.
        /// </summary>
        /// <returns>The connected players.</returns>
        [HttpGet]
        [Route("players")]
        public async Task<IActionResult> GetPlayersAsync()
        {
            var result = new List<object>();
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var players = await server.Context.GetPlayersAsync().ConfigureAwait(false);
                foreach (var player in players)
                {
                    if (player.SelectedCharacter is not { } character)
                    {
                        continue;
                    }

                    var map = player.CurrentMap?.Definition;
                    result.Add(new
                    {
                        name = character.Name,
                        account = player.Account?.LoginName,
                        characterId = character.GetId(),
                        mapId = map?.GetId(),
                        mapNumber = map?.Number,
                        mapName = map is null ? null : (string)map.Name,
                        x = player.Position.X,
                        y = player.Position.Y,
                        level = player.Attributes is { } attributes ? (int)attributes[Stats.Level] : 0,
                        resets = player.Attributes is { } attributes2 ? (int)attributes2[Stats.Resets] : 0,
                        money = player.Money,
                    });
                }
            }

            return this.Ok(result);
        }

        /// <summary>
        /// Creates or changes a monster spawn area. The game applies it without a restart.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The id of the saved spawn area.</returns>
        [HttpPost]
        [Route("spawn/save")]
        public async Task<IActionResult> SaveSpawnAsync([FromBody] SpawnRequest request)
        {
            if (request is null)
            {
                return this.BadRequest("Falta el cuerpo del pedido.");
            }

            var context = await this._configDataSource.GetContextAsync().ConfigureAwait(false);
            var config = await this._configDataSource.GetOwnerAsync().ConfigureAwait(false);

            var monster = config.Monsters.FirstOrDefault(m => m.GetId() == request.MonsterId);
            if (monster is null)
            {
                return this.NotFound("No existe ese monstruo.");
            }

            MonsterSpawnArea? spawn;
            if (request.Id is { } spawnId)
            {
                spawn = config.Maps.SelectMany(m => m.MonsterSpawns).FirstOrDefault(s => s.GetId() == spawnId);
                if (spawn is null)
                {
                    return this.NotFound("No existe ese spot.");
                }
            }
            else
            {
                var map = config.Maps.FirstOrDefault(m => m.GetId() == request.MapId);
                if (map is null)
                {
                    return this.NotFound("No existe ese mapa.");
                }

                spawn = context.CreateNew<MonsterSpawnArea>();
                spawn.GameMap = map;
                spawn.SpawnTrigger = SpawnTrigger.Automatic;
                map.MonsterSpawns.Add(spawn);
            }

            spawn.MonsterDefinition = monster;
            spawn.X1 = Math.Min(request.X1, request.X2);
            spawn.Y1 = Math.Min(request.Y1, request.Y2);
            spawn.X2 = Math.Max(request.X1, request.X2);
            spawn.Y2 = Math.Max(request.Y1, request.Y2);
            spawn.Quantity = Math.Max((short)1, request.Quantity);
            if (request.Direction is { } direction && Enum.GetValues<Direction>().Any(d => (int)d == direction))
            {
                spawn.Direction = (Direction)direction;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            this._logger.LogWarning(
                "[EDITOR BRIDGE] spot guardado: {Monster} x{Quantity} en {Map} ({X1},{Y1})-({X2},{Y2})",
                (string)monster.Designation,
                spawn.Quantity,
                spawn.GameMap is { } savedMap ? (string)savedMap.Name : "?",
                spawn.X1,
                spawn.Y1,
                spawn.X2,
                spawn.Y2);
            return this.Ok(new { id = spawn.GetId() });
        }

        /// <summary>
        /// Deletes a monster spawn area. Its monsters disappear without a restart.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The result.</returns>
        [HttpPost]
        [Route("spawn/delete")]
        public async Task<IActionResult> DeleteSpawnAsync([FromBody] IdRequest request)
        {
            if (request is null)
            {
                return this.BadRequest("Falta el cuerpo del pedido.");
            }

            var context = await this._configDataSource.GetContextAsync().ConfigureAwait(false);
            var config = await this._configDataSource.GetOwnerAsync().ConfigureAwait(false);
            foreach (var map in config.Maps)
            {
                var spawn = map.MonsterSpawns.FirstOrDefault(s => s.GetId() == request.Id);
                if (spawn is null)
                {
                    continue;
                }

                map.MonsterSpawns.Remove(spawn);
                await context.DeleteAsync(spawn).ConfigureAwait(false);
                await context.SaveChangesAsync().ConfigureAwait(false);
                this._logger.LogWarning("[EDITOR BRIDGE] spot borrado en {Map}", (string)map.Name);
                return this.Ok(new { deleted = true });
            }

            return this.NotFound("No existe ese spot.");
        }

        /// <summary>
        /// Replaces the items of a merchant store in the memory of the game, after the editor changed them in the database.
        /// </summary>
        /// <param name="request">The request with the id of the merchant (a monster definition) and the complete list of items.</param>
        /// <returns>The number of items which the store has now.</returns>
        /// <remarks>
        /// Los items de una tienda son filas de data.Item, y el esquema data no publica avisos de
        /// cambio: por eso ni siquiera el editor de tiendas del panel se ve sin reiniciar.
        /// La version 1 del puente releia la tienda desde la base y la dejo vacia en el juego. Esta no
        /// lee nada: el editor, que es quien escribio la base, manda la lista completa, y aca se arma
        /// entera ANTES de tocar la tienda. Si algo falla armandola, la tienda queda como estaba.
        /// </remarks>
        [HttpPost]
        [Route("merchant/set")]
        public async Task<IActionResult> SetMerchantItemsAsync([FromBody] MerchantItemsRequest request)
        {
            if (request is null)
            {
                return this.BadRequest("Falta el cuerpo del pedido.");
            }

            var done = new HashSet<ItemStorage>(ReferenceEqualityComparer.Instance);
            var count = -1;
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var gameConfig = server.Context.Configuration;
                var merchant = gameConfig.Monsters.FirstOrDefault(m => m.GetId() == request.Id);
                if (merchant?.MerchantStore is not { } store || !done.Add(store))
                {
                    continue;
                }

                // Este contexto no se libera ni se guarda a proposito: solo se usa para crear objetos
                // del tipo que la tienda acepta. Nunca escribe en la base.
                var context = this._contextProvider.CreateNewPlayerContext(gameConfig);
                var newItems = new List<Item>();
                foreach (var requested in request.Items ?? new List<MerchantItemRequest>())
                {
                    var definition = gameConfig.Items.FirstOrDefault(d => d.GetId() == requested.DefinitionId);
                    if (definition is null)
                    {
                        continue;
                    }

                    var item = context.CreateNew<Item>();
                    item.Definition = definition;
                    item.ItemSlot = requested.Slot;
                    item.Level = (byte)Math.Clamp(requested.Level, 0, 15);
                    item.Durability = requested.Durability > 0 ? requested.Durability : definition.Durability;
                    item.HasSkill = requested.Skill && definition.Skill is not null;
                    var possibleOptions = definition.PossibleItemOptions.SelectMany(o => o.PossibleOptions).ToList();
                    foreach (var requestedOption in requested.Options ?? new List<ItemOptionRequest>())
                    {
                        var option = possibleOptions.FirstOrDefault(o => o.GetId() == requestedOption.Id);
                        if (option is null)
                        {
                            continue;
                        }

                        var link = context.CreateNew<ItemOptionLink>();
                        link.ItemOption = option;
                        link.Level = Math.Max(1, requestedOption.Level);
                        item.ItemOptions.Add(link);
                    }

                    newItems.Add(item);
                }

                // El cambio se hace todo o nada. La version 2 vaciaba la tienda y despues se caia
                // agregando, y el error no llegaba al log (ASP.NET lo anota como Microsoft, y eso
                // esta filtrado). Ahora, si falla, se vuelve a poner lo que habia y el error se
                // anota con nuestro logger y vuelve al editor, que lo muestra.
                var oldItems = store.Items.ToList();
                try
                {
                    store.Items.Clear();
                    foreach (var item in newItems)
                    {
                        store.Items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    store.Items.Clear();
                    foreach (var item in oldItems)
                    {
                        store.Items.Add(item);
                    }

                    this._logger.LogError(
                        ex,
                        "[EDITOR BRIDGE] no se pudo cambiar la tienda de {Merchant} (coleccion {CollectionType}, item {ItemType}); quedo como estaba",
                        (string)merchant.Designation,
                        store.Items.GetType().FullName,
                        newItems.FirstOrDefault()?.GetType().FullName);
                    return this.StatusCode(500, $"No se pudo cambiar la tienda, quedo como estaba. {ex.GetType().Name}: {ex.Message}");
                }

                count = store.Items.Count;
                try
                {
                    // Solo le reenvia la lista a quien tenga la tienda abierta en este momento.
                    await this._changeMediator.HandleConfigurationChangedAsync(typeof(ItemStorage), store.GetId(), store).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this._logger.LogError(ex, "[EDITOR BRIDGE] la tienda de {Merchant} cambio, pero fallo el aviso a quien la tenia abierta", (string)merchant.Designation);
                }
                this._logger.LogWarning(
                    "[EDITOR BRIDGE] tienda actualizada: {Merchant}, pedidos {Requested}, quedaron {Count} items",
                    (string)merchant.Designation,
                    request.Items?.Count ?? 0,
                    count);
            }

            if (count < 0)
            {
                return this.NotFound("Ese NPC no tiene tienda, o el servidor de juego no esta levantado.");
            }

            return this.Ok(new { items = count });
        }

        /// <summary>
        /// Gets the items of a merchant store as the game has them in memory.
        /// </summary>
        /// <param name="id">The id of the merchant, which is a monster definition.</param>
        /// <returns>The items.</returns>
        /// <remarks>Solo lee. Sirve para ver en el editor si el juego tiene lo mismo que la base.</remarks>
        [HttpGet]
        [Route("merchant/info")]
        public IActionResult GetMerchantInfo([FromQuery(Name = "id")] Guid id)
        {
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var merchant = server.Context.Configuration.Monsters.FirstOrDefault(m => m.GetId() == id);
                if (merchant?.MerchantStore is not { } store)
                {
                    continue;
                }

                var items = store.Items.ToList();
                return this.Ok(new
                {
                    count = items.Count,
                    collectionType = store.Items.GetType().Name,
                    itemTypes = items.Select(i => i.GetType().Name).Distinct().ToList(),
                    items = items.Select(i => new
                    {
                        slot = i.ItemSlot,
                        name = i.Definition is { } d ? (string)d.Name : null,
                        level = i.Level,
                    }).ToList(),
                });
            }

            return this.NotFound("Ese NPC no tiene tienda, o el servidor de juego no esta levantado.");
        }

        /// <summary>
        /// Gets the inventory of a connected player, as the game has it in memory.
        /// </summary>
        /// <param name="name">The character name.</param>
        /// <returns>The items.</returns>
        [HttpGet]
        [Route("player/inventory")]
        public IActionResult GetInventory([FromQuery(Name = "name")] string name)
        {
            if (this.FindPlayer(name) is not { Inventory: { } inventory } player)
            {
                return this.NotFound("Ese personaje no esta conectado.");
            }

            var items = inventory.Items
                .Where(item => item.Definition is not null)
                .Select(item => new
                {
                    slot = item.ItemSlot,
                    group = item.Definition!.Group,
                    number = item.Definition.Number,
                    name = (string)item.Definition.Name,
                    width = item.Definition.Width,
                    height = item.Definition.Height,
                    level = item.Level,
                    skill = item.HasSkill,
                    durability = item.Durability,
                    options = item.ItemOptions.Count,
                    definitionId = item.Definition.GetId(),
                    optionList = item.ItemOptions
                        .Where(link => link.ItemOption is not null)
                        .Select(link => new { id = link.ItemOption!.GetId(), level = link.Level })
                        .ToList(),
                })
                .ToList();
            return this.Ok(new { money = player.Money, items });
        }

        /// <summary>
        /// Changes level, resets, stats, points and money of a connected player, in memory.
        /// </summary>
        /// <param name="request">The request. Values which are not set stay as they are.</param>
        /// <returns>The result.</returns>
        [HttpPost]
        [Route("player/stats")]
        public async Task<IActionResult> SetPlayerStatsAsync([FromBody] PlayerStatsRequest request)
        {
            if (request is null || this.FindPlayer(request.Name) is not { SelectedCharacter: { } character, Attributes: { } attributes } player)
            {
                return this.NotFound("Ese personaje no esta conectado.");
            }

            var levelChanged = false;
            var statsChanged = false;
            if (request.Level is { } level && level >= 1)
            {
                attributes[Stats.Level] = level;
                levelChanged = true;
            }

            if (request.Resets is { } resets && resets >= 0)
            {
                attributes[Stats.Resets] = resets;
                levelChanged = true;
            }

            if (request.Strength is { } strength && strength >= 0)
            {
                attributes[Stats.BaseStrength] = strength;
                statsChanged = true;
            }

            if (request.Agility is { } agility && agility >= 0)
            {
                attributes[Stats.BaseAgility] = agility;
                statsChanged = true;
            }

            if (request.Vitality is { } vitality && vitality >= 0)
            {
                attributes[Stats.BaseVitality] = vitality;
                statsChanged = true;
            }

            if (request.Energy is { } energy && energy >= 0)
            {
                attributes[Stats.BaseEnergy] = energy;
                statsChanged = true;
            }

            if (request.Leadership is { } leadership && leadership >= 0)
            {
                attributes[Stats.BaseLeadership] = leadership;
                statsChanged = true;
            }

            if (request.Points is { } points && points >= 0)
            {
                character.LevelUpPoints = points;
                levelChanged = true;
            }

            if (request.Money is { } money && money >= 0)
            {
                player.Money = money;
            }

            if (statsChanged)
            {
                await player.InvokeViewPlugInAsync<IUpdateCharacterBaseStatsPlugIn>(p => p.UpdateCharacterBaseStatsAsync()).ConfigureAwait(false);
            }

            if (levelChanged)
            {
                await player.InvokeViewPlugInAsync<IUpdateLevelPlugIn>(p => p.UpdateLevelAsync()).ConfigureAwait(false);
            }

            this._logger.LogWarning("[EDITOR BRIDGE] personaje conectado editado: {Character}", character.Name);
            return this.Ok(new { saved = true });
        }

        /// <summary>
        /// Puts items into the inventory of a connected player.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>How many items could be added.</returns>
        [HttpPost]
        [Route("player/give-item")]
        public async Task<IActionResult> GiveItemAsync([FromBody] GiveItemRequest request)
        {
            if (request is null || this.FindPlayer(request.Name) is not { Inventory: { } inventory, SelectedCharacter: { } character } player)
            {
                return this.NotFound("Ese personaje no esta conectado.");
            }

            var definition = player.GameContext.Configuration.Items.FirstOrDefault(d => d.GetId() == request.DefinitionId);
            if (definition is null)
            {
                return this.NotFound("No existe ese item.");
            }

            var possibleOptions = definition.PossibleItemOptions.SelectMany(o => o.PossibleOptions).ToList();
            var added = 0;
            var count = Math.Clamp(request.Count, 1, 10);
            for (var i = 0; i < count; i++)
            {
                var item = player.PersistenceContext.CreateNew<Item>();
                item.Definition = definition;
                item.Level = (byte)Math.Clamp(request.Level, 0, definition.MaximumItemLevel);
                item.Durability = definition.Durability;
                item.HasSkill = request.Skill && definition.Skill is not null;
                foreach (var requestedOption in request.Options ?? new List<ItemOptionRequest>())
                {
                    var option = possibleOptions.FirstOrDefault(o => o.GetId() == requestedOption.Id);
                    if (option is null)
                    {
                        continue;
                    }

                    var link = player.PersistenceContext.CreateNew<ItemOptionLink>();
                    link.ItemOption = option;
                    link.Level = Math.Max(1, requestedOption.Level);
                    item.ItemOptions.Add(link);
                }

                if (!await inventory.AddItemAsync(item).ConfigureAwait(false))
                {
                    await player.PersistenceContext.DeleteAsync(item).ConfigureAwait(false);
                    break;
                }

                await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(item)).ConfigureAwait(false);
                added++;
            }

            this._logger.LogWarning("[EDITOR BRIDGE] {Count} x {Item} +{Level} para {Character}", added, (string)definition.Name, request.Level, character.Name);
            return this.Ok(new { added });
        }

        /// <summary>
        /// Removes an item from the inventory of a connected player.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The result.</returns>
        [HttpPost]
        [Route("player/remove-item")]
        public async Task<IActionResult> RemoveItemAsync([FromBody] RemoveItemRequest request)
        {
            if (request is null || this.FindPlayer(request.Name) is not { Inventory: { } inventory, SelectedCharacter: { } character } player)
            {
                return this.NotFound("Ese personaje no esta conectado.");
            }

            var item = inventory.Items.FirstOrDefault(i => i.ItemSlot == request.Slot);
            if (item is null)
            {
                return this.NotFound("No hay un item en esa ranura.");
            }

            await player.DestroyInventoryItemAsync(item).ConfigureAwait(false);
            this._logger.LogWarning("[EDITOR BRIDGE] item sacado de la ranura {Slot} de {Character}", request.Slot, character.Name);
            return this.Ok(new { removed = true });
        }

        /// <summary>
        /// Changes level, skill and options of an item which a connected player has in the inventory.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The slot of the changed item.</returns>
        /// <remarks>
        /// No se modifica el item que ya tiene: se le saca y se le pone uno nuevo igual, con los
        /// cambios, en la misma ranura. Es el camino que el juego ya sabe hacer bien (lo mismo que
        /// soltar y volver a agarrar), y asi el cliente y los bonus de lo que lleva puesto quedan al dia.
        /// </remarks>
        [HttpPost]
        [Route("player/replace-item")]
        public async Task<IActionResult> ReplaceItemAsync([FromBody] ReplaceItemRequest request)
        {
            if (request is null || this.FindPlayer(request.Name) is not { Inventory: { } inventory, SelectedCharacter: { } character } player)
            {
                return this.NotFound("Ese personaje no esta conectado.");
            }

            var oldItem = inventory.Items.FirstOrDefault(i => i.ItemSlot == request.Slot);
            if (oldItem?.Definition is not { } definition)
            {
                return this.NotFound("No hay un item en esa ranura.");
            }

            var possibleOptions = definition.PossibleItemOptions.SelectMany(o => o.PossibleOptions).ToList();
            var newItem = player.PersistenceContext.CreateNew<Item>();
            newItem.Definition = definition;
            newItem.Level = (byte)Math.Clamp(request.Level, 0, definition.MaximumItemLevel);
            newItem.Durability = oldItem.Durability;
            newItem.HasSkill = request.Skill && definition.Skill is not null;
            newItem.SocketCount = oldItem.SocketCount;
            newItem.PetExperience = oldItem.PetExperience;
            foreach (var requestedOption in request.Options ?? new List<ItemOptionRequest>())
            {
                var option = possibleOptions.FirstOrDefault(o => o.GetId() == requestedOption.Id);
                if (option is null)
                {
                    continue;
                }

                var link = player.PersistenceContext.CreateNew<ItemOptionLink>();
                link.ItemOption = option;
                link.Level = Math.Max(1, requestedOption.Level);
                newItem.ItemOptions.Add(link);
            }

            var slot = oldItem.ItemSlot;
            await player.DestroyInventoryItemAsync(oldItem).ConfigureAwait(false);
            var added = await inventory.AddItemAsync(slot, newItem).ConfigureAwait(false);
            if (!added)
            {
                // Si la ranura original no lo acepta, que al menos no se pierda: va a cualquier lugar libre.
                added = await inventory.AddItemAsync(newItem).ConfigureAwait(false);
            }

            if (!added)
            {
                await player.PersistenceContext.DeleteAsync(newItem).ConfigureAwait(false);
                this._logger.LogError("[EDITOR BRIDGE] no se pudo volver a poner {Item} a {Character}", (string)definition.Name, character.Name);
                return this.StatusCode(500, "Se saco el item pero no hubo lugar para ponerlo de nuevo.");
            }

            await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(newItem)).ConfigureAwait(false);
            this._logger.LogWarning("[EDITOR BRIDGE] item editado: {Item} +{Level} de {Character}", (string)definition.Name, newItem.Level, character.Name);
            return this.Ok(new { slot = newItem.ItemSlot });
        }

        /// <summary>
        /// Changes the respawn time and the attributes of a monster. The game applies it without a restart.
        /// </summary>
        /// <param name="request">The request. Values which are not set stay as they are.</param>
        /// <returns>The result.</returns>
        /// <remarks>
        /// Se guarda por el mismo contexto que el editor de mapas del panel: MonsterDefinition y
        /// MonsterAttribute estan en el esquema config, asi que el cambio se publica, la copia que usa
        /// el juego se actualiza y los monstruos ya puestos recargan sus atributos (MapInitializer).
        /// El tiempo de reaparicion se lee cada vez que un monstruo muere: vale desde la proxima muerte.
        /// </remarks>
        [HttpPost]
        [Route("monster/save")]
        public async Task<IActionResult> SaveMonsterAsync([FromBody] MonsterRequest request)
        {
            if (request is null)
            {
                return this.BadRequest("Falta el cuerpo del pedido.");
            }

            try
            {
                var context = await this._configDataSource.GetContextAsync().ConfigureAwait(false);
                var config = await this._configDataSource.GetOwnerAsync().ConfigureAwait(false);
                var monster = config.Monsters.FirstOrDefault(m => m.GetId() == request.Id);
                if (monster is null)
                {
                    return this.NotFound("No existe ese monstruo.");
                }

                var changed = new List<string>();
                if (request.RespawnSeconds is { } seconds)
                {
                    if (seconds < 1 || seconds > 86400)
                    {
                        return this.BadRequest("La reaparicion tiene que estar entre 1 segundo y 24 horas.");
                    }

                    monster.RespawnDelay = TimeSpan.FromSeconds(seconds);
                    changed.Add($"reaparece {seconds} s");
                }

                foreach (var requested in request.Attributes ?? new List<MonsterAttributeRequest>())
                {
                    var attribute = monster.Attributes.FirstOrDefault(a => a.AttributeDefinition?.Designation == requested.Name);
                    if (attribute is null)
                    {
                        return this.BadRequest($"{(string)monster.Designation} no tiene el atributo '{requested.Name}'.");
                    }

                    if (Math.Abs(attribute.Value - requested.Value) > 0.0001f)
                    {
                        attribute.Value = requested.Value;
                        changed.Add($"{requested.Name} = {requested.Value}");
                    }
                }

                if (changed.Count == 0)
                {
                    return this.Ok(new { saved = false });
                }

                await context.SaveChangesAsync().ConfigureAwait(false);

                // Por si la copia del juego no es la misma instancia que la del panel: que el cambio
                // llegue igual a los monstruos ya puestos.
                foreach (var server in this._gameServers.Values.OfType<GameServer>())
                {
                    var inGame = server.Context.Configuration.Monsters.FirstOrDefault(m => m.GetId() == request.Id);
                    if (inGame is null || ReferenceEquals(inGame, monster))
                    {
                        continue;
                    }

                    inGame.RespawnDelay = monster.RespawnDelay;
                    foreach (var attribute in monster.Attributes)
                    {
                        if (inGame.Attributes.FirstOrDefault(a => a.AttributeDefinition?.Designation == attribute.AttributeDefinition?.Designation) is { } target)
                        {
                            target.Value = attribute.Value;
                        }
                    }

                    await this._changeMediator.HandleConfigurationChangedAsync(typeof(MonsterDefinition), inGame.GetId(), inGame).ConfigureAwait(false);
                }

                this._logger.LogWarning("[EDITOR BRIDGE] monstruo {Monster}: {Changes}", (string)monster.Designation, string.Join("; ", changed));
                return this.Ok(new { saved = true });
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "[EDITOR BRIDGE] no se pudo guardar el monstruo {Id}", request.Id);
                return this.StatusCode(500, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the events (invasions, mini games, Happy Hour, LaCleon) and whether they are switched on.
        /// </summary>
        /// <returns>The events.</returns>
        [HttpGet]
        [Route("events")]
        public IActionResult GetEvents()
        {
            if (this._gameServers.Values.OfType<GameServer>().FirstOrDefault() is not { } server)
            {
                return this.NotFound("No hay un servidor de juego levantado.");
            }

            var manager = server.Context.PlugInManager;
            var active = manager.GetActivePlugInsOf<IPeriodicTaskPlugIn>().ToList();
            var result = new List<object>();
            foreach (var type in EventTypes(manager))
            {
                var instance = active.FirstOrDefault(p => p.GetType() == type);
                DateTime? next = null;
                try
                {
                    next = (instance as IScheduledPlugIn)?.GetNextStartUtc(DateTime.UtcNow, TimeZoneInfo.Local);
                }
                catch (Exception ex)
                {
                    this._logger.LogDebug(ex, "No se pudo calcular el proximo inicio de {Type}", type.Name);
                }

                result.Add(new
                {
                    id = type.GUID,
                    name = EventName(type),
                    active = manager.IsPlugInActive(type),
                    canStart = typeof(IPeriodicTaskPlugIn).IsAssignableFrom(type),
                    nextStartUtc = next,
                });
            }

            return this.Ok(result);
        }

        /// <summary>
        /// Switches an event on or off. It is saved, so it stays like that after a restart.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Whether the event is active now.</returns>
        /// <remarks>
        /// Apagar no corta un evento que ya esta en marcha: evita los proximos.
        /// </remarks>
        [HttpPost]
        [Route("events/active")]
        public async Task<IActionResult> SetEventActiveAsync([FromBody] EventActiveRequest request)
        {
            if (request is null)
            {
                return this.BadRequest("Falta el cuerpo del pedido.");
            }

            try
            {
                var servers = this._gameServers.Values.OfType<GameServer>().ToList();
                if (servers.FirstOrDefault() is not { } first
                    || EventTypes(first.Context.PlugInManager).FirstOrDefault(t => t.GUID == request.Id) is not { } type)
                {
                    return this.NotFound("No existe ese evento.");
                }

                var context = await this._configDataSource.GetContextAsync().ConfigureAwait(false);
                var config = await this._configDataSource.GetOwnerAsync().ConfigureAwait(false);
                var configuration = config.PlugInConfigurations.FirstOrDefault(c => c.TypeId == request.Id);
                if (configuration is null)
                {
                    configuration = context.CreateNew<PlugInConfiguration>();
                    configuration.TypeId = request.Id;
                    config.PlugInConfigurations.Add(configuration);
                }

                // Al cambiar IsActive, el PlugInManager de cada servidor (que escucha esa propiedad)
                // prende o apaga el plugin, igual que la pantalla de plugins del panel.
                configuration.IsActive = request.Active;
                await context.SaveChangesAsync().ConfigureAwait(false);

                // Y si algun servidor no se entero (otra instancia de la configuracion), se le avisa a mano.
                // Solo si hace falta: activar dos veces un plugin lo registraria dos veces.
                foreach (var server in servers)
                {
                    var manager = server.Context.PlugInManager;
                    if (manager.IsPlugInActive(type) == request.Active)
                    {
                        continue;
                    }

                    if (request.Active)
                    {
                        manager.ActivatePlugIn(type);
                        if (server.Context.Configuration.PlugInConfigurations.FirstOrDefault(c => c.TypeId == request.Id) is { } inGame)
                        {
                            manager.ConfigurePlugIn(type.GUID, inGame);
                        }
                    }
                    else
                    {
                        manager.DeactivatePlugIn(type);
                    }
                }

                var nowActive = servers.All(s => s.Context.PlugInManager.IsPlugInActive(type) == request.Active);
                this._logger.LogWarning("[EDITOR BRIDGE] evento {Event} {State}", EventName(type), request.Active ? "prendido" : "apagado");
                return this.Ok(new { active = request.Active, applied = nowActive });
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "[EDITOR BRIDGE] no se pudo cambiar el evento {Id}", request.Id);
                return this.StatusCode(500, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts an event now, without waiting for its schedule.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>On how many game servers it was started.</returns>
        /// <remarks>
        /// Es el ForceStart de OpenMU: el evento arranca en el proximo chequeo del servidor (segundos),
        /// con sus avisos previos si los tiene. Tiene que estar prendido.
        /// </remarks>
        [HttpPost]
        [Route("events/start")]
        public IActionResult StartEvent([FromBody] IdRequest request)
        {
            if (request is null)
            {
                return this.BadRequest("Falta el cuerpo del pedido.");
            }

            var started = 0;
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var plugIn = server.Context.PlugInManager.GetActivePlugInsOf<IPeriodicTaskPlugIn>()
                    .FirstOrDefault(p => p.GetType().GUID == request.Id);
                if (plugIn is null)
                {
                    continue;
                }

                plugIn.ForceStart();
                started++;
            }

            if (started == 0)
            {
                return this.Conflict("Ese evento esta apagado o no se puede empezar a mano. Prendelo primero.");
            }

            this._logger.LogWarning("[EDITOR BRIDGE] evento {Id} empezado a mano en {Count} servidor(es)", request.Id, started);
            return this.Ok(new { started });
        }

        private static IEnumerable<Type> EventTypes(PlugInManager manager)
        {
            return manager.GetKnownPlugInsOf<IPeriodicTaskPlugIn>()
                .Where(t => EventPanelNames.Get(t) is not null)
                .Append(typeof(LaCleonEventPlugIn))
                .Distinct()
                .OrderBy(EventName);
        }

        private static string EventName(Type type)
        {
            if (type == typeof(LaCleonEventPlugIn))
            {
                return "LaCleon";
            }

            return EventPanelNames.Get(type)?.Name ?? type.Name;
        }

        private Player? FindPlayer(string? characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
            {
                return null;
            }

            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                var player = server.Context.GetPlayerByCharacterName(characterName);
                if (player?.SelectedCharacter is { } character
                    && character.Name.Equals(characterName, StringComparison.OrdinalIgnoreCase))
                {
                    return player;
                }
            }

            return null;
        }

        /// <summary>
        /// A request which only carries an id.
        /// </summary>
        public class IdRequest
        {
            /// <summary>
            /// Gets or sets the identifier.
            /// </summary>
            public Guid Id { get; set; }
        }

        /// <summary>
        /// The data of a spawn area to create or change.
        /// </summary>
        public class SpawnRequest
        {
            /// <summary>
            /// Gets or sets the id of the spawn area to change; empty to create a new one.
            /// </summary>
            public Guid? Id { get; set; }

            /// <summary>
            /// Gets or sets the id of the map; only used when creating.
            /// </summary>
            public Guid MapId { get; set; }

            /// <summary>
            /// Gets or sets the id of the monster definition.
            /// </summary>
            public Guid MonsterId { get; set; }

            /// <summary>
            /// Gets or sets the first x coordinate.
            /// </summary>
            public byte X1 { get; set; }

            /// <summary>
            /// Gets or sets the first y coordinate.
            /// </summary>
            public byte Y1 { get; set; }

            /// <summary>
            /// Gets or sets the second x coordinate.
            /// </summary>
            public byte X2 { get; set; }

            /// <summary>
            /// Gets or sets the second y coordinate.
            /// </summary>
            public byte Y2 { get; set; }

            /// <summary>
            /// Gets or sets the number of monsters.
            /// </summary>
            public short Quantity { get; set; } = 1;

            /// <summary>
            /// Gets or sets the direction; when not set, it stays as it is.
            /// </summary>
            public int? Direction { get; set; }
        }

        /// <summary>
        /// The complete list of items which a merchant sells.
        /// </summary>
        public class MerchantItemsRequest
        {
            /// <summary>
            /// Gets or sets the id of the merchant, which is a monster definition.
            /// </summary>
            public Guid Id { get; set; }

            /// <summary>
            /// Gets or sets the items.
            /// </summary>
            public List<MerchantItemRequest>? Items { get; set; }
        }

        /// <summary>
        /// One item of a merchant store.
        /// </summary>
        public class MerchantItemRequest
        {
            /// <summary>
            /// Gets or sets the id of the item definition.
            /// </summary>
            public Guid DefinitionId { get; set; }

            /// <summary>
            /// Gets or sets the slot in the store.
            /// </summary>
            public byte Slot { get; set; }

            /// <summary>
            /// Gets or sets the item level.
            /// </summary>
            public int Level { get; set; }

            /// <summary>
            /// Gets or sets the durability; 0 means the durability of the definition.
            /// </summary>
            public double Durability { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the item has its skill.
            /// </summary>
            public bool Skill { get; set; }

            /// <summary>
            /// Gets or sets the options of the item.
            /// </summary>
            public List<ItemOptionRequest>? Options { get; set; }
        }

        /// <summary>
        /// The values of a connected player to change. Unset values stay as they are.
        /// </summary>
        public class PlayerStatsRequest
        {
            /// <summary>
            /// Gets or sets the character name.
            /// </summary>
            public string? Name { get; set; }

            /// <summary>
            /// Gets or sets the level.
            /// </summary>
            public int? Level { get; set; }

            /// <summary>
            /// Gets or sets the resets.
            /// </summary>
            public int? Resets { get; set; }

            /// <summary>
            /// Gets or sets the base strength.
            /// </summary>
            public int? Strength { get; set; }

            /// <summary>
            /// Gets or sets the base agility.
            /// </summary>
            public int? Agility { get; set; }

            /// <summary>
            /// Gets or sets the base vitality.
            /// </summary>
            public int? Vitality { get; set; }

            /// <summary>
            /// Gets or sets the base energy.
            /// </summary>
            public int? Energy { get; set; }

            /// <summary>
            /// Gets or sets the base leadership.
            /// </summary>
            public int? Leadership { get; set; }

            /// <summary>
            /// Gets or sets the level-up points.
            /// </summary>
            public int? Points { get; set; }

            /// <summary>
            /// Gets or sets the money of the inventory.
            /// </summary>
            public int? Money { get; set; }
        }

        /// <summary>
        /// The item to give to a connected player.
        /// </summary>
        public class GiveItemRequest
        {
            /// <summary>
            /// Gets or sets the character name.
            /// </summary>
            public string? Name { get; set; }

            /// <summary>
            /// Gets or sets the id of the item definition.
            /// </summary>
            public Guid DefinitionId { get; set; }

            /// <summary>
            /// Gets or sets the item level.
            /// </summary>
            public int Level { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the item has its skill.
            /// </summary>
            public bool Skill { get; set; }

            /// <summary>
            /// Gets or sets how many items to give.
            /// </summary>
            public int Count { get; set; } = 1;

            /// <summary>
            /// Gets or sets the options of the item.
            /// </summary>
            public List<ItemOptionRequest>? Options { get; set; }
        }

        /// <summary>
        /// An option of an item to give.
        /// </summary>
        public class ItemOptionRequest
        {
            /// <summary>
            /// Gets or sets the id of the option.
            /// </summary>
            public Guid Id { get; set; }

            /// <summary>
            /// Gets or sets the level of the option.
            /// </summary>
            public int Level { get; set; } = 1;
        }

        /// <summary>
        /// The values of a monster to change.
        /// </summary>
        public class MonsterRequest
        {
            /// <summary>
            /// Gets or sets the id of the monster definition.
            /// </summary>
            public Guid Id { get; set; }

            /// <summary>
            /// Gets or sets the respawn time in seconds; when not set, it stays as it is.
            /// </summary>
            public int? RespawnSeconds { get; set; }

            /// <summary>
            /// Gets or sets the attributes to change.
            /// </summary>
            public List<MonsterAttributeRequest>? Attributes { get; set; }
        }

        /// <summary>
        /// One attribute of a monster.
        /// </summary>
        public class MonsterAttributeRequest
        {
            /// <summary>
            /// Gets or sets the designation of the attribute, e.g. "Level".
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the value.
            /// </summary>
            public float Value { get; set; }
        }

        /// <summary>
        /// Switches an event on or off.
        /// </summary>
        public class EventActiveRequest
        {
            /// <summary>
            /// Gets or sets the id of the plugin type of the event.
            /// </summary>
            public Guid Id { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the event should be active.
            /// </summary>
            public bool Active { get; set; }
        }

        /// <summary>
        /// The new values of an item of a connected player.
        /// </summary>
        public class ReplaceItemRequest
        {
            /// <summary>
            /// Gets or sets the character name.
            /// </summary>
            public string? Name { get; set; }

            /// <summary>
            /// Gets or sets the inventory slot of the item.
            /// </summary>
            public byte Slot { get; set; }

            /// <summary>
            /// Gets or sets the item level.
            /// </summary>
            public int Level { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the item has its skill.
            /// </summary>
            public bool Skill { get; set; }

            /// <summary>
            /// Gets or sets the complete list of options which the item should have.
            /// </summary>
            public List<ItemOptionRequest>? Options { get; set; }
        }

        /// <summary>
        /// The item to remove from a connected player.
        /// </summary>
        public class RemoveItemRequest
        {
            /// <summary>
            /// Gets or sets the character name.
            /// </summary>
            public string? Name { get; set; }

            /// <summary>
            /// Gets or sets the inventory slot of the item.
            /// </summary>
            public byte Slot { get; set; }
        }
    }
}
