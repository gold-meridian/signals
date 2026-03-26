using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Signals;

internal interface IDeferredCommand {
    void Execute(World world, Commands commands);
}

public readonly struct DeferredEntityRef {
    public readonly int SpawnIndex;
    public readonly uint EntityId;

    public DeferredEntityRef(int spawnIndex) {
        SpawnIndex = spawnIndex;
        EntityId = 0;
    }

    public DeferredEntityRef(uint entityId) {
        SpawnIndex = -1;
        EntityId = entityId;
    }

    public bool IsSpawned => SpawnIndex >= 0;
}

internal readonly struct SpawnEntityCommand(int spawnIndex) : IDeferredCommand {
    private readonly int spawnIndex = spawnIndex;

    public void Execute(World world, Commands commands) {
        var entity = world.Create();
        commands.SetSpawnedEntityId(spawnIndex, entity.Id);
    }
}

internal readonly struct DespawnEntityCommand(DeferredEntityRef entityRef) : IDeferredCommand {
    private readonly DeferredEntityRef entityRef = entityRef;

    public void Execute(World world, Commands commands) {
        var entityId = commands.ResolveEntityId(entityRef);
        if (world.Exists(entityId)) {
            world.Destroy(entityId, world.Generations[entityId]);
        }
    }
}

internal readonly struct InsertComponentCommand<T>(DeferredEntityRef entityRef, T component) : IDeferredCommand where T : struct {
    private readonly DeferredEntityRef entityRef = entityRef;
    private readonly T component = component;

    public void Execute(World world, Commands commands) {
        var entityId = commands.ResolveEntityId(entityRef);
        if (world.Exists(entityId)) {
            world.Set(entityId, component);
        }
    }
}

internal readonly struct RemoveComponentCommand<T>(DeferredEntityRef entityRef) : IDeferredCommand where T : struct {
    private readonly DeferredEntityRef entityRef = entityRef;

    public void Execute(World world, Commands commands) {
        var entityId = commands.ResolveEntityId(entityRef);
        if (world.Exists(entityId) && world.Has<T>(entityId)) {
            world.Remove<T>(entityId);
        }
    }
}

public sealed class Commands {
    private World? world;
    private readonly ConcurrentBag<IDeferredCommand> localCommands = new();
    private uint[] spawnedEntityIds = new uint[1024];
    private int spawnedEntityCount = 0;
    private readonly object resizeLock = new();

    public bool IsInitialized => world != null;

    public void Fetch(World world) {
        this.world = world;
        localCommands.Clear();
        Array.Clear(spawnedEntityIds);
        spawnedEntityCount = 0;
    }

    internal void Apply() {
        if (world == null) return;

        var commands = localCommands.ToList();
    
        foreach (var cmd in commands) {
            if (cmd is SpawnEntityCommand) {
                cmd.Execute(world, this);
            }
        }

        foreach (var cmd in commands) {
            if (cmd is not SpawnEntityCommand) {
                cmd.Execute(world, this);
            }
        }

        localCommands.Clear();
        Array.Clear(spawnedEntityIds, 0, spawnedEntityCount);
        spawnedEntityCount = 0;
    }

    public EntityCommands Spawn() {
        int spawnIndex = 
            Interlocked.Increment(ref spawnedEntityCount) - 1;
        
        if (spawnIndex >= spawnedEntityIds.Length) {
            lock (resizeLock) {
                if (spawnIndex >= spawnedEntityIds.Length) {
                    Array.Resize(ref spawnedEntityIds, Math.Max(spawnIndex + 1, spawnedEntityIds.Length * 2));
                }
            }
        }

        localCommands.Add(new SpawnEntityCommand(spawnIndex));
        return new EntityCommands(this, new DeferredEntityRef(spawnIndex));
    }

    public EntityCommands Entity(uint entityId) {
        return new EntityCommands(this, new DeferredEntityRef(entityId));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void QueueCommand(IDeferredCommand command) {
        localCommands.Add(command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetSpawnedEntityId(int index, uint entityId) => spawnedEntityIds[index] = entityId;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint GetSpawnedEntityId(int index) => spawnedEntityIds[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint ResolveEntityId(in DeferredEntityRef entityRef) {
        return entityRef.IsSpawned
            ? GetSpawnedEntityId(entityRef.SpawnIndex)
            : entityRef.EntityId;
    }
}

public readonly ref struct EntityCommands(Commands commands, DeferredEntityRef entityRef) {
    private readonly Commands commands = commands;
    private readonly DeferredEntityRef entityRef = entityRef;
    
    public readonly EntityCommands Set<T>(T component) where T : struct {
        commands.QueueCommand(new InsertComponentCommand<T>(entityRef, component));
        return this;
    }

    public readonly EntityCommands Remove<T>() where T : struct {
        commands.QueueCommand(new RemoveComponentCommand<T>(entityRef));
        return this;
    }

    public readonly void Despawn() {
        commands.QueueCommand(new DespawnEntityCommand(entityRef));
    }
}