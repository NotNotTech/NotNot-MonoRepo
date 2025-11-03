using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NotNot.Bcl.Test.Simple;

/// <summary>
/// Assembly-level initialization for test suite.
/// Initializes NotNot.Bcl core services (logging, assertions, etc.) before any tests run.
/// </summary>
public static class TestAssemblyInit
{
   [ModuleInitializer]
   public static void Initialize()
   {
      // Create minimal service provider for test environment
      var services = new ServiceCollection();

      // Add minimal logging (required by LoLoRoot)
      services.AddLogging(builder =>
      {
         builder.AddConsole();
         builder.SetMinimumLevel(LogLevel.Warning); // Quiet during tests
      });

      var serviceProvider = services.BuildServiceProvider();

      // Initialize NotNot.Bcl core services
      // This sets up the __ global helper (logging, assertions, etc.)
      NotNot.LoLoRoot.__.Initialize(serviceProvider);

      // Put LoLoRoot into test mode so its assertions throw managed exceptions
      // that the xUnit runner can capture reliably.
      NotNot.LoLoRoot.__.Test.InitTest();
   }
}
