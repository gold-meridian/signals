namespace Signals.V2;

public readonly struct Entity(int id, int generation, World world) {
    public readonly int Id = id;
    public readonly int Generation = generation;
    public readonly World World = world;

    public bool IsAlive => World.IsValid(Id, Generation);

    public ref T Get<T>() where T : struct => ref World.Get<T>(Id);
    public void Set<T>(in T value) where T : struct => World.Set<T>(Id, value);
    public bool Has<T>() where T : struct => World.Has<T>(Id);
    public void Destroy() => World.Destroy(Id, Generation);
}