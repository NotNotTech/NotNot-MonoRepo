using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Hosting;
using NotNot.Serialization;
using Xunit;

namespace NotNot.Bcl.Test.Simple;

/// <summary>
/// The shared log options (<c>__.SerializationHelper._logJsonOptions</c>) are built with their EntityEntry rule and
/// made read-only (<c>MakeReadOnly()</c>) at construction: NotNot.Bcl adds nothing to them at load, EntityEntry
/// renders as <c>ToString()</c>, and adding a converter fails at its own call.
/// </summary>
public class SerializationHelperLogOptionsTests
{
	/// <summary>
	/// Serializing with the shared options before NotNot.Bcl loads must not break NotNot.Bcl host startup.
	/// This method body holds no NotNot.Bcl token, so NotNot.Bcl's module activates only when the
	/// non-inlined helper runs, after step (a).
	/// </summary>
	[Fact]
	public async Task SharedLogOptionsUsedBeforeNotNotBclLoads_HostStillStarts()
	{
		// (a) use the shared options first
		JsonSerializer.Serialize(new { Probe = 1 }, __.SerializationHelper._logJsonOptions);
		Assert.True(__.SerializationHelper._logJsonOptions.IsReadOnly);

		await StartNotNotBclHostAsync();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async Task StartNotNotBclHostAsync()
	{
		// (b) first NotNot.Bcl activation
		var builder = Host.CreateApplicationBuilder();
		await builder._NotNotEzSetup(CancellationToken.None, scanAssemblies: []);

		// (c) host starts and stops
		using var host = builder.Build();
		await host.StartAsync();
		await host.StopAsync();
	}

	[Fact]
	public void EntityEntry_SerializesAsToString_AtDepthLimit_SharedAndFreshOptions()
	{
		using var ctx = new ProbeDbContext();
		var entity = new ProbeEntity { Id = 7, Name = "probe" };
		EntityEntry entry = ctx.Entry((object)entity);
		EntityEntry<ProbeEntity> typed = ctx.Entry(entity);

		// prior use of the shared options
		JsonSerializer.Serialize(new { Probe = 1 }, __.SerializationHelper._logJsonOptions);

		// (iii) inside a writer at depth 10 (discriminates converter order against the depth-truncation factory)
		Assert.Equal(entry.ToString(), SerializeAtDepth10(entry, __.SerializationHelper._logJsonOptions));
		Assert.Equal(typed.ToString(), SerializeAtDepth10(typed, __.SerializationHelper._logJsonOptions));

		// (i) shared options
		Assert.Equal(entry.ToString(), JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(entry, __.SerializationHelper._logJsonOptions)));
		Assert.Equal(typed.ToString(), JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(typed, __.SerializationHelper._logJsonOptions)));

		// (ii) freshly constructed helper
		var fresh = new SerializationHelper()._logJsonOptions;
		Assert.Equal(entry.ToString(), JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(entry, fresh)));
		Assert.Equal(typed.ToString(), JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(typed, fresh)));
	}

	[Fact]
	public void FreshLogOptions_AreReadOnly_AndRejectConverterAdd()
	{
		var h = new SerializationHelper();
		Assert.True(h._logJsonOptions.IsReadOnly);
		Assert.Throws<InvalidOperationException>(() => h._logJsonOptions.Converters.Add(new JsonStringEnumConverter()));
	}

	private static string? SerializeAtDepth10<T>(T value, JsonSerializerOptions options)
	{
		const int depth = 10;
		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			for (var i = 0; i < depth; i++)
			{
				writer.WriteStartArray();
			}
			JsonSerializer.Serialize(writer, value, options);
			for (var i = 0; i < depth; i++)
			{
				writer.WriteEndArray();
			}
		}

		using var doc = JsonDocument.Parse(buffer.ToArray());
		var element = doc.RootElement;
		for (var i = 0; i < depth - 1; i++)
		{
			element = element[0];
		}
		return element[0].Deserialize<string>();
	}

	private sealed class ProbeEntity
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
	}

	private sealed class ProbeDbContext : DbContext
	{
		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
			=> optionsBuilder.UseNpgsql("Host=localhost");

		protected override void OnModelCreating(ModelBuilder modelBuilder)
			=> modelBuilder.Entity<ProbeEntity>().HasKey(e => e.Id);
	}
}
