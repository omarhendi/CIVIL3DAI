namespace CivilAI;

public sealed record PropertyValue(string Name, string Value);
public sealed record DrawingItem(string Kind, string Name, IReadOnlyList<PropertyValue> Properties)
{
    public string Label => $"{Kind}: {Name}";
}
public sealed record DrawingSnapshot(string Drawing, IReadOnlyList<DrawingItem> Items, int Unavailable, int Scanned, IReadOnlyList<string> Diagnostics);
