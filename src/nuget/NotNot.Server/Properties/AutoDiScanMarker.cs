// Opts this assembly INTO NotNot AutoDI scanning (AddNotNotDiServices).
// NotNot.Server calls the scan via _NotNotEzSetup_Server and contains auto-scanned types (Cache<TValue>,
// GCloudUtils, StripeServiceLayer). Marker gate replaces the former assembly-name inference.
[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]
