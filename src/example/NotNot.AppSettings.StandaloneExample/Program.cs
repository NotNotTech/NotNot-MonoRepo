namespace NotNot.AppSettings.StandaloneExample;

public class Program
{
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var appSettings = builder.Configuration._AppSettings();
		Console.WriteLine($"appSettings.AllowedHosts={appSettings.AllowedHosts}");

		// Add services to the container.

		builder.Services.AddControllers();

		var app = builder.Build();

		// Configure the HTTP request pipeline.

		app.UseHttpsRedirection();

		app.UseAuthorization();


		app.MapControllers();

		app.Run();



	}
}
