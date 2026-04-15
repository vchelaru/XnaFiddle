// wired to Monaco in follow-up step
using System;
using System.Collections.Generic;
using System.Composition.Hosting;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.JSInterop;
using XnaFiddle.Intellisense;

namespace XnaFiddle
{
    /// <summary>
    /// Roslyn-backed completion service. Holds a long-lived AdhocWorkspace with a
    /// single document that is updated per request. Metadata references are shared
    /// with CompilationService so the completion surface matches the compile surface.
    /// </summary>
    public class IntellisenseService
    {
        private readonly CompilationService _compilationService;

        private AdhocWorkspace _workspace;
        private DocumentId _documentId;
        private bool _initialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private CancellationTokenSource _currentCompletionCts;
        private CancellationTokenSource _currentSignatureHelpCts;
        private CancellationTokenSource _currentHoverCts;
        private CancellationTokenSource _currentDiagnosticsCts;

        public IntellisenseService(CompilationService compilationService)
        {
            _compilationService = compilationService;
        }

        /// <summary>
        /// True once <see cref="WarmupAsync"/> has completed a successful priming
        /// completion request. UI layers use this to gate "IntelliSense ready" indicators
        /// and to skip .NET completion calls that would otherwise queue behind warmup on
        /// the single WASM thread.
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// Raised when <see cref="IsReady"/> transitions to true. Subscribers should
        /// unsubscribe when disposed; this service is a singleton for the app lifetime.
        /// </summary>
        public event Action ReadyChanged;

        public class CompletionResult
        {
            public string DisplayText { get; set; }
            public string InsertionText { get; set; }
            public string Kind { get; set; }
            public string Detail { get; set; }
            public string SortText { get; set; }
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized) return;
            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_initialized) return;

                (List<MetadataReference> references, _, _) =
                    await _compilationService.GetMetadataReferencesAsync(cancellationToken: cancellationToken);

                var host = CreateWasmSafeHostServices();
                _workspace = new AdhocWorkspace(host);

                // Best-effort: enable unimported-namespace completions. The CompletionOptions
                // type is internal in Roslyn 4.14, and the public PerLanguageOption key used
                // in older versions ("ShowItemsFromUnimportedNamespaces") is no longer exposed.
                // TODO: unimported-namespace option API needs verification — reflection-based
                // attempt below may no-op silently on 4.14. Confirmed public overload of
                // GetCompletionsAsync does not accept a CompletionOptions argument publicly.
                TryEnableUnimportedNamespaceCompletions(_workspace);

                var projectId = ProjectId.CreateNewId();
                var projectInfo = ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    name: "IntellisenseProject",
                    assemblyName: "IntellisenseProject",
                    language: LanguageNames.CSharp,
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        concurrentBuild: false),
                    parseOptions: CSharpParseOptions.Default
                        .WithLanguageVersion(LanguageVersion.LatestMajor)
                        .WithPreprocessorSymbols("BLAZORGL"),
                    metadataReferences: references);

                var project = _workspace.AddProject(projectInfo);

                _documentId = DocumentId.CreateNewId(project.Id);
                var documentInfo = DocumentInfo.Create(
                    _documentId,
                    name: "User.cs",
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(""), VersionStamp.Create())));
                _workspace.AddDocument(documentInfo);

                _initialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Builds <see cref="MefHostServices"/> from the Roslyn default assemblies, but
        /// filters out <c>Microsoft.CodeAnalysis.Host.DefaultPersistentStorageConfiguration</c>.
        /// That type's static ctor calls <c>System.Diagnostics.Process.GetCurrentProcess()</c>,
        /// which throws <see cref="PlatformNotSupportedException"/> on Blazor WASM. Removing the
        /// part is safe: Roslyn's workspace services fall back to <c>NoOpPersistentStorageService</c>
        /// when no <c>IPersistentStorageConfiguration</c> is composed, which is exactly what we want
        /// in-browser (there's no real disk to cache to anyway).
        /// </summary>
        private static MefHostServices CreateWasmSafeHostServices()
        {
            const string blockedTypeFullName =
                "Microsoft.CodeAnalysis.Host.DefaultPersistentStorageConfiguration";

            var parts = new List<Type>();
            foreach (var assembly in MefHostServices.DefaultAssemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    if (type.FullName == blockedTypeFullName) continue;
                    parts.Add(type);
                }
            }

            var compositionContext = new ContainerConfiguration()
                .WithParts(parts)
                .CreateContainer();

            return MefHostServices.Create(compositionContext);
        }

        private static void TryEnableUnimportedNamespaceCompletions(Workspace workspace)
        {
            try
            {
                // Reflect against internal CompletionOptionsStorage.ShowItemsFromUnimportedNamespaces
                // if present. This is a best-effort path; safe to no-op if unavailable.
                var featuresAsm = typeof(CompletionService).Assembly;
                var storageType = featuresAsm.GetType("Microsoft.CodeAnalysis.Completion.CompletionOptionsStorage");
                if (storageType == null) return;
                var field = storageType.GetField("ShowItemsFromUnimportedNamespaces",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null) return;
                var optionKey = field.GetValue(null);
                if (optionKey == null) return;

                // Build a new OptionSet with this key set to true, per language.
                var optionSet = workspace.Options;
                // PerLanguageOption2<bool> overload of WithChangedOption(PerLanguageOption2<T>, string, T)
                var withChanged = optionSet.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "WithChangedOption"
                        && m.GetParameters().Length == 3
                        && m.GetParameters()[1].ParameterType == typeof(string));
                if (withChanged == null) return;
                var generic = withChanged.IsGenericMethodDefinition
                    ? withChanged.MakeGenericMethod(typeof(bool))
                    : withChanged;
                var newOptions = generic.Invoke(optionSet, [optionKey, LanguageNames.CSharp, true]);
                if (newOptions is Microsoft.CodeAnalysis.Options.OptionSet os)
                    workspace.TryApplyChanges(workspace.CurrentSolution.WithOptions(os));
            }
            catch
            {
                // Best-effort; swallow so completion still works without unimported namespaces.
            }
        }

        [JSInvokable]
        public async Task<CompletionResult[]> GetCompletionsAsync(string source, int position)
        {
            // Cancel any in-flight completion request so Roslyn's async work aborts mid-flight.
            var previousCts = _currentCompletionCts;
            previousCts?.Cancel();
            previousCts?.Dispose();

            var cts = new CancellationTokenSource();
            _currentCompletionCts = cts;
            var token = cts.Token;

            try
            {
                await EnsureInitializedAsync(token);

                var newSolution = _workspace.CurrentSolution.WithDocumentText(
                    _documentId, SourceText.From(source ?? ""));
                if (!_workspace.TryApplyChanges(newSolution))
                {
                    // Fall back: force-set the solution directly via a fresh apply on the current.
                    newSolution = _workspace.CurrentSolution.WithDocumentText(
                        _documentId, SourceText.From(source ?? ""));
                    _workspace.TryApplyChanges(newSolution);
                }

                token.ThrowIfCancellationRequested();

                var document = _workspace.CurrentSolution.GetDocument(_documentId);
                if (document == null) return [];

                var completionService = CompletionService.GetService(document);
                if (completionService == null) return [];

                if (position < 0) position = 0;
                int textLen = (source ?? "").Length;
                if (position > textLen) position = textLen;

                var completions = await completionService.GetCompletionsAsync(
                    document,
                    position,
                    trigger: default,
                    roles: null,
                    options: null,
                    cancellationToken: token);
                if (completions == null) return [];

                var items = completions.ItemsList;
                var results = new CompletionResult[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    string kind = item.Tags.Length > 0 ? item.Tags[0] : "";
                    string detail = string.Join(" ", item.Tags);
                    results[i] = new CompletionResult
                    {
                        DisplayText = item.DisplayText,
                        InsertionText = string.IsNullOrEmpty(item.DisplayText)
                            ? item.FilterText
                            : item.DisplayText,
                        Kind = kind,
                        Detail = detail,
                        SortText = item.SortText
                    };
                }
                return results;
            }
            catch (OperationCanceledException)
            {
                return [];
            }
        }

        /// <summary>
        /// Primes Roslyn's MEF composition and initial semantic model caches by running
        /// a throwaway completion request. The first real completion on WASM takes ~5s;
        /// calling this at startup moves that cost off the user's first keystroke.
        /// Errors are swallowed — warmup is best-effort.
        /// </summary>
        [JSInvokable]
        public async Task WarmupAsync()
        {
            try
            {
                const string primeSource = "class C { void M() { System. } }";
                // Position is right after "System." — offset of the dot + 1.
                int position = primeSource.IndexOf("System.", StringComparison.Ordinal) + "System.".Length;
                await GetCompletionsAsync(primeSource, position);

                if (!IsReady)
                {
                    IsReady = true;
                    ReadyChanged?.Invoke();
                }
            }
            catch
            {
                // Swallow — warmup must not crash page init.
            }
        }

        public class SignatureParameter
        {
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
            public string Documentation { get; set; }
        }

        public class SignatureInfo
        {
            public string Label { get; set; }
            public string Documentation { get; set; }
            public SignatureParameter[] Parameters { get; set; }
        }

        public class SignatureHelpResult
        {
            public SignatureInfo[] Signatures { get; set; }
            public int ActiveSignature { get; set; }
            public int ActiveParameter { get; set; }
        }

        [JSInvokable]
        public async Task<SignatureHelpResult> GetSignatureHelpAsync(string source, int position)
        {
            // Use a separate CTS from completion so sig help and completion don't cancel each other.
            var previousCts = _currentSignatureHelpCts;
            previousCts?.Cancel();
            previousCts?.Dispose();

            var cts = new CancellationTokenSource();
            _currentSignatureHelpCts = cts;
            var token = cts.Token;

            try
            {
                await EnsureInitializedAsync(token);

                var newSolution = _workspace.CurrentSolution.WithDocumentText(
                    _documentId, SourceText.From(source ?? ""));
                _workspace.TryApplyChanges(newSolution);

                token.ThrowIfCancellationRequested();

                var document = _workspace.CurrentSolution.GetDocument(_documentId);
                if (document == null) return null;

                var syntaxRoot = await document.GetSyntaxRootAsync(token);
                if (syntaxRoot == null) return null;

                if (position < 0) position = 0;
                int textLen = (source ?? "").Length;
                if (position > textLen) position = textLen;

                // Walk up from the token at `position` looking for the innermost
                // InvocationExpression whose argument list contains `position`.
                var token1 = syntaxRoot.FindToken(Math.Max(0, position - 1));
                Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation = null;
                for (var node = token1.Parent; node != null; node = node.Parent)
                {
                    if (node is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax inv)
                    {
                        var argList = inv.ArgumentList;
                        if (argList != null
                            && position > argList.OpenParenToken.SpanStart
                            && position <= argList.CloseParenToken.Span.End)
                        {
                            invocation = inv;
                            break;
                        }
                    }
                }

                if (invocation == null) return null;

                var semanticModel = await document.GetSemanticModelAsync(token);
                if (semanticModel == null) return null;

                var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression, token);
                var candidates = new List<IMethodSymbol>();
                if (symbolInfo.Symbol is IMethodSymbol resolved)
                {
                    candidates.Add(resolved);
                }
                foreach (var candidate in symbolInfo.CandidateSymbols)
                {
                    if (candidate is IMethodSymbol m && !candidates.Contains(m))
                    {
                        candidates.Add(m);
                    }
                }

                if (candidates.Count == 0) return null;

                // Active parameter = number of top-level commas between '(' and position.
                int activeParameter = ActiveParameterLocator.FindActiveParameter(
                    invocation.ArgumentList, position);

                var signatures = new SignatureInfo[candidates.Count];
                for (int i = 0; i < candidates.Count; i++)
                {
                    var method = candidates[i];
                    var formatted = SignatureFormatter.Format(method, semanticModel, position);
                    var parameters = new SignatureParameter[method.Parameters.Length];
                    for (int p = 0; p < method.Parameters.Length; p++)
                    {
                        var range = formatted.ParameterRanges[p];
                        var param = method.Parameters[p];
                        parameters[p] = new SignatureParameter
                        {
                            StartOffset = range.Start,
                            EndOffset = range.End,
                            Documentation = param.GetDocumentationCommentXml(cancellationToken: token) ?? ""
                        };
                    }

                    signatures[i] = new SignatureInfo
                    {
                        Label = formatted.Label,
                        Documentation = method.GetDocumentationCommentXml(cancellationToken: token) ?? "",
                        Parameters = parameters
                    };
                }

                // Pick the signature whose parameter count best fits activeParameter.
                int activeSignature = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Parameters.Length > activeParameter)
                    {
                        activeSignature = i;
                        break;
                    }
                }

                return new SignatureHelpResult
                {
                    Signatures = signatures,
                    ActiveSignature = activeSignature,
                    ActiveParameter = activeParameter
                };
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static readonly SymbolDisplayFormat HoverFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
                | SymbolDisplayGenericsOptions.IncludeTypeConstraints
                | SymbolDisplayGenericsOptions.IncludeVariance,
            memberOptions: SymbolDisplayMemberOptions.IncludeType
                | SymbolDisplayMemberOptions.IncludeContainingType
                | SymbolDisplayMemberOptions.IncludeParameters
                | SymbolDisplayMemberOptions.IncludeModifiers
                | SymbolDisplayMemberOptions.IncludeAccessibility
                | SymbolDisplayMemberOptions.IncludeConstantValue
                | SymbolDisplayMemberOptions.IncludeRef,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType
                | SymbolDisplayParameterOptions.IncludeName
                | SymbolDisplayParameterOptions.IncludeDefaultValue
                | SymbolDisplayParameterOptions.IncludeExtensionThis
                | SymbolDisplayParameterOptions.IncludeParamsRefOut,
            localOptions: SymbolDisplayLocalOptions.IncludeType
                | SymbolDisplayLocalOptions.IncludeConstantValue
                | SymbolDisplayLocalOptions.IncludeRef,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier,
            kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword
                | SymbolDisplayKindOptions.IncludeMemberKeyword
                | SymbolDisplayKindOptions.IncludeNamespaceKeyword);

        public class HoverResult
        {
            public string Content { get; set; }
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
        }

        /// <summary>
        /// Returns Roslyn-backed hover (QuickInfo) content for the symbol at
        /// <paramref name="position"/>. Uses the SemanticModel directly because
        /// <c>Microsoft.CodeAnalysis.QuickInfo.QuickInfoService</c> is internal in
        /// Roslyn 4.14. The content is the symbol's MinimallyQualifiedFormat display
        /// on the first line followed by the raw XML doc comment (if any) — we don't
        /// parse the XML yet, so docs render as plain text in Monaco. Good enough for
        /// a first pass; revisit if we want nicer markdown.
        /// </summary>
        [JSInvokable]
        public async Task<HoverResult> GetHoverAsync(string source, int position)
        {
            if (!IsReady) return null;

            var previousCts = _currentHoverCts;
            previousCts?.Cancel();
            previousCts?.Dispose();

            var cts = new CancellationTokenSource();
            _currentHoverCts = cts;
            var token = cts.Token;

            try
            {
                await EnsureInitializedAsync(token);

                var newSolution = _workspace.CurrentSolution.WithDocumentText(
                    _documentId, SourceText.From(source ?? ""));
                _workspace.TryApplyChanges(newSolution);

                token.ThrowIfCancellationRequested();

                var document = _workspace.CurrentSolution.GetDocument(_documentId);
                if (document == null) return null;

                var syntaxRoot = await document.GetSyntaxRootAsync(token);
                if (syntaxRoot == null) return null;

                if (position < 0) position = 0;
                int textLen = (source ?? "").Length;
                if (position > textLen) position = textLen;

                var hoverToken = syntaxRoot.FindToken(Math.Max(0, Math.Min(position, textLen - 1 < 0 ? 0 : textLen - 1)));
                if (hoverToken.Span.Length == 0) return null;

                var node = hoverToken.Parent;
                if (node == null) return null;

                var semanticModel = await document.GetSemanticModelAsync(token);
                if (semanticModel == null) return null;

                // Prefer declared symbol (when hovering a declaration identifier),
                // otherwise resolve via SymbolInfo on the enclosing expression/name.
                ISymbol symbol = semanticModel.GetDeclaredSymbol(node, token);
                if (symbol == null)
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(node, token);
                    symbol = symbolInfo.Symbol
                        ?? (symbolInfo.CandidateSymbols.Length > 0 ? symbolInfo.CandidateSymbols[0] : null);
                }

                if (symbol == null) return null;

                var signature = symbol.ToDisplayString(HoverFormat);
                var xmlDocs = symbol.GetDocumentationCommentXml(cancellationToken: token) ?? "";
                var formatted = XmlDocFormatter.Format(xmlDocs);

                var content = string.IsNullOrWhiteSpace(formatted)
                    ? signature
                    : signature + "\n\n" + formatted;

                return new HoverResult
                {
                    Content = content,
                    StartOffset = hoverToken.SpanStart,
                    EndOffset = hoverToken.Span.End
                };
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        public class DiagnosticDto
        {
            public string Severity { get; set; }
            public string Message { get; set; }
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
            public string Code { get; set; }
        }

        /// <summary>
        /// Returns live diagnostics (syntax + semantic) for the given source. Intended to
        /// be called on a debounce as the user types so Monaco can render squiggles without
        /// requiring a full compile. Readiness-gated on <see cref="IsReady"/>.
        /// </summary>
        [JSInvokable]
        public async Task<DiagnosticDto[]> GetDiagnosticsAsync(string source)
        {
            if (!IsReady) return [];

            var previousCts = _currentDiagnosticsCts;
            previousCts?.Cancel();
            previousCts?.Dispose();

            var cts = new CancellationTokenSource();
            _currentDiagnosticsCts = cts;
            var token = cts.Token;

            try
            {
                await EnsureInitializedAsync(token);

                var newSolution = _workspace.CurrentSolution.WithDocumentText(
                    _documentId, SourceText.From(source ?? ""));
                _workspace.TryApplyChanges(newSolution);

                token.ThrowIfCancellationRequested();

                var document = _workspace.CurrentSolution.GetDocument(_documentId);
                if (document == null) return [];

                var compilation = await document.Project.GetCompilationAsync(token);
                if (compilation == null) return [];

                var diagnostics = compilation.GetDiagnostics(token);
                var results = new List<DiagnosticDto>(diagnostics.Length);
                foreach (var d in diagnostics)
                {
                    if (d.Severity == DiagnosticSeverity.Hidden) continue;

                    string severity = d.Severity switch
                    {
                        DiagnosticSeverity.Error => "error",
                        DiagnosticSeverity.Warning => "warning",
                        _ => "info"
                    };
                    var span = d.Location.SourceSpan;
                    results.Add(new DiagnosticDto
                    {
                        Severity = severity,
                        Message = d.GetMessage(),
                        StartOffset = span.Start,
                        EndOffset = span.End,
                        Code = d.Id
                    });
                }
                return results.ToArray();
            }
            catch (OperationCanceledException)
            {
                return [];
            }
            catch
            {
                return [];
            }
        }

    }
}
