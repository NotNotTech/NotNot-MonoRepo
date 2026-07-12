// Opts this assembly INTO NotNot AutoDI scanning (AddNotNotDiServices).
// NotNot.Bcl.Core contains auto-scanned types (e.g. LoLoRunner : IHostedLifecycleService) and is the
// executing assembly the scanner auto-includes only when marked. Marker gate replaces the former
// assembly-name inference. See NotNot/Diagnostics/AutoDiScanAssemblyAttribute.cs.
[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]
