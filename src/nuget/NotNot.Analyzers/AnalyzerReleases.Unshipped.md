; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NN_R001 | NotNot_Reliability_Concurrency | Error | TaskAwaitedOrReturnedAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R001)
NN_R002 | NotNot_Reliability_Concurrency | Error | TaskResultNotObservedAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R002)
NN_A002 | Architecture | Warning | DirectMaybeReturnAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_A002)
NN_R003 | Reliability | Info | NullMaybeValueAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R003)
NN_R004 | Reliability | Info | ToMaybeExceptionAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R004)
