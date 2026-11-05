using CivilAI;

var messages = new List<string>();
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}
var native = new Probe(false);
var nativeRows = PropertyInspection.ReadScalars(native, false, messages.Add);
Check(native.ReferenceReads == 0, "All five reference-only getters are skipped on native objects");
Check(nativeRows.Single(p => p.Name == "IsReferencedSourceExisting").Value.Contains("Not applicable"), "Native reference properties display not-applicable status");
Check(nativeRows.Single(p => p.Name == "Name").Value == "Ground", "Valid properties survive another getter failure");
Check(messages.Count == 1 && messages[0].Contains("Broken"), "Only the failing non-reference getter is diagnosed");
var unknown = new Probe(false);
PropertyInspection.ReadScalars(unknown, null, _ => { });
Check(unknown.ReferenceReads == 0, "Unknown reference status also skips restricted getters");
var reference = new Probe(true);
var referenceRows = PropertyInspection.ReadScalars(reference, true, _ => { });
Check(reference.ReferenceReads == 5 && referenceRows.Single(p => p.Name == "IsReferencedSourceExisting").Value == "True", "Reference-only getters are read on confirmed reference objects");
var errors = new List<string>();
var failed = PropertyInspection.ReadValue("MinimumElevation", () => throw new InvalidOperationException("No triangles"), errors.Add);
var valid = PropertyInspection.ReadValue("MaximumElevation", () => 12.5, errors.Add);
Check(failed.Value.Contains("No triangles") && valid.Value == "12.5" && errors.Count == 1, "One statistic failure does not prevent the next statistic read");

internal sealed class Probe(bool reference)
{
    public int ReferenceReads { get; private set; }
    private bool ReadReference()
    {
        ReferenceReads++;
        if (!reference) throw new InvalidOperationException("It is not a reference entity.");
        return true;
    }
    public string Broken => throw new InvalidOperationException("Probe getter failed");
    public string Name => "Ground";
    public bool IsReferencedSourceExisting => ReadReference();
    public bool IsReferenceValid => ReadReference();
    public bool IsReferenceStale => ReadReference();
    public bool IsReferenceSubObject => ReadReference();
    public bool IsPartialReferenceObject => ReadReference();
}
