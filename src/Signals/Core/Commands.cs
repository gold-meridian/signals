using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Signals;

internal readonly struct Command {
    internal enum CommandKind : byte {
        Spawn,
        Despawn,
        InsertComponent,
        RemoveComponent,
    }
    
    public readonly CommandKind Kind;
    public readonly u32 EntityId;
    public readonly u16 Generation;
    public readonly i32 ComponentId;
    public readonly object? ComponentData;

    private Command(CommandKind kind, u32 entityId, u16 generation, i32 componentId = -1, object? data = null) {
        Kind = kind;
        EntityId = entityId;
        Generation = generation;
        ComponentId = componentId;
        ComponentData = data;
    }
    
    public static Command Spawn(Entity entity)
        => new(CommandKind.Spawn, entity.Id, entity.Generation);

    public static Command Despawn(Entity entity)
        => new(CommandKind.Despawn, entity.Id, entity.Generation);

    public static Command InsertComponent<T>(Entity entity, in T component) where T : struct
        => new(CommandKind.InsertComponent, entity.Id, entity.Generation, Component.GetId<T>(), (object)component);

    public static Command RemoveComponent<T>(Entity entity) where T : struct
        => new(CommandKind.RemoveComponent, entity.Id, entity.Generation, Component.GetId<T>());
}

public sealed class Commands {
    private World? world;
    private readonly List<Command> commands = new(256);

    public bool IsInitialized => world != null;

    public void Fetch(World world) {
        this.world = world;
        commands.Clear();
    }

    internal void Apply() {
        if (world == null) return;

        for (var i = 0; i < commands.Count; i++) {
            var cmd = commands[i];
            if (cmd.Kind == Command.CommandKind.Spawn) {
                world.Activate(cmd.EntityId);
            }
        }

        for (int i = 0; i < commands.Count; i++) {
            var cmd = commands[i];

            switch (cmd.Kind) {
                case Command.CommandKind.Spawn:
                    break;

                case Command.CommandKind.Despawn:
                    if (world.IsValid(cmd.EntityId, cmd.Generation)) {
                        world.Destroy(cmd.EntityId, cmd.Generation);
                    }
                    break;

                case Command.CommandKind.InsertComponent:
                    if (world.IsValid(cmd.EntityId, cmd.Generation)) {
                        ExecuteInsert(cmd);
                    }
                    break;

                case Command.CommandKind.RemoveComponent:
                    if (world.IsValid(cmd.EntityId, cmd.Generation)) {
                        ExecuteRemove(cmd);
                    }
                    break;
            }
        }

        commands.Clear();
    }

    private void ExecuteInsert(Command cmd) {
        var info = Component.GetInfo(cmd.ComponentId);
        var method = typeof(Commands)
            .GetMethod(nameof(ExecuteInsertGeneric), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(info.Type);

        method.Invoke(this, new[] { cmd.ComponentData, cmd.EntityId });
    }

    private void ExecuteInsertGeneric<T>(object data, u32 entityId)
        where T : struct {
        world!.Set(entityId, (T)data);
    }

    private void ExecuteRemove(Command cmd) {
        var info = Component.GetInfo(cmd.ComponentId);
        var method = typeof(World)
            .GetMethod(nameof(World.Remove), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(info.Type);

        method.Invoke(world, new object[] { cmd.EntityId });
    }

    public EntityCommands Spawn() {
        if (world == null)
            throw new InvalidOperationException("commands not initialized");

        var entity = world.ReserveId();
        QueueCommand(Command.Spawn(entity));
        return new EntityCommands(this, entity);
    }

    public EntityCommands Entity(Entity entity) {
        return new EntityCommands(this, entity);
    }

    public EntityCommands Entity(u32 entityId) {
        if (world == null)
            throw new InvalidOperationException("commands not initialized");

        var entity = new Entity(
            entityId,
            world.Generations[entityId],
            world.Id
        );
        return new EntityCommands(this, entity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void QueueCommand(Command cmd) {
        commands.Add(cmd);
    }
}

public readonly ref struct EntityCommands(Commands commands, Entity entity) {
    private readonly Commands commands = commands;
    public readonly Entity Entity = entity;

    public readonly EntityCommands Set<T>(T component)
        where T : struct {
        commands.QueueCommand(Command.InsertComponent(Entity, in component));
        return this;
    }

    public readonly EntityCommands Remove<T>() where T : struct {
        commands.QueueCommand(Command.RemoveComponent<T>(Entity));
        return this;
    }

    public readonly void Despawn() {
        commands.QueueCommand(Command.Despawn(Entity));
    }
}