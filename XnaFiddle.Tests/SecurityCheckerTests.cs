using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XnaFiddle;

namespace XnaFiddle.Tests;

/// <summary>
/// Tests for SecurityChecker — verifies that forbidden APIs are rejected and
/// safe APIs are allowed in user-submitted code.
///
/// Note: System.Runtime.InteropServices.JavaScript is WASM-only and is not
/// present in the .NET desktop runtime used by the test host, so it cannot
/// be tested via semantic resolution here.
/// </summary>
public class SecurityCheckerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<CompilationService.DiagnosticInfo> Check(string code)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.LatestMajor);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code, parseOptions);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return SecurityChecker.Check(compilation, syntaxTree);
    }

    private static bool HasError(List<CompilationService.DiagnosticInfo> errors, string fragment) =>
        errors.Any(e => e.Message.Contains(fragment));

    /// <summary>
    /// Loads BCL assemblies needed for the security tests. Uses typeof(T).Assembly.Location
    /// to pin each DLL precisely, since types are spread across many small assemblies in .NET 8.
    /// </summary>
    private static MetadataReference[] GetReferences()
    {
        // Collect assembly locations via typeof() — reliable across runtime versions.
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,                                   // System.Runtime
            typeof(System.IO.File).Assembly.Location,                           // System.IO.FileSystem
            typeof(System.IO.Stream).Assembly.Location,
            typeof(System.IO.MemoryStream).Assembly.Location,
            typeof(System.IO.BinaryReader).Assembly.Location,
            typeof(System.Net.Http.HttpClient).Assembly.Location,
            typeof(System.Threading.Thread).Assembly.Location,
            typeof(System.Threading.Tasks.Task).Assembly.Location,
            typeof(System.Diagnostics.Process).Assembly.Location,
            typeof(System.Diagnostics.Debug).Assembly.Location,
            typeof(System.Runtime.InteropServices.Marshal).Assembly.Location,
            typeof(System.Security.Cryptography.SHA256).Assembly.Location,
            typeof(System.AppDomain).Assembly.Location,
            typeof(System.Environment).Assembly.Location,
            typeof(System.Linq.Enumerable).Assembly.Location,
            typeof(System.Linq.Expressions.Expression).Assembly.Location,       // System.Linq.Expressions
            typeof(System.Runtime.CompilerServices.Unsafe).Assembly.Location,   // System.Runtime.CompilerServices.Unsafe
            typeof(System.Collections.Generic.List<>).Assembly.Location,
            // Roslyn itself (tests blocking Microsoft.CodeAnalysis usage).
            typeof(CSharpCompilation).Assembly.Location,
            typeof(Compilation).Assembly.Location,
        };

        // Assemblies that are copied to the output dir but not directly referenced
        // by the test project (Blazor / KNI interop assemblies).
        string[] outputDirDlls =
        [
            "Microsoft.JSInterop.dll",
            "nkast.Wasm.Clipboard.dll",       // assembly name; namespace is nkast.Wasm.WebClipboard
            "nkast.Wasm.JSInterop.dll",        // needed for Clipboard base type resolution
            "nkast.Wasm.Dom.dll",
        ];
        string baseDir = AppContext.BaseDirectory;
        foreach (string dll in outputDirDlls)
        {
            string path = Path.Combine(baseDir, dll);
            if (File.Exists(path))
                locations.Add(path);
        }

        return [.. locations.Select(l => MetadataReference.CreateFromFile(l))];
    }

    // ── Forbidden: System.Reflection ─────────────────────────────────────────

    [Fact]
    public void Reflection_Assembly_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { void M() { var a = Assembly.GetExecutingAssembly(); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_MethodInfo_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { void M() { MethodInfo mi = typeof(string).GetMethod("ToString"); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_FullyQualified_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { var a = System.Reflection.Assembly.GetExecutingAssembly(); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_MethodInfo_Invoke_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { void M() { MethodInfo mi = typeof(string).GetMethod("ToString"); mi.Invoke("hi", null); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_FieldInfo_GetValue_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { int x; void M() { FieldInfo fi = GetType().GetField("x"); fi.GetValue(this); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_FieldInfo_SetValue_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { int x; void M() { FieldInfo fi = GetType().GetField("x"); fi.SetValue(this, 42); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_PropertyInfo_GetValue_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { int X { get; set; } void M() { PropertyInfo pi = GetType().GetProperty("X"); pi.GetValue(this); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_PropertyInfo_SetValue_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { int X { get; set; } void M() { PropertyInfo pi = GetType().GetProperty("X"); pi.SetValue(this, 99); } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Reflection_BindingFlags_IsBlocked()
    {
        var errors = Check("""
            using System.Reflection;
            class C { void M() { var flags = BindingFlags.NonPublic | BindingFlags.Instance; } }
            """);
        Assert.True(HasError(errors, "System.Reflection"));
    }

    [Fact]
    public void Activator_CreateInstance_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { object o = System.Activator.CreateInstance(typeof(string)); } }
            """);
        Assert.True(HasError(errors, "System.Activator"));
    }

    // ── Forbidden: System.Linq.Expressions ───────────────────────────────────

    [Fact]
    public void LinqExpressions_Compile_IsBlocked()
    {
        var errors = Check("""
            using System.Linq.Expressions;
            class C { void M() { Expression<System.Func<int>> e = () => 42; var f = e.Compile(); } }
            """);
        Assert.True(HasError(errors, "System.Linq.Expressions"));
    }

    [Fact]
    public void LinqExpressions_ExpressionCall_IsBlocked()
    {
        var errors = Check("""
            using System.Linq.Expressions;
            class C { void M() { var e = Expression.Constant(42); } }
            """);
        Assert.True(HasError(errors, "System.Linq.Expressions"));
    }

    // ── Forbidden: System.Runtime.CompilerServices.Unsafe ────────────────────

    [Fact]
    public void CompilerServices_Unsafe_As_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { int x = 0; ref int r = ref System.Runtime.CompilerServices.Unsafe.AsRef(ref x); } }
            """);
        Assert.True(HasError(errors, "System.Runtime.CompilerServices.Unsafe"));
    }

    [Fact]
    public void CompilerServices_Unsafe_Add_IsBlocked()
    {
        var errors = Check("""
            using System.Runtime.CompilerServices;
            class C { void M(ref int x) { ref int next = ref Unsafe.Add(ref x, 1); } }
            """);
        Assert.True(HasError(errors, "System.Runtime.CompilerServices.Unsafe"));
    }

    // ── Forbidden: Microsoft.JSInterop ───────────────────────────────────────

    [Fact]
    public void JSInterop_IJSRuntime_IsBlocked()
    {
        var errors = Check("""
            using Microsoft.JSInterop;
            class C { void M(IJSRuntime js) { js.InvokeVoidAsync("eval", "alert(1)"); } }
            """);
        Assert.True(HasError(errors, "Microsoft.JSInterop"));
    }

    [Fact]
    public void JSInterop_JSRuntime_IsBlocked()
    {
        var errors = Check("""
            using Microsoft.JSInterop;
            class C { void M(JSRuntime js) { } }
            """);
        Assert.True(HasError(errors, "Microsoft.JSInterop"));
    }

    // ── Forbidden: nkast.Wasm.WebClipboard ───────────────────────────────────

    [Fact]
    public void WasmClipboard_IsBlocked()
    {
        var errors = Check("""
            using nkast.Wasm.WebClipboard;
            class C { void M(Clipboard cb) { } }
            """);
        Assert.True(HasError(errors, "nkast.Wasm.WebClipboard"));
    }

    // ── Allowed: System.Linq (LINQ queries must still work) ──────────────────

    [Fact]
    public void Linq_Where_IsAllowed()
    {
        var errors = Check("""
            using System.Linq;
            using System.Collections.Generic;
            class C { void M() { var xs = new List<int> { 1, 2 }.Where(x => x > 1); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Linq_Select_IsAllowed()
    {
        var errors = Check("""
            using System.Linq;
            using System.Collections.Generic;
            class C { void M() { var xs = new List<int> { 1, 2 }.Select(x => x * 2); } }
            """);
        Assert.Empty(errors);
    }

    // ── Forbidden: System.Net ─────────────────────────────────────────────────

    [Fact]
    public void Net_HttpClient_IsBlocked()
    {
        var errors = Check("""
            using System.Net.Http;
            class C { void M() { var c = new HttpClient(); } }
            """);
        Assert.True(HasError(errors, "System.Net"));
    }

    [Fact]
    public void Net_FullyQualified_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { var c = new System.Net.Http.HttpClient(); } }
            """);
        Assert.True(HasError(errors, "System.Net"));
    }

    // ── Forbidden: System.Runtime.InteropServices ─────────────────────────────

    [Fact]
    public void Interop_Marshal_IsBlocked()
    {
        var errors = Check("""
            using System.Runtime.InteropServices;
            class C { void M() { int sz = Marshal.SizeOf<int>(); } }
            """);
        Assert.True(HasError(errors, "System.Runtime.InteropServices"));
    }

    // ── Forbidden: System.Security ────────────────────────────────────────────

    [Fact]
    public void Security_Cryptography_IsBlocked()
    {
        var errors = Check("""
            using System.Security.Cryptography;
            class C { void M() { var h = SHA256.Create(); } }
            """);
        Assert.True(HasError(errors, "System.Security"));
    }

    // ── Forbidden: Microsoft.CodeAnalysis ────────────────────────────────────

    [Fact]
    public void CodeAnalysis_CSharpCompilation_IsBlocked()
    {
        var errors = Check("""
            using Microsoft.CodeAnalysis.CSharp;
            class C { void M() { var c = CSharpCompilation.Create("x"); } }
            """);
        Assert.True(HasError(errors, "Microsoft.CodeAnalysis"));
    }

    // ── Forbidden specific types ──────────────────────────────────────────────

    [Fact]
    public void IO_File_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { string s = System.IO.File.ReadAllText("x"); } }
            """);
        Assert.True(HasError(errors, "System.IO.File"));
    }

    [Fact]
    public void IO_Directory_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { var d = System.IO.Directory.GetFiles("."); } }
            """);
        Assert.True(HasError(errors, "System.IO.Directory"));
    }

    [Fact]
    public void IO_FileStream_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { var fs = new System.IO.FileStream("x", System.IO.FileMode.Open); } }
            """);
        Assert.True(HasError(errors, "System.IO.FileStream"));
    }

    [Fact]
    public void Diagnostics_Process_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { System.Diagnostics.Process.Start("cmd"); } }
            """);
        Assert.True(HasError(errors, "System.Diagnostics.Process"));
    }

    [Fact]
    public void AppDomain_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { var d = System.AppDomain.CurrentDomain; } }
            """);
        Assert.True(HasError(errors, "System.AppDomain"));
    }

    [Fact]
    public void Threading_Thread_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { var t = new System.Threading.Thread(() => {}); } }
            """);
        Assert.True(HasError(errors, "System.Threading.Thread"));
    }

    [Fact]
    public void Environment_IsBlocked()
    {
        var errors = Check("""
            class C { void M() { System.Environment.Exit(0); } }
            """);
        Assert.True(HasError(errors, "System.Environment"));
    }

    // ── Allowed ───────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyClass_IsAllowed()
    {
        var errors = Check("class C { }");
        Assert.Empty(errors);
    }

    [Fact]
    public void IO_MemoryStream_IsAllowed()
    {
        var errors = Check("""
            class C { void M() { var ms = new System.IO.MemoryStream(); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void IO_Stream_IsAllowed()
    {
        var errors = Check("""
            class C { void M(System.IO.Stream s) { int b = s.ReadByte(); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void IO_BinaryReader_IsAllowed()
    {
        var errors = Check("""
            class C { void M(System.IO.Stream s) { var r = new System.IO.BinaryReader(s); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Diagnostics_Debug_IsAllowed()
    {
        var errors = Check("""
            class C { void M() { System.Diagnostics.Debug.WriteLine("hello"); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Typeof_IsAllowed()
    {
        var errors = Check("""
            class C { void M() { var t = typeof(string); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void GetType_IsAllowed()
    {
        var errors = Check("""
            class C { void M() { var t = "hello".GetType(); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Math_IsAllowed()
    {
        var errors = Check("""
            class C { void M() { double x = System.Math.Abs(-1.0); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Collections_IsAllowed()
    {
        var errors = Check("""
            using System.Collections.Generic;
            class C { void M() { var list = new List<int>(); list.Add(1); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Threading_Tasks_IsAllowed()
    {
        var errors = Check("""
            using System.Threading.Tasks;
            class C { async Task M() { await Task.Delay(100); } }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public void Linq_IsAllowed()
    {
        var errors = Check("""
            using System.Linq;
            using System.Collections.Generic;
            class C { void M() { var xs = new List<int> { 1, 2 }.Where(x => x > 1).ToList(); } }
            """);
        Assert.Empty(errors);
    }

    // ── Error location ────────────────────────────────────────────────────────

    [Fact]
    public void Error_ReportsCorrectLineNumber()
    {
        var errors = Check("""
            class C {
                void M() {
                    System.IO.File.ReadAllText("x");
                }
            }
            """);
        Assert.Contains(errors, e => e.StartLine == 3);
    }

    [Fact]
    public void Error_HasErrorSeverity()
    {
        var errors = Check("""
            class C { void M() { System.IO.File.ReadAllText("x"); } }
            """);
        Assert.True(errors.All(e => e.Severity == "error"));
    }
}
