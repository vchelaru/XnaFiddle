namespace XnaFiddle.Tests;

public class CompileFingerprintTests
{
    [Fact]
    public void Compute_SameInputs_ProducesSameHash()
    {
        var shaders = new List<ShaderFile>
        {
            new ShaderFile { Name = "A.fx", Source = "float4 main() { return 0; }" }
        };
        string a = CompileFingerprint.Compute("class Game1 {}", shaders);
        string b = CompileFingerprint.Compute("class Game1 {}", shaders);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ShaderTabOrder_Ignored()
    {
        var shadersA = new List<ShaderFile>
        {
            new ShaderFile { Name = "B.fx", Source = "b" },
            new ShaderFile { Name = "A.fx", Source = "a" },
        };
        var shadersB = new List<ShaderFile>
        {
            new ShaderFile { Name = "A.fx", Source = "a" },
            new ShaderFile { Name = "B.fx", Source = "b" },
        };
        Assert.Equal(
            CompileFingerprint.Compute("code", shadersA),
            CompileFingerprint.Compute("code", shadersB));
    }

    [Fact]
    public void Compute_CSharpChange_ChangesHash()
    {
        string before = CompileFingerprint.Compute("class A {}", null);
        string after = CompileFingerprint.Compute("class B {}", null);
        Assert.NotEqual(before, after);
    }
}
