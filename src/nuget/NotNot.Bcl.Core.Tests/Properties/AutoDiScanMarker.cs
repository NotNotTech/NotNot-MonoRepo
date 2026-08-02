// Opts this assembly INTO NotNot AutoDI scanning.
// AutoDiRuntimeBypassTests calls AddNotNotDiServices(typeof(AutoDiRuntimeBypassTests).Assembly) — an
// EXPLICIT scanAssemblies list. Under the marker contract the explicit path fails fast on any named
// assembly lacking [assembly: AutoDiScanAssembly], so the test assembly must carry the marker for its
// scan fixtures (ScannedHostedFixture / ScannedSingletonFixture) to register.
[assembly: NotNot.Bcl.Diagnostics.AutoDiScanAssembly]
