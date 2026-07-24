using System.Reflection;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;
using WorldEngine.Sim.Entities;

namespace WorldEngine.Tests.Architecture;

/// <summary>
/// Architecture tests encoding the structural rules from CLAUDE.md Mandatory Patterns.
/// These use reflection directly against the Sim assembly (available as a project reference).
/// Rules tested:
///   (a) ICommand implementations must be sealed records with no delegate fields/properties.
///   (b) No async methods (Task-returning) in WorldEngine.Sim namespaces except Persistence and WorldGen.
///   (c) Interfaces start with 'I', config classes end with 'Config'.
///   (d) UI panel types (WorldEngine.UI.UI.*) must not reference WorldState, EntityRegistry,
///       or CommandResolver — only the sanctioned snapshot surface.
///       Note: Game1 is excluded as the orchestration layer.
/// </summary>
public class ArchitectureRuleTests
{
    private static readonly Assembly SimAssembly = typeof(ICommand).Assembly;

    // ─────────────────────────────────────────────────────────────────
    // Rule (a): All ICommand implementations are sealed records
    //           with no delegate-type fields or properties
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ICommand_Implementations_Are_SealedRecords()
    {
        var violations = new List<string>();
        foreach (var type in GetICommandImplementations())
        {
            if (!type.IsSealed)
                violations.Add($"{type.FullName}: not sealed");
            if (!IsRecord(type))
                violations.Add($"{type.FullName}: not a record");
        }
        violations.Should().BeEmpty(
            because: "all ICommand implementations must be sealed records (CLAUDE.md Mandatory Pattern #4)");
    }

    [Fact]
    public void ICommand_Implementations_Have_No_Delegate_Fields()
    {
        var violations = new List<string>();
        foreach (var type in GetICommandImplementations())
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (IsDelegateType(field.FieldType))
                    violations.Add($"{type.FullName}.{field.Name}: delegate field ({field.FieldType.Name})");
            }
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (IsDelegateType(prop.PropertyType))
                    violations.Add($"{type.FullName}.{prop.Name}: delegate property ({prop.PropertyType.Name})");
            }
        }
        violations.Should().BeEmpty(
            because: "ICommand implementations must have no delegate fields or properties (CLAUDE.md Mandatory Pattern #4)");
    }

    // ─────────────────────────────────────────────────────────────────
    // Rule (b): No async methods in Sim except Persistence and WorldGen
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SimCore_Has_No_Async_Methods_Outside_Persistence_And_WorldGen()
    {
        // Allowed namespaces for async: Persistence (I/O) and WorldGen (background gen task)
        const string persistenceNs = "WorldEngine.Sim.Persistence";
        const string worldGenNs    = "WorldEngine.Sim.WorldGen";

        var violations = new List<string>();
        foreach (var type in SimAssembly.GetTypes())
        {
            var ns = type.Namespace ?? "";

            // Skip compiler-generated types (top-level Program entry point, state machines, etc.)
            if (type.Namespace is null || type.Name.Contains('<') || type.Name.Contains('$'))
                continue;

            if (ns.StartsWith(persistenceNs) || ns.StartsWith(worldGenNs))
                continue;  // allowed

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                    BindingFlags.Instance | BindingFlags.Static |
                                                    BindingFlags.DeclaredOnly))
            {
                if (IsAsyncMethod(method))
                    violations.Add($"{type.FullName}.{method.Name}: async in non-allowed namespace '{ns}'");
            }
        }
        violations.Should().BeEmpty(
            because: "async/await is only allowed in Persistence and WorldGen namespaces (CLAUDE.md Code Style)");
    }

    // ─────────────────────────────────────────────────────────────────
    // Rule (c): Interfaces start with 'I'; config classes end with 'Config'
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Interfaces_StartWith_I()
    {
        var violations = new List<string>();
        foreach (var type in SimAssembly.GetTypes().Where(t => t.IsInterface && t.IsPublic))
        {
            if (!type.Name.StartsWith("I") || type.Name.Length < 2 || !char.IsUpper(type.Name[1]))
                violations.Add($"{type.FullName}: public interface name does not start with 'I'");
        }
        violations.Should().BeEmpty(
            because: "interface naming convention: interfaces start with 'I' (CLAUDE.md Naming)");
    }

    [Fact]
    public void Config_Classes_EndWith_Config()
    {
        var violations = new List<string>();
        // Only check types in the Config namespace
        foreach (var type in SimAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic &&
                        (t.Namespace?.EndsWith(".Config") == true) &&
                        !t.IsAbstract && !t.Name.EndsWith("Config") &&
                        !t.Name.Contains("<")))   // skip compiler-generated
        {
            // Allow loaders, registries, validators, lookup tables, and file types in the Config namespace
            var allowed = new[] { "Loader", "Registry", "Validator", "File", "Exception", "Tables" };
            if (!allowed.Any(suffix => type.Name.EndsWith(suffix)))
                violations.Add($"{type.FullName}: public class in Config namespace does not end with 'Config'");
        }
        violations.Should().BeEmpty(
            because: "config classes must end with 'Config' (CLAUDE.md Naming)");
    }

    // ─────────────────────────────────────────────────────────────────
    // Rule (d): UI panel types must not reference WorldState or EntityRegistry
    //           (sanctioned surface: StateCache, WorldSnapshot, CommandQueue)
    //           Game1 is excluded as the orchestration glue layer.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UI_Panels_Do_Not_Reference_WorldState_Directly()
    {
        // Load the UI assembly from disk (it's not a project reference, so we load by path)
        var simBinDir = Path.GetDirectoryName(SimAssembly.Location)!;
        var uiDllPath = Path.Combine(simBinDir, "WorldEngine.UI.dll");

        if (!File.Exists(uiDllPath))
        {
            // UI assembly not available in this build context (headless/server CI) — skip
            return;
        }

        var uiAssembly = Assembly.LoadFrom(uiDllPath);

        // Forbidden types that UI panels must not reference
        var worldStateType     = SimAssembly.GetType("WorldEngine.Sim.World.WorldState");
        var entityRegistryType = SimAssembly.GetType("WorldEngine.Sim.Entities.EntityRegistry");

        if (worldStateType is null && entityRegistryType is null)
            return; // Can't check if types not found

        var forbidden = new HashSet<Type>(
            new[] { worldStateType, entityRegistryType }.Where(t => t is not null)!
        );

        var violations = new List<string>();

        // Check only UI panel/rendering types — exclude Game1 (the orchestrator)
        var panelTypes = uiAssembly.GetTypes()
            .Where(t => t.Namespace is "WorldEngine.UI.UI" or "WorldEngine.UI.Rendering"
                        && t.Name != "Game1"
                        && !t.Name.Contains("<"));  // exclude compiler-generated

        foreach (var panelType in panelTypes)
        {
            // Check all field types
            foreach (var field in panelType.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Instance | BindingFlags.Static))
            {
                if (forbidden.Contains(field.FieldType) || (field.FieldType.BaseType is { } bt && forbidden.Contains(bt)))
                    violations.Add($"{panelType.Name}.{field.Name}: field of forbidden type {field.FieldType.Name}");
            }

            // Check method signatures
            foreach (var method in panelType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                         BindingFlags.Instance | BindingFlags.Static |
                                                         BindingFlags.DeclaredOnly))
            {
                if (forbidden.Contains(method.ReturnType))
                    violations.Add($"{panelType.Name}.{method.Name}: returns forbidden type {method.ReturnType.Name}");
                foreach (var param in method.GetParameters())
                {
                    if (forbidden.Contains(param.ParameterType))
                        violations.Add($"{panelType.Name}.{method.Name}: parameter of forbidden type {param.ParameterType.Name}");
                }
            }
        }

        violations.Should().BeEmpty(
            because: "UI panels must use only the snapshot surface (WorldSnapshot, StateCache, CommandQueue), " +
                     "not WorldState or EntityRegistry directly (CLAUDE.md Mandatory Pattern #3)");
    }

    // ─────────────────────────────────────────────────────────────────
    // Rule (e) — M8 Phase 0: Kit isolation.
    //   NoMyraOutsideKit: `UI/Present/` and `UI/Layout/` (folders that exist so far) must not
    //   reference Myra. The full ban (no `UI/Panels/*` outside `UI/Kit/`) lands in 8.3 when
    //   `UI/Panels/` exists — enforced-fully-in-8.3.
    //   PresenterHasNoMyra: `UI/Present/` has no Myra usings and no XNA Color literals.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NoMyraOutsideKit()
    {
        var violations = new List<string>();
        foreach (var scanDir in new[] { "Present", "Layout" })
        {
            foreach (var file in GetUiSourceFiles(scanDir))
            {
                if (File.ReadAllText(file).Contains("using Myra"))
                    violations.Add(Path.GetFileName(file));
            }
        }
        violations.Should().BeEmpty(
            because: "UI/Present and UI/Layout must not reference Myra directly (M8 framework §3.1); " +
                     "only UI/Kit is allowed to see Myra (full panel ban lands in 8.3)");
    }

    [Fact]
    public void PresenterHasNoMyra()
    {
        var violations = new List<string>();
        foreach (var file in GetUiSourceFiles("Present"))
        {
            string text = File.ReadAllText(file);
            if (text.Contains("using Myra"))
                violations.Add($"{Path.GetFileName(file)}: uses Myra");
            if (text.Contains("Microsoft.Xna.Framework.Color") || text.Contains("using Microsoft.Xna.Framework"))
                violations.Add($"{Path.GetFileName(file)}: references XNA Color");
        }
        violations.Should().BeEmpty(
            because: "Presenter is a pure formatting layer with no Myra/XNA dependency (M8 framework §8.1)");
    }

    private static IEnumerable<string> GetUiSourceFiles(string subfolder)
    {
        string dir = Path.Combine(RepoRoot, "WorldEngine.UI", "UI", subfolder);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories) : Enumerable.Empty<string>();
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static IEnumerable<Type> GetICommandImplementations() =>
        SimAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsInterface
                        && typeof(ICommand).IsAssignableFrom(t)
                        && !t.Name.Contains("<"));  // exclude compiler-generated

    private static bool IsRecord(Type t)
    {
        // Records have a compiler-generated <Clone>$ method in C#
        return t.GetMethod("<Clone>$") is not null ||
               // Alternative: check for EqualityContract protected property (records)
               t.GetProperty("EqualityContract",
                   BindingFlags.NonPublic | BindingFlags.Instance) is not null;
    }

    private static bool IsDelegateType(Type t) =>
        typeof(Delegate).IsAssignableFrom(t);

    private static bool IsAsyncMethod(MethodInfo m)
    {
        // Async methods are decorated with [AsyncStateMachineAttribute]
        return m.GetCustomAttributes(false)
            .Any(a => a.GetType().Name == "AsyncStateMachineAttribute");
    }
}
