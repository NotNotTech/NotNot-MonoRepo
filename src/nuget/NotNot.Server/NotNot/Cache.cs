using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Plugins;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace NotNot;

/// <summary>
/// A strongly typed cache.
/// <para>this is a wrapper around IFusionCache that simplifies usage and fixes some workflow bugs (auto-casting cache items)</para>
/// <para>if you need a generic cache, you can use IFusionCache directly (very similar API)</para>
/// </summary>
/// <typeparam name="TValue"></typeparam>
public class Cache<TValue> : ISingletonService //, IAutoInitialize //Scrutor bug, decoration doesn't work with generics.  so need to workaround by calling init in the .ctor
{
   public Cache(IFusionCache fusionCache, IConfiguration config, ILogger<Cache<TValue>> logger)
   {
      _fusionCache = fusionCache;
      _config = config;
      _logger = logger;

      AutoInitialize();

   }
   private IFusionCache _fusionCache;
   private IConfiguration _config;
   private ILogger<Cache<TValue>> _logger;

   //public async ValueTask AutoInitialize(IServiceProvider services, CancellationToken ct)
   //{
   //   AutoInitialize();
   //}
   private void AutoInitialize()
   {
      //var config = services.GetRequiredService<IConfiguration>();


      var section = _config.GetSection("NotNot:Cache");
      var typeName = typeof(TValue)._GetReadableTypeName();
      var durationSec = section.GetValue<double?>($"CustomDurations:{typeName}");
      var defaultDurationSec = _fusionCache.DefaultEntryOptions.Duration.TotalSeconds;
      if (durationSec is null)
      {
         _logger.LogWarning($"No cache duration found for type {typeName}.  will use defaults instead ({defaultDurationSec} sec).  Add \"{typeName}\" to AppSettings.json under NotNot:Cache:CustomDurations to specify custom durations");
      }

      Duration = TimeSpan.FromSeconds(durationSec ?? defaultDurationSec);
   }
   public TimeSpan Duration { get; set; }
   private string _NamespaceKey(string key)
   {
      return $"{typeof(TValue).FullName}:{key}";
   }
   public bool TryGetValue(string key, [NotNullWhen(true)] out TValue? value)
   {
      key = _NamespaceKey(key);

      var maybeValue = _fusionCache.TryGet<TValue>(key);
      if (maybeValue.HasValue)
      {
         value = maybeValue.Value;
         return true;
      }
      else
      {
         value = default;
         return false;
      }
   }

   public async ValueTask<(bool hasValue, TValue? value)> TryGetValueAsync(string key)
   {
      key = _NamespaceKey(key);

      var maybeValue = await _fusionCache.TryGetAsync<TValue>(key);
      if (maybeValue.HasValue)
      {
         return (true, maybeValue.Value);
      }
      else
      {
         return (false, default);
      }


   }


   public async ValueTask Set(string key, TValue value, CancellationToken token = default)
   {
      key = _NamespaceKey(key);
      await _fusionCache.SetAsync(key, value, Duration, token);
   }

   public async ValueTask<TValue> GetOrSet(string key, Func<CancellationToken, Task<TValue>> factory, CancellationToken token = default)
   {
      key = _NamespaceKey(key);
      var toReturn = await _fusionCache.GetOrSetAsync(key, async (ct) => await factory(ct), Duration, token);
      __.Assert(toReturn is not null);
      return toReturn!;
   }


   public async ValueTask<TValue> GetOrSet(string key, TValue defaultValue, CancellationToken token = default)
   {
      key = _NamespaceKey(key);
      var toReturn = await _fusionCache.GetOrSetAsync(key, defaultValue, Duration, token);
      __.Assert(toReturn is not null);
      return toReturn!;
   }

   public async ValueTask<TValue> GetOrDefault(string key, TValue defaultValue, CancellationToken token = default)
   {
      key = _NamespaceKey(key);
      var toReturn = await _fusionCache.GetOrDefaultAsync(key, defaultValue, options => options.SetDuration(Duration), token);
      __.Assert(toReturn is not null);
      return toReturn!;
   }

}


/// <summary>
/// wrapper over fusionCache for convenience and fix some casting bugs
/// </summary>
/// <param name="_fusionCache"></param>
public class Cache(
IFusionCache _fusionCache
) : ISingletonService
{
   private IFusionCache _fusionCacheImplementation;




   public async ValueTask Set<TValue>(string key, TValue value, TimeSpan duration, CancellationToken token = default)
   {
      await _fusionCache.SetAsync(key, value, duration, token);
   }




   public void Dispose()
   {
      _fusionCacheImplementation.Dispose();
   }

   public FusionCacheEntryOptions CreateEntryOptions(Action<FusionCacheEntryOptions>? setupAction = null, TimeSpan? duration = null)
   {
      return _fusionCacheImplementation.CreateEntryOptions(setupAction, duration);
   }

   public async ValueTask<TValue?> GetOrSetAsync<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, Task<TValue?>> factory, MaybeValue<TValue?> failSafeDefaultValue = new MaybeValue<TValue?>(), FusionCacheEntryOptions? options = null,
      CancellationToken token = new CancellationToken())
   {
      return await _fusionCacheImplementation.GetOrSetAsync(key, factory, failSafeDefaultValue, options, token);
   }

   public TValue? GetOrSet<TValue>(string key, Func<FusionCacheFactoryExecutionContext<TValue>, CancellationToken, TValue?> factory, MaybeValue<TValue?> failSafeDefaultValue = new MaybeValue<TValue?>(), FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return _fusionCacheImplementation.GetOrSet(key, factory, failSafeDefaultValue, options, token);
   }

   public async ValueTask<TValue?> GetOrSetAsync<TValue>(string key, TValue? defaultValue, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return await _fusionCacheImplementation.GetOrSetAsync(key, defaultValue, options, token);
   }

   public TValue? GetOrSet<TValue>(string key, TValue? defaultValue, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return _fusionCacheImplementation.GetOrSet(key, defaultValue, options, token);
   }

   public async ValueTask<TValue?> GetOrDefaultAsync<TValue>(string key, TValue? defaultValue = default, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return await _fusionCacheImplementation.GetOrDefaultAsync(key, defaultValue, options, token);
   }

   public TValue? GetOrDefault<TValue>(string key, TValue? defaultValue = default, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return _fusionCacheImplementation.GetOrDefault(key, defaultValue, options, token);
   }

   public async ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(string key, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return await _fusionCacheImplementation.TryGetAsync<TValue>(key, options, token);
   }

   public MaybeValue<TValue> TryGet<TValue>(string key, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      return _fusionCacheImplementation.TryGet<TValue>(key, options, token);
   }


   public async ValueTask RemoveAsync(string key, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      await _fusionCacheImplementation.RemoveAsync(key, options, token);
   }

   public void Remove(string key, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      _fusionCacheImplementation.Remove(key, options, token);
   }

   public async ValueTask ExpireAsync(string key, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      await _fusionCacheImplementation.ExpireAsync(key, options, token);
   }

   public void Expire(string key, FusionCacheEntryOptions? options = null, CancellationToken token = new CancellationToken())
   {
      _fusionCacheImplementation.Expire(key, options, token);
   }

   public IFusionCache SetupDistributedCache(IDistributedCache distributedCache, IFusionCacheSerializer serializer)
   {
      return _fusionCacheImplementation.SetupDistributedCache(distributedCache, serializer);
   }

   public IFusionCache RemoveDistributedCache()
   {
      return _fusionCacheImplementation.RemoveDistributedCache();
   }

   public IFusionCache SetupBackplane(IFusionCacheBackplane backplane)
   {
      return _fusionCacheImplementation.SetupBackplane(backplane);
   }

   public IFusionCache RemoveBackplane()
   {
      return _fusionCacheImplementation.RemoveBackplane();
   }

   public void AddPlugin(IFusionCachePlugin plugin)
   {
      _fusionCacheImplementation.AddPlugin(plugin);
   }

   public bool RemovePlugin(IFusionCachePlugin plugin)
   {
      return _fusionCacheImplementation.RemovePlugin(plugin);
   }

   public string CacheName => _fusionCacheImplementation.CacheName;

   public string InstanceId => _fusionCacheImplementation.InstanceId;

   public FusionCacheEntryOptions DefaultEntryOptions => _fusionCacheImplementation.DefaultEntryOptions;

   public bool HasDistributedCache => _fusionCacheImplementation.HasDistributedCache;

   public bool HasBackplane => _fusionCacheImplementation.HasBackplane;

   public FusionCacheEventsHub Events => _fusionCacheImplementation.Events;
}


