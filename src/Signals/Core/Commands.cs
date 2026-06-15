using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Signals;

public readonly struct EntityIdOnly {
    public readonly int SpawnIndex;
    public readonly u32 EntityId;

    public EntityIdOnly(int spawnIndex) {
        SpawnIndex = spawnIndex;
        EntityId = 0;
    }

    public EntityIdOnly(u32 entityId) {
        SpawnIndex = -1;
        EntityId = entityId;
    }

    public bool IsSpawned => SpawnIndex >= 0;
}

internal readonly struct Command {
    internal enum CommandKind : byte {
        Spawn,
        Despawn,
        InsertComponent,
        RemoveComponent,
    }
    
    public readonly CommandKind Kind;
    public readonly EntityIdOnly EntityIdOnly;
    public readonly int ComponentId;
    public readonly object? ComponentData;

    private Command(CommandKind kind, EntityIdOnly entityIdOnly, int componentId = -1, object? data = null) {
        Kind = kind;
        EntityIdOnly = entityIdOnly;
        ComponentId = componentId;
        ComponentData = data;
    }

    public static Command Spawn(int spawnIndex) 
        => new(CommandKind.Spawn, new(spawnIndex));

    public static Command Despawn(EntityIdOnly entityIdOnly) 
        => new(CommandKind.Despawn, entityIdOnly);

    public static Command InsertComponent<T>(EntityIdOnly entityIdOnly, in T component) where T : struct 
        => new(CommandKind.InsertComponent, entityIdOnly, Component.GetId<T>(), (object)component);

    public static Command RemoveComponent<T>(EntityIdOnly entityIdOnly) where T : struct
        => new(CommandKind.RemoveComponent, entityIdOnly, Component.GetId<T>());
}

public sealed class Commands {
    private World? world;
    private readonly List<Command> commands = new(256);
    private readonly List<Command> spawnCommands = new(64);
    private uint[] spawnedEntityIds = new uint[256];
    private int spawnedEntityCount = 0;

    public bool IsInitialized => world != null;

    public void Fetch(World world) {
        this.world = world;
        commands.Clear();
        spawnCommands.Clear();
        Array.Clear(spawnedEntityIds, 0, spawnedEntityCount);
        spawnedEntityCount = 0;
    }

    internal void Apply() {
        if (world == null) return;

        for (int i = 0; i < spawnCommands.Count; i++) {
            var cmd = spawnCommands[i];
            var entity = world.Create();
            spawnedEntityIds[cmd.EntityIdOnly.SpawnIndex] = entity.Id;
        }

        for (int i = 0; i < commands.Count; i++) {
            var cmd = commands[i];

            var entityId = ResolveEntityId(cmd.EntityIdOnly);
            if (!world.Exists(entityId)) continue;

            switch (cmd.Kind) {
                case Command.CommandKind.Despawn:
                    world.Destroy(
                        entityId,
                        world.Generations[entityId]
                    );
                    break;

                case Command.CommandKind.InsertComponent:
                    ExecuteInsert(cmd, entityId);
                    break;

                case Command.CommandKind.RemoveComponent:
                    ExecuteRemove(cmd, entityId);
                    break;
            }
        }

        commands.Clear();
        spawnCommands.Clear();
    }

    private void ExecuteInsert(Command cmd, uint entityId) {
        var info = Component.GetInfo(cmd.ComponentId);
        var method = typeof(Commands)
            .GetMethod(
                nameof(ExecuteInsertGeneric),
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance
            )!
            .MakeGenericMethod(info.Type);

        method.Invoke(this, new[] { cmd.ComponentData, entityId });
    }

    private void ExecuteInsertGeneric<T>(object data, uint entityId) where T : struct {
        world!.Set(entityId, (T)data);
    }

    private void ExecuteRemove(Command cmd, uint entityId) {
        var info = Component.GetInfo(cmd.ComponentId);
        var method = typeof(World)
            .GetMethod(
                nameof(World.Remove),
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance
            )!
            .MakeGenericMethod(info.Type);

        method.Invoke(world, new object[] { entityId });
    }

    public EntityCommands Spawn() {
        int spawnIndex = spawnedEntityCount++;
        if (spawnIndex >= spawnedEntityIds.Length) {
            Array.Resize(ref spawnedEntityIds, Math.Max(spawnIndex + 1, spawnedEntityIds.Length * 2));
        }

        spawnCommands.Add(Command.Spawn(spawnIndex));
        return new EntityCommands(this, new EntityIdOnly(spawnIndex));
    }

    public EntityCommands Entity(uint entityId) {
        return new EntityCommands(this, new EntityIdOnly(entityId));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void QueueCommand(Command cmd) {
        commands.Add(cmd);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint ResolveEntityId(in EntityIdOnly entityIdOnly) {
        return entityIdOnly.IsSpawned
            ? spawnedEntityIds[entityIdOnly.SpawnIndex]
            : entityIdOnly.EntityId;
    }
}

public readonly ref struct EntityCommands(Commands commands, EntityIdOnly entityIdOnly ) {
    private readonly Commands commands = commands;
    private readonly EntityIdOnly entityIdOnly = entityIdOnly;

    public readonly EntityCommands Set<T>(T component) where T : struct {
        commands.QueueCommand(Command.InsertComponent(entityIdOnly, in component));
        return this;
    }

    public readonly EntityCommands Remove<T>() where T : struct {
        commands.QueueCommand(Command.RemoveComponent<T>(entityIdOnly));
        return this;
    }

    public readonly void Despawn() {
        commands.QueueCommand(Command.Despawn(entityIdOnly));
    }
}