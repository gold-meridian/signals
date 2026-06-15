using System.Numerics;
using System.Reflection;

namespace Signals.Systems;

/// <summary>
///     A unique handle assigned to a registered system,
///     providing providing access to its description.
/// </summary>
public readonly struct SystemHandle(u32 id) {
    public readonly u32 Id = id;
    public bool IsValid => Id != 0;
    
    public ref readonly SystemDescription Description => ref SystemStorage.GetDescription(this);

    public override string ToString() {
        return $"system handle: {Description.Function.Method.Name} #{Id}";
    }
}

/// <summary>
///     A delegate signature for executing a system.
/// </summary>
public delegate void SystemExecutor(Delegate system, World world, Commands commands);

/// <summary>
///     A builder for configuring, and registering systems with a fluent syntax api.
/// <remarks>
///     This is the main api for system registration.
/// </remarks>
/// </summary>
public ref struct SystemConfigurator(App app, Delegate systemFn, SystemExecutor? executor = null) {
    private readonly App app = app;
    private readonly SystemFunction system = new SystemFunction(systemFn, executor);
    private Stage stage = Stage.Update;
    private readonly List<Tag> tags = new();
    private readonly List<Tag> requiredTags = new();
    private readonly List<string> after = new();
    private readonly List<string> before = new();

    private Func<World, bool> condition;
    
    public SystemConfigurator When(Func<World, bool> condition) {
        this.condition = condition;
        return this;
    }

    public SystemConfigurator InStage(Stage stage) {
        this.stage = stage;
        return this;
    }

    public SystemConfigurator WithTag(string tagName) {
        tags.Add(Tags.GetOrCreate(tagName));
        return this;
    }

    public SystemConfigurator WithTags(ReadOnlySpan<string> tagNames) {
        foreach (var name in tagNames)
            tags.Add(Tags.GetOrCreate(name));
        return this;
    }

    public SystemConfigurator RequireTag(string tagName) {
        requiredTags.Add(Tags.GetOrCreate(tagName));
        return this;
    }

    public SystemConfigurator RequireTags(ReadOnlySpan<string> tagNames) {
        foreach (var name in tagNames)
            requiredTags.Add(Tags.GetOrCreate(name));
        return this;
    }

    public SystemConfigurator After(params string[] labels) {
        after.AddRange(labels);
        return this;
    }

    public SystemConfigurator Before(params string[] labels) {
        before.AddRange(labels);
        return this;
    }

    public void Build() {
        var description = new SystemDescription() {
            Function = system,
            Stage = stage,
            Tags = tags,
            RequiredTags = requiredTags,
            RunAfter = after,
            RunBefore = before,
            RunCondition = condition
        };
        
        app.RegisterSystem(description, system.Method);
    }
}

public struct SystemFunction {
    private readonly Delegate systemDelegate;
    private SystemExecutor executor;
    
    public System.Reflection.MethodInfo Method => systemDelegate.Method;

    public SystemFunction(Delegate del, SystemExecutor? executor = null) {
        systemDelegate = del;
        this.executor = executor ?? MakeDynamicExecutor(del);
    }

    public void Execute(World world, Commands commands) {
        executor(systemDelegate, world, commands);
    }

    private static SystemExecutor MakeDynamicExecutor(Delegate del) {
        var parameters = del.Method.GetParameters();

        return (system, world, commands) => {
            var args = new object[parameters.Length];
        
            for (int i = 0; i < parameters.Length; i++) {
                var paramType = parameters[i].ParameterType;
            
                if (paramType == typeof(World)) args[i] = world;
                else if (paramType == typeof(Commands)) args[i] = commands;
                else throw new ArgumentException($"unsupported parameter type: {paramType.Name}");
            }

            system.DynamicInvoke(args);
        };
    }
}

public struct SystemDescription() {
    public SystemHandle Handle;
    public SystemFunction Function;
    public Stage Stage;
    public List<Tag> Tags;
    public List<Tag> RequiredTags;
    
    public List<string> RunAfter;
    public List<string> RunBefore;

    public Func<World, bool> RunCondition;
}

internal static class SystemStorage {
    private static SystemDescription[] descriptions = new SystemDescription[64];
    private static Dictionary<MethodInfo, SystemHandle> systemsByMethod = new();
    private static u32 systemCount = 0;

    public static u32 SystemCount => systemCount;

    public static ref SystemDescription GetDescription(SystemHandle handle) => ref descriptions[handle.Id];

    public static bool TryGetSystem(MethodInfo method, out SystemHandle result) => systemsByMethod.TryGetValue(method, out result);

    public static SystemHandle GetSystem(MethodInfo method) 
        => TryGetSystem(method, out var result) ? result : throw new InvalidOperationException($"system not found for method {method.Name}!");

    internal static void Register(ref SystemDescription description, MethodInfo method) {
        u32 id = ++systemCount;
        var handle = new SystemHandle(id);
        description.Handle = handle;

        if (descriptions.Length <= id)
            Array.Resize(ref descriptions, (int)BitOperations.RoundUpToPowerOf2(id + 1));

        descriptions[id] = description;
        systemsByMethod[method] = handle;
    }
}