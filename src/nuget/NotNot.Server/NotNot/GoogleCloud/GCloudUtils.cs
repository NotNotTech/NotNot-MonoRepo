using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using NotNot;


public class GCloudUtils(ILogger<GCloudUtils> _logger) : ISingletonService, IAutoInitialize
{
   private AsyncLock _lock = new();

   /// <summary>
   ///    ensures logged in to gcloud.  if not, runs `gcloud auth application-default login`
   ///    This is automatically run upon creation of this service, but may be called again if needed.
   /// </summary>
   /// <returns></returns>
   public async Task TryLogin(bool forceRelogin = false)
   {
      using (await _lock.LockAsync())
      {
         //login if needed
         //check if credentials file exists
         //see https://cloud.google.com/docs/authentication/application-default-credentials#personal

         var credentialsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "gcloud",
            "application_default_credentials.json");

         if (forceRelogin && File.Exists(credentialsFile))
         {
            try
            {
               File.Delete(credentialsFile);
            }
            catch (DirectoryNotFoundException)
            {
               //ignore
            }
         }

         if (!File.Exists(credentialsFile))
         {
            _logger
               .LogInformation("credentials file not found, running gcloud auth application-default login...");
            var startInfo = new ProcessStartInfo
            {
               UseShellExecute = true, //needed for system env vars to be used
               FileName = "gcloud.cmd",
               Arguments = "auth application-default login",
            };

            //execute gcloud cmdline to store default application credentials by logging in
            await Process.Start(startInfo)!.WaitForExitAsync();
         }
         else
         {
            _logger.LogInformation("credentials file found.  already logged in");
         }
      }
   }

   async ValueTask IAutoInitialize.AutoInitialize(IServiceProvider services, CancellationToken ct)
   {
      await TryLogin();
   }



   //private int _retryDelayMs = 20;
   //private int _retryCount = 5;

   ///// <summary>
   ///// Retry the action when a Grpc.Core.RpcException is thrown.
   ///// </summary>
   //public async Task RetryRpc(Func<Task> action, CancellationToken ct)
   //{
   //   var result = await RetryRpc(async () => { await action(); return 0; }, ct);
   //}
   //public async Task<T> RetryRpc<T>(Func<Task<T>> action, CancellationToken ct)
   //{
   //   List<Grpc.Core.RpcException>? exceptions = null;
   //   var delayMs = _retryDelayMs;
   //   for (int tryCount = 0; tryCount < _retryCount; ++tryCount)
   //   {
   //      try
   //      {
   //         await action();
   //      }
   //      catch (Grpc.Core.RpcException e)
   //      {
   //         if (exceptions == null)
   //            exceptions = new List<Grpc.Core.RpcException>();
   //         exceptions.Add(e);
   //      }
   //      //System.Threading.Thread.Sleep(delayMs);
   //      await __.Async.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
   //      delayMs *= 2;  // Exponential back-off.
   //   }
   //   throw new GRpcAggregateException(exceptions!);
   //}

}

public class GRpcAggregateException : AggregateException
{
   public GRpcAggregateException(IEnumerable<Grpc.Core.RpcException> exceptions) : base(exceptions)
   {  
   }
}
