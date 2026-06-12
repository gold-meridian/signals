using System.Numerics;
using System.Reflection;

namespace Signals.Systems;

/*
 
 todo,
    - better name for app? 
    - observer pattern for watching entity creations / component removals, etc
    - messages / signals

 */

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
public ref struct SystemBuilder(App app, Delegate systemFn, SystemExecutor? executor = null) {
    private readonly App app = app;
    private readonly SystemFunction system = new SystemFunction(systemFn, executor);
    private Stage stage = Stage.Update;
    private readonly List<Tag> tags = new();
    private readonly List<Tag> requiredTags = new();
    private readonly List<string> after = new();
    private readonly List<string> before = new();

    private Func<World, bool> condition;
    
    public SystemBuilder When(Func<World, bool> condition) {
        this.condition = condition;
        return this;
    }

    public SystemBuilder InStage(Stage stage) {
        this.stage = stage;
        return this;
    }

    public SystemBuilder WithTag(string tagName) {
        tags.Add(Tags.GetOrCreate(tagName));
        return this;
    }

    public SystemBuilder WithTags(ReadOnlySpan<string> tagNames) {
        foreach (var name in tagNames)
            tags.Add(Tags.GetOrCreate(name));
        return this;
    }

    public SystemBuilder RequireTag(string tagName) {
        requiredTags.Add(Tags.GetOrCreate(tagName));
        return this;
    }

    public SystemBuilder RequireTags(ReadOnlySpan<string> tagNames) {
        foreach (var name in tagNames)
            requiredTags.Add(Tags.GetOrCreate(name));
        return this;
    }

    public SystemBuilder After(params string[] labels) {
        after.AddRange(labels);
        return this;
    }

    public SystemBuilder Before(params string[] labels) {
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

public sealed class App {
    private readonly World world;
    private readonly Dictionary<Stage, List<SystemDescription>> stages = new();
    private readonly Dictionary<string, SystemHandle> systemsByLabel = new();
    private SystemDescription[] systemsById = new SystemDescription[64];
    private Dictionary<MethodInfo, SystemHandle> systemsByMethod = new();
    private uint systemCount = 0;

    public App(World world) => this.world = world;

    public SystemBuilder AddGeneratedSystem(Delegate systemFn, SystemExecutor executor) => new SystemBuilder(this, systemFn, executor);

    internal void RegisterSystem(SystemDescription description, MethodInfo method) {
        SystemStorage.Register(ref description, method);
    
        var handle = description.Handle;
    
        foreach (var tag in description.Tags)
            Tags.AddSystem(tag, handle);

        if (!stages.ContainsKey(description.Stage))
            stages[description.Stage] = new();

        stages[description.Stage].Add(description);
    }

    public void Run() {
        var commands = new Commands();
        commands.Fetch(world);

        foreach (var (stage, systems) in stages.OrderBy(kvp => kvp.Key.Id)) {
            var ordered = Sort(systems);

            foreach (var system in ordered) {
                if (system.RunCondition != null && !system.RunCondition(world))
                    continue;
                
                system.Function.Execute(world, commands);
                commands.Apply();
            }
        }
    }

    private List<SystemDescription> Sort(List<SystemDescription> systems) {
        int count = systems.Count;
        var result = new List<SystemDescription>(count);
        var states = new byte[count];
        var indexMap = new Dictionary<string, int>();
        var handleToIndex = new Dictionary<string, int>();

        for (int i = 0; i < count; i++) {
            handleToIndex[systems[i].Handle.ToString()] = i;
            foreach (var tag in systems[i].Tags) {
                var tagName = Tags.GetName(tag);
                indexMap[tagName] = i;
            }
        }

        foreach (var system in systems) {
            foreach (var beforeLabel in system.RunBefore) {
                if (indexMap.TryGetValue(beforeLabel, out int targetIdx)) {
                    var key = system.Handle.ToString();
                    if (!systems[targetIdx].RunAfter.Contains(key))
                        systems[targetIdx].RunAfter.Add(key);
                }
            }
        }

        const byte bit_visited = 0b_10000000;
        const byte bit_sortable = 0b_01000000;

        for (int i = 0; i < count; i++)
            states[i] = bit_sortable;

        for (int i = 0; i < count; i++)
            recursiveVisit(i);

        return result;

        void recursiveVisit(int index) {
            ref var state = ref states[index];

            if ((state & bit_visited) != 0)
                return;

            state |= bit_visited;

            var system = systems[index];

            foreach (var afterLabel in system.RunAfter) {
                if (handleToIndex.TryGetValue(afterLabel, out int depIdx))
                    recursiveVisit(depIdx);
            }

            if ((state & bit_sortable) != 0) {
                result.Add(system);
                state &= unchecked((byte)~bit_sortable);
            }
        }
    }
}