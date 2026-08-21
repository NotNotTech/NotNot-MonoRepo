using Godot;

namespace NotNot.GodotNet.SourceGen.Consumer;

public partial class AnalyzerProbe : Node
{
    public void TriggerPackagedDiagnostic()
    {
        var multiMesh = new MultiMesh();
        multiMesh.CustomAabb = new Aabb(Vector3.Zero, Vector3.One);
    }
}
