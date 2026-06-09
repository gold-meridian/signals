namespace Signals.Systems;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SystemAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class GeneratedSystemBindingAttribute(int queryId) : Attribute {
    public int QueryId => queryId;
}