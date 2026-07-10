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
NN_R005 | Reliability | Error | CatchBlockMustRethrowAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R005)
NN_C001 | Naming | Error | RefVarNamingAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C001)
NN_C002 | Naming | Error | RefPrefixMustBeRefAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C002)
NN_CULTURE_BANNED | NotNot_Architecture | Error | NnCultureBannedAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_CULTURE_BANNED)
NN_R006 | Reliability | Error | EmptyCatchBlockAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R006)
NN_C004 | CodeStyle | Error | AppSettingsCodeDefaultAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C004)
NN_C005 | CodeStyle | Error | NnAppSettingsServerOnlyReadAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#nn_c005)
NN_R007 | Reliability | Error | HandRolledAtomicFileWriteAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R007)
NN_DI_006 | Reliability | Error | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#nn_di_006)
NN_R008 | Reliability | Error | DirectFileAppendAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R008)
NN_R009 | Reliability | Error | PeriodicTimerDisposalRaceAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R009)
