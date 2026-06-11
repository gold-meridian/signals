using System.Numerics;
using System.Reflection;

namespace Signals.Systems;

/// <summary>
///     A unique handle assigned to a registered system,
///     providing providing access to its description.
/// </summary>
public readonly struct SystemHandle(uint id) {
    public readonly uint Id = id;
    public bool IsValid => Id != 0;
    
    public ref readonly SystemDescription Description => ref SystemStorage.GetDescription(this);

    public override string ToString() {
        return $"system handle: {Description.Function.Method.Name} #{Id}";
    }
}

internal static class SystemStorage {
    private static SystemDescription[] descriptions = new SystemDescription[64];
    private static Dictionary<MethodInfo, SystemHandle> systemsByMethod = new();
    private static uint systemCount = 0;

    public static uint SystemCount => systemCount;

    public static ref SystemDescription GetDescription(SystemHandle handle) => ref descriptions[handle.Id];

    public static bool TryGetSystem(MethodInfo method, out SystemHandle result) => systemsByMethod.TryGetValue(method, out result);

    public static SystemHandle GetSystem(MethodInfo method) 
        => TryGetSystem(method, out var result) ? result : throw new InvalidOperationException($"system not found for method {method.Name}!");

    internal static void Register(ref SystemDescription description, MethodInfo method) {
        uint id = ++systemCount;
        var handle = new SystemHandle(id);
        description.Handle = handle;

        if (descriptions.Length <= id)
            Array.Resize(ref descriptions, (int)BitOperations.RoundUpToPowerOf2(id + 1));

        descriptions[id] = description;
        systemsByMethod[method] = handle;
    }
}