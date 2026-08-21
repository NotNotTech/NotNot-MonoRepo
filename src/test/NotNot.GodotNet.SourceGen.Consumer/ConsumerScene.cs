using Godot;
using NotNot;

namespace NotNot.GodotNet.SourceGen.Consumer;

[NotNotSceneRoot]
public partial class ConsumerScene : Node2D
{
}

public static class GeneratedConsumerUsage
{
    public static StringName ImportedAssetPath => _ResPath.Assets_consumer_png;

    public static ConsumerScene CreateScene() => ConsumerScene.InstantiateTscn();
}
