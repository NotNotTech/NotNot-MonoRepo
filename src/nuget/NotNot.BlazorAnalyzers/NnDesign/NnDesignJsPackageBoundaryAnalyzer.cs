using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NotNot.BlazorAnalyzers.NnDesign;

/// <summary>
/// NNB048: a design-system component must not reach for application-owned JavaScript.
/// </summary>
/// <remarks>
/// The package cannot see its consumer's scripts. A producer component that calls a global JS
/// identifier works only in the one application that happens to define it and degrades SILENTLY
/// everywhere else — the interop call throws, the catch swallows it, and the feature is simply
/// missing with nothing in the build to say so. That is not hypothetical: a sidebar in this package
/// called <c>vowShared.initSidebarResize</c>, defined in the consuming application, for a long time
/// before anyone noticed, because the only symptom was "resize does nothing" for a consumer nobody
/// had yet.
/// <para>
/// The sanctioned shape is an ES module that ships INSIDE the package and is imported by path:
/// <c>await JS.InvokeAsync&lt;IJSObjectReference&gt;("import", "./_content/NotNot.BlazorDesign/x.js")</c>.
/// Calls made ON the resulting <see cref="object"/> reference are unrestricted — the module was
/// already validated at its import.
/// </para>
/// <para>
/// Decidability: the rule fires only on a CONSTANT identifier. A computed identifier is undecidable
/// and is left alone, preserving the never-false-positive guarantee.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class NnDesignJsPackageBoundaryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Diagnostic ID for the analyzer rule.
    /// </summary>
    public const string DiagnosticId = "NNB048";

    /// <summary>Namespace root of the design-system package this rule fences.</summary>
    private const string PackageNamespace = "NotNot.BlazorDesign";

    /// <summary>Path fragment every in-package JS module import must carry.</summary>
    private const string InPackageModulePrefix = "_content/NotNot.BlazorDesign/";

    /// <summary>
    /// Prefix marking a global the PACKAGE itself registers from its own classic scripts
    /// (<c>nnDesign</c>, <c>nnToast</c>, <c>nnBrowserNotification</c>). These ship with the package, so
    /// they are not an inverted dependency; a module import is preferred but not enforced here.
    /// </summary>
    private const string PackageGlobalPrefix = "nn";

    /// <summary>
    /// Browser-provided roots. An interop identifier rooted here addresses the platform, not any
    /// application, so it is outside this rule's concern. Derived from the roots actually present in the
    /// package plus their obvious siblings; an unlisted platform global is added here or suppressed with
    /// a documented pragma, which keeps the never-false-positive guarantee explicit rather than assumed.
    /// </summary>
    private static readonly ImmutableHashSet<string> BrowserNativeRoots = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "window", "document", "navigator", "location", "history", "screen", "parent", "top", "self",
        "localStorage", "sessionStorage", "indexedDB", "caches", "crypto", "performance", "console",
        "fetch", "eval", "atob", "btoa", "structuredClone", "matchMedia", "getComputedStyle",
        "getSelection", "requestAnimationFrame", "cancelAnimationFrame", "requestIdleCallback",
        "setTimeout", "clearTimeout", "setInterval", "clearInterval",
        "alert", "confirm", "prompt", "open", "close", "print", "focus", "blur", "scrollTo", "scrollBy",
        "Notification", "Intl", "JSON", "Math", "Date", "URL", "Blob", "File", "FileReader",
        "WebSocket", "XMLHttpRequest", "AbortController", "Image", "Audio", "ResizeObserver",
        "IntersectionObserver", "MutationObserver", "BroadcastChannel");

    /// <summary>
    /// The single descriptor for NNB048. One rule, two shapes, distinguished by the message argument:
    /// a global identifier reach, or an import of a module outside the package.
    /// </summary>
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Design-system component must not call application-owned JavaScript",
        messageFormat: "JS interop call '{0}' is rooted in a global that is neither browser-provided nor registered "
            + "by this package, so it can only belong to the consuming application. The package cannot see its "
            + "consumer's scripts: this resolves in the one host that defines it and fails SILENTLY (caught and "
            + "swallowed) for every other consumer. Ship the runtime with the package and import it by path instead: "
            + "JS.InvokeAsync<IJSObjectReference>(\"import\", \"./_content/NotNot.BlazorDesign/{{module}}.js\"), then "
            + "invoke on the returned module reference — calls on that reference are unrestricted. If this root is a "
            + "platform global missing from the analyzer's allow-list, add it there rather than suppressing.",
        category: "NnDesign Convention",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Components inside NotNot.BlazorDesign must not invoke application-owned JavaScript. The design system is "
            + "referenced BY applications and cannot reference them back, so a global identifier such as "
            + "'vowShared.initSidebarResize' is an inverted dependency: it resolves in exactly one host application and "
            + "throws in every other, where the surrounding catch turns the failure into a silently missing feature. "
            + "Identifiers are classified by their ROOT global: a browser-provided root (localStorage, navigator, eval) "
            + "addresses the platform, and an 'nn'-prefixed root is registered by the package's own scripts — neither "
            + "is an inverted dependency and neither is reported. Everything else can only have come from the host. "
            + "The preferred channel remains an ES module shipped in the package's wwwroot and imported by its "
            + "'_content/NotNot.BlazorDesign/' path; invocations on the resulting IJSObjectReference are unrestricted, "
            + "and an import of a module OUTSIDE that path is reported. Only a CONSTANT identifier and a CONSTANT "
            + "module path are analyzed — computed values are undecidable and never reported.",
        helpLinkUri: $"https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.BlazorAnalyzers#{DiagnosticId}");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        // Razor components compile to generated C#, so generated code must be analyzed.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // IJSRuntime's surface is InvokeAsync / InvokeVoidAsync, instance or extension.
        if (!method.Name.StartsWith("Invoke", StringComparison.Ordinal))
            return;

        if (!IsInPackage(context.ContainingSymbol))
            return;

        var receiverType = ResolveReceiverType(invocation, method);
        if (receiverType is null)
            return;

        // A call ON a module reference is fine — the module path was validated at its import.
        if (IsOrImplements(receiverType, "IJSObjectReference"))
            return;
        if (!IsOrImplements(receiverType, "IJSRuntime"))
            return;

        var identifierArgument = FindArgument(invocation, "identifier");
        if (identifierArgument?.Value.ConstantValue is not { HasValue: true, Value: string identifier })
            return; // computed identifier — undecidable, never reported

        if (!string.Equals(identifier, "import", StringComparison.Ordinal))
        {
            // Only a global that is neither the platform's nor the package's own can belong to the
            // consuming application, which is the dependency this rule exists to stop.
            if (!IsOwnedByPlatformOrPackage(identifier))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), identifier));
            }
            return;
        }

        // An import: the module path travels in the params array, so it is only decidable when constant.
        var modulePath = FirstConstantStringInParamsArray(invocation);
        if (modulePath is null)
            return;

        if (modulePath.IndexOf(InPackageModulePrefix, StringComparison.Ordinal) < 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, invocation.Syntax.GetLocation(), $"import {modulePath}"));
        }
    }

    /// <summary>
    /// Instance calls expose the receiver as <see cref="IInvocationOperation.Instance"/>; a reduced
    /// extension call (the shape <c>JS.InvokeVoidAsync(...)</c> actually compiles to) carries it as the
    /// first argument instead, so both forms have to be resolved or the rule misses every real call site.
    /// </summary>
    private static ITypeSymbol? ResolveReceiverType(IInvocationOperation invocation, IMethodSymbol method)
    {
        if (invocation.Instance?.Type is { } instanceType)
            return instanceType;

        if (method.IsExtensionMethod && invocation.Arguments.Length > 0)
            return invocation.Arguments[0].Value?.Type;

        return null;
    }

    private static IArgumentOperation? FindArgument(IInvocationOperation invocation, string parameterName) =>
        invocation.Arguments.FirstOrDefault(a =>
            string.Equals(a.Parameter?.Name, parameterName, StringComparison.Ordinal));

    /// <summary>
    /// Reads the first constant string out of the <c>params object?[]</c> argument, which is where the
    /// module path sits for an <c>("import", path)</c> call. Returns null when the array is absent,
    /// non-literal, or built dynamically — all undecidable, so none are reported.
    /// </summary>
    private static string? FirstConstantStringInParamsArray(IInvocationOperation invocation)
    {
        var argsArgument = FindArgument(invocation, "args");
        if (argsArgument?.Value is not IArrayCreationOperation { Initializer: { } initializer })
            return null;

        foreach (var element in initializer.ElementValues)
        {
            var unwrapped = element is IConversionOperation conversion ? conversion.Operand : element;
            if (unwrapped.ConstantValue is { HasValue: true, Value: string text })
                return text;
        }

        return null;
    }

    /// <summary>
    /// Classifies an interop identifier by its ROOT global (the text before the first dot):
    /// a browser-provided root addresses the platform, and an <c>nn</c>-prefixed root is registered by
    /// the package's own scripts. Anything else can only have come from the host application.
    /// </summary>
    private static bool IsOwnedByPlatformOrPackage(string identifier)
    {
        var dot = identifier.IndexOf('.');
        var root = dot < 0 ? identifier : identifier.Substring(0, dot);

        return root.StartsWith(PackageGlobalPrefix, StringComparison.Ordinal)
            || BrowserNativeRoots.Contains(root);
    }

    /// <summary>Whether the analyzed symbol lives inside the design-system package's namespace tree.</summary>
    private static bool IsInPackage(ISymbol? symbol)
    {
        var ns = symbol?.ContainingNamespace?.ToDisplayString();
        if (ns is null)
            return false;

        return ns == PackageNamespace
            || (ns.StartsWith(PackageNamespace, StringComparison.Ordinal)
                && ns.Length > PackageNamespace.Length
                && ns[PackageNamespace.Length] == '.');
    }

    /// <summary>
    /// Simple-name match against the type and its interfaces. The JS interop names are distinctive
    /// enough that a namespace comparison would add nothing but a second way to drift.
    /// </summary>
    private static bool IsOrImplements(ITypeSymbol type, string interfaceName)
    {
        if (type.Name == interfaceName)
            return true;

        return type.AllInterfaces.Any(i => i.Name == interfaceName);
    }
}
