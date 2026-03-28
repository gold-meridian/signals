namespace Signals.Systems;

public readonly struct Stage(int id, string name) : IEquatable<Stage> {
    private static int idCounter = 0;

    public readonly int Id = id;
    public readonly string Name = name;

    public static Stage Create(string name) {
        int id = Interlocked.Increment(ref idCounter) - 1;
        return new Stage(id, name);
    }

    public static readonly Stage Update = Create("Update");

    public bool Equals(Stage other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is Stage other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(Stage a, Stage b) => a.Equals(b);
    public static bool operator !=(Stage a, Stage b) => !a.Equals(b);
    public override string ToString() => Name;
}