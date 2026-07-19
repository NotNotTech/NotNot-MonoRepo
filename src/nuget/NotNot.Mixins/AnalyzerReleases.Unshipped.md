; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NNM001 | NotNot.Mixins | Info | InlineCompositionGenerator: processing inline class
NNM002 | NotNot.Mixins | Info | InlineCompositionGenerator: base class source found
NNM003 | NotNot.Mixins | Warning | InlineCompositionGenerator: base class source not available
NNM004 | NotNot.Mixins | Info | InlineCompositionGenerator: members inlined
NNM005 | NotNot.Mixins | Info | InlineCompositionGenerator: output generated
NNM006 | NotNot.Mixins | Warning | InlineCompositionGenerator: no members inlined
NNM007 | NotNot.Mixins | Error | InlineCompositionGenerator: multiple variable declaration not supported
