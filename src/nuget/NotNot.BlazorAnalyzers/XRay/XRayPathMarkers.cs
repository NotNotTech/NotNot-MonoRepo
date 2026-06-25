namespace NotNot.BlazorAnalyzers.XRay;

/// <summary>
/// SSOT for the XRay single-segment folder markers. The source generator's relative-path
/// FALLBACK (<see cref="XRayMetadataGenerator"/>) and the NNB046 drift analyzer's canonical
/// baseline both consume this ONE array, so the in-assembly producers cannot drift from each
/// other. The cross-assembly runtime consumer mirrors
/// (NotNot.BlazorComponents.XRay.XRayHelper._fallbackMarkers and
/// XRayEndpoints.fallbackPrefixes) cannot reference this const (the analyzer is referenced
/// analyzer-only, ReferenceOutputAssembly=false) — NNB046 enforces their equality at compile
/// time instead. Slashes are the generator's normalized-path form; NNB046 normalizes them away
/// before comparing the segment SET (membership, not order).
/// </summary>
internal static class XRayPathMarkers
{
    public static readonly string[] SingleSegment =
        { "/Features/", "/Pages/", "/Shared/", "/Layout/", "/Components/" };
}
