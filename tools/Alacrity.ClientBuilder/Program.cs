using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static partial class Program
{
    private static int Main(string[] args) => ClientBuilderCommandLine.Run(args);

    internal static DefaultAssemblyResolver CreateResolver(string executablePath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(executablePath)!);
        AddXnaSearchDirectories(resolver);
        return resolver;
    }

    private static void AddXnaSearchDirectories(DefaultAssemblyResolver resolver)
    {
        var xnaRoot = Environment.GetEnvironmentVariable("ALACRITY_XNA_REFERENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(xnaRoot))
        {
            xnaRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Microsoft.NET",
                "assembly",
                "GAC_32");
        }

        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Game", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Graphics", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Xact", "v4.0_4.0.0.0__842cf8be1de50553"));
        AddIfExists(resolver, Path.Combine(xnaRoot, "Microsoft.Xna.Framework.Content.Pipeline", "v4.0_4.0.0.0__842cf8be1de50553"));
    }

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;
        foreach (var nested in type.NestedTypes)
            foreach (var child in Flatten(nested))
                yield return child;
    }

    private static void AddIfExists(DefaultAssemblyResolver resolver, string path)
    {
        if (Directory.Exists(path))
            resolver.AddSearchDirectory(path);
    }

    private static bool IsInterestingType(TypeDefinition type)
    {
        var name = type.FullName.ToLowerInvariant();
        return name.Contains("chat") ||
               name.Contains("textbox") ||
               name.Contains("input") ||
               name.Contains("ime") ||
                name.Contains("textsnippet") ||
                name.Contains("command") ||
                name == "terraria.main";
    }

    private static bool IsInterestingMethod(MethodDefinition method)
    {
        var name = method.Name.ToLowerInvariant();
        return name.Contains("chat") ||
               name.Contains("text") ||
               name.Contains("input") ||
               name.Contains("key") ||
               name.Contains("draw") ||
               name.Contains("update") ||
               name.Contains("parse") ||
               name.Contains("submit") ||
               name.Contains("click");
    }

    private static void DumpFields(ModuleDefinition module, string typeName)
    {
        var type = module.Types.SelectMany(Flatten).FirstOrDefault(t => t.FullName == typeName);
        if (type == null)
            throw new InvalidOperationException($"Type not found: {typeName}");

        foreach (var field in type.Fields.OrderBy(f => f.Name))
            Console.WriteLine($"{field.FieldType.FullName} {field.Name}");
    }

    private static void DumpMethods(ModuleDefinition module, string typeName)
    {
        var type = module.Types.SelectMany(Flatten).FirstOrDefault(t => t.FullName == typeName);
        if (type == null)
            throw new InvalidOperationException($"Type not found: {typeName}");

        foreach (var method in type.Methods.OrderBy(m => m.Name))
            Console.WriteLine($"{method.ReturnType.FullName} {method.Name}({string.Join(", ", method.Parameters.Select(p => p.ParameterType.FullName + " " + p.Name))})");
    }

    private static void DumpReferences(ModuleDefinition module)
    {
        foreach (var reference in module.AssemblyReferences.OrderBy(r => r.Name))
            Console.WriteLine($"{reference.Name}, Version={reference.Version}, PublicKeyToken={BitConverter.ToString(reference.PublicKeyToken ?? Array.Empty<byte>()).Replace("-", "").ToLowerInvariant()}");
    }

    private static void PatchTerraria(ModuleDefinition module, string exePath)
    {
        if (module.Assembly.Name.Version?.ToString() != "1.4.5.6")
            throw new InvalidOperationException($"Expected Terraria 1.4.5.6, got {module.Assembly.Name.Version}.");

        var helperPath = Path.Combine(Path.GetDirectoryName(exePath)!, "VanillaChatEnhancer.dll");
        if (!File.Exists(helperPath))
            throw new InvalidOperationException($"Missing helper DLL beside Terraria.exe: {helperPath}");

        using var helper = ModuleDefinition.ReadModule(helperPath);
        var inputType = helper.Types.First(t => t.FullName == "VanillaChatEnhancer.ChatInput");
        var linksType = helper.Types.First(t => t.FullName == "VanillaChatEnhancer.Links");
        var chatFeaturesType = helper.Types.First(t => t.FullName == "VanillaChatEnhancer.ChatFeatures");
        var performanceType = helper.Types.First(t => t.FullName == "VanillaChatEnhancer.Performance");
        var startupTweaksType = helper.Types.First(t => t.FullName == "VanillaChatEnhancer.StartupTweaks");
        var processInput = module.ImportReference(inputType.Methods.First(m => m.Name == "Process"));
        var withCursor = module.ImportReference(inputType.Methods.First(m => m.Name == "WithCursor"));
        var openIfUrl = module.ImportReference(linksType.Methods.First(m => m.Name == "OpenIfUrl"));
        var hoverSnippet = module.ImportReference(linksType.Methods.First(m => m.Name == "HoverSnippet"));
        var highlightMessageColor = module.ImportReference(linksType.Methods.First(m => m.Name == "HighlightMessageColor"));
        var linkify = module.ImportReference(linksType.Methods.First(m => m.Name == "Linkify"));
        var applyOutgoingPrefix = module.ImportReference(chatFeaturesType.Methods.First(m => m.Name == "ApplyOutgoingPrefix"));
        var shouldRenderDust = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldRenderDust"));
        var shouldSimulateDust = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldSimulateDust"));
        var shouldCreateDust = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldCreateDust"));
        var shouldRenderGore = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldRenderGore"));
        var shouldSimulateGore = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldSimulateGore"));
        var shouldRenderCombatTextInstance = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldRenderCombatTextInstance"));
        var shouldRenderServerPopups = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldRenderServerPopups"));
        var drawEnhancerSettings = module.ImportReference(performanceType.Methods.First(m => m.Name == "DrawEnhancerSettings"));
        var drawPlayerList = module.ImportReference(performanceType.Methods.First(m => m.Name == "DrawPlayerList"));
        var prepareServerBrowserMenu = module.ImportReference(performanceType.Methods.First(m => m.Name == "PrepareServerBrowserMenu"));
        var drawServerBrowser = module.ImportReference(performanceType.Methods.First(m => m.Name == "DrawServerBrowser"));
        var drawHeldItemsAsEmotes = module.ImportReference(performanceType.Methods.First(m => m.Name == "DrawHeldItemsAsEmotes"));
        var shouldDrawEmoteBubble = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawEmoteBubble"));
        var shouldForcePlayerPreviewFullbright = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldForcePlayerPreviewFullbright"));
        var shouldDrawPlayer = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawPlayer"));
        var shouldDrawWorldItem = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawWorldItem"));
        var shouldDrawProjectile = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawProjectile"));
        var shouldDrawProjectileObject = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawProjectileObject"));
        var shouldDrawDustInstance = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawDustInstance"));
        var shouldDrawGrassSpecial = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawGrassSpecial"));
        var shouldDrawVineSpecial = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawVineSpecial"));
        var tryDrawBlackOptimized = module.ImportReference(performanceType.Methods.First(m => m.Name == "TryDrawBlackOptimized"));
        var shouldDrawPlayerProjectileVisuals = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldDrawPlayerProjectileVisuals"));
        var suppressVanillaInputWhenUnfocused = module.ImportReference(performanceType.Methods.First(m => m.Name == "SuppressVanillaInputWhenUnfocused"));
        var throttleInactiveFrame = module.ImportReference(performanceType.Methods.First(m => m.Name == "ThrottleInactiveFrame"));
        var checkAudioDeviceChange = module.ImportReference(performanceType.Methods.First(m => m.Name == "CheckAudioDeviceChange"));
        var shouldSyncDashKeybind = module.ImportReference(performanceType.Methods.First(m => m.Name == "ShouldSyncDashKeybind"));
        var observeDrawnUiElement = module.ImportReference(performanceType.Methods.First(m => m.Name == "ObserveDrawnUiElement"));
        var applyStartupTweaks = module.ImportReference(startupTweaksType.Methods.First(m => m.Name == "ApplyEarly"));

        var mainType = module.Types.First(t => t.FullName == "Terraria.Main");
        var programType = module.Types.First(t => t.FullName == "Terraria.Program");
        var dustType = module.Types.First(t => t.FullName == "Terraria.Dust");
        var goreType = module.Types.First(t => t.FullName == "Terraria.Gore");
        var ingameOptionsType = module.Types.First(t => t.FullName == "Terraria.IngameOptions");
        var uiElementType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.UI.UIElement");
        var textSnippetType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.UI.Chat.TextSnippet");

        PatchStartupTweaks(programType, applyStartupTweaks);
        PatchGetInputText(mainType, processInput);
        PatchDoUpdateHandleChat(mainType, applyOutgoingPrefix);
        PatchDrawPlayerChat(mainType, withCursor);
        PatchTextSnippetClick(textSnippetType, openIfUrl);
        PatchTextSnippetHover(textSnippetType, hoverSnippet);
        PatchTextSnippetGetVisibleColor(textSnippetType, highlightMessageColor);
        PatchParseMessage(module, linkify);
        PatchDrawDust(mainType, shouldRenderDust);
        PatchGoreRendering(mainType, shouldRenderGore);
        PatchDustSimulation(dustType, shouldSimulateDust, shouldCreateDust);
        PatchGoreSimulation(goreType, shouldSimulateGore);
        PatchDamageNumberRendering(module, mainType, shouldRenderCombatTextInstance, shouldRenderServerPopups);
        PatchIngameOptionsDraw(ingameOptionsType, drawEnhancerSettings);
        PatchUiElementInspector(uiElementType, observeDrawnUiElement);
        PatchPlayerListOverlay(mainType, drawPlayerList);
        PatchServerBrowser(mainType, prepareServerBrowserMenu, drawServerBrowser);
        PatchHeldItemsEmoteLayer(module, mainType, drawHeldItemsAsEmotes, shouldDrawEmoteBubble);
        PatchPlayerPreviewFullbright(module, shouldForcePlayerPreviewFullbright);
        PatchEntityDrawCulling(module, mainType, shouldDrawWorldItem, shouldDrawDustInstance);
        PatchSpecialTileDrawCulling(module, shouldDrawGrassSpecial, shouldDrawVineSpecial);
        PatchOptimizedDrawBlack(mainType, tryDrawBlackOptimized);
        PatchHiddenPlayerRendering(module, mainType, shouldDrawPlayer, shouldDrawProjectile, shouldDrawProjectileObject, shouldDrawPlayerProjectileVisuals);
        PatchVanillaInputFocusGuard(mainType, suppressVanillaInputWhenUnfocused);
        PatchInactiveFrameThrottle(mainType, throttleInactiveFrame);
        PatchAudioDeviceWatcher(mainType, checkAudioDeviceChange);
        PatchDashKeybindPlayerControlSync(module, mainType, shouldSyncDashKeybind);

        var outputPath = Path.Combine(Path.GetDirectoryName(exePath)!, "Terraria.ChatEnhanced.exe");
        module.Write(outputPath);
        Console.WriteLine($"Wrote {outputPath}");
    }

    private static void PatchPluginUiDemo(ModuleDefinition module, string exePath, bool includeBetterChat = false)
    {
        ApplyPluginUiPatches(module, exePath, includeBetterChat);

        var outputPath = Path.Combine(Path.GetDirectoryName(exePath)!, "Alacrity.exe");
        module.Write(outputPath);
        Console.WriteLine($"Wrote {outputPath}");
    }

    /// <summary>
    /// Applies the audited permanent 1.4.5.6 transformation set without choosing an output path.
    /// The authoritative client builder owns staging, validation, and publication of the result.
    /// </summary>
    internal static void ApplyPermanentAlacrityPatches(ModuleDefinition module, string sourceExecutablePath)
    {
        ApplyPermanentStartupAndMenu(module, sourceExecutablePath);
        ApplyPermanentInputAndKeybinds(module, sourceExecutablePath);
        ApplyPermanentRenderingAndCombat(module, sourceExecutablePath);
        ApplyPermanentVisualEffects(module, sourceExecutablePath);
        ApplyPermanentChatInputAndCommands(module, sourceExecutablePath);
        ApplyPermanentChatDisplayAndInteraction(module, sourceExecutablePath);
    }

    internal static void ApplyPermanentStartupAndMenu(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        var ingameOptionsType = CecilPatchPrimitives.RequireType(module, "Terraria.IngameOptions");
        PatchTerrariaVersionLabels(mainType);
        PatchPluginMenuEntry(module, mainType, ImportRuntimeMethod(module, sourceExecutablePath, "OpenPluginManager", "System.Void"));
        PatchIngamePluginSettings(
            module,
            ingameOptionsType,
            ImportRuntimeMethod(module, sourceExecutablePath, "OpenIngamePluginSettings", "System.Void"),
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawIngamePluginSettings", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        PatchAlacrityVersionDraw(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "DrawAlacrityVersion", "System.Void", "Microsoft.Xna.Framework.Color", "System.Single", "System.String"),
            ReadAlacrityVersion(sourceExecutablePath));
    }

    internal static void ApplyPermanentInputAndKeybinds(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        PatchPluginDemoInput(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleInput", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "UpdatePluginKeybinds", "System.Void"));
        PatchPluginKeybindStateShape(module, ImportRuntimeMethod(module, sourceExecutablePath, "EnsurePluginKeybindStateShape", "System.Void"));
        PatchPluginKeybindControls(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "AppendPluginKeybindControls", "System.Void", "Terraria.GameContent.UI.States.UIManageControls"));
    }

    internal static void ApplyPermanentRenderingAndCombat(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        PatchPluginRuntimeDraw(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "DrawNotifications", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        PatchHitboxWorldOverlay(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "DrawHitboxes", "System.Void", "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        PatchSwingHitboxCapture(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "CaptureSwingHitbox", "System.Void", "Terraria.Player", "System.Boolean", "Microsoft.Xna.Framework.Rectangle"));
    }

    internal static void ApplyPermanentVisualEffects(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        PatchVisualEffects(
            module,
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldRunDustSystem", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldCreateDust", "System.Boolean", "System.Int32"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldUpdateDustInstance", "System.Boolean", "Terraria.Dust"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDrawDustInstance", "System.Boolean", "Terraria.Dust"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldRunGoreSystem", "System.Boolean"));
    }

    internal static void ApplyPermanentChatInputAndCommands(ModuleDefinition module, string sourceExecutablePath)
    {
        var mainType = CecilPatchPrimitives.RequireType(module, "Terraria.Main");
        var programType = CecilPatchPrimitives.RequireType(module, "Terraria.Program");
        PatchBetterChatInput(
            mainType,
            ImportRuntimeMethod(module, sourceExecutablePath, "IsBetterChatActive", "System.Boolean"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ProcessPlayerChatInput", "System.String", "System.String", "System.Boolean"));
        PatchPluginChatCommands(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "TryHandlePluginChatCommand", "System.Boolean", "System.String"));
        PatchBetterChatStartup(programType, ImportRuntimeMethod(module, sourceExecutablePath, "BootstrapPluginRuntime", "System.Void"));
        PatchBetterChatDraw(mainType, ImportRuntimeMethod(module, sourceExecutablePath, "FormatPlayerChatText", "System.String", "System.String"));
    }

    internal static void ApplyPermanentChatDisplayAndInteraction(ModuleDefinition module, string sourceExecutablePath)
    {
        var snippets = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.TextSnippet");
        var chatManager = CecilPatchPrimitives.RequireType(module, "Terraria.UI.Chat.ChatManager");
        PatchBetterChatSnippet(
            snippets,
            chatManager,
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleChatSnippetHover", "System.Void", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "HandleChatSnippetClick", "System.Boolean", "System.Object"),
            ImportRuntimeMethod(module, sourceExecutablePath, "GetChatSnippetVisibleColor", "Microsoft.Xna.Framework.Color", "System.Object", "Microsoft.Xna.Framework.Color"),
            ImportRuntimeMethod(module, sourceExecutablePath, "CopyChatSnippetContext", "System.Void", "System.Object", "System.Object"));
        PatchBetterChatParse(chatManager, ImportRuntimeMethod(module, sourceExecutablePath, "DecorateChatMessage", "System.Object", "System.Object", "Microsoft.Xna.Framework.Color", "System.String"));
        PatchBetterChatVisibility(
            module,
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDisplayNetworkChatMessage", "System.Boolean", "System.Byte"),
            ImportRuntimeMethod(module, sourceExecutablePath, "ShouldDisplayLocalChatMessage", "System.Boolean"));
    }

    private static MethodReference ImportRuntimeMethod(ModuleDefinition module, string sourceExecutablePath, string name, string returnType, params string[] parameterTypes)
    {
        var facadePath = Path.Combine(Path.GetDirectoryName(sourceExecutablePath)!, "bin", "Alacrity.PluginUiRuntime.dll");
        if (!File.Exists(facadePath))
        {
            throw new ClientBuildException("Required staged ABI facade was not found: " + facadePath);
        }

        using var facade = ModuleDefinition.ReadModule(facadePath);
        var bridgeType = CecilPatchPrimitives.RequireType(facade, "AlacrityTerraria.PluginUiRuntime");
        var method = CecilPatchPrimitives.RequireMethod(bridgeType, name, returnType, parameterTypes);
        if (!method.IsPublic || !method.IsStatic || method.GenericParameters.Count != 0)
        {
            throw new ClientBuildException("Required staged bridge method is not a public non-generic static ABI method: " + method.FullName);
        }

        return module.ImportReference(method);
    }

    private static void ApplyPluginUiPatches(ModuleDefinition module, string exePath, bool includeBetterChat)
    {
        if (module.Assembly.Name.Version?.ToString() != "1.4.5.6")
            throw new InvalidOperationException($"Expected Terraria 1.4.5.6, got {module.Assembly.Name.Version}.");
        VerifyTerraria1456ReferenceHash(exePath);

        var helperPath = Path.Combine(Path.GetDirectoryName(exePath)!, "bin", "Alacrity.PluginUiRuntime.dll");
        if (!File.Exists(helperPath))
            throw new InvalidOperationException($"Missing Alacrity plugin UI runtime beside Terraria.exe: {helperPath}");

        var bridgePath = Path.Combine(Path.GetDirectoryName(exePath)!, "bin", "Alacrity.PluginUiCoreBridge.dll");
        if (!File.Exists(bridgePath))
            throw new InvalidOperationException($"Missing lazy Alacrity Core/SDK bridge beside Terraria.exe: {bridgePath}");

        using var helper = ModuleDefinition.ReadModule(helperPath);
        var runtimeType = helper.Types.First(t => t.FullName == "AlacrityTerraria.PluginUiRuntime");
        var openPluginManager = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "OpenPluginManager"));
        var openIngamePluginSettings = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "OpenIngamePluginSettings"));
        var drawIngamePluginSettings = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "DrawIngamePluginSettings"));
        var drawAlacrityVersion = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "DrawAlacrityVersion"));
        var handlePluginDemoInput = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "HandleInput"));
        var updatePluginKeybinds = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "UpdatePluginKeybinds" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0));
        var ensurePluginKeybindStateShape = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "EnsurePluginKeybindStateShape" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0));
        var appendPluginKeybindControls = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "AppendPluginKeybindControls" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Terraria.GameContent.UI.States.UIManageControls"));
        var drawNotifications = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "DrawNotifications" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        var drawHitboxes = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "DrawHitboxes" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch"));
        var captureSwingHitbox = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "CaptureSwingHitbox" && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.FullName == "Terraria.Player" && m.Parameters[1].ParameterType.FullName == "System.Boolean" && m.Parameters[2].ParameterType.FullName == "Microsoft.Xna.Framework.Rectangle"));
        var shouldRunDustSystem = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "ShouldRunDustSystem" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0));
        var shouldCreateDust = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "ShouldCreateDust" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32"));
        var shouldUpdateDustInstance = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "ShouldUpdateDustInstance" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Terraria.Dust"));
        var shouldDrawDustInstance = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "ShouldDrawDustInstance" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "Terraria.Dust"));
        var shouldRunGoreSystem = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "ShouldRunGoreSystem" && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 0));
        var alacrityVersion = ReadAlacrityVersion(exePath);

        var mainType = module.Types.First(t => t.FullName == "Terraria.Main");
        PatchTerrariaVersionLabels(mainType);
        PatchPluginMenuEntry(module, mainType, openPluginManager);
        var ingameOptionsType = module.Types.First(t => t.FullName == "Terraria.IngameOptions");
        PatchIngamePluginSettings(module, ingameOptionsType, openIngamePluginSettings, drawIngamePluginSettings);
        PatchAlacrityVersionDraw(mainType, drawAlacrityVersion, alacrityVersion);
        PatchPluginDemoInput(mainType, handlePluginDemoInput, updatePluginKeybinds);
        PatchPluginKeybindStateShape(module, ensurePluginKeybindStateShape);
        PatchPluginRuntimeDraw(mainType, drawNotifications);
        PatchHitboxWorldOverlay(mainType, drawHitboxes);
        PatchSwingHitboxCapture(module, captureSwingHitbox);
        PatchPluginKeybindControls(module, appendPluginKeybindControls);
        PatchVisualEffects(module, mainType, shouldRunDustSystem, shouldCreateDust, shouldUpdateDustInstance, shouldDrawDustInstance, shouldRunGoreSystem);
        if (includeBetterChat)
            ApplyBetterChatHooks(module, exePath);

    }

    private static void PatchPluginKeybindControls(ModuleDefinition module, MethodReference appendPluginKeybindControls)
    {
        var controls = module.Types.SingleOrDefault(type => type.FullName == "Terraria.GameContent.UI.States.UIManageControls")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UIManageControls type was not found.");
        var initialize = controls.Methods.SingleOrDefault(method => method.Name == "OnInitialize" && !method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UIManageControls.OnInitialize signature did not match the verified keybind-controls hook.");
        var finalReturn = initialize.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 UIManageControls.OnInitialize has no return instruction.");
        var il = initialize.Body.GetILProcessor();
        CecilPatchPrimitives.InsertBefore(
            il,
            finalReturn,
            il.Create(OpCodes.Ldarg_0),
            il.Create(OpCodes.Call, appendPluginKeybindControls));
    }

    private static void VerifyTerraria1456ReferenceHash(string exePath)
    {
        using var stream = File.OpenRead(exePath);
        using var hasher = SHA256.Create();
        string actualHash = Convert.ToHexString(hasher.ComputeHash(stream));
        if (!string.Equals(actualHash, SupportedTerrariaBuildCatalog.Terraria1456Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Terraria.exe hash mismatch. Expected {SupportedTerrariaBuildCatalog.Terraria1456Sha256}, got {actualHash}.");
    }

    private static string ReadAlacrityVersion(string exePath)
    {
        var versionPath = Path.Combine(Path.GetDirectoryName(exePath)!, "VERSION");
        if (!File.Exists(versionPath))
            throw new InvalidOperationException($"Missing Alacrity version file: {versionPath}");

        var version = File.ReadAllText(versionPath).Trim();
        if (!Version.TryParse(version, out _))
            throw new InvalidOperationException($"Alacrity version must be numeric (for example 0.1.0): {versionPath}");

        return "Alacrity v" + version;
    }

    private static void PatchAlacrityVersionDraw(TypeDefinition mainType, MethodReference drawAlacrityVersion, string versionText)
    {
        var drawMenu = mainType.Methods.Single(method => method.Name == "DrawMenu" && method.Parameters.Count == 1);
        var color = drawMenu.Body.Variables[3];
        var verticalOffset = drawMenu.Body.Variables[31];
        if (color.VariableType.FullName != "Microsoft.Xna.Framework.Color" || verticalOffset.VariableType.FullName != "System.Single")
            throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu version display locals did not match the verified layout.");

        var versionDraw = drawMenu.Body.Instructions.SingleOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference reference &&
            reference.FullName == "System.Void Terraria.Main::DrawVersionNumber(Microsoft.Xna.Framework.Color,System.Single)")
            ?? throw new InvalidOperationException("Could not find Terraria's verified version-number draw call.");

        var il = drawMenu.Body.GetILProcessor();
        var insertAfter = versionDraw;
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldloc, color));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldloc, verticalOffset));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldc_R4, 22f));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Add));
        insertAfter = InsertAfter(il, insertAfter, il.Create(OpCodes.Ldstr, versionText));
        InsertAfter(il, insertAfter, il.Create(OpCodes.Call, drawAlacrityVersion));
    }

    private static void PatchTerrariaVersionLabels(TypeDefinition mainType)
    {
        var constructor = mainType.Methods.Single(method => method.Name == ".cctor" && method.IsStatic);
        var labels = new[] { "versionNumber", "versionNumber2" };
        foreach (string fieldName in labels)
        {
            var field = mainType.Fields.Single(candidate => candidate.Name == fieldName && candidate.FieldType.FullName == "System.String");
            var assignment = constructor.Body.Instructions.SingleOrDefault(instruction =>
                instruction.OpCode == OpCodes.Stsfld && instruction.Operand is FieldReference reference && reference.Resolve() == field)
                ?? throw new InvalidOperationException("Could not find Terraria's verified " + fieldName + " assignment.");
            var value = assignment.Previous;
            if (value == null || value.OpCode != OpCodes.Ldstr || !string.Equals((string)value.Operand, "v1.4.5.6", StringComparison.Ordinal))
                throw new InvalidOperationException("Terraria 1.4.5.6 " + fieldName + " label did not match the verified value.");
            value.Operand = "Terraria v1.4.5.6";
        }
    }

    private static Instruction InsertAfter(ILProcessor il, Instruction target, Instruction instruction)
    {
        il.InsertAfter(target, instruction);
        return instruction;
    }

    private static void PatchPluginCoreDemo(ModuleDefinition module, string exePath)
    {
        if (module.Assembly.Name.Version?.ToString() != "1.4.5.6")
            throw new InvalidOperationException($"Expected Terraria 1.4.5.6, got {module.Assembly.Name.Version}.");

        var helperPath = Path.Combine(Path.GetDirectoryName(exePath)!, "bin", "AlacrityBootstrapRuntime.dll");
        if (!File.Exists(helperPath))
            throw new InvalidOperationException($"Missing Alacrity bootstrap beside Terraria.exe: {helperPath}");

        using var helper = ModuleDefinition.ReadModule(helperPath);
        var runtimeType = helper.Types.First(t => t.FullName == "AlacrityTerraria.AlacrityBootstrapRuntime");
        var load = module.ImportReference(runtimeType.Methods.Single(m => m.Name == "Load"));
        var programType = module.Types.First(t => t.FullName == "Terraria.Program");
        var launchGame = programType.Methods.Single(m => m.Name == "LaunchGame");
        var il = launchGame.Body.GetILProcessor();
        il.InsertBefore(launchGame.Body.Instructions.First(), il.Create(OpCodes.Call, load));

        var outputPath = Path.Combine(Path.GetDirectoryName(exePath)!, "Alacrity.exe");
        module.Write(outputPath);
        EmbedAlacrityIcon(outputPath, exePath);
        Console.WriteLine($"Wrote {outputPath}");
    }

    private static void EmbedAlacrityIcon(string outputPath, string sourceExePath)
    {
        var iconPath = Path.Combine(Path.GetDirectoryName(sourceExePath)!, "assets", "Alacrity-Logo.png");
        if (!File.Exists(iconPath))
            throw new InvalidOperationException($"Missing Alacrity executable icon: {iconPath}");

        var png = File.ReadAllBytes(iconPath);
        if (png.Length < 24 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
            throw new InvalidOperationException("Alacrity executable icon must be a valid PNG.");

        var group = CreateIconGroup(png.Length);
        int lastError = 0;
        for (int attempt = 0; attempt != 4; attempt++)
        {
            var update = BeginUpdateResource(outputPath, false);
            if (update == IntPtr.Zero)
            {
                lastError = Marshal.GetLastWin32Error();
                System.Threading.Thread.Sleep(150);
                continue;
            }

            try
            {
                if (!UpdateResource(update, (IntPtr)3, (IntPtr)1, 0, png, (uint)png.Length))
                    throw new InvalidOperationException("Could not write the Alacrity icon image resource (Win32 error " + Marshal.GetLastWin32Error() + ").");
                if (!UpdateResource(update, (IntPtr)14, (IntPtr)1, 0, group, (uint)group.Length))
                    throw new InvalidOperationException("Could not write the Alacrity icon group resource (Win32 error " + Marshal.GetLastWin32Error() + ").");
                if (EndUpdateResource(update, false))
                {
                    update = IntPtr.Zero;
                    return;
                }
                lastError = Marshal.GetLastWin32Error();
            }
            finally
            {
                if (update != IntPtr.Zero)
                    EndUpdateResource(update, true);
            }

            System.Threading.Thread.Sleep(150);
        }

        throw new InvalidOperationException("Could not commit the Alacrity icon resource after retries (Win32 error " + lastError + ").");
    }

    private static byte[] CreateIconGroup(int imageLength)
    {
        var group = new byte[20];
        group[2] = 1;
        group[4] = 1;
        group[6] = 0;
        group[7] = 0;
        group[10] = 1;
        group[12] = 32;
        BitConverter.GetBytes(imageLength).CopyTo(group, 14);
        group[18] = 1;
        return group;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint dataSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr update, bool discard);

    private static void PatchPluginDemoDraw(TypeDefinition mainType, MethodReference drawPluginDemoMenu)
    {
        var method = mainType.Methods.Single(m => m.Name == "DrawMenu" && m.Parameters.Count == 1);
        var spriteBatchField = mainType.Fields.Single(f => f.Name == "spriteBatch");
        var il = method.Body.GetILProcessor();
        var firstBegin = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Callvirt &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Microsoft.Xna.Framework.Graphics.SpriteBatch::Begin"));
        var ret = method.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        var insertionPoint = firstBegin.Next ?? throw new InvalidOperationException("DrawMenu SpriteBatch.Begin has no following instruction.");

        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, spriteBatchField));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawPluginDemoMenu));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue, ret));
    }

    private static void PatchPluginDemoInput(TypeDefinition mainType, MethodReference handlePluginDemoInput, MethodReference updatePluginKeybinds)
    {
        var method = mainType.Methods.Single(m => m.Name == "DoUpdate_HandleInput");
        var il = method.Body.GetILProcessor();
        var ret = method.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);

        // This is the verified post-input boundary. Keybind dispatch is kept out of rendering.
        il.InsertBefore(ret, il.Create(OpCodes.Call, updatePluginKeybinds));

        // The helper returns true when vanilla input should continue. Returning false
        // for the demo screen leaves Terraria's original input path untouched otherwise.
        il.InsertBefore(ret, il.Create(OpCodes.Call, handlePluginDemoInput));
        il.InsertBefore(ret, il.Create(OpCodes.Brtrue, ret));
    }

    private static void PatchPluginKeybindStateShape(ModuleDefinition module, MethodReference ensurePluginKeybindStateShape)
    {
        var playerInput = module.Types.SelectMany(Flatten).SingleOrDefault(type => type.FullName == "Terraria.GameInput.PlayerInput")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 PlayerInput type was not found.");
        var updateInput = playerInput.Methods.SingleOrDefault(method => method.Name == "UpdateInput" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 PlayerInput.UpdateInput signature did not match the verified keybind-state hook.");
        var first = updateInput.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference target && target.FullName == "System.Void Terraria.GameInput.PlayerInput/SettingsForUI::UpdateCounters()")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 PlayerInput.UpdateInput did not contain the verified input-update entry pattern.");

        // CopyKeyState directly indexes trigger dictionaries, so shape them before Terraria resets/copies input state.
        updateInput.Body.GetILProcessor().InsertBefore(first, updateInput.Body.GetILProcessor().Create(OpCodes.Call, ensurePluginKeybindStateShape));
    }

    private static void PatchPluginRuntimeDraw(TypeDefinition mainType, MethodReference drawNotifications)
    {
        var method = mainType.Methods.SingleOrDefault(method => method.Name == "DrawInterface_33_MouseText" && !method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawInterface_33_MouseText signature did not match the verified plugin draw boundary.");
        var spriteBatch = mainType.Fields.SingleOrDefault(field => field.Name == "spriteBatch" && field.FieldType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 spriteBatch field did not match the verified plugin draw boundary.");
        var first = method.Body.Instructions.FirstOrDefault()
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawInterface_33_MouseText has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, spriteBatch));
        il.InsertBefore(first, il.Create(OpCodes.Call, drawNotifications));
    }

    private static void PatchSwingHitboxCapture(ModuleDefinition module, MethodReference captureSwingHitbox)
    {
        var playerType = module.Types.SingleOrDefault(type => type.FullName == "Terraria.Player")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Player type was not found.");
        var method = playerType.Methods.SingleOrDefault(candidate =>
            candidate.Name == "ItemCheck_GetMeleeHitbox" &&
            !candidate.IsStatic &&
            candidate.ReturnType.FullName == "System.Void" &&
            candidate.Parameters.Count == 4 &&
            candidate.Parameters[0].ParameterType.FullName == "Terraria.Item" &&
            candidate.Parameters[1].ParameterType.FullName == "Microsoft.Xna.Framework.Rectangle" &&
            candidate.Parameters[2].ParameterType.FullName == "System.Boolean&" &&
            candidate.Parameters[3].ParameterType.FullName == "Microsoft.Xna.Framework.Rectangle&")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Player.ItemCheck_GetMeleeHitbox signature did not match the verified hitbox capture hook.");
        if (!method.HasBody)
            throw new InvalidOperationException("Terraria 1.4.5.6 Player.ItemCheck_GetMeleeHitbox has no body.");

        var returns = method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray();
        if (returns.Length == 0)
            throw new InvalidOperationException("Terraria 1.4.5.6 Player.ItemCheck_GetMeleeHitbox has no return instructions.");

        foreach (Instruction ret in returns)
        {
            var il = method.Body.GetILProcessor();
            var firstCapture = il.Create(OpCodes.Ldarg_0);
            il.InsertBefore(ret, firstCapture);
            il.InsertBefore(ret, il.Create(OpCodes.Ldarg_3));
            il.InsertBefore(ret, il.Create(OpCodes.Ldind_I1));
            il.InsertBefore(ret, il.Create(OpCodes.Ldarg_S, method.Parameters[3]));
            il.InsertBefore(ret, il.Create(OpCodes.Ldobj, module.ImportReference(method.Parameters[3].ParameterType.GetElementType())));
            il.InsertBefore(ret, il.Create(OpCodes.Call, captureSwingHitbox));
            RetargetInstructionReferences(method, ret, firstCapture);
        }
    }

    private static void PatchHitboxWorldOverlay(TypeDefinition mainType, MethodReference drawHitboxes)
    {
        var method = mainType.Methods.SingleOrDefault(candidate => candidate.Name == "DrawInterface_1_1_DrawEmoteBubblesInWorld" && candidate.IsStatic && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawInterface_1_1_DrawEmoteBubblesInWorld signature did not match the verified Hitboxes draw boundary.");
        var spriteBatch = mainType.Fields.SingleOrDefault(field => field.Name == "spriteBatch" && field.FieldType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 spriteBatch field did not match the verified Hitboxes draw boundary.");
        var drawAll = method.Body.Instructions.SingleOrDefault(instruction => instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference target && target.FullName == "System.Void Terraria.GameContent.UI.EmoteBubble::DrawAll(Microsoft.Xna.Framework.Graphics.SpriteBatch)")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 emote-bubble draw call did not match the verified Hitboxes draw boundary.");
        Instruction insertionPoint = drawAll.Next ?? throw new InvalidOperationException("Terraria 1.4.5.6 emote-bubble draw call has no continuation.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, spriteBatch));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawHitboxes));
    }

    private static void RetargetInstructionReferences(MethodDefinition method, Instruction from, Instruction to)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (ReferenceEquals(instruction.Operand, from))
                instruction.Operand = to;
            else if (instruction.Operand is Instruction[] targets)
            {
                for (int index = 0; index < targets.Length; index++)
                    if (ReferenceEquals(targets[index], from))
                        targets[index] = to;
            }
        }

        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
        {
            if (ReferenceEquals(handler.TryStart, from)) handler.TryStart = to;
            if (ReferenceEquals(handler.TryEnd, from)) handler.TryEnd = to;
            if (ReferenceEquals(handler.HandlerStart, from)) handler.HandlerStart = to;
            if (ReferenceEquals(handler.HandlerEnd, from)) handler.HandlerEnd = to;
            if (ReferenceEquals(handler.FilterStart, from)) handler.FilterStart = to;
        }
    }

    private static void PatchVisualEffects(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldRunDustSystem, MethodReference shouldCreateDust, MethodReference shouldUpdateDustInstance, MethodReference shouldDrawDustInstance, MethodReference shouldRunGoreSystem)
    {
        var dustType = module.Types.SingleOrDefault(type => type.FullName == "Terraria.Dust")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Dust type was not found.");
        var goreType = module.Types.SingleOrDefault(type => type.FullName == "Terraria.Gore")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Gore type was not found.");
        var drawDust = mainType.Methods.SingleOrDefault(method => method.Name == "DrawDust" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DrawDust signature did not match the verified visual-effects hook.");
        var updateDust = dustType.Methods.SingleOrDefault(method => method.Name == "UpdateDust" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Dust.UpdateDust signature did not match the verified visual-effects hook.");
        var newDust = dustType.Methods.SingleOrDefault(method => method.Name == "NewDust" && method.ReturnType.FullName == "System.Int32" && method.Parameters.Count == 9 && method.Parameters[3].ParameterType.FullName == "System.Int32")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Dust.NewDust signature did not match the verified visual-effects hook.");

        PatchRenderGate(drawDust, shouldRunDustSystem);
        PatchDustCreationTypeGuard(newDust, shouldCreateDust);
        PatchVoidReturnGate(updateDust, shouldRunDustSystem);
        PatchDustInstanceGuard(updateDust, shouldUpdateDustInstance);
        PatchDustDrawInstanceGuard(module, drawDust, shouldDrawDustInstance);

        foreach (string name in new[] { "DrawGore", "DrawGoreBehind", "DrawBackGore" })
        {
            var drawGore = mainType.Methods.SingleOrDefault(method => method.Name == name && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
                ?? throw new InvalidOperationException("Terraria 1.4.5.6 " + name + " signature did not match the verified visual-effects hook.");
            PatchRenderGate(drawGore, shouldRunGoreSystem);
        }
        var newGore = goreType.Methods.SingleOrDefault(method => method.Name == "NewGore" && method.ReturnType.FullName == "System.Int32")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Gore.NewGore signature did not match the verified visual-effects hook.");
        var updateGore = goreType.Methods.SingleOrDefault(method => method.Name == "Update" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 Gore.Update signature did not match the verified visual-effects hook.");
        PatchIntReturnGate(newGore, shouldRunGoreSystem, 600);
        PatchVoidReturnGate(updateGore, shouldRunGoreSystem);
    }

    private static void PatchDustCreationTypeGuard(MethodDefinition method, MethodReference shouldCreateDust)
    {
        var first = method.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("Dust.NewDust has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_3));
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldCreateDust));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 6000));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchDustInstanceGuard(MethodDefinition method, MethodReference shouldUpdateDustInstance)
    {
        var dustType = method.DeclaringType;
        var dustLocal = method.Body.Variables.FirstOrDefault(variable => variable.VariableType.FullName == dustType.FullName)
            ?? throw new InvalidOperationException("Dust.UpdateDust does not contain the verified Dust loop local.");
        var activeField = dustType.Fields.SingleOrDefault(field => field.Name == "active" && field.FieldType.FullName == "System.Boolean")
            ?? throw new InvalidOperationException("Terraria.Dust.active field did not match the verified visual-effects hook.");
        var activeLoad = method.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference field && field.FullName == activeField.FullName)
            ?? throw new InvalidOperationException("Dust.UpdateDust active check was not found.");
        if (!(activeLoad.Next?.Operand is Instruction loopContinue))
            throw new InvalidOperationException("Dust.UpdateDust active branch did not have the verified loop continuation.");
        var insertionPoint = activeLoad.Next.Next ?? throw new InvalidOperationException("Dust.UpdateDust active branch had no body after the continuation.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, dustLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldUpdateDustInstance));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue, insertionPoint));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Br, loopContinue));
    }


    private static void PatchPluginMenuEntry(ModuleDefinition module, TypeDefinition mainType, MethodReference openPluginManager)
    {
        var drawMenu = mainType.Methods.Single(m => m.Name == "DrawMenu" && m.Parameters.Count == 1);
        var il = drawMenu.Body.GetILProcessor();
        var stringArray = drawMenu.Body.Variables[27];
        var menuItemCount = drawMenu.Body.Variables[9];
        var menuIndex = drawMenu.Body.Variables[45];
        if (stringArray.VariableType.FullName != "System.String[]" || menuItemCount.VariableType.FullName != "System.Int32" || menuIndex.VariableType.FullName != "System.Int32")
            throw new InvalidOperationException("Terraria 1.4.5.6 DrawMenu local layout did not match the verified plugin insertion boundary.");

        var workshopType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.Social.SocialAPI");
        var workshopField = workshopType.Fields.Single(f => f.Name == "Workshop");
        var selectedMenu = mainType.Fields.Single(f => f.Name == "selectedMenu");
        var workshopLoad = drawMenu.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldsfld && i.Operand == workshopField)
            ?? throw new InvalidOperationException("Could not find the verified SocialAPI.Workshop menu boundary.");
        var workshopIndex = drawMenu.Body.Instructions.IndexOf(workshopLoad);
        var originalItemCount = drawMenu.Body.Instructions.Take(workshopIndex).LastOrDefault(i =>
            i.OpCode == OpCodes.Ldc_I4_7 && i.Next?.IsStlocFor(menuItemCount) == true)
            ?? throw new InvalidOperationException("Could not find the verified seven-row main-menu item count.");
        var insertionPoint = workshopLoad;

        originalItemCount.OpCode = OpCodes.Ldc_I4_8;

        // DrawMenu owns the menu list, hover state, mouse hit-testing, and controller navigation.
        // Insert directly before Workshop so the original Workshop and Settings rows shift down intact.
        il.InsertBefore(insertionPoint, LoadLocal(il, stringArray));
        il.InsertBefore(insertionPoint, LoadLocal(il, menuIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldstr, "Plugins"));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Stelem_Ref));

        var advanceToNextItem = il.Create(OpCodes.Ldloc, menuIndex);
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldfld, selectedMenu));
        il.InsertBefore(insertionPoint, LoadLocal(il, menuIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Bne_Un, advanceToNextItem));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, openPluginManager));
        il.InsertBefore(insertionPoint, advanceToNextItem);
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Add));
        il.InsertBefore(insertionPoint, StoreLocal(il, menuIndex));
    }

    private static void PatchStartupTweaks(TypeDefinition programType, MethodReference applyStartupTweaks)
    {
        var method = programType.Methods.Single(m => m.Name == "LaunchGame");
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Call, applyStartupTweaks));
    }

    private static void PatchGetInputText(TypeDefinition mainType, MethodReference processInput)
    {
        var method = mainType.Methods.Single(m => m.Name == "GetInputText");
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Call, processInput));
        il.Append(il.Create(OpCodes.Ret));
    }

    // This patch is intentionally limited to the established Alacrity executable. It leaves the
    // original input body intact and reaches BetterChat only while Terraria's player-chat focus is active.
    private static void PatchBetterChat(ModuleDefinition module, string exePath)
    {
        if (module.Assembly.Name.Version?.ToString() != "1.4.5.6")
            throw new InvalidOperationException($"Expected Terraria 1.4.5.6, got {module.Assembly.Name.Version}.");

        ApplyBetterChatHooks(module, exePath);

        string outputPath = Path.Combine(Path.GetDirectoryName(exePath)!, "Alacrity.BetterChat.exe");
        module.Write(outputPath);
        Console.WriteLine($"Wrote {outputPath}");
    }

    private static void ApplyBetterChatHooks(ModuleDefinition module, string exePath)
    {
        string helperPath = Path.Combine(Path.GetDirectoryName(exePath)!, "bin", "Alacrity.PluginUiRuntime.dll");
        if (!File.Exists(helperPath))
            throw new InvalidOperationException($"Missing Alacrity runtime helper: {helperPath}");

        using var helper = ModuleDefinition.ReadModule(helperPath);
        var runtimeType = helper.Types.Single(type => type.FullName == "AlacrityTerraria.PluginUiRuntime");
        MethodReference Import(string name, string returnType, params string[] parameterTypes)
        {
            var method = runtimeType.Methods.SingleOrDefault(candidate => candidate.Name == name && candidate.ReturnType.FullName == returnType && candidate.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypes))
                ?? throw new InvalidOperationException($"Alacrity runtime method {name} did not match the expected 1.4.5.6 hook signature.");
            return module.ImportReference(method);
        }

        var isActive = Import("IsBetterChatActive", "System.Boolean");
        var bootstrap = Import("BootstrapPluginRuntime", "System.Void");
        var process = Import("ProcessPlayerChatInput", "System.String", "System.String", "System.Boolean");
        var tryHandlePluginCommand = Import("TryHandlePluginChatCommand", "System.Boolean", "System.String");
        var format = Import("FormatPlayerChatText", "System.String", "System.String");
        var decorate = Import("DecorateChatMessage", "System.Object", "System.Object", "Microsoft.Xna.Framework.Color", "System.String");
        var networkVisibility = Import("ShouldDisplayNetworkChatMessage", "System.Boolean", "System.Byte");
        var localVisibility = Import("ShouldDisplayLocalChatMessage", "System.Boolean");
        var hover = Import("HandleChatSnippetHover", "System.Void", "System.Object");
        var click = Import("HandleChatSnippetClick", "System.Boolean", "System.Object");
        var color = Import("GetChatSnippetVisibleColor", "Microsoft.Xna.Framework.Color", "System.Object", "Microsoft.Xna.Framework.Color");
        var copyContext = Import("CopyChatSnippetContext", "System.Void", "System.Object", "System.Object");

        var main = module.Types.Single(type => type.FullName == "Terraria.Main");
        var snippets = module.Types.SelectMany(Flatten).Single(type => type.FullName == "Terraria.UI.Chat.TextSnippet");
        var chatManager = module.Types.SelectMany(Flatten).Single(type => type.FullName == "Terraria.UI.Chat.ChatManager");
        PatchBetterChatInput(main, isActive, process);
        PatchPluginChatCommands(main, tryHandlePluginCommand);
        PatchBetterChatStartup(module.Types.Single(type => type.FullName == "Terraria.Program"), bootstrap);
        PatchBetterChatDraw(main, format);
        PatchBetterChatSnippet(snippets, chatManager, hover, click, color, copyContext);
        PatchBetterChatParse(chatManager, decorate);
        PatchBetterChatVisibility(module, networkVisibility, localVisibility);

    }

    private static void PatchBetterChatInput(TypeDefinition mainType, MethodReference isActive, MethodReference process)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "GetInputText" && candidate.ReturnType.FullName == "System.String" && candidate.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Boolean" }));
        var drawingChat = mainType.Fields.Single(field => field.Name == "drawingPlayerChat" && field.FieldType.FullName == "System.Boolean");
        var first = method.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("GetInputText has no body.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, drawingChat));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Call, isActive));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, process));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchPluginChatCommands(TypeDefinition mainType, MethodReference tryHandlePluginCommand)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "DoUpdate_HandleChat" && candidate.ReturnType.FullName == "System.Void" && candidate.Parameters.Count == 0);
        var chatText = mainType.Fields.Single(field => field.Name == "chatText" && field.FieldType.FullName == "System.String");
        var submitCheck = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.FullName == chatText.FullName &&
            instruction.Next?.OpCode == OpCodes.Ldstr && (string)instruction.Next.Operand == string.Empty &&
            instruction.Next.Next?.Operand is MethodReference comparison && comparison.Name == "op_Inequality")
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat outgoing-message check was not found.");
        var closeChat = method.Body.Instructions.SkipWhile(instruction => instruction != submitCheck).FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == string.Empty &&
            instruction.Next?.OpCode == OpCodes.Stsfld && instruction.Next.Operand is FieldReference field && field.FullName == chatText.FullName)
            ?? throw new InvalidOperationException("Terraria 1.4.5.6 DoUpdate_HandleChat close-chat path was not found.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(submitCheck, il.Create(OpCodes.Ldsfld, chatText));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Call, tryHandlePluginCommand));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Brfalse, submitCheck));
        il.InsertBefore(submitCheck, il.Create(OpCodes.Br, closeChat));
    }

    private static void PatchBetterChatStartup(TypeDefinition programType, MethodReference bootstrap)
    {
        var launchGame = programType.Methods.Single(method => method.Name == "LaunchGame" && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String[]", "System.Boolean" }));
        var first = launchGame.Body.Instructions.FirstOrDefault() ?? throw new InvalidOperationException("Terraria.Program.LaunchGame has no body.");
        launchGame.Body.GetILProcessor().InsertBefore(first, launchGame.Body.GetILProcessor().Create(OpCodes.Call, bootstrap));
    }

    private static void PatchBetterChatDraw(TypeDefinition mainType, MethodReference format)
    {
        var method = mainType.Methods.Single(candidate => candidate.Name == "DrawPlayerChat" && !candidate.IsStatic && candidate.Parameters.Count == 0);
        var chatText = mainType.Fields.Single(field => field.Name == "chatText" && field.FieldType.FullName == "System.String");
        var textLocal = method.Body.Variables.ElementAtOrDefault(2) ?? throw new InvalidOperationException("DrawPlayerChat local layout is not the verified 1.4.5.6 layout.");
        if (textLocal.VariableType.FullName != "System.String")
            throw new InvalidOperationException("DrawPlayerChat text local did not match the verified 1.4.5.6 layout.");
        var load = method.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field && field.FullName == chatText.FullName && instruction.Next != null && instruction.Next.IsStlocFor(textLocal))
            ?? throw new InvalidOperationException("Could not locate the verified DrawPlayerChat chatText capture.");
        var il = method.Body.GetILProcessor();
        il.InsertAfter(load, il.Create(OpCodes.Call, format));

        var cursor = method.Body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == "|")
            ?? throw new InvalidOperationException("Could not locate Terraria's DrawPlayerChat cursor literal.");
        var start = cursor;
        while (start.Previous != null && start.OpCode != OpCodes.Ldarg_0)
            start = start.Previous;
        var end = cursor;
        while (end.Next != null && !(end.OpCode == OpCodes.Callvirt && end.Operand is MethodReference reference && reference.Name == "Add"))
            end = end.Next;
        if (end.Next == null)
            throw new InvalidOperationException("Could not locate the verified DrawPlayerChat cursor append.");
        for (var current = start; current != end.Next; current = current.Next)
            current.OpCode = OpCodes.Nop;
    }

    private static void PatchBetterChatSnippet(TypeDefinition snippets, TypeDefinition chatManager, MethodReference hover, MethodReference click, MethodReference color, MethodReference copyContext)
    {
        var visible = snippets.Methods.Single(method => method.Name == "GetVisibleColor" && method.ReturnType.FullName == "Microsoft.Xna.Framework.Color" && method.Parameters.Count == 0);
        var wave = chatManager.Methods.Single(method => method.Name == "WaveColor" && method.ReturnType.FullName == "Microsoft.Xna.Framework.Color" && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "Microsoft.Xna.Framework.Color");
        var colorField = snippets.Fields.Single(field => field.Name == "Color" && field.FieldType.FullName == "Microsoft.Xna.Framework.Color");
        ReplaceBody(visible, il =>
        {
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, colorField));
            il.Append(il.Create(OpCodes.Call, wave));
            il.Append(il.Create(OpCodes.Call, color));
            il.Append(il.Create(OpCodes.Ret));
        });
        var onHover = snippets.Methods.Single(method => method.Name == "OnHover" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0);
        ReplaceBody(onHover, il => { il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Call, hover)); il.Append(il.Create(OpCodes.Ret)); });
        var onClick = snippets.Methods.Single(method => method.Name == "OnClick" && method.ReturnType.FullName == "System.Void" && method.Parameters.Count == 0);
        ReplaceBody(onClick, il => { il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Call, click)); il.Append(il.Create(OpCodes.Pop)); il.Append(il.Create(OpCodes.Ret)); });

        var copyMorph = snippets.Methods.Single(method => method.Name == "CopyMorph" && method.ReturnType.FullName == snippets.FullName && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.String");
        var result = new VariableDefinition(copyMorph.ReturnType);
        copyMorph.Body.Variables.Add(result);
        var returnInstruction = copyMorph.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret)
            ?? throw new InvalidOperationException("TextSnippet.CopyMorph has no return instruction.");
        var copyIl = copyMorph.Body.GetILProcessor();
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Stloc, result));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Ldarg_0));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Ldloc, result));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Call, copyContext));
        copyIl.InsertBefore(returnInstruction, copyIl.Create(OpCodes.Ldloc, result));
    }

    private static void PatchBetterChatParse(TypeDefinition chatManager, MethodReference decorate)
    {
        var method = chatManager.Methods.Single(candidate => candidate.Name == "ParseMessage" && candidate.ReturnType.FullName == "System.Collections.Generic.List`1<Terraria.UI.Chat.TextSnippet>" && candidate.Parameters.Count == 2);
        if (method.Parameters[0].ParameterType.FullName != "System.String" || method.Parameters[1].ParameterType.FullName != "Microsoft.Xna.Framework.Color")
            throw new InvalidOperationException("ParseMessage did not match the verified 1.4.5.6 signature.");
        var ret = method.Body.Instructions.LastOrDefault(instruction => instruction.OpCode == OpCodes.Ret) ?? throw new InvalidOperationException("ParseMessage has no return.");
        var il = method.Body.GetILProcessor();
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, decorate));
        il.InsertBefore(ret, il.Create(OpCodes.Castclass, method.ReturnType));
    }

    private static void PatchBetterChatVisibility(ModuleDefinition module, MethodReference networkVisibility, MethodReference localVisibility)
    {
        var chatHelper = module.Types.SelectMany(Flatten).Single(type => type.FullName == "Terraria.Chat.ChatHelper");
        var display = chatHelper.Methods.Single(method => method.Name == "DisplayMessage" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "Terraria.Localization.NetworkText", "Microsoft.Xna.Framework.Color", "System.Byte" }));
        InsertDisplayGate(display, networkVisibility, 2, "ChatHelper.DisplayMessage");

        var main = module.Types.Single(type => type.FullName == "Terraria.Main");
        var newText = main.Methods.Single(method => method.Name == "NewText" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Byte", "System.Byte", "System.Byte" }));
        InsertDisplayGate(newText, localVisibility, null, "Main.NewText");
        var newTextMultiline = main.Methods.Single(method => method.Name == "NewTextMultiline" && method.IsStatic && method.ReturnType.FullName == "System.Void" && method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "System.String", "System.Boolean", "Microsoft.Xna.Framework.Color", "System.Int32" }));
        InsertDisplayGate(newTextMultiline, localVisibility, null, "Main.NewTextMultiline");
    }

    private static void InsertDisplayGate(MethodDefinition method, MethodReference gate, int? argumentIndex, string methodName)
    {
        if (!method.HasBody || method.Body.Instructions.Count == 0)
            throw new InvalidOperationException(methodName + " has no verified method body.");
        var first = method.Body.Instructions[0];
        var il = method.Body.GetILProcessor();
        if (argumentIndex.HasValue)
            il.InsertBefore(first, il.Create(OpCodes.Ldarg, argumentIndex.Value));
        il.InsertBefore(first, il.Create(OpCodes.Call, gate));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }


    private static void ReplaceBody(MethodDefinition method, Action<ILProcessor> write)
    {
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.Instructions.Clear();
        write(method.Body.GetILProcessor());
    }

    private static void PatchDoUpdateHandleChat(TypeDefinition mainType, MethodReference applyOutgoingPrefix)
    {
        var method = mainType.Methods.Single(m => m.Name == "DoUpdate_HandleChat");
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;
        var chatTextField = mainType.Fields.Single(f => f.Name == "chatText");

        var submitNonEmptyCheck = instructions.FirstOrDefault(i =>
            i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference field &&
            field.Name == chatTextField.Name &&
            i.Next != null &&
            i.Next.OpCode == OpCodes.Ldstr &&
            (string)i.Next.Operand == "" &&
            i.Next.Next != null &&
            i.Next.Next.Operand is MethodReference methodReference &&
            methodReference.Name == "op_Inequality");
        if (submitNonEmptyCheck == null)
            throw new InvalidOperationException("Could not find outgoing chat non-empty check in DoUpdate_HandleChat.");

        il.InsertBefore(submitNonEmptyCheck, il.Create(OpCodes.Ldsfld, chatTextField));
        il.InsertBefore(submitNonEmptyCheck, il.Create(OpCodes.Call, applyOutgoingPrefix));
        il.InsertBefore(submitNonEmptyCheck, il.Create(OpCodes.Stsfld, chatTextField));

    }

    private static void PatchDrawPlayerChat(TypeDefinition mainType, MethodReference withCursor)
    {
        var method = mainType.Methods.Single(m => m.Name == "DrawPlayerChat");
        var il = method.Body.GetILProcessor();
        var instructions = method.Body.Instructions;
        var chatTextField = mainType.Fields.Single(f => f.Name == "chatText");

        for (var i = 0; i < instructions.Count - 1; i++)
        {
            if (instructions[i].OpCode == OpCodes.Ldsfld &&
                instructions[i].Operand is FieldReference field &&
                field.Name == chatTextField.Name &&
                instructions[i + 1].IsStlocFor(method.Body.Variables[2]))
            {
                il.InsertAfter(instructions[i], il.Create(OpCodes.Call, withCursor));
                break;
            }
        }

        var cursorText = instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "|");
        if (cursorText == null)
            throw new InvalidOperationException("Could not find vanilla cursor text in DrawPlayerChat.");

        var start = cursorText;
        while (start.Previous != null && start.OpCode != OpCodes.Ldarg_0)
            start = start.Previous;

        var end = cursorText;
        while (end.Next != null && !(end.OpCode == OpCodes.Callvirt && end.Operand is MethodReference mr && mr.Name == "Add"))
            end = end.Next;

        for (var current = start; current != end.Next; current = current.Next)
            current.OpCode = OpCodes.Nop;
    }

    private static void PatchTextSnippetClick(TypeDefinition textSnippetType, MethodReference openIfUrl)
    {
        var method = textSnippetType.Methods.Single(m => m.Name == "OnClick");
        var textField = textSnippetType.Fields.Single(f => f.Name == "Text");
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, textField));
        il.Append(il.Create(OpCodes.Call, openIfUrl));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchTextSnippetHover(TypeDefinition textSnippetType, MethodReference hoverSnippet)
    {
        var method = textSnippetType.Methods.Single(m => m.Name == "OnHover");
        var textField = textSnippetType.Fields.Single(f => f.Name == "Text");
        var originalField = textSnippetType.Fields.Single(f => f.Name == "TextOriginal");
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, textField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, originalField));
        il.Append(il.Create(OpCodes.Call, hoverSnippet));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchTextSnippetGetVisibleColor(TypeDefinition textSnippetType, MethodReference highlightMessageColor)
    {
        var method = textSnippetType.Methods.Single(m => m.Name == "GetVisibleColor");
        var colorField = textSnippetType.Fields.Single(f => f.Name == "Color");
        var originalField = textSnippetType.Fields.Single(f => f.Name == "TextOriginal");
        var chatManagerType = textSnippetType.Module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.UI.Chat.ChatManager");
        var waveColor = chatManagerType.Methods.Single(m => m.Name == "WaveColor");
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        var il = method.Body.GetILProcessor();
        method.Body.Instructions.Clear();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, originalField));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, colorField));
        il.Append(il.Create(OpCodes.Call, waveColor));
        il.Append(il.Create(OpCodes.Call, highlightMessageColor));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchParseMessage(ModuleDefinition module, MethodReference linkify)
    {
        var chatManagerType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.UI.Chat.ChatManager");
        var method = chatManagerType.Methods.Single(m => m.Name == "ParseMessage");
        var il = method.Body.GetILProcessor();
        var ret = method.Body.Instructions.Last(i => i.OpCode == OpCodes.Ret);
        var returnType = method.ReturnType;
        var colorType = method.Parameters[1].ParameterType;

        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(ret, il.Create(OpCodes.Box, colorType));
        il.InsertBefore(ret, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(ret, il.Create(OpCodes.Call, linkify));
        il.InsertBefore(ret, il.Create(OpCodes.Castclass, returnType));
    }

    private static void PatchDrawDust(TypeDefinition mainType, MethodReference shouldRenderDust)
    {
        PatchRenderGate(mainType.Methods.Single(m => m.Name == "DrawDust"), shouldRenderDust);
    }

    private static void PatchGoreRendering(TypeDefinition mainType, MethodReference shouldRenderGore)
    {
        PatchRenderGate(mainType.Methods.Single(m => m.Name == "DrawGore"), shouldRenderGore);
        PatchRenderGate(mainType.Methods.Single(m => m.Name == "DrawGoreBehind"), shouldRenderGore);
        PatchRenderGate(mainType.Methods.Single(m => m.Name == "DrawBackGore"), shouldRenderGore);
    }

    private static void PatchDustSimulation(TypeDefinition dustType, MethodReference shouldSimulateDust, MethodReference shouldCreateDust)
    {
        var newDust = dustType.Methods.Single(m => m.Name == "NewDust");
        PatchIntReturnGate(newDust, shouldSimulateDust, 6000);
        PatchDustCreationGuard(newDust, shouldCreateDust);
        PatchVoidReturnGate(dustType.Methods.Single(m => m.Name == "UpdateDust"), shouldSimulateDust);
    }

    private static void PatchDustCreationGuard(MethodDefinition method, MethodReference shouldCreateDust)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_3));
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldCreateDust));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, 6000));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchGoreSimulation(TypeDefinition goreType, MethodReference shouldSimulateGore)
    {
        PatchIntReturnGate(goreType.Methods.Single(m => m.Name == "NewGore"), shouldSimulateGore, 600);
        PatchVoidReturnGate(goreType.Methods.Single(m => m.Name == "Update"), shouldSimulateGore);
    }

    private static void PatchRenderGate(MethodDefinition method, MethodReference shouldRender)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Call, shouldRender));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchVoidReturnGate(MethodDefinition method, MethodReference shouldRun)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Call, shouldRun));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchIntReturnGate(MethodDefinition method, MethodReference shouldRun, int disabledReturnValue)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Call, shouldRun));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4, disabledReturnValue));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchIngamePluginSettings(
        ModuleDefinition module,
        TypeDefinition ingameOptionsType,
        MethodReference openIngamePluginSettings,
        MethodReference drawIngamePluginSettings)
    {
        var draw = ingameOptionsType.Methods.Single(method => method.Name == "Draw" && method.Parameters.Count == 2);
        var langType = module.Types.First(type => type.FullName == "Terraria.Lang");
        var menuField = langType.Fields.Single(field => field.Name == "menu");
        var il = draw.Body.GetILProcessor();

        // This exact Lang.menu[118] load is Terraria 1.4.5.6's Close Menu row.
        // Keeping the native draw helper intact preserves its layout, hover, and input behavior.
        var closeMenuLabel = draw.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Ldsfld &&
            instruction.Operand == menuField &&
            IsLoadInt32(instruction.Next, 118) &&
            instruction.Next?.Next?.OpCode == OpCodes.Ldelem_Ref &&
            instruction.Next.Next.Next?.Operand is MethodReference reference && reference.Name == "get_Value")
            ?? throw new InvalidOperationException("Could not find Terraria 1.4.5.6's verified Close Menu label.");
        var afterNativeLabel = closeMenuLabel.Next!.Next!.Next!.Next
            ?? throw new InvalidOperationException("Close Menu label has no continuation point.");

        il.InsertBefore(closeMenuLabel, il.Create(OpCodes.Ldstr, "Plugins"));
        il.InsertBefore(closeMenuLabel, il.Create(OpCodes.Br, afterNativeLabel));

        var closeMenuAction = draw.Body.Instructions
            .Skip(draw.Body.Instructions.IndexOf(afterNativeLabel))
            .FirstOrDefault(instruction =>
                instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodReference reference &&
                reference.FullName == "System.Void Terraria.IngameOptions::Close()")
            ?? throw new InvalidOperationException("Could not find Terraria 1.4.5.6's verified Close Menu action.");
        closeMenuAction.Operand = openIngamePluginSettings;

        var drawThickCursor = draw.Body.Instructions.LastOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference reference &&
            reference.DeclaringType.FullName == "Terraria.Main" &&
            reference.Name == "DrawThickCursor" &&
            reference.Parameters.Count == 1)
            ?? throw new InvalidOperationException("Could not find final cursor draw in IngameOptions.Draw.");
        var insertionPoint = drawThickCursor.Previous
            ?? throw new InvalidOperationException("Final cursor draw has no safe insertion point.");
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawIngamePluginSettings));
    }

    private static bool IsLoadInt32(Instruction instruction, int value)
    {
        if (instruction == null)
            return false;

        return value switch
        {
            -1 => instruction.OpCode == OpCodes.Ldc_I4_M1,
            0 => instruction.OpCode == OpCodes.Ldc_I4_0,
            1 => instruction.OpCode == OpCodes.Ldc_I4_1,
            2 => instruction.OpCode == OpCodes.Ldc_I4_2,
            3 => instruction.OpCode == OpCodes.Ldc_I4_3,
            4 => instruction.OpCode == OpCodes.Ldc_I4_4,
            5 => instruction.OpCode == OpCodes.Ldc_I4_5,
            6 => instruction.OpCode == OpCodes.Ldc_I4_6,
            7 => instruction.OpCode == OpCodes.Ldc_I4_7,
            8 => instruction.OpCode == OpCodes.Ldc_I4_8,
            _ => instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is int constant && constant == value ||
                 instruction.OpCode == OpCodes.Ldc_I4_S && Convert.ToInt32(instruction.Operand) == value
        };
    }

    private static void PatchIngameOptionsDraw(TypeDefinition ingameOptionsType, MethodReference drawEnhancerSettings)
    {
        var method = ingameOptionsType.Methods.Single(m => m.Name == "Draw");
        var il = method.Body.GetILProcessor();
        var drawThickCursor = method.Body.Instructions.FirstOrDefault(i =>
            i.Operand is MethodReference methodReference && methodReference.Name == "DrawThickCursor");
        if (drawThickCursor == null || drawThickCursor.Previous == null)
            throw new InvalidOperationException("Could not find final cursor draw in IngameOptions.Draw.");

        var insertionPoint = drawThickCursor.Previous;
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawEnhancerSettings));
    }

    private static void PatchUiElementInspector(TypeDefinition uiElementType, MethodReference observeDrawnUiElement)
    {
        var method = uiElementType.Methods.Single(m => m.Name == "Draw" && m.Parameters.Count == 1);
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, observeDrawnUiElement));
    }

    private static void PatchPlayerListOverlay(TypeDefinition mainType, MethodReference drawPlayerList)
    {
        var method = mainType.Methods.Single(m => m.Name == "DrawInterface_33_MouseText");
        var spriteBatchField = mainType.Fields.Single(f => f.Name == "spriteBatch");
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, spriteBatchField));
        il.InsertBefore(first, il.Create(OpCodes.Call, drawPlayerList));
    }

    private static void PatchServerBrowser(TypeDefinition mainType, MethodReference prepareServerBrowserMenu, MethodReference drawServerBrowser)
    {
        var method = mainType.Methods.Single(m => m.Name == "DrawMenu");
        var spriteBatchField = mainType.Fields.Single(f => f.Name == "spriteBatch");
        var il = method.Body.GetILProcessor();
        var firstBegin = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Callvirt &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Microsoft.Xna.Framework.Graphics.SpriteBatch::Begin"));
        var earlyInsertionPoint = firstBegin.Next;
        il.InsertBefore(earlyInsertionPoint, il.Create(OpCodes.Call, prepareServerBrowserMenu));

        var cursorDraw = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Terraria.Main::DrawThickCursor"));
        il.InsertBefore(cursorDraw, il.Create(OpCodes.Ldsfld, spriteBatchField));
        il.InsertBefore(cursorDraw, il.Create(OpCodes.Call, drawServerBrowser));
    }

    private static void PatchHeldItemsEmoteLayer(ModuleDefinition module, TypeDefinition mainType, MethodReference drawHeldItemsAsEmotes, MethodReference shouldDrawEmoteBubble)
    {
        var method = mainType.Methods.Single(m => m.Name == "DrawInterface_1_1_DrawEmoteBubblesInWorld");
        var spriteBatchField = mainType.Fields.Single(f => f.Name == "spriteBatch");
        var il = method.Body.GetILProcessor();
        var drawAllCall = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Terraria.GameContent.UI.EmoteBubble::DrawAll"));
        var insertionPoint = drawAllCall.Next;

        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, spriteBatchField));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, drawHeldItemsAsEmotes));

        var emoteBubbleType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.GameContent.UI.EmoteBubble");
        var drawMethod = emoteBubbleType.Methods.Single(m => m.Name == "Draw" && m.Parameters.Count == 1);
        var drawIl = drawMethod.Body.GetILProcessor();
        var first = drawMethod.Body.Instructions.First();
        drawIl.InsertBefore(first, drawIl.Create(OpCodes.Ldarg_0));
        drawIl.InsertBefore(first, drawIl.Create(OpCodes.Call, shouldDrawEmoteBubble));
        drawIl.InsertBefore(first, drawIl.Create(OpCodes.Brtrue_S, first));
        drawIl.InsertBefore(first, drawIl.Create(OpCodes.Ret));
    }

    private static void PatchPlayerPreviewFullbright(ModuleDefinition module, MethodReference shouldForcePlayerPreviewFullbright)
    {
        var lightingType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.Lighting");
        var method = lightingType.Methods.First(m =>
            m.Name == "GetColorClamped" &&
            m.Parameters.Count == 3 &&
            m.Parameters[0].ParameterType.FullName == "System.Int32" &&
            m.Parameters[1].ParameterType.FullName == "System.Int32" &&
            m.Parameters[2].ParameterType.FullName == "Microsoft.Xna.Framework.Color");

        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldForcePlayerPreviewFullbright));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse_S, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchEntityDrawCulling(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldDrawWorldItem, MethodReference shouldDrawDustInstance)
    {
        PatchWorldItemDrawGuard(mainType.Methods.Single(m => m.Name == "DrawItem" && m.Parameters.Count == 2), shouldDrawWorldItem);
        PatchDustDrawInstanceGuard(module, mainType.Methods.Single(m => m.Name == "DrawDust"), shouldDrawDustInstance);
    }

    private static void PatchSpecialTileDrawCulling(ModuleDefinition module, MethodReference shouldDrawGrassSpecial, MethodReference shouldDrawVineSpecial)
    {
        var tileDrawingType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.GameContent.Drawing.TileDrawing");
        PatchSpecialTilePointGuard(tileDrawingType.Methods.Single(m => m.Name == "DrawGrass"), shouldDrawGrassSpecial, 5, 6);
        PatchSpecialTilePointGuard(tileDrawingType.Methods.Single(m => m.Name == "DrawVines"), shouldDrawVineSpecial, 5, 6);
    }

    private static void PatchOptimizedDrawBlack(TypeDefinition mainType, MethodReference tryDrawBlackOptimized)
    {
        var method = mainType.Methods.Single(m => m.Name == "DrawBlack" && m.Parameters.Count == 2);
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(first, il.Create(OpCodes.Call, tryDrawBlackOptimized));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchSpecialTilePointGuard(MethodDefinition method, MethodReference shouldDrawSpecial, int xLocalIndex, int yLocalIndex)
    {
        var xLocal = method.Body.Variables[xLocalIndex];
        var yLocal = method.Body.Variables[yLocalIndex];
        var insertionPoint = method.Body.Instructions.First(i => i.IsStlocFor(yLocal)).Next;
        var continueTarget = method.Body.Instructions.First(i => i.IsLdlocFor(method.Body.Variables[4]) && i.Next != null && i.Next.OpCode == OpCodes.Ldc_I4_1);
        var il = method.Body.GetILProcessor();

        il.InsertBefore(insertionPoint, LoadLocal(il, xLocal));
        il.InsertBefore(insertionPoint, LoadLocal(il, yLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawSpecial));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, continueTarget));
    }

    private static void PatchWorldItemDrawGuard(MethodDefinition method, MethodReference shouldDrawWorldItem)
    {
        var il = method.Body.GetILProcessor();
        var earlyReturn = method.Body.Instructions.First(i => i.OpCode == OpCodes.Ret);
        var insertionPoint = earlyReturn.Next ?? method.Body.Instructions.First();

        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawWorldItem));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue_S, insertionPoint));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ret));
    }

    private static void PatchDustDrawInstanceGuard(ModuleDefinition module, MethodDefinition method, MethodReference shouldDrawDustInstance)
    {
        var dustType = module.Types.First(t => t.FullName == "Terraria.Dust");
        var activeField = dustType.Fields.Single(f => f.Name == "active");
        var dustLocal = method.Body.Variables.First(v => v.VariableType.FullName == "Terraria.Dust");
        var activeLoad = method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var loopContinue = (Instruction)activeLoad.Next.Operand;
        var broadVisibilityCheck = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Microsoft.Xna.Framework.Rectangle::Intersects"));
        var insertionPoint = broadVisibilityCheck.Next.Next;
        var il = method.Body.GetILProcessor();

        il.InsertBefore(insertionPoint, LoadLocal(il, dustLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawDustInstance));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, loopContinue));
    }

    private static void PatchHiddenPlayerRendering(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldDrawPlayer, MethodReference shouldDrawProjectile, MethodReference shouldDrawProjectileObject, MethodReference shouldDrawPlayerProjectileVisuals)
    {
        var rendererType = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.Graphics.Renderers.LegacyPlayerRenderer");
        PatchPlayerDrawGuard(rendererType.Methods.Single(m => m.Name == "DrawPlayer" && m.Parameters.Count == 7), shouldDrawPlayer, 1);
        PatchPlayerDrawGuard(rendererType.Methods.Single(m => m.Name == "DrawPlayerFull"), shouldDrawPlayer, 1);
        PatchProjectileDrawGuard(mainType.Methods.Single(m => m.Name == "DrawProj"), shouldDrawProjectile);
        PatchProjectileObjectDrawGuard(mainType.Methods.Single(m => m.Name == "DrawProjDirect"), shouldDrawProjectileObject, 0);
        PatchProjectileObjectDrawGuard(mainType.Methods.Single(m => m.Name == "DrawMultisegmentPet"), shouldDrawProjectileObject, 0);
        PatchProjectileObjectDrawGuard(mainType.Methods.Single(m => m.Name == "DrawTwinsPet"), shouldDrawProjectileObject, 0);
        PatchInfernoRingGuard(module, mainType, shouldDrawPlayerProjectileVisuals);
        PatchPlayerHealthBarGuard(module, mainType, shouldDrawPlayer);
        PatchClosePlayerOverlayGuards(module, shouldDrawPlayer);
        PatchMouseOverHiddenPlayerGuard(module, mainType, shouldDrawPlayer);
    }

    private static void PatchPlayerHealthBarGuard(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldDrawPlayer)
    {
        var playerType = module.Types.First(t => t.FullName == "Terraria.Player");
        var activeField = playerType.Fields.Single(f => f.Name == "active");
        var playerField = mainType.Fields.Single(f => f.Name == "player");
        var method = mainType.Methods.Single(m => m.Name == "DrawInterface_14_EntityHealthBars");
        var loopIndex = method.Body.Variables[28];

        var activeLoad = method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var continueTarget = (Instruction)activeLoad.Next.Operand;
        var insertionPoint = activeLoad.Next.Next;
        var il = method.Body.GetILProcessor();

        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, playerField));
        il.InsertBefore(insertionPoint, LoadLocal(il, loopIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldelem_Ref));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawPlayer));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, continueTarget));
    }

    private static void PatchInfernoRingGuard(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldDrawPlayerProjectileVisuals)
    {
        var playerType = module.Types.First(t => t.FullName == "Terraria.Player");
        var activeField = playerType.Fields.Single(f => f.Name == "active");
        var playerField = mainType.Fields.Single(f => f.Name == "player");
        var method = mainType.Methods.Single(m => m.Name == "DrawInfernoRings");
        var loopIndex = method.Body.Variables[1];

        var activeLoad = method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var continueTarget = (Instruction)activeLoad.Next.Operand;
        var insertionPoint = activeLoad.Next.Next;
        var il = method.Body.GetILProcessor();

        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, playerField));
        il.InsertBefore(insertionPoint, LoadLocal(il, loopIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldelem_Ref));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawPlayerProjectileVisuals));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, continueTarget));
    }

    private static void PatchMouseOverHiddenPlayerGuard(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldDrawPlayer)
    {
        var playerType = module.Types.First(t => t.FullName == "Terraria.Player");
        var activeField = playerType.Fields.Single(f => f.Name == "active");
        var playerField = mainType.Fields.Single(f => f.Name == "player");
        var method = mainType.Methods.Single(m => m.Name == "DrawMouseOver");
        var loopIndex = method.Body.Variables[7];

        var activeLoad = method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var continueTarget = (Instruction)activeLoad.Next.Operand;
        var insertionPoint = activeLoad.Next.Next;
        var il = method.Body.GetILProcessor();

        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldsfld, playerField));
        il.InsertBefore(insertionPoint, LoadLocal(il, loopIndex));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldelem_Ref));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawPlayer));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, continueTarget));
    }

    private static void PatchVanillaInputFocusGuard(TypeDefinition mainType, MethodReference suppressVanillaInputWhenUnfocused)
    {
        var method = mainType.Methods.Single(m => m.Name == "DoUpdate_HandleInput");
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Call, suppressVanillaInputWhenUnfocused));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchInactiveFrameThrottle(TypeDefinition mainType, MethodReference throttleInactiveFrame)
    {
        var method = mainType.Methods.Single(m => m.Name == "DoDraw");
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Call, throttleInactiveFrame));
    }

    private static void PatchAudioDeviceWatcher(TypeDefinition mainType, MethodReference checkAudioDeviceChange)
    {
        var method = mainType.Methods.Single(m => m.Name == "UpdateAudio");
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Call, checkAudioDeviceChange));
    }

    private static void PatchDashKeybindPlayerControlSync(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldSyncDashKeybind)
    {
        var method = mainType.Module.Types.First(t => t.FullName == "Terraria.Player").Methods.Single(m => m.Name == "Update");
        var netMessageType = module.Types.First(t => t.FullName == "Terraria.NetMessage");
        var sendData = netMessageType.Methods.Single(m => m.Name == "SendData" && m.Parameters.Count == 11);
        var sendDataCall = method.Body.Instructions.FirstOrDefault(i =>
        {
            if (i.OpCode != OpCodes.Call ||
                i.Operand is not MethodReference called ||
                called.FullName != sendData.FullName ||
                i.Previous?.OpCode != OpCodes.Ldc_I4_0 ||
                i.Previous?.Previous?.OpCode != OpCodes.Ldc_I4_0 ||
                i.Previous?.Previous?.Previous?.OpCode != OpCodes.Ldc_I4_0)
            {
                return false;
            }

            var candidateMessageId = i;
            while (candidateMessageId != null && candidateMessageId.OpCode != OpCodes.Brfalse && candidateMessageId.OpCode != OpCodes.Brfalse_S)
                candidateMessageId = candidateMessageId.Previous;
            candidateMessageId = candidateMessageId?.Next;
            return candidateMessageId != null && IsLoadInt(candidateMessageId, 13);
        });
        if (sendDataCall == null)
            throw new InvalidOperationException("Could not find Player.Update PlayerControls SendData call.");

        var messageIdLoad = sendDataCall;
        while (messageIdLoad != null && messageIdLoad.OpCode != OpCodes.Brfalse && messageIdLoad.OpCode != OpCodes.Brfalse_S)
            messageIdLoad = messageIdLoad.Previous;
        messageIdLoad = messageIdLoad?.Next;
        if (messageIdLoad == null || !IsLoadInt(messageIdLoad, 13))
            throw new InvalidOperationException("Could not verify PlayerControls message id in Player.Update.");

        var dirtyFlagLoad = messageIdLoad.Previous?.Previous;
        if (dirtyFlagLoad == null || dirtyFlagLoad.Next == null || dirtyFlagLoad.Next.OpCode != OpCodes.Brfalse_S && dirtyFlagLoad.Next.OpCode != OpCodes.Brfalse)
            throw new InvalidOperationException("Could not find PlayerControls dirty flag branch.");

        var dirtyFlag = GetLoadedLocalVariable(method, dirtyFlagLoad);
        if (dirtyFlag == null || dirtyFlag.VariableType.FullName != "System.Boolean")
            throw new InvalidOperationException("Could not resolve PlayerControls dirty flag local.");

        var il = method.Body.GetILProcessor();
        var continueOriginal = dirtyFlagLoad;
        il.InsertBefore(continueOriginal, LoadLocal(il, dirtyFlag));
        il.InsertBefore(continueOriginal, il.Create(OpCodes.Brtrue_S, continueOriginal));
        il.InsertBefore(continueOriginal, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(continueOriginal, il.Create(OpCodes.Call, shouldSyncDashKeybind));
        il.InsertBefore(continueOriginal, il.Create(OpCodes.Brfalse_S, continueOriginal));
        il.InsertBefore(continueOriginal, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(continueOriginal, StoreLocal(il, dirtyFlag));
    }

    private static void PatchPlayerDrawGuard(MethodDefinition method, MethodReference shouldDrawPlayer, int playerParameterIndex)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[playerParameterIndex]));
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldDrawPlayer));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchProjectileDrawGuard(MethodDefinition method, MethodReference shouldDrawProjectile)
    {
        var il = method.Body.GetILProcessor();
        var visibilityCheck = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Microsoft.Xna.Framework.Rectangle::Intersects"));
        var visibilityBranch = visibilityCheck.Next;
        if (visibilityBranch == null || visibilityBranch.OpCode != OpCodes.Brtrue_S && visibilityBranch.OpCode != OpCodes.Brtrue)
            throw new InvalidOperationException("Unable to find projectile visibility branch.");

        var insertionPoint = (Instruction)visibilityBranch.Operand;
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawProjectile));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brtrue_S, insertionPoint));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ret));
    }

    private static void PatchProjectileObjectDrawGuard(MethodDefinition method, MethodReference shouldDrawProjectileObject, int projectileParameterIndex)
    {
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[projectileParameterIndex]));
        il.InsertBefore(first, il.Create(OpCodes.Call, shouldDrawProjectileObject));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue_S, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
    }

    private static void PatchClosePlayerOverlayGuards(ModuleDefinition module, MethodReference shouldDrawPlayer)
    {
        var playerType = module.Types.First(t => t.FullName == "Terraria.Player");
        var activeField = playerType.Fields.Single(f => f.Name == "active");

        var legacy = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.GameContent.UI.LegacyMultiplayerClosePlayersOverlay");
        var legacyDraw = legacy.Methods.Single(m => m.Name == "Draw");
        var legacyActiveLoad = legacyDraw.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var legacyContinue = (Instruction)legacyActiveLoad.Next.Operand;
        var legacyInsert = legacyActiveLoad.Next.Next;
        InsertLegacyOverlayGuard(legacyDraw, legacyInsert, legacyContinue, shouldDrawPlayer);

        var modern = module.Types.SelectMany(Flatten).First(t => t.FullName == "Terraria.GameContent.UI.NewMultiplayerClosePlayersOverlay");
        var modernDraw = modern.Methods.Single(m => m.Name == "Draw");
        var modernPlayerLocal = modernDraw.Body.Variables[11];
        var modernInsert = modernDraw.Body.Instructions.First(i => i.IsStlocFor(modernPlayerLocal)).Next;
        var modernActiveLoad = modernDraw.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var modernContinue = (Instruction)modernActiveLoad.Next.Operand;
        InsertLocalPlayerGuard(modernDraw, modernInsert, modernContinue, shouldDrawPlayer, modernPlayerLocal);
    }

    private static void InsertLegacyOverlayGuard(MethodDefinition method, Instruction insertionPoint, Instruction continueTarget, MethodReference shouldDrawPlayer)
    {
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, method.Body.Variables[7]));
        il.InsertBefore(insertionPoint, LoadLocal(il, method.Body.Variables[15]));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Ldelem_Ref));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawPlayer));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, continueTarget));
    }

    private static void InsertLocalPlayerGuard(MethodDefinition method, Instruction insertionPoint, Instruction continueTarget, MethodReference shouldDrawPlayer, VariableDefinition playerLocal)
    {
        var il = method.Body.GetILProcessor();
        il.InsertBefore(insertionPoint, LoadLocal(il, playerLocal));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Call, shouldDrawPlayer));
        il.InsertBefore(insertionPoint, il.Create(OpCodes.Brfalse, continueTarget));
    }

    private static void PatchDamageNumberRendering(ModuleDefinition module, TypeDefinition mainType, MethodReference shouldRenderCombatTextInstance, MethodReference shouldRenderServerPopups)
    {
        var method = mainType.Methods.Single(m => m.Name == "DoDraw");
        var il = method.Body.GetILProcessor();
        var combatTextType = module.Types.First(t => t.FullName == "Terraria.CombatText");
        var combatTextField = mainType.Fields.Single(f => f.Name == "combatText");
        var activeField = combatTextType.Fields.Single(f => f.Name == "active");
        var activeLoad = method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld && i.Operand == activeField);
        var activeBranch = activeLoad.Next;
        var combatContinueTarget = (Instruction)activeBranch.Operand;
        var combatIndexLocal = GetLoadedLocalVariable(method, activeLoad.Previous.Previous);
        if (combatIndexLocal == null)
            throw new InvalidOperationException("Unable to find CombatText loop index local.");

        var combatInsertionPoint = activeBranch.Next;
        var popupTargetScale = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Terraria.PopupText::get_TargetScale"));
        var netplayStatusText = method.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call &&
            i.Operand is MethodReference methodReference &&
            methodReference.FullName.Contains("Terraria.Main::DrawNetplayStatusText"));

        il.InsertBefore(combatInsertionPoint, il.Create(OpCodes.Ldsfld, combatTextField));
        il.InsertBefore(combatInsertionPoint, LoadLocal(il, combatIndexLocal));
        il.InsertBefore(combatInsertionPoint, il.Create(OpCodes.Ldelem_Ref));
        il.InsertBefore(combatInsertionPoint, il.Create(OpCodes.Call, shouldRenderCombatTextInstance));
        il.InsertBefore(combatInsertionPoint, il.Create(OpCodes.Brfalse, combatContinueTarget));

        var popupGate = il.Create(OpCodes.Call, shouldRenderServerPopups);
        il.InsertBefore(popupTargetScale, popupGate);
        il.InsertBefore(popupTargetScale, il.Create(OpCodes.Brfalse, netplayStatusText));
    }

    private static void DumpMethod(ModuleDefinition module, string typeName, string methodName)
    {
        var type = module.Types.SelectMany(Flatten).FirstOrDefault(t => t.FullName == typeName);
        if (type == null)
            throw new InvalidOperationException($"Type not found: {typeName}");

        var methods = type.Methods.Where(m => m.Name == methodName).ToList();
        if (methods.Count == 0)
            throw new InvalidOperationException($"Method not found: {typeName}.{methodName}");

        foreach (var method in methods)
        {
            Console.WriteLine($"{method.FullName}");
            Console.WriteLine($"Locals: {string.Join(", ", method.Body.Variables.Select((v, i) => $"V_{i}:{v.VariableType.FullName}"))}");
            foreach (var instruction in method.Body.Instructions)
                Console.WriteLine($"{instruction.Offset:X4}: {Format(instruction)}");
            Console.WriteLine();
        }
    }

    private static void DumpFieldReferences(ModuleDefinition module, string typeName, string fieldName)
    {
        foreach (var method in module.Types.SelectMany(Flatten).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            if (method.Body.Instructions.Any(i => i.Operand is FieldReference field && field.DeclaringType.FullName == typeName && field.Name == fieldName))
                Console.WriteLine(method.FullName);
        }
    }

    private static void DumpStrings(ModuleDefinition module, string typeName, string methodName)
    {
        var type = module.Types.SelectMany(Flatten).FirstOrDefault(t => t.FullName == typeName);
        if (type == null)
            throw new InvalidOperationException($"Type not found: {typeName}");
        foreach (var method in type.Methods.Where(m => m.Name == methodName && m.HasBody))
        {
            Console.WriteLine(method.FullName);
            foreach (var instruction in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr))
                Console.WriteLine($"{instruction.Offset:X4}: \"{instruction.Operand}\"");
        }
    }

    private static string Format(Instruction instruction)
    {
        return instruction.Operand switch
        {
            Instruction target => $"{instruction.OpCode} IL_{target.Offset:X4}",
            Instruction[] targets => $"{instruction.OpCode} {string.Join(", ", targets.Select(t => "IL_" + t.Offset.ToString("X4")))}",
            MethodReference method => $"{instruction.OpCode} {method.FullName}",
            FieldReference field => $"{instruction.OpCode} {field.FullName}",
            TypeReference type => $"{instruction.OpCode} {type.FullName}",
            _ => instruction.ToString()
        };
    }

    private static bool IsStlocFor(this Instruction instruction, VariableDefinition variable)
    {
        var index = variable.Index;
        if (instruction.OpCode == OpCodes.Stloc_0)
            return index == 0;
        if (instruction.OpCode == OpCodes.Stloc_1)
            return index == 1;
        if (instruction.OpCode == OpCodes.Stloc_2)
            return index == 2;
        if (instruction.OpCode == OpCodes.Stloc_3)
            return index == 3;
        if (instruction.OpCode == OpCodes.Stloc || instruction.OpCode == OpCodes.Stloc_S)
            return instruction.Operand == variable;
        return false;
    }

    private static bool IsLdlocFor(this Instruction instruction, VariableDefinition variable)
    {
        var index = variable.Index;
        if (instruction.OpCode == OpCodes.Ldloc_0)
            return index == 0;
        if (instruction.OpCode == OpCodes.Ldloc_1)
            return index == 1;
        if (instruction.OpCode == OpCodes.Ldloc_2)
            return index == 2;
        if (instruction.OpCode == OpCodes.Ldloc_3)
            return index == 3;
        if (instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S)
            return instruction.Operand == variable;
        return false;
    }

    private static Instruction LoadLocal(ILProcessor il, VariableDefinition variable)
    {
        return variable.Index switch
        {
            0 => il.Create(OpCodes.Ldloc_0),
            1 => il.Create(OpCodes.Ldloc_1),
            2 => il.Create(OpCodes.Ldloc_2),
            3 => il.Create(OpCodes.Ldloc_3),
            <= byte.MaxValue => il.Create(OpCodes.Ldloc_S, variable),
            _ => il.Create(OpCodes.Ldloc, variable)
        };
    }

    private static Instruction StoreLocal(ILProcessor il, VariableDefinition variable)
    {
        return variable.Index switch
        {
            0 => il.Create(OpCodes.Stloc_0),
            1 => il.Create(OpCodes.Stloc_1),
            2 => il.Create(OpCodes.Stloc_2),
            3 => il.Create(OpCodes.Stloc_3),
            <= byte.MaxValue => il.Create(OpCodes.Stloc_S, variable),
            _ => il.Create(OpCodes.Stloc, variable)
        };
    }

    private static bool IsLoadInt(Instruction instruction, int value)
    {
        if (instruction.OpCode == OpCodes.Ldc_I4)
            return instruction.Operand is int operand && operand == value;
        if (instruction.OpCode == OpCodes.Ldc_I4_S)
            return instruction.Operand is sbyte operand && operand == value;
        if (value == -1 && instruction.OpCode == OpCodes.Ldc_I4_M1)
            return true;
        if (value >= 0 && value <= 8)
        {
            return value switch
            {
                0 => instruction.OpCode == OpCodes.Ldc_I4_0,
                1 => instruction.OpCode == OpCodes.Ldc_I4_1,
                2 => instruction.OpCode == OpCodes.Ldc_I4_2,
                3 => instruction.OpCode == OpCodes.Ldc_I4_3,
                4 => instruction.OpCode == OpCodes.Ldc_I4_4,
                5 => instruction.OpCode == OpCodes.Ldc_I4_5,
                6 => instruction.OpCode == OpCodes.Ldc_I4_6,
                7 => instruction.OpCode == OpCodes.Ldc_I4_7,
                8 => instruction.OpCode == OpCodes.Ldc_I4_8,
                _ => false
            };
        }
        return false;
    }

    private static VariableDefinition? GetLoadedLocalVariable(MethodDefinition method, Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Ldloc_0)
            return method.Body.Variables[0];
        if (instruction.OpCode == OpCodes.Ldloc_1)
            return method.Body.Variables[1];
        if (instruction.OpCode == OpCodes.Ldloc_2)
            return method.Body.Variables[2];
        if (instruction.OpCode == OpCodes.Ldloc_3)
            return method.Body.Variables[3];
        if (instruction.OpCode == OpCodes.Ldloc || instruction.OpCode == OpCodes.Ldloc_S)
            return instruction.Operand as VariableDefinition;
        return null;
    }
}
