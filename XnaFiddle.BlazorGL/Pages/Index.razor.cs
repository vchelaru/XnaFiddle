using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace XnaFiddle.Pages
{
    public partial class Index
    {
        // Status/diagnostics color palette — update here to retheme both panels at once.
        // static readonly rather than const: Razor generates a second partial class file and
        // const evaluation across partial parts can produce CS0110 circular definition errors.
        static readonly string ColorSuccess = "#4ec9b0";
        static readonly string ColorError   = "#f48771";
        static readonly string ColorPending = "#dcdcaa";
        static readonly string ColorMuted   = "#888";

        Game _game;

        // The GraphicsProfile the shared canvas's WebGL context is currently bound to, or
        // null until the first game of this page-load runs. A canvas permanently binds to one
        // context type on its first getContext (Reach→WebGL1, HiDef→WebGL2) and can't be
        // rebound, so a profile change requires a page reload with a fresh canvas. See issue #25.
        GraphicsProfile? _canvasProfile;

        // Profile-switch confirmation dialog state (#25). When a game needs a different WebGL
        // version than the canvas is bound to, we show a notice (rather than reloading silently)
        // because the reload required to switch loses locally uploaded files.
        bool _profileSwitchPending;
        GraphicsProfile _pendingProfile;
        List<string> _assetsLostOnReload = new();

        // DotNetObjectReference wrappers for JS interop. Kept as fields so we can
        // dispose them when this page is torn down (prevents the .NET runtime from
        // leaking references to this component).
        DotNetObjectReference<Index> _pageDotNetRef;
        DotNetObjectReference<IntellisenseService> _intellisenseDotNetRef;

        string _diagnosticsOutput = "";
        string _diagnosticsColor = ColorMuted;
        string _statusMessage = "";
        string _statusColor = ColorSuccess;
        bool _isCompiling;
        CancellationTokenSource _compileCts;
        bool _pendingCompile;
        int _compileThrottleFrame;
        bool _monacoReady;
        bool _staleAssets;
        bool _assetsOpen;
        bool _gistOpen;
        bool _gistCodeCopied;
        bool _layoutVertical;
        bool _embedMode;
        bool _shareOpen;
        bool _shareAsSnippet;
        string _shareCodeEncoded;
        string _shareCodeUrl;
        SnippetRevertResult _revertResult;
        // Shader tab sources collected when the share dialog opens / content changes, so the
        // synchronous snippet preview (RecomputeSnippetPreview) can include them. Issue #26.
        List<ShaderFile> _shareShaders = new();
        bool _snipMembers, _snipInitialize, _snipLoadContent, _snipUpdate, _snipDraw;
        string _snippetPreviewJson;
        string _snippetPreviewUrl;
        string _editUrl = "";

        static string BuildTimeLocal =>
            DateTime.Parse(BuildInfo.BuildTime, null, System.Globalization.DateTimeStyles.RoundtripKind)
                .ToLocalTime()
                .ToString("MMM d, h:mm tt");
        string _gistInput = "";
        int _compileProgress;
        int _compileTotal;
        DateTime _compileStartTime;
        bool _hasCompiledOnce;
        string _selectedExample = "";
        bool _exampleBrowserOpen;
        string _selectedCategory = "";

        struct AssetInfo
        {
            public string FileName;
            public int Size;
            public string[] FntTextures; // non-null for .fnt files: texture filenames referenced by page lines
            public string SourceUrl;     // non-null when loaded from a URL (for share link encoding)
        }

        enum ExportRuntime { Kni, MonoGame }
        enum ExportPlatform { DesktopGL, WindowsDX, Android, BlazorGL }

        bool _exportOpen;
        bool _isExporting;
        ExportRuntime _exportRuntime = ExportRuntime.Kni;
        HashSet<ExportPlatform> _selectedPlatforms = new() { ExportPlatform.DesktopGL };
        string _exportProjectName = "MyFiddle";
        List<AssetInfo> _assets = new();
        string _assetUrlInput = "";
        bool _isFetchingAssetUrl;

        // ---- Tabbed editor (issue #26 phase 2) ----
        // The C# program is always the first tab; shader (.fx) tabs follow. A tab's filename is
        // the Content.Load<Effect>("Name") key. Shader sources live in their Monaco models and
        // are pulled + compiled on Run (see CompileRegisteredShadersAsync). Must match
        // monacoInterop.CSHARP_TAB in monaco-interop.js.
        const string CSharpTabName = "Game.cs";
        readonly List<string> _shaderTabs = new();              // shader filenames, e.g. "Grayscale.fx"
        string _activeTab = CSharpTabName;
        string _renamingTab;                                    // shader tab being inline-renamed, or null
        string _renameValue = "";
        // Bare shader names registered as compiled effects on the previous Run, so the next Run
        // can drop ones whose tab was removed/renamed (stale Content.Load<Effect> entries).
        HashSet<string> _lastCompiledShaders = new(StringComparer.OrdinalIgnoreCase);

        // Starter content for a new shader tab: a pass-through SpriteBatch pixel shader that
        // compiles as-is. The user edits MainPS to change pixels.
        const string DefaultShaderTemplate = @"#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
	float4 col = tex2D(SpriteTextureSampler, input.TextureCoordinates) * input.Color;
	// TODO: transform col.rgb here (e.g. col.rgb = 1.0 - col.rgb; to invert).
	return col;
}

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
";

        protected override void OnInitialized()
        {
            // Set _embedMode before the first render so the editor panel is never shown in embed mode.
            // NavigationManager is available synchronously here, unlike IJSRuntime which requires OnAfterRender.
            var uri = new Uri(Navigation.Uri);
            _embedMode = UrlCodec.ParseQueryParam(uri.Query, "embed") == "true";
        }

        protected override async void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            if (firstRender)
            {
                // Read URL params first so embed mode is known before any setup.
                // ?example= and ?gist= use query strings — values are short and it's conventional
                // for named resources to appear as query params rather than fragments.
                // #code= and #snippet= use the URL fragment (#) for two reasons:
                //   1. Fragments are never sent to the server — the static host only ever sees "/",
                //      so large payloads stay entirely in the browser.
                //   2. Query strings have server/proxy length limits (often 2-8 KB); fragments do not,
                //      which matters for #code= which can contain a full compressed source file.
                string search = await JsRuntime.InvokeAsync<string>("eval", "window.location.search");

                if (_embedMode)
                {
                    _editUrl = await JsRuntime.InvokeAsync<string>("eval",
                        "(() => { const u = new URL(window.location.href); u.searchParams.delete('embed'); return u.toString(); })()");
                }
                else
                {
                    double viewportWidth = await JsRuntime.InvokeAsync<double>("eval", "window.innerWidth");
                    if (viewportWidth < 768)
                    {
                        _layoutVertical = true;
                        await JsRuntime.InvokeVoidAsync("setLayoutMode", true);
                    }
                }

                string defaultCode = ExampleGallery.Load("BouncingBall") ?? "";
                await JsRuntime.InvokeVoidAsync("monacoInterop.init", "monacoContainer", defaultCode);
                _monacoReady = true;
                _pageDotNetRef = DotNetObjectReference.Create(this);
                var dotNetRef = _pageDotNetRef;
                await JsRuntime.InvokeAsync<object>("initRenderJS", dotNetRef);
                await JsRuntime.InvokeVoidAsync("fileDropInterop.init", dotNetRef);
                await JsRuntime.InvokeVoidAsync("keyboardInterop.init", dotNetRef);
                try { await JsRuntime.InvokeVoidAsync("monacoInterop.registerChangeCallback", dotNetRef); }
                catch { /* stale JS cache — hard-reload the page to pick up the new monaco-interop.js */ }

                // Wire Roslyn-backed IntelliSense. The JS side calls
                // IntellisenseService.GetCompletionsAsync via this reference.
                _intellisenseDotNetRef = DotNetObjectReference.Create(Intellisense);
                try { await JsRuntime.InvokeVoidAsync("monacoInterop.setIntellisenseRef", _intellisenseDotNetRef); }
                catch { /* stale JS cache — hard-reload to pick up setIntellisenseRef */ }

                // Subscribe before kicking off warmup so we don't miss the ReadyChanged event.
                Intellisense.ReadyChanged += OnIntellisenseReadyChanged;
                // If warmup already finished (e.g., page re-renders with a singleton service
                // that was primed earlier), push the ready state immediately.
                if (Intellisense.IsReady)
                {
                    try { await JsRuntime.InvokeVoidAsync("monacoInterop.setIntellisenseReady", true); }
                    catch { /* stale JS cache */ }
                }

                // Warm up Roslyn caches (MEF composition + first semantic model) so the
                // user's first completion doesn't pay the ~5s first-call cost. Run after
                // a short delay so it doesn't compete with initial page render.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500);
                        await _intellisenseDotNetRef.Value.WarmupAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("IntelliSense warmup failed: " + ex.Message);
                    }
                });

                // Detect stale static assets: build-version.js sets window._buildVersion
                // at load time; if it doesn't match the C# BuildInfo the browser is serving
                // cached JS files from an older build.
                string jsBuildVersion = await JsRuntime.InvokeAsync<string>("eval", "window._buildVersion || ''");
                if (!string.IsNullOrEmpty(jsBuildVersion) && jsBuildVersion != BuildInfo.BuildTime)
                {
                    _staleAssets = true;
                    _statusMessage = "Static assets are out of date.";
                    _statusColor = ColorError;
                    StateHasChanged();
                }
                string exampleFromQuery = UrlCodec.ParseQueryParam(search, "example");
                string gistFromQuery = UrlCodec.ParseQueryParam(search, "gist");
                bool autoCompile = false;
                if (!string.IsNullOrEmpty(exampleFromQuery))
                {
                    string exCode = ExampleGallery.Load(exampleFromQuery);
                    if (exCode != null)
                    {
                        await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", exCode);
                        _selectedExample = exampleFromQuery;
                        await LoadExampleAssetsAsync(exampleFromQuery);
                        autoCompile = true;
                    }
                }
                else if (!string.IsNullOrEmpty(gistFromQuery))
                {
                    bool loaded = await LoadFromGistId(gistFromQuery);
                    autoCompile = loaded;
                }

                string hash = await JsRuntime.InvokeAsync<string>("eval", "window.location.hash");
                if (hash.StartsWith("#snippet="))
                {
                    if (await LoadFromSnippet(hash.Substring(9)))
                        autoCompile = true;
                }
                else if (hash.StartsWith("#code="))
                {
                    // #code=<code>[&shaders=<...>][&assets=<...>] — shaders and assets optional.
                    string fragment = hash.Substring(1); // drop '#', keeps "code=..."
                    string codePart = ExtractFragmentParam(fragment, "code");
                    string shadersPart = ExtractFragmentParam(fragment, "shaders");
                    string assetsPart = ExtractFragmentParam(fragment, "assets");

                    if (await LoadFromCode(codePart))
                        autoCompile = true;

                    if (shadersPart != null)
                        await ApplyShadersFragmentAsync(shadersPart);

                    if (assetsPart != null)
                    {
                        try
                        {
                            string json = UrlCodec.Decode(assetsPart);
                            string[] urls = JsonSerializer.Deserialize<string[]>(json);
                            if (urls != null && urls.Length > 0)
                            {
                                await FetchAssetUrls(urls);
                                StateHasChanged();
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"[XnaFiddle] Failed to parse asset URLs: {e.Message}");
                        }
                    }
                }

                if (autoCompile)
                    CompileAndRun();
            }

        }

        static readonly HashSet<string> SupportedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".fnt", ".ttf", ".ember", ".wav", ".xnb"
        };

        static readonly string SupportedExtensionsDisplay =
            string.Join(", ", SupportedAssetExtensions);

        [JSInvokable]
        public void TriggerCompileAndRun()
        {
            if (!_isCompiling)
                CompileAndRun();
        }

        private string GetAssetUrlsFragment()
        {
            var urls = new List<string>();
            foreach (var asset in _assets)
            {
                if (!string.IsNullOrEmpty(asset.SourceUrl))
                    urls.Add(asset.SourceUrl);
            }
            if (urls.Count == 0) return "";
            string json = JsonSerializer.Serialize(urls);
            return "&assets=" + UrlCodec.Encode(json);
        }

        // ---- Shader round-trip in share / snippet / gist payloads (issue #26 phase 2b) ----

        // Reads each open shader tab's current source from its Monaco model.
        private async Task<List<ShaderFile>> CollectShaderFilesAsync()
        {
            var list = new List<ShaderFile>();
            foreach (string name in _shaderTabs)
            {
                string source = await JsRuntime.InvokeAsync<string>("monacoInterop.getModelValue", name);
                list.Add(new ShaderFile { Name = name, Source = source });
            }
            return list;
        }

        // Replaces the current shader tabs with the given set (used when loading a share link,
        // snippet, or gist). Null/empty just clears existing tabs.
        private async Task ApplyShaderFilesAsync(List<ShaderFile> shaders)
        {
            await ResetShaderTabsAsync();
            if (shaders == null) return;
            foreach (var s in shaders)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Name)) continue;
                string name = s.Name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase) ? s.Name : s.Name + ".fx";
                await OpenShaderTabFromSourceAsync(name, s.Source ?? "", select: false);
            }
        }

        // The "&shaders=<encoded JSON>" fragment for a share-code URL, or "" when no shader tabs
        // are open. Refreshes _shareShaders as a side effect so the snippet preview stays in sync.
        private async Task<string> BuildShadersFragmentAsync()
        {
            _shareShaders = await CollectShaderFilesAsync();
            if (_shareShaders.Count == 0) return "";
            string json = JsonSerializer.Serialize(_shareShaders);
            return "&shaders=" + UrlCodec.Encode(json);
        }

        // Decodes a "&shaders=" fragment value and re-opens the shader tabs it carries.
        private async Task ApplyShadersFragmentAsync(string encoded)
        {
            try
            {
                string json = UrlCodec.Decode(encoded);
                var shaders = JsonSerializer.Deserialize<List<ShaderFile>>(json);
                await ApplyShaderFilesAsync(shaders);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[XnaFiddle] Failed to parse shaders fragment: {e.Message}");
            }
        }

        // Extracts a single "key=value" parameter from a URL fragment of base64url-encoded values
        // joined by '&' (e.g. "code=..&shaders=..&assets=.."). Safe because UrlCodec output never
        // contains '&' or '='. Returns null when the key is absent.
        private static string ExtractFragmentParam(string fragment, string key)
        {
            int i = fragment.IndexOf(key + "=", StringComparison.Ordinal);
            if (i < 0) return null;
            int start = i + key.Length + 1;
            int amp = fragment.IndexOf('&', start);
            return amp < 0 ? fragment.Substring(start) : fragment.Substring(start, amp - start);
        }

        [JSInvokable]
        public async Task OnEditorContentChanged()
        {
            if (!_shareOpen) return;

            string code = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");

            _shareCodeEncoded = UrlCodec.Encode(code);
            string shadersFragment = await BuildShadersFragmentAsync();
            _shareCodeUrl = "https://xnafiddle.net/#code=" + _shareCodeEncoded + shadersFragment + GetAssetUrlsFragment();

            var newResult = SnippetReverter.Revert(code);

            // Update checkbox state:
            //   - section gained content → auto-check it
            //   - section lost content   → force-uncheck it (nothing to include)
            //   - section still has content → preserve the user's choice
            UpdateSnipCheckbox(ref _snipMembers,     _revertResult?.Members,     newResult.Members);
            UpdateSnipCheckbox(ref _snipInitialize,  _revertResult?.Initialize,  newResult.Initialize);
            UpdateSnipCheckbox(ref _snipLoadContent, _revertResult?.LoadContent, newResult.LoadContent);
            UpdateSnipCheckbox(ref _snipUpdate,      _revertResult?.Update,      newResult.Update);
            UpdateSnipCheckbox(ref _snipDraw,        _revertResult?.Draw,        newResult.Draw);

            _revertResult = newResult;
            RecomputeSnippetPreview();
            StateHasChanged();
        }

        static void UpdateSnipCheckbox(ref bool checkbox, string oldContent, string newContent)
        {
            bool hadContent = !string.IsNullOrWhiteSpace(oldContent);
            bool hasContent = !string.IsNullOrWhiteSpace(newContent);
            if (!hadContent && hasContent) checkbox = true;   // newly appeared → check
            else if (!hasContent)          checkbox = false;  // gone → uncheck
            // else: still has content → leave checkbox as-is
        }

        [JSInvokable]
        public async Task OnFileDropped(string fileName, string base64Data)
        {
            // A dropped .fx is shader source, not a content asset: open it in its own editor
            // tab (HLSL highlighting, compiled on Run) instead of routing it to the asset list.
            // Mirrors how example .fx files load (see LoadExampleAssetsAsync). Issue #26.
            if (fileName.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))
            {
                if (!_monacoReady)
                    return;
                byte[] fxData = Convert.FromBase64String(base64Data);
                if (fxData.Length > 10 * 1024 * 1024)
                {
                    SetError("File too large.", $"{fileName} exceeds the 10 MB limit.");
                    StateHasChanged();
                    return;
                }
                string fxSource = Encoding.UTF8.GetString(fxData);
                await OpenShaderTabFromSourceAsync(fileName, fxSource, select: true);
                _statusMessage = "Opened shader tab: " + fileName;
                _statusColor = ColorSuccess;
                StateHasChanged();
                return;
            }

            string ext = System.IO.Path.GetExtension(fileName);
            if (!SupportedAssetExtensions.Contains(ext))
            {
                _statusMessage = $"Unsupported file: {fileName} (supported: {SupportedExtensionsDisplay})";
                _statusColor = ColorError;
                _assetsOpen = true;
                StateHasChanged();
                return;
            }

            byte[] data = Convert.FromBase64String(base64Data);
            if (data.Length > 10 * 1024 * 1024)
            {
                _statusMessage = $"File too large: {fileName} (max 10 MB)";
                _statusColor = ColorError;
                _assetsOpen = true;
                StateHasChanged();
                return;
            }
            RegisterContentFile(fileName, data);

            string[] fntTextures = null;
            if (fileName.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase))
                fntTextures = ParseFntTextures(data);

            // Update UI list (replace if same name)
            _assets.RemoveAll(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            _assets.Add(new AssetInfo { FileName = fileName, Size = data.Length, FntTextures = fntTextures });
            _assetsOpen = true;

            _statusMessage = "Loaded: " + fileName;
            _statusColor = ColorSuccess;
            StateHasChanged();
        }

        private static string[] ParseFntTextures(byte[] data)
        {
            // Parse lines like: page id=0 file="Font10Arial_0.png"
            string text = System.Text.Encoding.UTF8.GetString(data);
            var results = new List<string>();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("page ")) continue;
                int fileIdx = trimmed.IndexOf("file=\"", StringComparison.Ordinal);
                if (fileIdx < 0) continue;
                int start = fileIdx + 6;
                // IndexOf(char, startIndex) returns -1 or a value >= start, so end < start
                // is impossible and end - start is always non-negative when end >= 0.
                int end = trimmed.IndexOf('"', start);
                if (end < 0) continue;
                results.Add(trimmed.Substring(start, end - start));
            }
            return results.ToArray();
        }

        private async Task ForceRefresh()
        {
            // Clear browser caches for this origin, then reload.
            await JsRuntime.InvokeVoidAsync("eval",
                "caches.keys().then(ks => Promise.all(ks.map(k => caches.delete(k)))).then(() => location.reload())");
        }

        private async Task CopyEditorContent()
        {
            if (!_monacoReady) return;
            string code = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");
            await CopyToClipboard(code);
        }

        private async Task CopyToClipboard(string text)
        {
            await JsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
            // Don't echo the copied text into the status bar — it can be arbitrarily large
            // (e.g. full editor content) and would produce a useless wall of text in the UI.
            _statusMessage = "Copied to clipboard.";
            _statusColor = ColorSuccess;
            StateHasChanged();
        }

        /// <summary>
        /// Registers a content file in both InMemoryContentManager and the JS-side
        /// XHR cache so that TitleContainer.OpenStream can resolve it.
        /// </summary>
        private void RegisterContentFile(string fileName, byte[] data)
        {
            InMemoryContentManager.AddFile(fileName, data);
            ((IJSInProcessRuntime)JsRuntime).InvokeVoid("contentFileCache.register", fileName, Convert.ToBase64String(data));
        }

        private void UnregisterContentFile(string fileName)
        {
            InMemoryContentManager.RemoveFile(fileName);
            ((IJSInProcessRuntime)JsRuntime).InvokeVoid("contentFileCache.unregister", fileName);
        }

        // Compiles every registered HLSL .fx file to .mgfx via the in-browser ShadowDusk
        // compiler and re-registers the result under the bare shader name, so user code can
        // load it idiomatically with Content.Load<Effect>("Name"). Returns null on success, or
        // an already-formatted error describing the first failing shader. When no .fx files are
        // registered it returns immediately without touching the WASM compiler (so the heavy DXC
        // module is never downloaded for non-shader runs). See issue #26.
        private async Task<string> CompileRegisteredShadersAsync()
        {
#if !SHADOWDUSK
            // Test-only net8.0 build: ShadowDusk isn't referenced, so there is no shader
            // compiler. This path never runs as an app; return a no-op result.
            await Task.CompletedTask;
            return null;
#else
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_shaderTabs.Count > 0)
            {
                var compiler = new ShadowDusk.Wasm.WasmShaderCompiler();
                foreach (string fileName in _shaderTabs)
                {
                    string bareName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    current.Add(bareName);
                    string source = await JsRuntime.InvokeAsync<string>("monacoInterop.getModelValue", fileName);
                    var options = new ShadowDusk.Core.CompilerOptions
                    {
                        // ShadowDusk's OpenGL target emits a profile-agnostic .mgfx that loads under
                        // KNI's Reach (WebGL1), HiDef (WebGL2), and desktop GL alike, so a single
                        // compile works regardless of the game's GraphicsProfile (issue #26).
                        Target = ShadowDusk.Core.PlatformTarget.OpenGL,
                        SourceFileName = fileName,
                    };

                    var result = await compiler.CompileAsync(source, options, _compileCts.Token);
                    if (result.IsFailure)
                    {
                        string detail = string.Join("\n",
                            System.Linq.Enumerable.Select(result.Error, e => e.FxcFormattedMessage));
                        return $"{fileName}:\n{detail}";
                    }

                    // Register the compiled .mgfx under the bare name (no extension) so
                    // Content.Load<Effect>("Name") resolves it through the Effect branch.
                    RegisterContentFile(bareName, result.Value.Data);
                }
            }

            // Drop compiled effects whose shader tab was removed/renamed since the last Run, so a
            // stale Content.Load<Effect>("Name") can't resolve old bytes.
            foreach (string old in _lastCompiledShaders)
                if (!current.Contains(old))
                    InMemoryContentManager.RemoveFile(old);
            _lastCompiledShaders = current;
            return null;
#endif
        }

        // ---- Tabbed editor: tab operations (issue #26 phase 2) ----

        private bool TabNameExists(string fileName) =>
            string.Equals(fileName, CSharpTabName, StringComparison.OrdinalIgnoreCase)
            || _shaderTabs.Any(t => string.Equals(t, fileName, StringComparison.OrdinalIgnoreCase));

        // Creates (or replaces) a shader tab's Monaco model and tracks it, optionally activating
        // it. Used by the [+] button, example loading, and (later) drag-and-drop of a .fx.
        private async Task OpenShaderTabFromSourceAsync(string fileName, string source, bool select)
        {
            await JsRuntime.InvokeVoidAsync("monacoInterop.createModel", fileName, source, "hlsl");
            if (!_shaderTabs.Any(t => string.Equals(t, fileName, StringComparison.OrdinalIgnoreCase)))
                _shaderTabs.Add(fileName);
            if (select)
            {
                _activeTab = fileName;
                await JsRuntime.InvokeVoidAsync("monacoInterop.switchToModel", fileName);
            }
        }

        // Disposes all shader tabs and shows the C# tab. Used when loading an example/fiddle that
        // brings its own (or no) shaders.
        private async Task ResetShaderTabsAsync()
        {
            if (_shaderTabs.Count > 0)
                await JsRuntime.InvokeVoidAsync("monacoInterop.resetToCSharpOnly");
            _shaderTabs.Clear();
            _lastCompiledShaders.Clear();
            _activeTab = CSharpTabName;
        }

        private async Task SelectTab(string name)
        {
            if (string.Equals(_activeTab, name, StringComparison.OrdinalIgnoreCase)) return;
            _activeTab = name;
            await JsRuntime.InvokeVoidAsync("monacoInterop.switchToModel", name);
        }

        private async Task AddShaderTab()
        {
            if (!_monacoReady) return;
            string fileName = "Shader.fx";
            int n = 1;
            while (TabNameExists(fileName)) fileName = $"Shader{n++}.fx";
            await OpenShaderTabFromSourceAsync(fileName, DefaultShaderTemplate, select: true);
            StateHasChanged();
        }

        private async Task CloseShaderTab(string fileName)
        {
            // Closing disposes the Monaco model — the shader source is gone with no undo, so
            // confirm first. (A dropped/example shader can be re-added, but hand-edited code can't.)
            bool confirmed = await JsRuntime.InvokeAsync<bool>("confirm",
                $"Close {fileName}? Its shader code will be discarded and can't be recovered.");
            if (!confirmed)
                return;
            await JsRuntime.InvokeVoidAsync("monacoInterop.disposeModel", fileName);
            _shaderTabs.RemoveAll(t => string.Equals(t, fileName, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_activeTab, fileName, StringComparison.OrdinalIgnoreCase))
            {
                _activeTab = CSharpTabName;
                await JsRuntime.InvokeVoidAsync("monacoInterop.switchToModel", CSharpTabName);
            }
            StateHasChanged();
        }

        private void BeginRename(string fileName)
        {
            _renamingTab = fileName;
            _renameValue = fileName;
            StateHasChanged();
        }

        private void CancelRename()
        {
            _renamingTab = null;
            StateHasChanged();
        }

        private async Task OnRenameKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter") await CommitRenameAsync();
            else if (e.Key == "Escape") CancelRename();
        }

        // Applies an inline tab rename. The filename (minus .fx) is the Content.Load<Effect> key,
        // so renaming changes how user code references the shader.
        private async Task CommitRenameAsync()
        {
            string oldName = _renamingTab;
            if (oldName == null) return;          // already committed/cancelled (e.g. Enter then blur)
            _renamingTab = null;

            string newName = (_renameValue ?? "").Trim();
            if (string.IsNullOrEmpty(newName) || string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                StateHasChanged();
                return;
            }
            if (!newName.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))
                newName += ".fx";
            if (TabNameExists(newName))
            {
                SetError("Rename failed.", $"A tab named \"{newName}\" already exists.");
                StateHasChanged();
                return;
            }

            await JsRuntime.InvokeVoidAsync("monacoInterop.renameModel", oldName, newName);
            int idx = _shaderTabs.FindIndex(t => string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _shaderTabs[idx] = newName;
            if (string.Equals(_activeTab, oldName, StringComparison.OrdinalIgnoreCase))
                _activeTab = newName;
            // Drop the stale compiled effect registered under the old bare name.
            string oldBare = System.IO.Path.GetFileNameWithoutExtension(oldName);
            InMemoryContentManager.RemoveFile(oldBare);
            _lastCompiledShaders.Remove(oldBare);
            StateHasChanged();
        }

        private void RemoveAsset(string fileName)
        {
            UnregisterContentFile(fileName);
            _assets.RemoveAll(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            StateHasChanged();
        }

        private async Task OnAssetUrlKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                await FetchAssetFromUrl();
        }

        private async Task FetchAssetFromUrl()
        {
            string url = _assetUrlInput?.Trim() ?? "";
            if (string.IsNullOrEmpty(url)) return;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                _statusMessage = "Invalid URL.";
                _statusColor = ColorError;
                StateHasChanged();
                return;
            }

            url = GitHubUrlNormalizer.Normalize(url);

            await FetchAndAddAssetUrl(url);
            if (_statusColor == ColorSuccess)
                _assetUrlInput = "";
            StateHasChanged();
        }

        private async Task FetchAndAddAssetUrl(string url)
        {
            string fileName = System.IO.Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(fileName))
                fileName = "asset";

            string ext = System.IO.Path.GetExtension(fileName);
            if (!SupportedAssetExtensions.Contains(ext))
            {
                _statusMessage = $"Unsupported file type: {ext} (supported: {SupportedExtensionsDisplay})";
                _statusColor = ColorError;
                return;
            }

            _isFetchingAssetUrl = true;
            _statusMessage = $"Fetching {fileName}...";
            _statusColor = ColorPending;
            StateHasChanged();

            try
            {
                byte[] data = await Http.GetByteArrayAsync(url);

                if (data.Length > 10 * 1024 * 1024)
                {
                    _statusMessage = $"File too large: {fileName} (max 10 MB)";
                    _statusColor = ColorError;
                    return;
                }

                RegisterContentFile(fileName, data);

                string[] fntTextures = null;
                if (fileName.EndsWith(".fnt", StringComparison.OrdinalIgnoreCase))
                    fntTextures = ParseFntTextures(data);

                _assets.RemoveAll(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
                _assets.Add(new AssetInfo { FileName = fileName, Size = data.Length, FntTextures = fntTextures, SourceUrl = url });
                _assetsOpen = true;

                _statusMessage = $"Loaded: {fileName}";
                _statusColor = ColorSuccess;
            }
            catch (HttpRequestException e)
            {
                string hint = e.Message.Contains("TypeError") ? " (CORS blocked?)" : "";
                _statusMessage = $"Failed to fetch {fileName}{hint}";
                _statusColor = ColorError;
            }
            catch (Exception e)
            {
                _statusMessage = $"Failed to fetch {fileName}: {e.Message}";
                _statusColor = ColorError;
            }
            finally
            {
                _isFetchingAssetUrl = false;
            }
        }

        private async Task FetchAssetUrls(string[] urls)
        {
            for (int i = 0; i < urls.Length; i++)
            {
                await FetchAndAddAssetUrl(urls[i]);
                if (_statusColor == ColorError) break;
            }
        }

        [JSInvokable]
        public void OnGameTimedOut(double frameMs)
        {
            _game = null;
            int seconds = (int)(frameMs / 1000);
            _diagnosticsOutput = $"Game stopped: a frame blocked for {seconds}s. Check for infinite loops or excessive work in Update/Draw.";
            _diagnosticsColor = ColorError;
            _statusMessage = "Stopped (frame timeout).";
            _statusColor = ColorError;
            StateHasChanged();
        }

        [JSInvokable]
        public void OnCanvasResized(int width, int height)
        {
            if (_game == null)
                return;

            var service = _game.Services.GetService(typeof(IGraphicsDeviceManager));
            if (service is GraphicsDeviceManager gdm)
            {
                gdm.PreferredBackBufferWidth = width;
                gdm.PreferredBackBufferHeight = height;
                gdm.ApplyChanges();
            }
        }

        [JSInvokable]
        public void TickDotNet()
        {
            // Pick up pending compile request (runs in requestAnimationFrame context,
            // same as Gumknix, to avoid Blazor sync context Monitor issues)
            if (_pendingCompile)
            {
                _pendingCompile = false;
                _ = DoCompileAndRun();
            }

            // No interlocked read needed: Blazor WASM is single-threaded. The only
            // interleaving points are await boundaries, and DoCompileAndRun() has none
            // between _game = null and _game = newGame, so this null check is sufficient.
            if (_game == null)
                return;

            // Throttle to ~4 fps while compiling so the compiler gets most of the CPU.
            if (_isCompiling && ++_compileThrottleFrame % 15 != 0)
                return;

            try
            {
                _game.Tick();
            }
            catch (Exception e)
            {
                _diagnosticsOutput = "Runtime error: " + e.Message;
                _diagnosticsColor = ColorError;
                _game = null;
                StateHasChanged();
            }
        }

        private void CompileAndRun()
        {
            if (_isCompiling)
                return;

            if (_game != null)
            {
                _game = null;
                LibraryRegistry.RunAllCleanups();
                _ = JsRuntime.InvokeVoidAsync("clearCanvas");
            }

            _isCompiling = true;
            _compileCts = new CancellationTokenSource();
            _pendingCompile = true;
            _diagnosticsOutput = "";
            _statusMessage = "Compiling...";
            _statusColor = ColorPending;
            _compileProgress = 0;
            _compileTotal = 0;
            _compileStartTime = DateTime.Now;
            StateHasChanged();
        }

        private void StopCompilation()
        {
            _compileCts?.Cancel();
        }

        private void StopGame()
        {
            if (_game == null)
                return;
            _game = null;
            LibraryRegistry.RunAllCleanups();
            _ = JsRuntime.InvokeVoidAsync("clearCanvas");
            _statusMessage = "Stopped.";
            _statusColor = ColorPending;
            StateHasChanged();
        }

        private async Task DoCompileAndRun()
        {
            try
            {
                string sourceCode = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");
                await JsRuntime.InvokeVoidAsync("compileTimerInterop.start");
                CompilationService.CompilationResult result = await Compiler.CompileAsync(sourceCode, (current, total) =>
                {
                    _compileProgress = current;
                    _compileTotal = total;
                    StateHasChanged();
                }, _compileCts.Token);
                await JsRuntime.InvokeVoidAsync("compileTimerInterop.stop");
                double compileSeconds = (DateTime.Now - _compileStartTime).TotalSeconds;
                _hasCompiledOnce = true;
                string failedNote = result.FailedAssemblies.Count > 0
                    ? $"\n[missing refs: {string.Join(", ", result.FailedAssemblies)}]"
                    : "";
                string versionNote = string.IsNullOrEmpty(result.VersionInfo) ? "" : $"\n{result.VersionInfo}";
                _diagnosticsOutput = $"Compiled in {compileSeconds:0.0}s" + failedNote + versionNote +
                    (string.IsNullOrEmpty(result.Log) ? "" : "\n" + result.Log);
                _diagnosticsColor = result.Success ? ColorMuted : ColorError;

                // Send diagnostics to Monaco as inline markers
                if (_monacoReady)
                {
                    if (result.Diagnostics.Count > 0)
                        await JsRuntime.InvokeVoidAsync("monacoInterop.setDiagnostics", result.Diagnostics);
                    else
                        await JsRuntime.InvokeVoidAsync("monacoInterop.clearDiagnostics");
                }

                if (result.Success && result.ILBytes != null)
                {
                    _statusMessage = "Loading game...";
                    _statusColor = ColorSuccess;
                    StateHasChanged();

                    // Load the compiled assembly directly in-memory
                    Assembly loadedAssembly = Assembly.Load(result.ILBytes);
                    Type gameType = FindGameType(loadedAssembly);

                    if (gameType != null)
                    {
                        // Compile any registered HLSL .fx shaders to .mgfx in-browser BEFORE the
                        // game runs, so user code can Content.Load<Effect>("Name") the result. This
                        // runs while the old game may still be ticking (the await is intentionally
                        // OUTSIDE the synchronous swap window below). The ~17 MB DXC wasm is fetched
                        // lazily on the first shader compile only — non-shader runs never touch it.
                        // See issue #26.
                        string shaderError = await CompileRegisteredShadersAsync();
                        if (shaderError != null)
                        {
                            SetError("Shader compilation failed.", shaderError);
                            _isCompiling = false;
                            StateHasChanged();
                            return;
                        }

                        // Drop the old game without calling Dispose(). Dispose() invalidates
                        // the GraphicsDevice textures which breaks Gum on the next run.
                        // GC will reclaim the old game eventually; this is acceptable in a fiddle context.
                        _game = null;

                        // NOTE: Everything between here and _game = newGame is synchronous —
                        // no awaits, so TickDotNet() cannot be called in this window (WASM is single-threaded).
                        LibraryRegistry.RunAllCleanups();

                        Game newGame = (Game)Activator.CreateInstance(gameType);
                        newGame.Content = new InMemoryContentManager(newGame.Services);

                        // The game's GraphicsProfile (set in its constructor, which has now run)
                        // decides the canvas's WebGL context type. If a previous game this session
                        // bound the shared canvas to the other type, KNI's getContext returns null
                        // and crashes — so reload onto a fresh canvas instead. See issue #25.
                        GraphicsProfile desiredProfile = GetGameProfile(newGame);
                        if (_canvasProfile.HasValue && _canvasProfile.Value != desiredProfile)
                        {
                            // The canvas can't change WebGL version in place, so switching needs a
                            // full page reload — which loses locally uploaded files. Don't do that
                            // silently: ask the user first and explain. See issue #25.
                            PromptProfileSwitch(desiredProfile);
                            _isCompiling = false;
                            StateHasChanged();
                            return;
                        }

                        try
                        {
                            // Force a DOM render flush so the "Loading game..." status appears
                            // before the potentially-blocking Run() call.
                            await Task.Delay(1);
                            newGame.Run();
                        }
                        catch (Exception runEx)
                        {
                            try { newGame.Dispose(); } catch { }
                            LibraryRegistry.RunAllCleanups();
                            throw new Exception("Game crashed during initialization: " + runEx.Message, runEx);
                        }
                        _game = newGame;
                        // Re-read after Run so the tracked profile reflects any change made in
                        // Initialize()/LoadContent(), not just the constructor.
                        _canvasProfile = GetGameProfile(newGame);
                        // Tell clearCanvas which context type the canvas is now bound to, so it
                        // clears through the right context instead of binding a fresh one (#25).
                        await JsRuntime.InvokeVoidAsync("eval",
                            $"window._canvasContextType='{(_canvasProfile == GraphicsProfile.HiDef ? "webgl2" : "webgl")}'");
                        _statusMessage = "Running.";
                        _statusColor = ColorSuccess;
                    }
                    else
                    {
                        _statusMessage = "No class extending Game found.";
                        _statusColor = ColorError;
                    }
                }
                else
                {
                    _statusMessage = "Compilation failed.";
                    _statusColor = ColorError;
                }
            }
            catch (OperationCanceledException)
            {
                _statusMessage = "Cancelled.";
                _statusColor = ColorMuted;
                _diagnosticsOutput = "Compilation cancelled by user.";
                _diagnosticsColor = ColorMuted;
            }
            catch (Exception e)
            {
                SetError("Error.", e.ToString());
            }

            _isCompiling = false;
            StateHasChanged();
        }

        private async Task ShareAsCode()
        {
            try
            {
                string code = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");
                string encoded = UrlCodec.Encode(code);
                string shareUrl = "https://xnafiddle.net/#code=" + encoded;
                await JsRuntime.InvokeVoidAsync("eval", $"history.replaceState(null,'','#code={encoded}')");
                await JsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", shareUrl);

                _statusMessage = "Link copied!";
                _statusColor = ColorSuccess;
            }
            catch (Exception e)
            {
                SetError("Share failed.", e.Message);
            }

            StateHasChanged();
        }

        private async Task OpenShareDialog()
        {
            if (_shareOpen)
            {
                _shareOpen = false;
                StateHasChanged();
                return;
            }

            if (!_monacoReady) return;

            try
            {
                string code = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");

                _shareCodeEncoded = UrlCodec.Encode(code);
                string shadersFragment = await BuildShadersFragmentAsync();
                _shareCodeUrl = "https://xnafiddle.net/#code=" + _shareCodeEncoded + shadersFragment + GetAssetUrlsFragment();

                _revertResult = SnippetReverter.Revert(code);
                _snipMembers     = !string.IsNullOrWhiteSpace(_revertResult?.Members);
                _snipInitialize  = !string.IsNullOrWhiteSpace(_revertResult?.Initialize);
                _snipLoadContent = !string.IsNullOrWhiteSpace(_revertResult?.LoadContent);
                _snipUpdate      = !string.IsNullOrWhiteSpace(_revertResult?.Update);
                _snipDraw        = !string.IsNullOrWhiteSpace(_revertResult?.Draw);

                RecomputeSnippetPreview();
                _shareAsSnippet = false;
                _shareOpen = true;
            }
            catch (Exception e)
            {
                SetError("Share failed.", e.Message);
            }

            StateHasChanged();
        }

        private void RecomputeSnippetPreview()
        {
            if (_revertResult == null || !_revertResult.Success)
            {
                _snippetPreviewJson = null;
                _snippetPreviewUrl = null;
                return;
            }

            var model = new SnippetModel
            {
                IsGum              = _revertResult.IsGum,
                IsAposShapes       = _revertResult.IsAposShapes,
                IsMonoGameExtended = _revertResult.IsMonoGameExtended,
                Usings             = _revertResult.ExtraUsings.Count > 0 ? _revertResult.ExtraUsings : null,
                Members     = _snipMembers     ? _revertResult.Members     : null,
                Initialize  = _snipInitialize  ? _revertResult.Initialize  : null,
                LoadContent = _snipLoadContent ? _revertResult.LoadContent : null,
                Update      = _snipUpdate      ? _revertResult.Update      : null,
                Draw        = _snipDraw        ? _revertResult.Draw        : null,
                Shaders     = _shareShaders.Count > 0 ? _shareShaders : null,
            };

            var ignoreDefaults = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            string compactJson = JsonSerializer.Serialize(model, ignoreDefaults);
            _snippetPreviewJson = JsonSerializer.Serialize(model,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, WriteIndented = true });

            string encoded = UrlCodec.Encode(compactJson);
            _snippetPreviewUrl = "https://xnafiddle.net/#snippet=" + encoded;
        }

        private async Task CopyShareUrl()
        {
            // Refresh shader sources first: a shader tab may have been edited since the dialog
            // opened, and shader edits don't fire the C#-only content callback that rebuilds the
            // share URLs. Collect now and build the URL fresh so the copied link is current.
            _shareShaders = await CollectShaderFilesAsync();
            string url;

            if (_shareAsSnippet)
            {
                if (_revertResult == null || !_revertResult.Success) return;
                // Compact JSON (no indentation) for the actual URL encoding
                var opts = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
                var model = new SnippetModel
                {
                    IsGum              = _revertResult.IsGum,
                    IsAposShapes       = _revertResult.IsAposShapes,
                    IsMonoGameExtended = _revertResult.IsMonoGameExtended,
                    Usings             = _revertResult.ExtraUsings.Count > 0 ? _revertResult.ExtraUsings : null,
                    Members     = _snipMembers     ? _revertResult.Members     : null,
                    Initialize  = _snipInitialize  ? _revertResult.Initialize  : null,
                    LoadContent = _snipLoadContent ? _revertResult.LoadContent : null,
                    Update      = _snipUpdate      ? _revertResult.Update      : null,
                    Draw        = _snipDraw        ? _revertResult.Draw        : null,
                    Shaders     = _shareShaders.Count > 0 ? _shareShaders : null,
                };
                string encoded = UrlCodec.Encode(JsonSerializer.Serialize(model, opts));
                url = "https://xnafiddle.net/#snippet=" + encoded;
                await JsRuntime.InvokeVoidAsync("eval", $"history.replaceState(null,'','#snippet={encoded}')");
            }
            else
            {
                string shadersFragment = _shareShaders.Count > 0
                    ? "&shaders=" + UrlCodec.Encode(JsonSerializer.Serialize(_shareShaders))
                    : "";
                string assetsFragment = GetAssetUrlsFragment();
                string fragment = $"#code={_shareCodeEncoded}{shadersFragment}{assetsFragment}";
                url = "https://xnafiddle.net/" + fragment;
                await JsRuntime.InvokeVoidAsync("eval", $"history.replaceState(null,'','{fragment}')");
            }

            await CopyToClipboard(url);
        }

        private async Task<bool> LoadFromSnippet(string encoded)
        {
            try
            {
                string json = UrlCodec.Decode(encoded);
                var model = JsonSerializer.Deserialize<SnippetModel>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                string code = SnippetExpander.Expand(model);

                if (_monacoReady)
                {
                    await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);
                    // Re-open any shaders carried in the snippet (issue #26 phase 2b).
                    await ApplyShaderFilesAsync(model.Shaders);
                }

                _statusMessage = "Loaded from snippet link.";
                _statusColor = ColorSuccess;
                StateHasChanged();
                return true;
            }
            catch (Exception e)
            {
                SetError("Invalid snippet link.", e.Message);
                StateHasChanged();
                return false;
            }
        }

        private async Task<bool> LoadFromCode(string encoded)
        {
            try
            {
                string code = UrlCodec.Decode(encoded);

                if (_monacoReady)
                    await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);

                _statusMessage = "Loaded from link.";
                _statusColor = ColorSuccess;
                StateHasChanged();
                return true;
            }
            catch (Exception e)
            {
                SetError("Invalid share link.", e.Message);
                StateHasChanged();
                return false;
            }
        }

        private async Task OpenGistSite()
        {
            try
            {
                string code = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");
                await JsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", code);
                await JsRuntime.InvokeVoidAsync("eval", "window.open('https://gist.github.com/new', '_blank')");
                _gistCodeCopied = true;
                _gistInput = "";
            }
            catch (Exception e)
            {
                SetError("Failed to open gist site.", e.Message);
            }

            StateHasChanged();
        }

        private async Task OnGistInputKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                await LoadGistFromInput();
        }

        private async Task LoadGistFromInput()
        {
            string input = _gistInput?.Trim() ?? "";
            if (string.IsNullOrEmpty(input)) return;
            bool loaded = await LoadFromGistId(input);
            if (loaded)
            {
                _gistOpen = false;
                CompileAndRun();
            }
        }

        private async Task<bool> LoadFromGistId(string input)
        {
            try
            {
                // Accept full URL (https://gist.github.com/user/ID or /ID) or bare ID
                string gistId = input.Trim();
                if (gistId.Contains("gist.github.com/"))
                {
                    var parts = gistId.TrimEnd('/').Split('/');
                    gistId = parts[^1];
                }

                // Gist IDs are hex strings — if the extracted value is empty or contains a dot
                // it's still a domain/path fragment, not a valid ID.
                if (string.IsNullOrEmpty(gistId) || gistId.Contains('.'))
                {
                    _statusMessage = "Invalid gist URL or ID.";
                    _statusColor = ColorError;
                    StateHasChanged();
                    return false;
                }

                _statusMessage = "Loading gist...";
                _statusColor = ColorPending;
                StateHasChanged();

                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/gists/{gistId}");
                request.Headers.Add("Accept", "application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

                var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _statusMessage = $"Gist not found ({(int)response.StatusCode}).";
                    _statusColor = ColorError;
                    StateHasChanged();
                    return false;
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var files = doc.RootElement.GetProperty("files");

                // First .cs file (fall back to first .txt) is the program; every .fx file is a
                // shader tab (issue #26 phase 2b). Don't break early — we must see all files.
                string code = null;
                string txtFallback = null;
                var shaderFiles = new List<ShaderFile>();
                foreach (var file in files.EnumerateObject())
                {
                    string content = file.Value.GetProperty("content").GetString();
                    if (file.Name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))
                        shaderFiles.Add(new ShaderFile { Name = file.Name, Source = content });
                    else if (code == null && file.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        code = content;
                    else if (txtFallback == null && file.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        txtFallback = content;
                }
                code ??= txtFallback;

                if (code == null)
                {
                    _statusMessage = "No .cs or .txt file found in gist.";
                    _statusColor = ColorError;
                    StateHasChanged();
                    return false;
                }

                if (_monacoReady)
                {
                    // Loading a gist replaces the fiddle. Set the C# program, then re-open any
                    // .fx files as shader tabs (ApplyShaderFilesAsync clears stale tabs first).
                    await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);
                    await ApplyShaderFilesAsync(shaderFiles);
                }

                await JsRuntime.InvokeVoidAsync("eval",
                    $"history.replaceState(null,'','?gist={Uri.EscapeDataString(gistId)}')");

                _statusMessage = "Gist loaded.";
                _statusColor = ColorSuccess;
                _selectedExample = "";
                StateHasChanged();
                return true;
            }
            catch (Exception e)
            {
                SetError("Failed to load gist.", e.Message);
                StateHasChanged();
                return false;
            }
        }

        ExportTarget GetExportTarget(ExportPlatform platform) => (_exportRuntime, platform) switch
        {
            (ExportRuntime.Kni, ExportPlatform.DesktopGL)  => ExportTarget.KniDesktopGL,
            (ExportRuntime.Kni, ExportPlatform.WindowsDX)  => ExportTarget.KniWindowsDX,
            (ExportRuntime.Kni, ExportPlatform.Android)    => ExportTarget.KniAndroid,
            (ExportRuntime.Kni, ExportPlatform.BlazorGL)   => ExportTarget.KniBlazorGL,
            (ExportRuntime.MonoGame, ExportPlatform.DesktopGL) => ExportTarget.MonoGameDesktopGL,
            (ExportRuntime.MonoGame, ExportPlatform.WindowsDX) => ExportTarget.MonoGameWindowsDX,
            (ExportRuntime.MonoGame, ExportPlatform.Android)   => ExportTarget.MonoGameAndroid,
            // MonoGame + BlazorGL is not valid — fall back to DesktopGL
            _ => ExportTarget.MonoGameDesktopGL,
        };

        List<ExportTarget> GetExportTargets()
        {
            var targets = new List<ExportTarget>(_selectedPlatforms.Count);
            foreach (var p in _selectedPlatforms)
                targets.Add(GetExportTarget(p));
            return targets;
        }

        void TogglePlatform(ExportPlatform platform)
        {
            if (_selectedPlatforms.Contains(platform))
                _selectedPlatforms.Remove(platform);
            else
                _selectedPlatforms.Add(platform);
        }

        void SetExportRuntime(ExportRuntime runtime)
        {
            _exportRuntime = runtime;
            if (runtime == ExportRuntime.MonoGame)
                _selectedPlatforms.Remove(ExportPlatform.BlazorGL);
        }

        private async Task OnExportNameKeyDown(KeyboardEventArgs e)
        {
            if (e.Key != "Enter") return;
            if (_isExporting) return;
            if (string.IsNullOrWhiteSpace(_exportProjectName)) return;
            if (_selectedPlatforms.Count == 0) return;
            await ExportProject();
        }

        private async Task ExportProject()
        {
            string projectName = SanitizeProjectName(
                string.IsNullOrWhiteSpace(_exportProjectName) ? "MyFiddle" : _exportProjectName.Trim());
            _isExporting = true;
            StateHasChanged();
            try
            {
                string code = _monacoReady
                    ? await JsRuntime.InvokeAsync<string>("monacoInterop.getValue")
                    : "";

                var assets = InMemoryContentManager.Files;
                var targets = GetExportTargets();
                byte[] zipBytes = ProjectExporter.Export(code, targets, projectName, assets: assets.Count > 0 ? assets : null, libraryRegistry: LibraryRegistry);
                string base64 = Convert.ToBase64String(zipBytes);
                await JsRuntime.InvokeVoidAsync("downloadFile", projectName + ".zip", base64);
            }
            catch (Exception ex)
            {
                SetError("Export failed.", ex.Message);
            }
            finally
            {
                _isExporting = false;
                StateHasChanged();
            }
        }

        static string SanitizeProjectName(string name)
        {
            // Replace any character that isn't valid in a C# identifier with underscore
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

            string result = sb.ToString();

            // Must not start with a digit
            if (result.Length > 0 && char.IsDigit(result[0]))
                result = "_" + result;

            return result.Length > 0 ? result : "MyFiddle";
        }

        private async Task ToggleLayout()
        {
            _layoutVertical = !_layoutVertical;
            await JsRuntime.InvokeVoidAsync("setLayoutMode", _layoutVertical);
        }

        private void OpenExampleBrowser()
        {
            // Default to the category of the currently selected example, or the first category
            if (!string.IsNullOrEmpty(_selectedExample))
            {
                for (int i = 0; i < ExampleGallery.Catalog.Length; i++)
                {
                    if (ExampleGallery.Catalog[i].Name == _selectedExample)
                    {
                        _selectedCategory = ExampleGallery.Catalog[i].Category;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(_selectedCategory) && ExampleGallery.Categories.Length > 0)
                _selectedCategory = ExampleGallery.Categories[0];

            _exampleBrowserOpen = true;
        }

        private async Task SelectExample(string name)
        {
            if (string.IsNullOrEmpty(name) || !_monacoReady)
                return;

            string code = ExampleGallery.Load(name);
            if (code != null)
            {
                _selectedExample = name;
                _exampleBrowserOpen = false;
                // Drop any shader tabs from the previous fiddle before loading this one's.
                await ResetShaderTabsAsync();
                await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);
                await JsRuntime.InvokeVoidAsync("eval",
                    $"history.replaceState(null,'','?example={Uri.EscapeDataString(name)}')");

                // Stop the running game and clear the canvas
                _game = null;
                _statusMessage = "";
                _diagnosticsOutput = "";
                await JsRuntime.InvokeVoidAsync("clearCanvas");

                await LoadExampleAssetsAsync(name);

                CompileAndRun();
            }
        }

        private async Task LoadExampleAssetsAsync(string exampleName)
        {
            // Clear previous content files from both InMemoryContentManager and the JS XHR cache
            InMemoryContentManager.ClearFiles();
            ((IJSInProcessRuntime)JsRuntime).InvokeVoid("contentFileCache.clear");
            _assets.Clear();

            ExampleAsset[] assets = ExampleGallery.LoadAssets(exampleName);

            for (int i = 0; i < assets.Length; i++)
            {
                // Shader sources open in their own editor tab, not the asset panel.
                if (assets[i].FileName.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))
                {
                    string fxSource = Encoding.UTF8.GetString(assets[i].Data);
                    await OpenShaderTabFromSourceAsync(assets[i].FileName, fxSource, select: false);
                    continue;
                }

                RegisterContentFile(assets[i].FileName, assets[i].Data);

                // Update the UI asset list (same as drag-and-drop path)
                _assets.RemoveAll(a => string.Equals(a.FileName, assets[i].FileName, System.StringComparison.OrdinalIgnoreCase));
                string sourceUrl = $"{Navigation.BaseUri}examples/{exampleName}/{assets[i].FileName}";
                _assets.Add(new AssetInfo { FileName = assets[i].FileName, Size = assets[i].Data.Length, SourceUrl = sourceUrl });
            }
            if (_assets.Count > 0) _assetsOpen = true;
        }



        // Sets both status bar and diagnostics panel to an error state together.
        // Always use this for error paths — setting them individually risks leaving
        // _diagnosticsColor in a stale neutral state while showing red error text.
        private void SetError(string statusMessage, string diagnosticsDetail)
        {
            _statusMessage = statusMessage;
            _statusColor = ColorError;
            _diagnosticsOutput = diagnosticsDetail;
            _diagnosticsColor = ColorError;
        }

        // Reads the GraphicsProfile a constructed (but not yet Run) game will use. A
        // GraphicsDeviceManager registers itself in game.Services from its constructor, so this
        // is available before Run() creates the device. Falls back to Reach — KNI's default —
        // when the game configures no manager.
        private static GraphicsProfile GetGameProfile(Game game)
        {
            if (game.Services.GetService(typeof(IGraphicsDeviceManager)) is GraphicsDeviceManager gdm)
                return gdm.GraphicsProfile;
            return GraphicsProfile.Reach;
        }

        // Human-readable name for the profile/WebGL pairing, shown in the switch dialog.
        private static string ProfileName(GraphicsProfile p) =>
            p == GraphicsProfile.HiDef ? "HiDef (WebGL2)" : "Reach (WebGL1)";

        // Opens the profile-switch confirmation dialog and works out which uploaded files would be
        // lost on reload — i.e. those not backed by a URL we can re-fetch. See issue #25.
        private void PromptProfileSwitch(GraphicsProfile target)
        {
            _pendingProfile = target;
            _assetsLostOnReload.Clear();
            foreach (var asset in _assets)
            {
                if (string.IsNullOrEmpty(asset.SourceUrl))
                    _assetsLostOnReload.Add(asset.FileName);
            }
            _profileSwitchPending = true;
            _statusMessage = "Graphics mode switch needed.";
            _statusColor = ColorPending;
        }

        private void CancelProfileSwitch()
        {
            _profileSwitchPending = false;
            _statusMessage = "Graphics mode switch cancelled — game not started.";
            _statusColor = ColorMuted;
            StateHasChanged();
        }

        private async Task ConfirmProfileSwitch()
        {
            _profileSwitchPending = false;
            await ReloadForProfileChange();
        }

        // The shared canvas can't switch WebGL context type, so a GraphicsProfile change is
        // handled by reloading onto a fresh canvas. Code (and URL-backed assets) are preserved in
        // the URL fragment so the reloaded page auto-loads and re-runs. Locally dropped assets are
        // not carried across the reload. See issue #25.
        private async Task ReloadForProfileChange()
        {
            string code = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");
            string encoded = UrlCodec.Encode(code);
            string assetsFragment = GetAssetUrlsFragment();
            _statusMessage = "Switching graphics profile — reloading...";
            _statusColor = ColorPending;
            StateHasChanged();
            await JsRuntime.InvokeVoidAsync("eval",
                $"location.hash='#code={encoded}{assetsFragment}';location.reload();");
        }

        private static Type FindGameType(Assembly assembly)
        {
            Type[] types = assembly.GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                try
                {
                    string baseTypeName = types[i].BaseType?.ToString();
                    if (baseTypeName == "Game" || baseTypeName == "Microsoft.Xna.Framework.Game")
                        return types[i];
                }
                catch { }
            }
            return null;
        }

        void OnIntellisenseReadyChanged()
        {
            // ReadyChanged may fire on a background task; marshal back onto the Blazor
            // sync context via InvokeAsync so StateHasChanged and JS interop are safe.
            _ = InvokeAsync(async () =>
            {
                try { await JsRuntime.InvokeVoidAsync("monacoInterop.setIntellisenseReady", true); }
                catch { /* stale JS cache */ }
                StateHasChanged();
            });
        }

        public void Dispose()
        {
            if (Intellisense != null)
            {
                Intellisense.ReadyChanged -= OnIntellisenseReadyChanged;
            }
            _pageDotNetRef?.Dispose();
            _pageDotNetRef = null;
            _intellisenseDotNetRef?.Dispose();
            _intellisenseDotNetRef = null;
        }
    }
}
