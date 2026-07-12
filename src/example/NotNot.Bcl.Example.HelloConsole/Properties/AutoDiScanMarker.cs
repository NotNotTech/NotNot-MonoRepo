// Opts this assembly INTO NotNot AutoDI scanning (AddNotNotDiServices).
// Program.cs calls builder._NotNotEzSetup(...) — the default-AppDomain path, which SILENTLY excludes
// unmarked assemblies. HelloHostedService (: BackgroundService, IDiAutoInitialize) auto-registers only
// via this scan (its explicit AddHostedService registration is commented out), so without the marker it
// would never register and the host would run with zero hosted services.
[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]
