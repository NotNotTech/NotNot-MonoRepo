using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Hosting;

public static class AssemblyReflectionHelper
{

   /// <summary>
   /// 
   /// </summary>
   /// <param name="scanAssemblies">assemblies you want to scan for automapper and scrutor types.  default is everything: AppDomain.CurrentDomain.GetAssemblies()</param>
   /// <param name="scanIgnore">assemblies to not scan for DI types.   by default this is 'Microsoft.*' because ASP NetCore IHostedService internal registrations conflict.</param>
   /// <param name="matcher"></param>
   /// <returns></returns>
   public static List<Assembly> _FilterAssemblies(IEnumerable<Assembly>? scanAssemblies, IEnumerable<string>? scanIgnore=null, IEnumerable<string>? keepRegardless=null)
   {
      scanAssemblies ??= AppDomain.CurrentDomain.GetAssemblies();
      var targetAssemblies = new List<Assembly>(scanAssemblies);
      scanIgnore ??= new[] { "Microsoft.*" };//, "System.*", "netstandard", "AutoMapper.*", "Serilog.*" }; // by default microsoft so we don't step on it's internal DI registrations

      //ensure this assembly is included in targetAssemblies
      //this is so various DI services inside this assembly can be auto-registered
      var thisAssembly = Assembly.GetExecutingAssembly();
      if (!targetAssemblies.Contains(thisAssembly))
      {
         targetAssemblies.Add(thisAssembly);
      }
      //remove ignored assemblies. 
      var removeMatcher = new Matcher();
      removeMatcher.AddIncludePatterns(scanIgnore);

      var keepMatcher = new Matcher();
      if(keepRegardless is not null)
      {
         keepMatcher.AddIncludePatterns(keepRegardless);
      }

      for (var i = targetAssemblies.Count - 1; i >= 0; i--)
      {
         var current = targetAssemblies[i];
         var name = current.FullName;
         var removeResults = removeMatcher.Match(name);         
         if (removeResults.HasMatches)
         {
            var keepResults = keepMatcher.Match(name);
            if (keepResults.HasMatches is false)
            {
               targetAssemblies.RemoveAt(i);
            }
         }

      }

      return targetAssemblies;
   }
}

/// <summary>
/// mark a DI services and it will be auto-registered for use.  It will have lifetime Singleton, 
/// </summary>
public interface ISingletonService
{
}

/// <summary>
/// mark a DI services and it will be auto-registered for use.  It will have lifetime Transient, 
/// </summary>
public interface ITransientService
{
}

/// <summary>
/// mark a DI services and it will be auto-registered for use.  It will have lifetime Scoped, 
/// </summary>
public interface IScopedService
{
}

/// <summary>
/// use to signal that when your service is created, it should be initialized via a call to .AutoInitialize()
/// <para>note:  already created services returned by serviceDescriptor.ImplementationInstance will not have AutoInitialize() called.</para>
/// <para>also note:  "open generics" services  can not implement a DI decorator interface like IAutoInitialize. Open generic types are not supported for decoration because generic factories are not supported in C# DI. You must do a workaround (for example, init via the ctor instead of IAutoInitialize)</para>
/// </summary>
public interface IAutoInitialize
{
	/// <summary>
	/// override to provide (optional) custom initialization logic for your service
	/// </summary>
	/// <param name="services"></param>
	/// <param name="ct"></param>
	/// <returns></returns>
	ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}


/// <summary>
///  helper  DI service for letting LoLo automatically get a reference to services (__.Services)
/// </summary>
public class LoLoRunner(IServiceProvider _services) : IHostedLifecycleService, IAutoInitialize
{
   public async Task StartAsync(CancellationToken cancellationToken)
   {
      //  _logger._EzTrace("in");
      //  __.Services = _services;
   }

   public async Task StopAsync(CancellationToken cancellationToken)
   {
      //  _logger._EzTrace("in");
   }

   public async Task StartingAsync(CancellationToken cancellationToken)
   {
      //  _logger._EzTrace("in");
      // __.Services = _services;
   }

   public async Task StartedAsync(CancellationToken cancellationToken)
   {
      // _logger._EzTrace("in");
   }

   public async Task StoppingAsync(CancellationToken cancellationToken)
   {
   }

   public async Task StoppedAsync(CancellationToken cancellationToken)
   {
      //   _logger._EzTrace("in");
   }

   public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
   {
      if (__.Services is null)
      {
         __.Services = _services;
      }
   }
}
