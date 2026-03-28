using System.Reflection;

namespace Signals.Systems;

/*
 todo,
    - better name for app? 
    - automatic parameter resolving (currently hardcoded to just World and Commands, and uses DynamicInvoke
    - observer pattern for watching entity creations / component removals, etc
    - messages / signals
 */

public delegate void SystemDelegate(World world);

public ref struct SystemBuilder {
    private readonly App _app;
    private readonly SystemFunction _system;
    private Stage _stage = Stage.Update;
    private string? _label;
    private List<string> _after = new();
    private List<string> _before = new();

    public SystemBuilder(App app, Delegate systemFn) {
        _app = app;
        _system = new SystemFunction(systemFn);
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
        _app.RegisterSystem(new SystemMetadata {
            Function = _system,
            Stage = _stage,
            Label = _label,
            RunAfter = _after,
            RunBefore = _before
        });
    }
}

public struct SystemFunction {
    private readonly Delegate _delegate;
    private readonly ParameterInfo[] _parameters;

    public SystemFunction(Delegate del) {
        _delegate = del;
        _parameters = del.Method.GetParameters();
    }

    public void Execute(World world, Commands commands) {
        var args = new object[_parameters.Length];
        
        for (int i = 0; i < _parameters.Length; i++) {
            var paramType = _parameters[i].ParameterType;
            
            if (paramType == typeof(World)) args[i] = world;
            else if (paramType == typeof(Commands)) args[i] = commands;
            else throw new ArgumentException($"unsupported parameter type: {paramType.Name}");
        }

        _delegate.DynamicInvoke(args);
    }
}

public struct SystemMetadata() {
    public SystemFunction Function;
    public Stage Stage;
    public string? Label;
    public List<string> RunAfter;
    public List<string> RunBefore;
}

public class App {
    private readonly World _world;
    private readonly Dictionary<Stage, List<SystemMetadata>> _stages = new();
    private readonly Dictionary<string, SystemMetadata> _labeledSystems = new();

    public App(World world) => _world = world;

    public SystemBuilder AddSystem(Delegate systemFn) 
        => new SystemBuilder(this, systemFn);

    internal void RegisterSystem(SystemMetadata metadata) {
        if (metadata.Label != null) 
            _labeledSystems[metadata.Label] = metadata;

        if (!_stages.ContainsKey(metadata.Stage))
            _stages[metadata.Stage] = new();

        _stages[metadata.Stage].Add(metadata);
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

    private List<SystemMetadata> Sort(List<SystemMetadata> systems) {
        int systemCount = systems.Count;
        var result = new List<SystemMetadata>();
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
                if (systemIndexMap.TryGetValue(afterLabel, 
                        out int depIndex)) {
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