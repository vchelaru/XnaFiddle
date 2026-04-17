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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
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
        private readonly IJSRuntime _jsRuntime;

        private AdhocWorkspace _workspace;
        private DocumentId _documentId;
        private bool _initialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private CancellationTokenSource _currentCompletionCts;
        private CancellationTokenSource _currentSignatureHelpCts;
        private CancellationTokenSource _currentHoverCts;
        private CancellationTokenSource _currentDiagnosticsCts;
        private CancellationTokenSource _currentAddUsingSuggestionsCts;

        public IntellisenseService(CompilationService compilationService, IJSRuntime jsRuntime)
        {
            _compilationService = compilationService;
            _jsRuntime = jsRuntime;
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
        /// which throws <see cref="PlatformNotSupportedException"/> on Blazor WASM. We then
        /// substitute our own WASM-safe implementation (see
        /// <see cref="WasmSafePersistentStorageProvider"/>) so code paths that explicitly
        /// request <c>IPersistentStorageConfiguration</c> — notably
        /// <c>SymbolFinder.FindDeclarationsAsync</c>, which backs the Add-using code action —
        /// don't throw "Service ... is required" on our 'Custom' workspace. Paths that don't
        /// touch the config (completion, hover, live diagnostics) behave the same as before.
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

            // Emit and add our WASM-safe replacement. If emission fails (e.g., Roslyn
            // renamed the internal type on a future upgrade), fall back to the old
            // behavior — completion/hover still work; only the code-action path fails.
            var wasmSafeConfig = WasmSafePersistentStorageProvider.GetOrCreateConfigurationType();
            if (wasmSafeConfig != null)
            {
                parts.Add(wasmSafeConfig);
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
        /// Returns Roslyn-backed hover content for the symbol at
        /// <paramref name="position"/>. Uses the SemanticModel directly because
        /// <c>Microsoft.CodeAnalysis.QuickInfo.QuickInfoService</c> is internal in
        /// Roslyn 4.14. The content is the formatted signature plus parsed XML doc
        /// markdown. When the identifier is unresolved, any applicable "Add using
        /// ...;" suggestions are appended as clickable <c>command:</c>-URI markdown
        /// links (see <see cref="BuildAddUsingMarkdown"/>).
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

                string content = null;
                if (symbol != null)
                {
                    var signature = symbol.ToDisplayString(HoverFormat);
                    var xmlDocs = symbol.GetDocumentationCommentXml(cancellationToken: token) ?? "";
                    var formatted = XmlDocFormatter.Format(xmlDocs);
                    content = string.IsNullOrWhiteSpace(formatted)
                        ? signature
                        : signature + "\n\n" + formatted;
                }

                // If the name at this position is unresolved (or resolves to an error symbol),
                // try to offer "Add using ...;" links. We only bother when there's no resolved
                // symbol, since a resolved symbol implies the namespace is already imported or
                // fully-qualified and no import is needed.
                bool symbolUnresolved = symbol == null
                    || symbol.Kind == SymbolKind.ErrorType;
                if (symbolUnresolved)
                {
                    var suggestions = await ComputeAddUsingSuggestionsAsync(
                        document, syntaxRoot, semanticModel, position, token);
                    if (suggestions.Count > 0)
                    {
                        var addUsingMarkdown = BuildAddUsingMarkdown(suggestions);
                        content = string.IsNullOrEmpty(content)
                            ? addUsingMarkdown
                            : content + "\n\n---\n\n" + addUsingMarkdown;
                    }
                }

                if (string.IsNullOrEmpty(content)) return null;

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


        public class AddUsingSuggestion
        {
            /// <summary>Human-readable title, e.g. <c>"using Microsoft.Xna.Framework;"</c>.</summary>
            public string Title { get; set; }
            /// <summary>Namespace to import (no trailing semicolon).</summary>
            public string NamespaceToImport { get; set; }
            /// <summary>Zero-based offset at which to insert the new <c>using</c> line.</summary>
            public int InsertOffset { get; set; }
            /// <summary>Exact text to insert (ends with <c>"\n"</c>).</summary>
            public string InsertText { get; set; }
        }

        /// <summary>
        /// Returns <c>using</c>-import suggestions for an unresolved identifier at
        /// <paramref name="position"/>. Surfaced to users via clickable
        /// <c>command:</c>-URI links in the hover tooltip (see <see cref="GetHoverAsync"/>).
        /// Kept as a JSInvokable entry point so a follow-up surface (e.g. a
        /// right-click menu) can reuse it without duplicating Roslyn work.
        ///
        /// Why hand-rolled instead of <c>CSharpAddImportCodeFixProvider</c>: the full
        /// Features assembly is large, the provider is internal, and MEF-composing all
        /// code-fix providers would leak dozens of unwanted quick-fixes. See
        /// <see cref="AddUsingSuggester"/> for detail.
        /// </summary>
        [JSInvokable]
        public async Task<AddUsingSuggestion[]> GetAddUsingSuggestionsAsync(string source, int position)
        {
            if (!IsReady) return [];

            var previousCts = _currentAddUsingSuggestionsCts;
            previousCts?.Cancel();
            previousCts?.Dispose();

            var cts = new CancellationTokenSource();
            _currentAddUsingSuggestionsCts = cts;
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

                var syntaxRoot = await document.GetSyntaxRootAsync(token);
                if (syntaxRoot == null) return [];

                var semanticModel = await document.GetSemanticModelAsync(token);
                if (semanticModel == null) return [];

                if (position < 0) position = 0;
                int textLen = (source ?? "").Length;
                if (position > textLen) position = textLen;

                var suggestions = await ComputeAddUsingSuggestionsAsync(
                    document, syntaxRoot, semanticModel, position, token);
                return suggestions.ToArray();
            }
            catch (OperationCanceledException)
            {
                return [];
            }
            catch (Exception ex)
            {
                try
                {
                    await _jsRuntime.InvokeVoidAsync("console.warn",
                        "[AddUsingSuggestions] " + ex.GetType().Name + ": " + ex.Message);
                }
                catch
                {
                    // Logging must never break the feature.
                }
                return [];
            }
        }

        /// <summary>
        /// Shared implementation for the JSInvokable
        /// <see cref="GetAddUsingSuggestionsAsync"/> entry point and the inline
        /// append-to-hover path in <see cref="GetHoverAsync"/>. Assumes the document,
        /// syntax root, and semantic model have already been obtained for
        /// <paramref name="position"/>. Callers decide how to surface the result.
        /// </summary>
        private static async Task<List<AddUsingSuggestion>> ComputeAddUsingSuggestionsAsync(
            Document document,
            SyntaxNode syntaxRoot,
            SemanticModel semanticModel,
            int position,
            CancellationToken token)
        {
            var empty = new List<AddUsingSuggestion>();

            var nameNode = AddUsingSuggester.FindNameAtPosition(syntaxRoot, position);
            if (nameNode == null) return empty;

            // If the name already resolves to a non-error symbol, no import action is needed.
            // An IErrorTypeSymbol (Kind == ErrorType) means Roslyn couldn't bind — we SHOULD
            // still try to suggest imports in that case.
            var symbolInfo = semanticModel.GetSymbolInfo(nameNode, token);
            if (symbolInfo.Symbol != null && symbolInfo.Symbol.Kind != SymbolKind.ErrorType)
            {
                return empty;
            }

            // Use the simple identifier (drop any generic arity). SymbolFinder matches
            // by metadata name, which for generics includes a backtick arity suffix —
            // it will still find `List<T>` when queried for "List".
            string identifier = nameNode switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                GenericNameSyntax gen => gen.Identifier.Text,
                _ => null
            };
            if (string.IsNullOrEmpty(identifier)) return empty;

            // Gather namespaces already imported by the document so we don't suggest
            // redundant usings.
            var existingUsings = new HashSet<string>(StringComparer.Ordinal);
            if (syntaxRoot is CompilationUnitSyntax cu)
            {
                foreach (var usingDirective in cu.Usings)
                {
                    if (usingDirective.Alias != null) continue;
                    if (usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)) continue;
                    var name = usingDirective.Name?.ToString();
                    if (!string.IsNullOrEmpty(name)) existingUsings.Add(name);
                }
            }

            token.ThrowIfCancellationRequested();

            // First call is slow (hundreds of ms) as Roslyn builds its symbol index
            // across the project references. Acceptable: this path is only hit on
            // hover, which is already user-driven and low-frequency.
            var declarations = await SymbolFinder.FindDeclarationsAsync(
                document.Project,
                identifier,
                ignoreCase: false,
                filter: SymbolFilter.Type,
                cancellationToken: token);
            if (declarations == null) return empty;

            var declList = declarations as IList<ISymbol> ?? declarations.ToList();
            if (declList.Count == 0) return empty;

            var seenNamespaces = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<AddUsingSuggestion>();
            foreach (var symbol in declList)
            {
                if (symbol is not INamedTypeSymbol typeSymbol) continue;
                if (typeSymbol.DeclaredAccessibility != Accessibility.Public) continue;

                var containing = typeSymbol.ContainingNamespace;
                if (containing == null || containing.IsGlobalNamespace) continue;

                string ns = containing.ToDisplayString();
                if (!AddUsingSuggester.IsAllowedNamespace(ns, AddUsingSuggester.NamespaceAllowlist)) continue;
                if (existingUsings.Contains(ns)) continue;
                if (!seenNamespaces.Add(ns)) continue;

                var (offset, text) = AddUsingSuggester.ComputeInsertion(syntaxRoot, ns);
                results.Add(new AddUsingSuggestion
                {
                    Title = "using " + ns + ";",
                    NamespaceToImport = ns,
                    InsertOffset = offset,
                    InsertText = text,
                });

                if (results.Count >= 10) break;
            }

            return results;
        }

        /// <summary>
        /// Formats a list of add-using suggestions as a markdown fragment appended to
        /// the hover tooltip. Each entry is a clickable <c>command:</c>-URI link that
        /// invokes the JS-side <c>xnafiddle.addUsing</c> command.
        ///
        /// Command arguments are JSON-encoded per Monaco's command-URI contract:
        /// <c>command:id?["arg1","arg2",...]</c>. We pass a <c>__ACTIVE_MODEL__</c>
        /// placeholder for the model URI — the JS handler resolves that to the current
        /// editor's active model so .NET doesn't need a round-trip just to learn the
        /// URI.
        ///
        /// The hover content must be wrapped in a Monaco <c>MarkdownString</c> with
        /// <c>isTrusted: true</c> or Monaco will silently strip the <c>command:</c>
        /// links. That trust flag is set in the JS hover provider.
        /// </summary>
        private static string BuildAddUsingMarkdown(List<AddUsingSuggestion> suggestions)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("**Add using:**\n\n");
            foreach (var suggestion in suggestions)
            {
                // Command argument is a JSON array matching the JS command signature
                // [modelUri, insertOffset, insertText]. URL-encode the JSON so Monaco's
                // command-URI parser accepts it.
                string json = System.Text.Json.JsonSerializer.Serialize(new object[]
                {
                    "__ACTIVE_MODEL__",
                    suggestion.InsertOffset,
                    suggestion.InsertText,
                });
                string encoded = Uri.EscapeDataString(json);
                sb.Append("- [`using ");
                sb.Append(suggestion.NamespaceToImport);
                sb.Append(";`](command:xnafiddle.addUsing?");
                sb.Append(encoded);
                sb.Append(")\n");
            }
            return sb.ToString();
        }

    }
}
