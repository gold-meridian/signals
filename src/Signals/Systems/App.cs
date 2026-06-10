using System.Reflection;

namespace Signals.Systems;

/*
 todo,
    - better name for app? 
    - observer pattern for watching entity creations / component removals, etc
    - messages / signals
 */

public delegate void SystemDelegate(World world);

public delegate void SystemExecutor(Delegate system, World world, Commands commands);

public ref struct SystemBuilder {
    private readonly App _app;
    private readonly SystemFunction _system;
    private Stage _stage = Stage.Update;
    private string? _label;
    private List<string> _after = new();
    private List<string> _before = new();

    public SystemBuilder(App app, Delegate systemFn, SystemExecutor? executor = null) {
        _app = app;
        _system = new SystemFunction(systemFn, executor);
    }

    public SystemBuilder InStage(Stage stage) {
        _stage = stage;
        return this;
    }

    public SystemBuilder Label(string label) {
        _label = label;
        return this;
    }

    public SystemBuilder After(params string[] labels) {
        _after.AddRange(labels);
        return this;
    }

    public SystemBuilder Before(params string[] labels) {
        _before.AddRange(labels);
        return this;
    }

    public void Build() {
        _app.RegisterSystem(new SystemDescription {
            Function = _system,
            Stage = _stage,
            Label = _label,
            RunAfter = _after,
            RunBefore = _before
        });
    }
}

internal struct SystemFunction {
    private readonly Delegate _delegate;
    private SystemExecutor _executor;

    public SystemFunction(Delegate del, SystemExecutor? executor = null) {
        _delegate = del;
        _executor = executor ?? MakeDynamicExecutor(del);
    }

    public void Execute(World world, Commands commands) {
        _executor(_delegate, world, commands);
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

internal struct SystemDescription() {
    public SystemFunction Function;
    public Stage Stage;
    public string? Label;
    public List<string> RunAfter;
    public List<string> RunBefore;
}

public sealed class App {
    private readonly World _world;
    private readonly Dictionary<Stage, List<SystemDescription>> _stages = new();
    private readonly Dictionary<string, SystemDescription> _labeledSystems = new();

    public App(World world) => _world = world;

    /*
    public SystemBuilder AddSystem(Delegate systemFn) 
        => new SystemBuilder(this, systemFn);
    */

    public SystemBuilder AddGeneratedSystem(Delegate systemFn, SystemExecutor executor)
        => new SystemBuilder(this, systemFn, executor);

    internal void RegisterSystem(SystemDescription description) {
        if (description.Label != null) 
            _labeledSystems[description.Label] = description;

        if (!_stages.ContainsKey(description.Stage))
            _stages[description.Stage] = new();

        _stages[description.Stage].Add(description);
    }

    public void Run() {
        var commands = new Commands();
        commands.Fetch(_world);

        foreach (var (stage, systems) in _stages.OrderBy(kvp => kvp.Key.Id)) {
            var ordered = Sort(systems);
            
            foreach (var system in ordered) {
                system.Function.Execute(_world, commands);
                commands.Apply();
            }
        }
    }

    private List<SystemDescription> Sort(List<SystemDescription> systems) {
        int systemCount = systems.Count;
        var result = new List<SystemDescription>();
        var systemStates = new byte[systemCount];
        var systemIndexMap = new Dictionary<string, int>();

        for (int i = 0; i < systemCount; i++) {
            if (systems[i].Label != null) {
                systemIndexMap[systems[i].Label] = i;
            }
        }

        foreach (var system in systems) {
            foreach (var beforeLabel in system.RunBefore) {
                if (systemIndexMap.TryGetValue(beforeLabel, out int targetIndex)) {
                    if (!systems[targetIndex].RunAfter.Contains(system.Label!)) {
                        systems[targetIndex].RunAfter.Add(system.Label!);
                    }
                    
                }
            }
        }

        const byte bit_visited = 0b_10000000;
        const byte bit_sortable = 0b_01000000;

        for (int i = 0; i < systemCount; i++) {
            systemStates[i] = bit_sortable;
        }

        for (int i = 0; i < systemCount; i++) {
            recursiveVisit(i);
        }

        return result;
        
        void recursiveVisit(int index) {
            ref var state = ref systemStates[index];

            if ((state & bit_visited) != 0) {
                return;
            }

            state |= bit_visited;

            var system = systems[index];

            foreach (var afterLabel in system.RunAfter) {
                if (systemIndexMap.TryGetValue(afterLabel, out int depIndex)) {
                    recursiveVisit(depIndex);
                }
            }

            if ((state & bit_sortable) != 0) {
                result.Add(system);
                state &= unchecked((byte)~bit_sortable);
            }
        }
    }
}