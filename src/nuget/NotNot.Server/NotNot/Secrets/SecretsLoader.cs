using System;
using System.Text;
using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;

namespace NotNot.Secrets;

/// <summary>
/// load secrets from providers (just google cloud secret manager for now)
/// using secrets stored in appsettings.json under the `NotNot.SecretsLoader` node.
/// </summary>
public static class SecretsLoader
{
   /// <summary>
   /// Synchronously loads all secrets defined in appsettings.json `NotNot.SecretsLoader` node from providers and injects into the IConfigurationBuilder
   /// </summary>
   /// <param name="builder"></param>
   /// <returns></returns>
   /// <exception cref="InvalidOperationException"></exception>
   public static IConfigurationBuilder LoadSecrets(IConfigurationBuilder builder)
   {
      var secretsSectionName = "NotNot:SecretsLoader";
      //create a temp config to load up the secrets config from appsettings.json, before the "real" buider does its thing
      var tempConfig = builder.Build();
      var allSecrets = tempConfig.GetSection(secretsSectionName).GetChildren();

      foreach (var secretSection in allSecrets)
      {
         var key = secretSection.Key;
         var configSection = secretSection.GetSection("config");
         var provider = configSection["provider"];
         //var dataSection = secretSection.GetSection("data");

         switch (provider)
         {
            case "GoogleCloudSecretManagerV1":
               LoadSecretFromGoogleCloud(builder, secretsSectionName, key, configSection);
               break;
            // Additional providers can be implemented here
            default:
               throw new InvalidOperationException($"Unknown AppSettingsJson:{secretSection.Path}.config.provider: \"{provider}\"");
         }
      }

      return builder;
   }

   private static void LoadSecretFromGoogleCloud(IConfigurationBuilder builder, string secretsSectionName, string key, IConfigurationSection configSection)//, IConfigurationSection dataSection)
   {
      var projectId = configSection["projectId"]!;
      var secretId = configSection["secretId"]!;
      var secretVersionId = configSection["secretVersionId"];
      var onError = configSection["onError"] ?? "throw";

      SecretManagerServiceClient client = SecretManagerServiceClient.Create();

      if(secretVersionId is null)
      {
         ////get latest version
         secretVersionId = "latest"; //alias for the latest version

         ////projects/853031807592/secrets/pjsc_legacy_development/versions/latest
         //var results = client.ListSecretVersions(new SecretName(projectId, secretId));
         //client.GetSecretVersion(new SecretName(projectId, secretId));
         //client.GetSecretVersion(new SecretVersionName(projectId, secretId, "latest"));
      }

      SecretVersionName secretVersionName = new SecretVersionName(projectId, secretId, secretVersionId);
      AccessSecretVersionResponse result = client.AccessSecretVersion(secretVersionName);
      string payload = result.Payload.Data.ToStringUtf8();

      //build new config from the payload
      {
         var tempBuilder = new ConfigurationBuilder();
         tempBuilder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(payload)));
         var actualData = tempBuilder.Build();

         ValidateExpectedConfiguration(configSection.GetSection("expected_data"), actualData, onError);

         //add to the real builder
         foreach (var item in actualData.GetChildren())
         {
            builder.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>($"{secretsSectionName}:{key}:Data:{item.Key}", item.Value) });
         }
      }
   }

   /// <summary>
   /// validate expected data exists
   /// </summary>
   private static void ValidateExpectedConfiguration(IConfigurationSection expectedData, IConfigurationRoot actualData, string onError)
   {
      foreach (var expectedItem in expectedData.GetChildren())
      {
         var expectedKey = expectedItem.Key;
         var expectedValue = expectedItem.Value;
         var actualValue = actualData[expectedKey];

         if (actualValue != expectedValue)
         {
            switch (onError)
            {
               case "ignore":
                  break;
               case "throw":
               default:
                  throw new InvalidOperationException($"Expected configuration '{expectedKey}' value '{expectedValue}' does not match actual value. '{actualValue}'");
            }

         }
      }
   }
}
