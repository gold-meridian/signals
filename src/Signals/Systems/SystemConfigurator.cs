namespace Signals.Systems;

/// <summary>
///     A builder for configuring, and registering systems with a fluent syntax api.
/// <remarks>
///     This is the main api for system registration.
/// </remarks>
/// </summary>
public ref struct SystemConfigurator(App app, Delegate systemFn, SystemExecutor? executor = null) {
    private readonly App app = app;
    private readonly SystemFunction system = new SystemFunction(systemFn, executor);
    
    private readonly List<Tag> tags = new();
    private readonly List<Tag> requiredTags = new();
    
    private readonly List<string> after = new();
    private readonly List<string> before = new();
    
    private Type callback;
    private Func<World, bool> condition;
    
    public SystemConfigurator InCallback<T>() where T : struct {
        callback = typeof(T);
        return this;
    }
    
    public SystemConfigurator When(Func<World, bool> condition) {
        this.condition = condition;
        return this;
    }

    public SystemConfigurator WithTag(string tagName) {
        tags.Add(Tags.GetOrCreate(tagName));
        return this;
    }

    public SystemConfigurator WithTags(ReadOnlySpan<string> tagNames) {
        foreach (var name in tagNames)
            tags.Add(Tags.GetOrCreate(name));
        return this;
    }

    public SystemConfigurator RequireTag(string tagName) {
        requiredTags.Add(Tags.GetOrCreate(tagName));
        return this;
    }

    public SystemConfigurator RequireTags(ReadOnlySpan<string> tagNames) {
        foreach (var name in tagNames)
            requiredTags.Add(Tags.GetOrCreate(name));
        return this;
    }

    public SystemConfigurator After(params string[] labels) {
        after.AddRange(labels);
        return this;
    }

    public SystemConfigurator Before(params string[] labels) {
        before.AddRange(labels);
        return this;
    }

    public void Build() {
        var description = new SystemDescription() {
            Function = system,
            CallbackType = callback,
            Tags = tags,
            RequiredTags = requiredTags,
            RunAfter = after,
            RunBefore = before,
            RunCondition = condition
        };
        
        app.RegisterSystem(description, system.Method);
    }
}