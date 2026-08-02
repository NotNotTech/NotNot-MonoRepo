using NotNot.BlazorAnalyzers.XRay;

namespace NotNot.BlazorAnalyzers.Tests.XRay;

/// <summary>
/// Tests for XRayMarkerSetDriftAnalyzer (NNB046).
/// Production-shape fixtures: a bare-init FIELD (XRayHelper._fallbackMarkers shape) and a
/// collection-expression method-LOCAL (XRayEndpoints.fallbackPrefixes shape) — both declared subjects
/// must trip the analyzer on drift and stay silent when in-sync. Slash normalization, the XRay-namespace
/// guard, and the name guard are each pinned by a dedicated negative.
/// </summary>
public class XRayMarkerSetDriftAnalyzerTests
{
    private static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<XRayMarkerSetDriftAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        if (expected?.Length > 0)
        {
            test.ExpectedDiagnostics.AddRange(expected);
        }

        await test.RunAsync();
    }

    // The MessageFormat carries two args: {0}=mirror name, {1}=the drift detail clause.
    // CSharpAnalyzerTest matches WithArguments positionally against the formatted args, so each
    // expected diagnostic must supply BOTH. `detail` defaults to the segment-set clause.
    private const string SegmentDetail = "its segment set differs from the canonical 5";
    private const string LeadingTrailingDetail =
        "its slash convention is wrong (this mirror requires LEADING+TRAILING slashes, e.g. \"/Features/\")";
    private const string TrailingOnlyDetail =
        "its slash convention is wrong (this mirror requires TRAILING-ONLY slashes, e.g. \"Features/\")";

    private static DiagnosticResult Drift(string name, string detail = SegmentDetail) =>
        new DiagnosticResult(XRayMarkerSetDriftAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(name, detail);

    /// <summary>
    /// POSITIVE-1: divergent bare-init FIELD (mirrors XRayHelper._fallbackMarkers shape (a)) → fires.
    /// </summary>
    [Fact]
    public async Task NNB046_DivergentMarkerSet_BareInitField_FiresAtField()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class Mirror
{
    private static readonly string[] {|#0:_fallbackMarkers|} =
        { ""/Features/"", ""/Pages/"", ""/Shared/"", ""/Components/"" }; // /Layout/ dropped -> drift
}";
        await VerifyAsync(source, Drift("_fallbackMarkers"));
    }

    /// <summary>
    /// POSITIVE-2: divergent collection-expression method-LOCAL (mirrors XRayEndpoints.fallbackPrefixes
    /// shape (c)) → fires. Proves the LOCAL path actually trips the analyzer.
    /// </summary>
    [Fact]
    public async Task NNB046_DivergentMarkerSet_CollectionExprLocal_FiresAtLocal()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class M
{
    static void X()
    {
        string[] {|#0:fallbackPrefixes|} = [""Features/"", ""Pages/"", ""Shared/"", ""Areas/""]; // Layout/Components -> Areas -> drift
        _ = fallbackPrefixes;
    }
}";
        await VerifyAsync(source, Drift("fallbackPrefixes"));
    }

    /// <summary>
    /// NEGATIVE-1: in-sync slashed bare-init FIELD (the canonical 5 WITH slashes) → silent.
    /// </summary>
    [Fact]
    public async Task NNB046_InSync_SlashedBareInitField_Silent()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class Mirror
{
    private static readonly string[] _fallbackMarkers =
        { ""/Features/"", ""/Pages/"", ""/Shared/"", ""/Layout/"", ""/Components/"" };
}";
        await VerifyAsync(source);
    }

    /// <summary>
    /// NEGATIVE-2: in-sync slash-less collection-expression method-LOCAL (mirrors XRayEndpoints.cs:105
    /// exactly) → silent. Proves normalization + the local-declaration path + collection-expression handling.
    /// </summary>
    [Fact]
    public async Task NNB046_InSync_SlashlessCollectionExprLocal_Silent()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class M
{
    static void X()
    {
        string[] fallbackPrefixes = [""Features/"", ""Pages/"", ""Shared/"", ""Layout/"", ""Components/""];
        _ = fallbackPrefixes;
    }
}";
        await VerifyAsync(source);
    }

    /// <summary>
    /// NEGATIVE-3: same NAME, NON-XRay namespace → silent. Proves the namespace half of the identification guard.
    /// </summary>
    [Fact]
    public async Task NNB046_NonXRayNamespace_Silent()
    {
        var source = @"
namespace Some.Other.Place;
internal static class NotXRay
{
    private static readonly string[] _fallbackMarkers =
        { ""/Foo/"", ""/Bar/"" }; // same name, wrong namespace -> must NOT fire
}";
        await VerifyAsync(source);
    }

    /// <summary>
    /// NEGATIVE-4: XRay namespace, WRONG name → silent. Proves the name half of the identification guard.
    /// </summary>
    [Fact]
    public async Task NNB046_XRayNamespace_WrongName_Silent()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class M
{
    private static readonly string[] _otherList = { ""/Foo/"", ""/Bar/"" }; // XRay ns, non-mirror name -> must NOT fire
}";
        await VerifyAsync(source);
    }

    /// <summary>
    /// POSITIVE-3 (slash-convention axis): _fallbackMarkers (the LEADING+TRAILING site) with the CORRECT
    /// segments but the WRONG (slash-LESS) form → fires. The IndexOf/Substring(idx+1) fallback consumer
    /// requires the leading slash; a slash-less copy silently breaks resolution.
    /// </summary>
    [Fact]
    public async Task NNB046_SlashConventionDrift_FieldSlashless_FiresAtField()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class Mirror
{
    private static readonly string[] {|#0:_fallbackMarkers|} =
        { ""Features/"", ""Pages/"", ""Shared/"", ""Layout/"", ""Components/"" }; // correct segments, WRONG (trailing-only) form
}";
        await VerifyAsync(source, Drift("_fallbackMarkers", LeadingTrailingDetail));
    }

    /// <summary>
    /// POSITIVE-4 (slash-convention axis): fallbackPrefixes (the TRAILING-ONLY site) with the CORRECT
    /// segments but the WRONG (leading-slash) form → fires. The prefix+file Path.Combine consumer requires
    /// the trailing-only form; a leading-slash copy silently breaks endpoint resolution.
    /// </summary>
    [Fact]
    public async Task NNB046_SlashConventionDrift_LocalLeadingSlash_FiresAtLocal()
    {
        var source = @"
namespace NotNot.BlazorComponents.XRay;
internal static class M
{
    static void X()
    {
        string[] {|#0:fallbackPrefixes|} = [""/Features/"", ""/Pages/"", ""/Shared/"", ""/Layout/"", ""/Components/""]; // correct segments, WRONG (leading+trailing) form
        _ = fallbackPrefixes;
    }
}";
        await VerifyAsync(source, Drift("fallbackPrefixes", TrailingOnlyDetail));
    }
}
