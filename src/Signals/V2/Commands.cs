using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Signals.V2;

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
    private readonly List<IDeferredCommand> localCommands = new();
    private readonly List<uint> spawnedEntityIds = new();
    private readonly object @lock = new();

    public bool IsInitialized => world != null;

    public void Fetch(World world) {
        lock (@lock) {
            this.world = world;
            localCommands.Clear();
            spawnedEntityIds.Clear();
        }
    }
    
    internal void Apply() {
        lock (@lock) {
            if (world == null) return;

            for (int i = 0; i < localCommands.Count; i++) {
                localCommands[i].Execute(world, this);
            }
            localCommands.Clear();
            spawnedEntityIds.Clear();
        }
    }
    
    public EntityCommands Spawn() {
        lock (@lock) {
            int spawnIndex = spawnedEntityIds.Count;
            spawnedEntityIds.Add(0); 
            localCommands.Add(new SpawnEntityCommand(spawnIndex));
            return new EntityCommands(this, new DeferredEntityRef(spawnIndex));
        }
    }

    public EntityCommands Entity(uint entityId) {
        return new EntityCommands(this, new DeferredEntityRef(entityId));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void QueueCommand(IDeferredCommand command) {
        lock (@lock) {
            localCommands.Add(command);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetSpawnedEntityId(int index, uint entityId) => spawnedEntityIds[index] = entityId;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint GetSpawnedEntityId(int index) => spawnedEntityIds[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint ResolveEntityId(in DeferredEntityRef entityRef) {
        return entityRef.IsSpawned ? GetSpawnedEntityId(entityRef.SpawnIndex) : entityRef.EntityId;
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