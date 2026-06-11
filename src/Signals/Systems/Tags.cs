using System.Collections.Concurrent;
using System.Numerics;

namespace Signals.Systems;

public readonly struct Tag : IEquatable<Tag> {
    public readonly uint Id;

    public bool IsValid => Id != 0;

    internal Tag(uint id) => Id = id;

    public bool Equals(Tag other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is Tag other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(Tag a, Tag b) => a.Equals(b);
    public static bool operator !=(Tag a, Tag b) => !a.Equals(b);
    public override string ToString() => $"Tag #{Id}";
}

public static class Tags {
    internal struct Data {
        public string Name;
        public SystemHandle[] Systems;
        public uint SystemCount;
    }

    private static readonly ConcurrentDictionary<string, uint> tagIdsByString = new(StringComparer.Ordinal);
    private static Data[] tagDataById = new Data[64];
    private static uint tagCount = 0;

    public static uint Count => tagCount;
    
    public static string GetName(Tag tag) => tagDataById[tag.Id].Name;

    public static Tag GetOrCreate(string name) {
        if (tagIdsByString.TryGetValue(name, out uint id))
            return new Tag(id);

        var data = new Data {
            Name = name,
            Systems = GC.AllocateUninitializedArray<SystemHandle>(8),
            SystemCount = 0
        };

        id = tagCount++;

        if (id >= tagDataById.Length)
            Array.Resize(ref tagDataById, 
                (int)BitOperations.RoundUpToPowerOf2(id + 1));

        tagDataById[id] = data;
        tagIdsByString[name] = id;

        return new Tag(id);
    }

    public static ReadOnlySpan<SystemHandle> GetSystems(Tag tag) {
        var data = tagDataById[tag.Id];
        return new ReadOnlySpan<SystemHandle>(data.Systems, 0, (int)data.SystemCount);
    }

    internal static void AddSystem(Tag tag, SystemHandle system) {
        ref var data = ref tagDataById[tag.Id];
        uint index = data.SystemCount++;

        if (index >= data.Systems.Length)
            Array.Resize(ref data.Systems, 
                (int)BitOperations.RoundUpToPowerOf2(index + 1));

        data.Systems[index] = system;
    }
    
    public static ReadOnlySpan<string> GetSystemTagNames(SystemHandle handle) {
        ref readonly var description = ref SystemStorage.GetDescription(handle);
        var names = new string[description.Tags.Count];
        
        for (int i = 0; i < description.Tags.Count; i++)
            names[i] = GetName(description.Tags[i]);
        
        return names;
    }
}