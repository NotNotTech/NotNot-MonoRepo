; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
NN_A001 | Architecture | Warning | MaybeReturnContractAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_A001)
NN_A002 | Architecture | Warning | DirectMaybeReturnAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_A002)
NN_A003 | Architecture | Error | ForbidConcreteApiResponseAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_A003)
NN_A004 | Architecture | Warning | ForbidApiExceptionCatchAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_A004)
NN_A005 | Architecture | Warning | IgnoredApiResponseAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_A005)
NN_C001 | Naming | Error | RefVarNamingAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C001)
NN_C002 | Naming | Error | RefPrefixMustBeRefAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C002)
NN_C003 | CodeStyle | Error | BoolDefaultFalseAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C003)
NN_C004 | CodeStyle | Error | AppSettingsCodeDefaultAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C004)
NN_C005 | CodeStyle | Error | NnAppSettingsServerOnlyReadAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_C005)
NN_CULTURE_BANNED | NotNot_Architecture | Error | NnCultureBannedAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_CULTURE_BANNED)
NN_DI_001 | Reliability | Error | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_001)
NN_DI_002 | Design | Warning | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_002)
NN_DI_003 | Design | Info | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_003)
NN_DI_004 | Reliability | Error | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_004)
NN_DI_005 | Design | Info | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_005)
NN_DI_006 | Reliability | Error | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_006)
NN_DI_007 | Reliability | Error | DiMarkerEnforcementAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_DI_007)
NN_R001 | Reliability | Error | TaskAwaitedOrReturnedAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R001)
NN_R002 | Reliability | Error | TaskResultNotObservedAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R002)
NN_R003 | Reliability | Error | NullMaybeValueAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R003)
NN_R004 | Reliability | Info | ToMaybeExceptionAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R004)
NN_R005 | Reliability | Error | CatchBlockMustRethrowAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R005)
NN_R006 | Reliability | Error | EmptyCatchBlockAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R006)
NN_R007 | Reliability | Error | HandRolledAtomicFileWriteAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R007)
NN_R008 | Reliability | Error | DirectFileAppendAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R008)
NN_R009 | Reliability | Error | PeriodicTimerDisposalRaceAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NN_R009)
NOTNOT001 | Reliability | Error | DestructorExceptionSafetyAnalyzer, [Documentation](https://github.com/NotNotTech/NotNot-MonoRepo/tree/master/src/nuget/NotNot.Analyzers/#NOTNOT001)
