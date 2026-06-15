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
    public Type CallbackType;
    
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