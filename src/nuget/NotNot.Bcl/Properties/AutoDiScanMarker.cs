// Opts this assembly INTO NotNot AutoDI scanning (AddNotNotDiServices).
// NotNot.Bcl calls the scan via _NotNotEzSetup and contains auto-scanned types (e.g.
// OperatorService : IDiSingletonService). Marker gate replaces the former assembly-name inference.
[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]
