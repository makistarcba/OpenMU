// <copyright file="EditorBridgeConfigController.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.API
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using MUnique.OpenMU.AttributeSystem;
    using MUnique.OpenMU.DataModel;
    using MUnique.OpenMU.DataModel.Configuration;
    using MUnique.OpenMU.GameLogic;
    using MUnique.OpenMU.GameLogic.Views.Character;
    using MUnique.OpenMU.GameServer;
    using MUnique.OpenMU.Interfaces;
    using MUnique.OpenMU.Persistence;
    using MUnique.OpenMU.Persistence.AdminAuth;
    using MUnique.OpenMU.PlugIns;
    using MUnique.OpenMU.Web.AdminPanel.Auth;
    using MUnique.OpenMU.Web.AdminPanel.Operations;

    /// <summary>
    /// Vorthania: la parte del puente del MuEditor que cambia CONFIGURACION (puente v6).
    /// </summary>
    /// <remarks>
    /// No inventa un camino propio: usa las mismas operaciones que el panel de OpenMU
    /// (<see cref="ConfigurationWriteOperations"/> y <see cref="PluginWriteOperations"/>). Asi el editor y el
    /// panel guardan por el mismo lugar, quedan en el historial del panel y el juego se entera igual que
    /// cuando se guarda desde el panel.
    ///
    /// Esas operaciones piden rol Administrator: la API key del editor tiene que tener ese rol para esta parte.
    ///
    /// Lo que se ve al instante y lo que no:
    ///  - Ajustes generales y la experiencia por resets: al instante.
    ///  - Valores base de una clase (dano hecho x, dano recibido x, vida y mana base): al instante, tambien
    ///    para los personajes conectados (se les vuelve a enchufar el valor en su sistema de atributos).
    ///  - Formulas de una clase (Fuerza / 6, etc.): cuando el personaje vuelve a entrar. El sistema de
    ///    atributos de un personaje conectado se arma al entrar y copia esos numeros.
    /// </remarks>
    [Route("api/editor/")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.ApiSchemes, Policy = AdminPolicies.Operator)]
    public class EditorBridgeConfigController : Controller
    {
        private readonly IDictionary<int, IGameServer> _gameServers;
        private readonly IDataSource<GameConfiguration> _configDataSource;
        private readonly IPersistenceContextProvider _persistence;
        private readonly ConfigurationWriteOperations _configWrites;
        private readonly PluginWriteOperations _pluginWrites;
        private readonly AdminOperationExecutor _executor;
        private readonly ILogger<EditorBridgeConfigController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="EditorBridgeConfigController"/> class.
        /// </summary>
        /// <param name="gameServers">The game servers.</param>
        /// <param name="configDataSource">The configuration data source of the admin panel.</param>
        /// <param name="persistence">The persistence context provider.</param>
        /// <param name="configWrites">The configuration write operations of the admin panel.</param>
        /// <param name="pluginWrites">The plugin write operations of the admin panel.</param>
        /// <param name="executor">The audited executor of the admin panel.</param>
        /// <param name="logger">The logger.</param>
        public EditorBridgeConfigController(
            IDictionary<int, IGameServer> gameServers,
            IDataSource<GameConfiguration> configDataSource,
            IPersistenceContextProvider persistence,
            ConfigurationWriteOperations configWrites,
            PluginWriteOperations pluginWrites,
            AdminOperationExecutor executor,
            ILogger<EditorBridgeConfigController> logger)
        {
            this._gameServers = gameServers;
            this._configDataSource = configDataSource;
            this._persistence = persistence;
            this._configWrites = configWrites;
            this._pluginWrites = pluginWrites;
            this._executor = executor;
            this._logger = logger;
        }

        /// <summary>
        /// Changes fields of one configuration object (GameConfiguration, AttributeRelationship, Skill, ...).
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The before and after values.</returns>
        [HttpPost]
        [Route("config/update")]
        public async Task<IActionResult> UpdateConfigurationAsync([FromBody] ConfigUpdateRequest request, CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Type) || request.Patch.ValueKind != JsonValueKind.Object)
            {
                return this.BadRequest("Falta el tipo, el id o los campos a cambiar.");
            }

            try
            {
                var patch = request.Patch.GetRawText();
                var preview = await this._configWrites.UpdateAsync(request.Type, request.Id, patch, false, null, cancellationToken).ConfigureAwait(false);
                var token = preview["confirmationToken"]?.GetValue<string>();
                var result = await this._configWrites.UpdateAsync(request.Type, request.Id, patch, true, token, cancellationToken).ConfigureAwait(false);
                this._logger.LogWarning("[EDITOR BRIDGE] configuracion {Type} {Id}: {Patch}", request.Type, request.Id, patch);
                return this.Ok(new JsonObject { ["saved"] = true, ["change"] = result["change"]?.DeepClone() });
            }
            catch (Exception ex)
            {
                return this.Fail(ex, "no se pudo cambiar la configuracion " + request.Type);
            }
        }

        /// <summary>
        /// Changes a base value of one or more character classes (e.g. the damage multiplier) and applies
        /// it to the connected characters of those classes.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>How many classes were changed and how many connected characters got it.</returns>
        [HttpPost]
        [Route("class/base-value")]
        public async Task<IActionResult> SetClassBaseValueAsync([FromBody] ClassBaseValueRequest request, CancellationToken cancellationToken)
        {
            if (request is null || request.ClassIds is not { Count: > 0 } || !float.IsFinite(request.Value))
            {
                return this.BadRequest("Faltan las clases o el valor.");
            }

            try
            {
                var result = await this._executor.ExecuteAsync(
                    AdminRoles.Administrator,
                    "editor_class_base_value",
                    new { request.ClassIds, request.DefinitionId, request.Value },
                    _ => this.ApplyClassBaseValueAsync(request, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                return this.Ok(result);
            }
            catch (Exception ex)
            {
                return this.Fail(ex, "no se pudo cambiar el valor base de la clase");
            }
        }

        /// <summary>
        /// Gets the custom configuration of a plugin as plain JSON (without the reference ids of OpenMU).
        /// </summary>
        /// <param name="id">The plugin type id.</param>
        /// <returns>The configuration.</returns>
        [HttpGet]
        [Route("plugin/config")]
        public async Task<IActionResult> GetPlugInConfigurationAsync([FromQuery] Guid id)
        {
            try
            {
                var type = this.FindPlugInType(id);
                if (type?.GetCustomConfigurationType() is not { } configType)
                {
                    return this.NotFound("Ese plugin no existe o no tiene configuracion propia.");
                }

                var config = await this._configDataSource.GetOwnerAsync().ConfigureAwait(false);
                var persisted = config.PlugInConfigurations.FirstOrDefault(c => c.TypeId == id);
                var value = persisted?.GetConfiguration(configType, new ByDataSourceReferenceHandler(this._configDataSource))
                            ?? Activator.CreateInstance(configType);
                return this.Ok(new
                {
                    active = persisted?.IsActive ?? false,
                    json = JsonSerializer.Serialize(value, configType),
                });
            }
            catch (Exception ex)
            {
                return this.Fail(ex, "no se pudo leer la configuracion del plugin " + id);
            }
        }

        /// <summary>
        /// Replaces the custom configuration of a plugin, the same way the graphical editor of the panel does.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Whether it was saved.</returns>
        [HttpPost]
        [Route("plugin/config")]
        public async Task<IActionResult> SetPlugInConfigurationAsync([FromBody] PlugInConfigRequest request, CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Json))
            {
                return this.BadRequest("Falta la configuracion.");
            }

            try
            {
                var type = this.FindPlugInType(request.Id);
                if (type?.GetCustomConfigurationType() is not { } configType)
                {
                    return this.NotFound("Ese plugin no existe o no tiene configuracion propia.");
                }

                var model = JsonSerializer.Deserialize(request.Json, configType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                            ?? throw new JsonException("La configuracion vino vacia.");
                var serializer = new PlugInConfiguration();
                serializer.SetConfiguration(model, new ByDataSourceReferenceHandler(this._configDataSource));
                var json = serializer.CustomConfiguration ?? "{}";

                var preview = await this._pluginWrites.ReplaceFromPanelAsync(request.Id, model, json, false, null, cancellationToken).ConfigureAwait(false);
                var token = preview["confirmationToken"]?.GetValue<string>();
                await this._pluginWrites.ReplaceFromPanelAsync(request.Id, model, json, true, token, cancellationToken).ConfigureAwait(false);

                // Por si algun servidor tiene otra copia de la configuracion: que el plugin que esta
                // corriendo reciba los numeros nuevos ya.
                foreach (var server in this._gameServers.Values.OfType<GameServer>())
                {
                    if (server.Context.Configuration.PlugInConfigurations.FirstOrDefault(c => c.TypeId == request.Id) is { } inGame)
                    {
                        if (inGame.CustomConfiguration != json)
                        {
                            inGame.CustomConfiguration = json;
                        }

                        server.Context.PlugInManager.ConfigurePlugIn(request.Id, inGame);
                    }
                }

                this._logger.LogWarning("[EDITOR BRIDGE] plugin {Name} configurado", type.Name);
                return this.Ok(new { saved = true });
            }
            catch (JsonException ex)
            {
                return this.BadRequest("La configuracion no tiene la forma que espera el plugin: " + ex.Message);
            }
            catch (Exception ex)
            {
                return this.Fail(ex, "no se pudo guardar la configuracion del plugin " + request.Id);
            }
        }

        /// <summary>
        /// Gets the connected characters with their class, to tell who will see a class change.
        /// </summary>
        /// <returns>The connected characters.</returns>
        [HttpGet]
        [Route("online/classes")]
        public async Task<IActionResult> GetOnlineClassesAsync()
        {
            var result = new List<object>();
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                foreach (var player in await server.Context.GetPlayersAsync().ConfigureAwait(false))
                {
                    if (player.SelectedCharacter is { CharacterClass: { } characterClass } character)
                    {
                        result.Add(new { name = character.Name, classId = characterClass.GetId(), classNumber = characterClass.Number });
                    }
                }
            }

            return this.Ok(result);
        }

        private async ValueTask<JsonObject> ApplyClassBaseValueAsync(ClassBaseValueRequest request, CancellationToken cancellationToken)
        {
            var changed = new JsonArray();
            using (var context = this._persistence.CreateNewContext())
            {
                if (context is IConfigurationEditContext editContext)
                {
                    await editContext.BeginConfigurationEditAsync(cancellationToken).ConfigureAwait(false);
                }

                var ownerId = (await this._configDataSource.GetOwnerAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).GetId();
                var config = await context.GetByIdAsync<GameConfiguration>(ownerId, cancellationToken).ConfigureAwait(false)
                    ?? throw new AdminOperationException("No se pudo cargar la configuracion.");
                foreach (var classId in request.ClassIds!)
                {
                    var characterClass = config.CharacterClasses.FirstOrDefault(c => c.GetId() == classId)
                        ?? throw new AdminOperationException("No existe esa clase.", AdminOperationErrorKind.InvalidInput);
                    var matches = characterClass.BaseAttributeValues
                        .Where(v => v.Definition?.Id == request.DefinitionId && v.AggregateType == AggregateType.AddRaw)
                        .ToList();
                    if (matches.Count != 1 || matches[0] is not IAssignable assignable)
                    {
                        throw new AdminOperationException(
                            $"{characterClass.Name} no tiene exactamente un valor base de ese tipo. Hay que crearlo desde el panel.",
                            AdminOperationErrorKind.InvalidInput);
                    }

                    var before = matches[0].Value;
                    assignable.AssignValuesOf(new ConstValueAttribute(request.Value, matches[0].Definition, matches[0].AggregateType), config);
                    changed.Add(new JsonObject { ["class"] = (string)characterClass.Name, ["id"] = matches[0].GetId().ToString(), ["before"] = before, ["after"] = request.Value });
                }

                if (!await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new AdminOperationException("No se pudo guardar.");
                }
            }

            await this._configDataSource.InvalidateAsync().ConfigureAwait(false);
            var players = await this.RefreshConnectedAsync(request).ConfigureAwait(false);
            this._logger.LogWarning("[EDITOR BRIDGE] valor base {Definition} = {Value} en {Count} clase(s), {Players} conectado(s) actualizado(s)", request.DefinitionId, request.Value, changed.Count, players);
            return new JsonObject { ["saved"] = true, ["changed"] = changed, ["playersUpdated"] = players };
        }

        private async ValueTask<int> RefreshConnectedAsync(ClassBaseValueRequest request)
        {
            var updated = 0;
            foreach (var server in this._gameServers.Values.OfType<GameServer>())
            {
                // La copia de la configuracion que usa el juego: si el aviso de cambio no la actualizo, se
                // actualiza a mano, asi los que entren despues tambien tienen el numero nuevo.
                var gameClasses = server.Context.Configuration.CharacterClasses.Where(c => request.ClassIds!.Contains(c.GetId())).ToList();
                foreach (var gameClass in gameClasses)
                {
                    foreach (var value in gameClass.BaseAttributeValues.Where(v => v.Definition?.Id == request.DefinitionId && v.AggregateType == AggregateType.AddRaw).ToList())
                    {
                        if (Math.Abs(value.Value - request.Value) > 0.00001f && value is IAssignable assignable)
                        {
                            assignable.AssignValuesOf(new ConstValueAttribute(request.Value, value.Definition, value.AggregateType), server.Context.Configuration);
                        }
                    }
                }

                foreach (var player in await server.Context.GetPlayersAsync().ConfigureAwait(false))
                {
                    if (player.SelectedCharacter?.CharacterClass is not { } playerClass
                        || player.Attributes is not { } attributes
                        || !request.ClassIds!.Contains(playerClass.GetId()))
                    {
                        continue;
                    }

                    try
                    {
                        // El elemento del personaje es la misma instancia que el valor de la clase: sacarlo y
                        // volverlo a poner hace que el atributo se recalcule con el numero nuevo.
                        foreach (var value in playerClass.BaseAttributeValues.Where(v => v.Definition?.Id == request.DefinitionId && v.AggregateType == AggregateType.AddRaw).ToList())
                        {
                            attributes.RemoveElement(value, value.Definition);
                            attributes.AddElement(value, value.Definition);
                        }

                        await player.InvokeViewPlugInAsync<IUpdateCharacterStatsPlugIn>(p => p.UpdateCharacterStatsAsync()).ConfigureAwait(false);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, "[EDITOR BRIDGE] no se pudo actualizar a {Character}; lo ve al volver a entrar", player.SelectedCharacter?.Name);
                    }
                }
            }

            return updated;
        }

        private Type? FindPlugInType(Guid id)
        {
            return this._gameServers.Values.OfType<GameServer>().FirstOrDefault()?.Context.PlugInManager.KnownPlugInTypes.FirstOrDefault(t => t.GUID == id);
        }

        private IActionResult Fail(Exception ex, string what)
        {
            this._logger.LogError(ex, "[EDITOR BRIDGE] {What}", what);
            if (ex is AdminOperationException adminException)
            {
                var status = adminException.Kind switch
                {
                    AdminOperationErrorKind.Forbidden => 403,
                    AdminOperationErrorKind.InvalidInput => 400,
                    _ => 409,
                };
                var message = status == 403
                    ? "La API key del editor no tiene rol Administrator: el panel no la deja cambiar configuracion. (" + adminException.Message + ")"
                    : adminException.Message;
                return this.StatusCode(status, message);
            }

            return this.StatusCode(500, $"{ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        /// A change of fields of one configuration object.
        /// </summary>
        public class ConfigUpdateRequest
        {
            /// <summary>Gets or sets the configuration type name, e.g. GameConfiguration.</summary>
            public string? Type { get; set; }

            /// <summary>Gets or sets the id of the object.</summary>
            public Guid Id { get; set; }

            /// <summary>Gets or sets the fields to change (JSON merge patch).</summary>
            public JsonElement Patch { get; set; }
        }

        /// <summary>
        /// A change of a base value of character classes.
        /// </summary>
        public class ClassBaseValueRequest
        {
            /// <summary>Gets or sets the ids of the classes.</summary>
            public List<Guid>? ClassIds { get; set; }

            /// <summary>Gets or sets the id of the attribute definition.</summary>
            public Guid DefinitionId { get; set; }

            /// <summary>Gets or sets the new value.</summary>
            public float Value { get; set; }
        }

        /// <summary>
        /// A new custom configuration of a plugin.
        /// </summary>
        public class PlugInConfigRequest
        {
            /// <summary>Gets or sets the plugin type id.</summary>
            public Guid Id { get; set; }

            /// <summary>Gets or sets the configuration as plain JSON.</summary>
            public string? Json { get; set; }
        }
    }
}
