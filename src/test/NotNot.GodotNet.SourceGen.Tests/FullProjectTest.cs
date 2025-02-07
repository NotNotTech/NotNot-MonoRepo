using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NotNot.GodotNet.SourceGen.Tests;
public class FullProjectTest
{
   [Fact]
   public void TestGeneratedCode()
   {
      // Arrange
      var inputCompilation = CreateCompilation(@"
            namespace InputNamespace
            {
                public class InputClass
                {
                    public void TestMethod() { }
                }
            }");

      var generator = new NotNotSceneGenerator();

      // Act
      var driver = CSharpGeneratorDriver.Create(generator);
      driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation, out var diagnostics);

      // Assert
      Assert.Empty(diagnostics);
      Assert.NotNull(outputCompilation.GetTypeByMetadataName("GeneratedNamespace.GeneratedClass"));
   }

   private static Compilation CreateCompilation(string source)
   {
      return CSharpCompilation.Create("compilation",
         new[] { CSharpSyntaxTree.ParseText(source) },
         new[]
         {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
         },
         new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
   }
}