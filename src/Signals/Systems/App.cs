using System.Numerics;
using System.Reflection;

namespace Signals.Systems;

/*
 
 todo,
    - better name for app? 
    - observer pattern for watching entity creations / component removals, etc
    - messages / signals

 */


public struct AppConfig() {
    public string Label;
}

public sealed class App {
    private readonly World world;
    private readonly Dictionary<Stage, List<SystemDescription>> stages = new();
    private readonly Dictionary<string, SystemHandle> systemsByLabel = new();
    private SystemDescription[] systemsById = new SystemDescription[64];
    private Dictionary<MethodInfo, SystemHandle> systemsByMethod = new();
    private uint systemCount = 0;

    public App(World world) => this.world = world;

    public SystemConfigurator AddGeneratedSystem(Delegate systemFn, SystemExecutor executor) => new SystemConfigurator(this, systemFn, executor);

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