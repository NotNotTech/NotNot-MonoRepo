﻿
/** 
 * This file is generated by the NotNot.AppSettings nuget package (v0.0.0-0.localDebug).
 * Do not edit this file directly, instead edit the appsettings.json files and rebuild the project.
 * `AddBinderShims()` was called.
**/

using Microsoft.Extensions.Configuration;
namespace AppSettingsGen;


/// <summary>
/// Strongly typed AppSettings.json, recreated every build. 
/// <para>You can use this directly, extend it (it's a partial class), 
/// or get a populated instance of it via the <see cref="AppSettingsBinder"/> DI service</para>
/// </summary>
internal partial class AppSettings
{
}

/// <summary>
/// a DI service that contains a strongly-typed copy of your appsettings.json
/// <para><strong>DI Usage:</strong></para>
/// <para><c>builder.Services.AddSingleton&lt;IAppSettingsBinder, AppSettingsBinder&gt;();</c></para>
/// <para><c>var app = builder.Build();</c></para>
///  <para><c>var appSettings = app.Services.GetRequiredService&lt;IAppSettingsBinder&gt;().AppSettings;</c></para>
/// <para><strong>Non-DI Usage:</strong></para>
/// <para><c>var appSettings = AppSettingsBinder.LoadDirect();</c></para>
/// </summary>
internal partial class AppSettingsBinder : IAppSettingsBinder
{
	public AppSettings AppSettings { get; protected set; }

	public AppSettingsBinder(IConfiguration _config)
	{
		AppSettings = new AppSettings();

		//automatically reads and binds to config file
		_config.Bind(AppSettings);
	}

	/// <summary>
	/// Manually construct an AppSettings from your appsettings.json files.
	/// <para>NOTE: This method is provided for non-DI users.  If you use DI, don't use this method.  Instead just register this class as a service.</para>
	/// </summary>
	/// <param name="appSettingsLocation">folder where to search for appsettings.json.  defaults to current app folder.</param>
	/// <param name="appSettingsFileNames">lets you override the files to load up.  defaults to 'appsettings.json' and 'appsettings.{DOTNET_ENVIRONMENT}.json'</param>
	/// <param name="throwIfFilesMissing">default is to silently ignore if any of the .json files are missing.</param>
	/// <returns>your strongly typed appsettings with values from your .json loaded in</returns>
	public static AppSettings LoadDirect(string? appSettingsLocation = null,IEnumerable<string>? appSettingsFileNames=null,bool throwIfFilesMissing=false )
	{      
		//pick what .json files to load
		if (appSettingsFileNames is null)
		{
			//figure out what env
			var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
			env ??= Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
			env ??= Environment.GetEnvironmentVariable("ENVIRONMENT");
			//env ??= "Development"; //default to "Development
			if (env is null)
			{
				appSettingsFileNames = new[] { "appsettings.json" };
			}
			else
			{
				appSettingsFileNames = new[] { "appsettings.json", $"appsettings.{env}.json" };
			}
		}

		//build a config from the specified files
		var builder = new ConfigurationBuilder();
		if (appSettingsLocation != null)
		{
			builder.SetBasePath(appSettingsLocation);
		}
		var optional = !throwIfFilesMissing;
		foreach (var fileName in appSettingsFileNames)
		{         
			builder.AddJsonFile(fileName, optional: optional, reloadOnChange: false); // Add appsettings.json
		}
		IConfigurationRoot configuration = builder.Build();

		//now finally get the appsettings we care about
		var binder = new AppSettingsBinder(configuration);
		return binder.AppSettings;
	}

	/// <summary>
	/// helper to create an AppSettings from a string containing your json
	/// </summary>
	/// <param name="appSettingsJsonText"></param>
	/// <returns></returns>
	public static AppSettings LoadDirectFromText(string appSettingsJsonText)
	{
	

		//build a config from the specified files
		var builder = new ConfigurationBuilder();

		var configurationBuilder = new ConfigurationBuilder();

		IConfigurationRoot configuration;
		using (var stream = new MemoryStream())
		{
			using (var writer = new StreamWriter(stream))
			{
				writer.Write(appSettingsJsonText);
				writer.Flush();
				stream.Position = 0;
				configurationBuilder.AddJsonStream(stream);



				configuration = configurationBuilder.Build();
			}
		}


		//now finally get the appsettings we care about
		var binder = new AppSettingsBinder(configuration);
		return binder.AppSettings;
	}

	/// <summary>
	/// helper to create an AppSettings from strings containing your json
	/// </summary>
	/// <param name="appSettingsJsonText"></param>
	/// <returns></returns>
	public static AppSettings LoadDirectFromTexts(params string[] appSettingsJsonTexts)
	{

		//build a config from the specified files
		var configurationBuilder = new ConfigurationBuilder();

		IConfigurationRoot RecursiveLoader(Queue<string> textsQueue)
		{
			using var stream = new MemoryStream();
			using var writer = new StreamWriter(stream);

			if (textsQueue.Count > 0)
			{
				var appSettingsJsonText = textsQueue.Dequeue();
				writer.Write(appSettingsJsonText);
				writer.Flush();
				stream.Position = 0;
				configurationBuilder.AddJsonStream(stream);

				return RecursiveLoader(textsQueue);
			}
			else
			{
				return configurationBuilder.Build();
			}
		}


		var configuration = RecursiveLoader(new Queue<string>(appSettingsJsonTexts));


		//now finally get the appsettings we care about
		var binder = new AppSettingsBinder(configuration);
		return binder.AppSettings;
	}
}

/// <summary>
/// a DI service that contains a strongly-typed copy of your appsettings.json
/// <para><strong>DI Usage:</strong></para>
/// <para><c>builder.Services.AddSingleton&lt;IAppSettingsBinder, AppSettingsBinder&gt;();</c></para>
/// <para><c>var app = builder.Build();</c></para>
///  <para><c>var appSettings = app.Services.GetRequiredService&lt;IAppSettingsBinder&gt;().AppSettings;</c></para>
/// <para><strong>Non-DI Usage:</strong></para>
/// <para><c>var appSettings = AppSettingsBinder.LoadDirect();</c></para>
/// </summary>
internal interface IAppSettingsBinder
{
	public AppSettings AppSettings { get; }
}
