using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NotNot.AppSettingsHelper;
using Xunit;

namespace NotNot.AppSettings.Tests;

/// <summary>
/// Test settings class that implements ISettingsChangeAware for testing.
/// </summary>
public class TestSettings : ISettingsChangeAware
{
    private Action? _onChanged;
    private string? _name;
    private int? _value;

    public string? Name
    {
        get => _name;
        set
        {
            if (!Equals(_name, value))
            {
                _name = value;
                _onChanged?.Invoke();
            }
        }
    }

    public int? Value
    {
        get => _value;
        set
        {
            if (!Equals(_value, value))
            {
                _value = value;
                _onChanged?.Invoke();
            }
        }
    }

    void ISettingsChangeAware._SetChangeCallback(Action? callback)
    {
        _onChanged = callback;
    }
}

public class SettingsManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _baseFile;
    private readonly string _userFile;

    public SettingsManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"AppSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _baseFile = Path.Combine(_testDir, "appsettings.json");
        _userFile = Path.Combine(_testDir, "appsettings.user.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* Best effort cleanup */ }
    }

    #region LoadAsync Tests

    [Fact]
    public async Task LoadAsync_WhenFileExists_LoadsSettings()
    {
        File.WriteAllText(_baseFile, """{"Name": "Test", "Value": 42}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);

        settings.Name.Should().Be("Test");
        settings.Value.Should().Be(42);
    }

    [Fact]
    public async Task LoadAsync_WhenFileNotExists_ReturnsDefaults()
    {
        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, "nonexistent.json");

        settings.Should().NotBeNull();
        settings.Name.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_MergesUserSettings()
    {
        File.WriteAllText(_baseFile, """{"Name": "Base", "Value": 1}""");
        File.WriteAllText(_userFile, """{"Value": 100}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);

        settings.Name.Should().Be("Base");
        settings.Value.Should().Be(100, "user settings should override base");
    }

    [Fact]
    public async Task LoadAsync_EnablesSave()
    {
        File.WriteAllText(_baseFile, """{"Name": "Test"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        await manager.LoadAsync(default, _baseFile);

        manager.CanSave.Should().BeTrue();
    }

    #endregion

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_WhenDirty_WritesToUserFile()
    {
        File.WriteAllText(_baseFile, """{"Name": "Original"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);

        settings.Name = "Modified";
        await manager.SaveAsync();

        File.Exists(_userFile).Should().BeTrue();
        var userJson = JsonNode.Parse(File.ReadAllText(_userFile))!;
        userJson["Name"]!.GetValue<string>().Should().Be("Modified");
    }

    [Fact]
    public async Task SaveAsync_WhenNotDirty_DoesNothing()
    {
        File.WriteAllText(_baseFile, """{"Name": "Test"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        await manager.LoadAsync(default, _baseFile);
        await manager.SaveAsync();

        File.Exists(_userFile).Should().BeFalse("no changes should mean no user file");
    }

    [Fact]
    public async Task SaveAsync_InReadOnlyMode_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("""{"Name": "Test"}"""u8.ToArray()))
            .Build();

        var manager = new SettingsManager<TestSettings>();
        manager.LoadFromConfiguration(config);

        var act = async () => await manager.SaveAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region AutoSave Tests

    [Fact]
    public async Task EnableAutoSave_WhenPropertyChanges_SavesAfterDebounce()
    {
        File.WriteAllText(_baseFile, """{"Name": "Original"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);
        manager.EnableAutoSave(TimeSpan.FromMilliseconds(50));

        settings.Name = "AutoSaved";

        // Wait for debounce
        await Task.Delay(150);

        File.Exists(_userFile).Should().BeTrue();
        var userJson = JsonNode.Parse(File.ReadAllText(_userFile))!;
        userJson["Name"]!.GetValue<string>().Should().Be("AutoSaved");
    }

    [Fact]
    public async Task EnableAutoSave_InReadOnlyMode_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("""{"Name": "Test"}"""u8.ToArray()))
            .Build();

        var manager = new SettingsManager<TestSettings>();
        manager.LoadFromConfiguration(config);

        var act = () => manager.EnableAutoSave();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DisableAutoSaveAsync_CancelsPendingSave()
    {
        File.WriteAllText(_baseFile, """{"Name": "Original"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);
        manager.EnableAutoSave(TimeSpan.FromMilliseconds(500));

        settings.Name = "Changed";
        await manager.DisableAutoSaveAsync(saveNow: false);

        // Wait to ensure debounce would have fired
        await Task.Delay(100);

        File.Exists(_userFile).Should().BeFalse("auto-save should have been cancelled");
    }

    [Fact]
    public async Task DisableAutoSaveAsync_WithSaveNow_SavesImmediately()
    {
        File.WriteAllText(_baseFile, """{"Name": "Original"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);
        manager.EnableAutoSave(TimeSpan.FromMilliseconds(500));

        settings.Name = "Changed";
        await manager.DisableAutoSaveAsync(saveNow: true);

        File.Exists(_userFile).Should().BeTrue();
    }

    #endregion

    #region ReloadAsync Tests

    [Fact]
    public async Task ReloadAsync_PicksUpExternalChanges()
    {
        File.WriteAllText(_baseFile, """{"Name": "Initial"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        await manager.LoadAsync(default, _baseFile);

        // Simulate external edit
        File.WriteAllText(_baseFile, """{"Name": "ExternalEdit"}""");

        await manager.ReloadAsync();

        manager.Settings.Name.Should().Be("ExternalEdit");
    }

    #endregion

    #region ResetToDefaultsAsync Tests

    [Fact]
    public async Task ResetToDefaultsAsync_DeletesUserFile()
    {
        File.WriteAllText(_baseFile, """{"Name": "Base"}""");
        File.WriteAllText(_userFile, """{"Name": "User"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        await manager.LoadAsync(default, _baseFile);

        await manager.ResetToDefaultsAsync();

        File.Exists(_userFile).Should().BeFalse();
        manager.Settings.Name.Should().Be("Base");
    }

    #endregion

    #region Clear Tests

    [Fact]
    public async Task Clear_ResetsToBaseSettings()
    {
        File.WriteAllText(_baseFile, """{"Name": "Base", "Value": 10}""");
        File.WriteAllText(_userFile, """{"Value": 100}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        await manager.LoadAsync(default, _baseFile);

        manager.Settings.Value.Should().Be(100);

        manager.Clear();

        manager.Settings.Value.Should().Be(10, "should reset to base value");
    }

    [Fact]
    public async Task Clear_TriggersAutoSave()
    {
        File.WriteAllText(_baseFile, """{"Name": "Base"}""");
        File.WriteAllText(_userFile, """{"Name": "User"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        await manager.LoadAsync(default, _baseFile);
        manager.EnableAutoSave(TimeSpan.FromMilliseconds(50));

        manager.Clear();

        await Task.Delay(150);

        // User file should now have cleared values
        var userJson = JsonNode.Parse(File.ReadAllText(_userFile))!;
        userJson["Name"]!.GetValue<string>().Should().Be("Base");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_SavesDirtyChanges()
    {
        File.WriteAllText(_baseFile, """{"Name": "Original"}""");

        var manager = new SettingsManager<TestSettings> { UserSettingsPath = _userFile };
        var settings = await manager.LoadAsync(default, _baseFile);

        settings.Name = "BeforeDispose";
        await manager.DisposeAsync();

        File.Exists(_userFile).Should().BeTrue();
        var userJson = JsonNode.Parse(File.ReadAllText(_userFile))!;
        userJson["Name"]!.GetValue<string>().Should().Be("BeforeDispose");
    }

    #endregion
}

// ConfigurationBuilder extension for tests
file static class ConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddJsonStream(this IConfigurationBuilder builder, Stream stream)
    {
        return builder.Add(new JsonStreamConfigurationSource { Stream = stream });
    }
}

file class JsonStreamConfigurationSource : Microsoft.Extensions.Configuration.IConfigurationSource
{
    public Stream? Stream { get; set; }

    public Microsoft.Extensions.Configuration.IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new JsonStreamConfigurationProvider(this);
    }
}

file class JsonStreamConfigurationProvider : Microsoft.Extensions.Configuration.ConfigurationProvider
{
    private readonly JsonStreamConfigurationSource _source;

    public JsonStreamConfigurationProvider(JsonStreamConfigurationSource source)
    {
        _source = source;
    }

    public override void Load()
    {
        if (_source.Stream == null) return;

        using var reader = new StreamReader(_source.Stream);
        var json = reader.ReadToEnd();
        var node = JsonNode.Parse(json)?.AsObject();
        if (node == null) return;

        Data = FlattenJson(node, "");
    }

    private static Dictionary<string, string?> FlattenJson(JsonObject obj, string prefix)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj)
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Key : $"{prefix}:{prop.Key}";
            if (prop.Value is JsonObject nested)
            {
                foreach (var kvp in FlattenJson(nested, key))
                    result[kvp.Key] = kvp.Value;
            }
            else
            {
                result[key] = prop.Value?.ToString();
            }
        }
        return result;
    }
}
