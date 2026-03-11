using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

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

        string _diagnosticsOutput = "";
        string _diagnosticsColor = ColorMuted;
        string _statusMessage = "";
        string _statusColor = ColorSuccess;
        bool _isCompiling;
        bool _pendingCompile;
        bool _monacoReady;
        bool _assetsOpen;
        bool _gistOpen;
        bool _gistCodeCopied;
        bool _runLocallyOpen;
        bool _layoutVertical;

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

        struct AssetInfo
        {
            public string FileName;
            public int Size;
            public string[] FntTextures; // non-null for .fnt files: texture filenames referenced by page lines
        }

        struct PackageInfo
        {
            public string Feature;
            public string KniPackage;
            public string MonoGamePackage;
            public string DetectionString;
        }

        static readonly PackageInfo[] KnownPackages =
        [
            new PackageInfo { Feature = "Gum UI",          KniPackage = "Gum.KNI",            MonoGamePackage = "Gum.MonoGame",    DetectionString = "MonoGameGum" },
            new PackageInfo { Feature = "Shapes",           KniPackage = "Apos.Shapes.KNI",    MonoGamePackage = "Apos.Shapes",     DetectionString = "Apos.Shapes" },
            new PackageInfo { Feature = "FontStashSharp",   KniPackage = "FontStashSharp.Kni", MonoGamePackage = "FontStashSharp",  DetectionString = "FontStashSharp" },
            new PackageInfo { Feature = "MonoGame.Extended", KniPackage = "KNI.Extended",      MonoGamePackage = "MonoGame.Extended", DetectionString = "MonoGame.Extended" },
        ];

        List<PackageInfo> _runLocallyPackages = new();
        List<AssetInfo> _assets = new();

        protected override async void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);

            if (firstRender)
            {
                double viewportWidth = await JsRuntime.InvokeAsync<double>("eval", "window.innerWidth");
                if (viewportWidth < 768)
                {
                    _layoutVertical = true;
                    await JsRuntime.InvokeVoidAsync("setLayoutMode", true);
                }

                string defaultCode = ExampleGallery.Load("ColorCycle") ?? "";
                await JsRuntime.InvokeVoidAsync("monacoInterop.init", "monacoContainer", defaultCode);
                _monacoReady = true;
                var dotNetRef = DotNetObjectReference.Create(this);
                await JsRuntime.InvokeAsync<object>("initRenderJS", dotNetRef);
                await JsRuntime.InvokeVoidAsync("fileDropInterop.init", dotNetRef);

                // ?example= and ?gist= use query strings — values are short and it's conventional
                // for named resources to appear as query params rather than fragments.
                // #code= and #snippet= use the URL fragment (#) for two reasons:
                //   1. Fragments are never sent to the server — the static host only ever sees "/",
                //      so large payloads stay entirely in the browser.
                //   2. Query strings have server/proxy length limits (often 2-8 KB); fragments do not,
                //      which matters for #code= which can contain a full compressed source file.
                string search = await JsRuntime.InvokeAsync<string>("eval", "window.location.search");
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
                    await LoadFromSnippet(hash.Substring(9));
                    autoCompile = true;
                }
                else if (hash.StartsWith("#code="))
                {
                    await LoadFromCode(hash.Substring(6));
                    autoCompile = true;
                }

                if (autoCompile)
                    CompileAndRun();
            }

        }

        static readonly HashSet<string> SupportedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".fnt"
        };

        [JSInvokable]
        public void OnFileDropped(string fileName, string base64Data)
        {
            string ext = System.IO.Path.GetExtension(fileName);
            if (!SupportedAssetExtensions.Contains(ext))
            {
                _statusMessage = $"Unsupported file: {fileName} (supported: .png, .fnt)";
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
            InMemoryContentManager.AddFile(fileName, data);

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

        private void RemoveAsset(string fileName)
        {
            InMemoryContentManager.RemoveFile(fileName);
            _assets.RemoveAll(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            StateHasChanged();
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

            _isCompiling = true;
            _pendingCompile = true;
            _diagnosticsOutput = "";
            _statusMessage = "Compiling...";
            _statusColor = ColorPending;
            _compileProgress = 0;
            _compileTotal = 0;
            _compileStartTime = DateTime.Now;
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
                });
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
                        // Drop the old game without calling Dispose(). Dispose() invalidates
                        // the GraphicsDevice textures which breaks Gum on the next run.
                        // GC will reclaim the old game eventually; this is acceptable in a fiddle context.
                        _game = null;

                        // NOTE: Everything between here and _game = newGame is synchronous —
                        // no awaits, so TickDotNet() cannot be called in this window (WASM is single-threaded).
                        CleanUpGameWindowRegistry();
                        CleanUpGumService();

                        Game newGame = (Game)Activator.CreateInstance(gameType);
                        newGame.Content = new InMemoryContentManager(newGame.Services);
                        try
                        {
                            newGame.Run();
                        }
                        catch (Exception runEx)
                        {
                            try { newGame.Dispose(); } catch { }
                            CleanUpGameWindowRegistry();
                            throw new Exception("Game crashed during initialization: " + runEx.Message, runEx);
                        }
                        _game = newGame;
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

        private async Task LoadFromSnippet(string encoded)
        {
            try
            {
                string json = UrlCodec.Decode(encoded);
                string code = SnippetExpander.Expand(json);

                if (_monacoReady)
                    await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);

                _statusMessage = "Loaded from snippet link.";
                _statusColor = ColorSuccess;
            }
            catch (Exception e)
            {
                SetError("Failed to load snippet.", e.Message);
            }

            StateHasChanged();
        }

        private async Task LoadFromCode(string encoded)
        {
            try
            {
                string code = UrlCodec.Decode(encoded);

                if (_monacoReady)
                    await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);

                _statusMessage = "Loaded from link.";
                _statusColor = ColorSuccess;
            }
            catch (Exception e)
            {
                SetError("Failed to load from link.", e.Message);
            }

            StateHasChanged();
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

                // Find first .cs file, fall back to first .txt file
                string code = null;
                string txtFallback = null;
                foreach (var file in files.EnumerateObject())
                {
                    string content = file.Value.GetProperty("content").GetString();
                    if (file.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        code = content;
                        break;
                    }
                    if (txtFallback == null && file.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
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
                    await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);

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

        private async Task ToggleRunLocally()
        {
            _runLocallyOpen = !_runLocallyOpen;
            if (_runLocallyOpen)
            {
                string code = _monacoReady
                    ? await JsRuntime.InvokeAsync<string>("monacoInterop.getValue")
                    : "";
                RefreshRunLocallyPackages(code);
            }
        }

        private async Task ToggleLayout()
        {
            _layoutVertical = !_layoutVertical;
            await JsRuntime.InvokeVoidAsync("setLayoutMode", _layoutVertical);
        }

        private void RefreshRunLocallyPackages(string code)
        {
            _runLocallyPackages.Clear();
            foreach (var pkg in KnownPackages)
            {
                if (code.Contains(pkg.DetectionString))
                    _runLocallyPackages.Add(pkg);
            }
        }

        private async Task OnExampleSelected(ChangeEventArgs e)
        {
            string name = e.Value?.ToString();
            if (string.IsNullOrEmpty(name) || !_monacoReady)
                return;

            string code = ExampleGallery.Load(name);
            if (code != null)
            {
                _selectedExample = name;
                await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);
                await JsRuntime.InvokeVoidAsync("eval",
                    $"history.replaceState(null,'','?example={Uri.EscapeDataString(name)}')");

                // Stop the running game and clear the canvas
                _game = null;
                _statusMessage = "";
                _diagnosticsOutput = "";
                await JsRuntime.InvokeVoidAsync("clearCanvas");

                if (_runLocallyOpen)
                    RefreshRunLocallyPackages(code);

                StateHasChanged();
            }
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

        private static void CleanUpGumService()
        {
            try
            {
                var gumServiceType = Type.GetType("MonoGameGum.GumService, KniGum");
                if (gumServiceType == null) return;
                var defaultProp = gumServiceType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public);
                var gumService = defaultProp?.GetValue(null);
                if (gumService == null) return;

                // Clear Root, PopupRoot, and ModalRoot children.
                // These are persistent statics — old controls accumulate across runs without this.
                foreach (var rootPropName in new[] { "Root", "PopupRoot", "ModalRoot" })
                {
                    var rootProp = gumServiceType.GetProperty(rootPropName, BindingFlags.Instance | BindingFlags.Public);
                    var root = rootProp?.GetValue(gumService);
                    if (root == null) continue;
                    var childrenProp = root.GetType().GetProperty("Children", BindingFlags.Instance | BindingFlags.Public);
                    (childrenProp?.GetValue(root) as System.Collections.IList)?.Clear();
                }

                // Reset SystemManagers.Default so GumService.Initialize creates a fresh one
                var systemManagersType = Type.GetType("RenderingLibrary.SystemManagers, GumCommon");
                if (systemManagersType != null)
                {
                    var defaultPropSM = systemManagersType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public);
                    defaultPropSM?.SetValue(null, null);
                }

                // Clear LoaderManager cache WITHOUT disposing textures
                var loaderManagerType = Type.GetType("RenderingLibrary.Content.LoaderManager, GumCommon");
                if (loaderManagerType != null)
                {
                    var selfProp = loaderManagerType.GetProperty("Self", BindingFlags.Static | BindingFlags.Public);
                    var loaderInstance = selfProp?.GetValue(null);
                    if (loaderInstance != null)
                    {
                        var cacheField = loaderManagerType.GetField("mCachedDisposables", BindingFlags.Instance | BindingFlags.NonPublic);
                        (cacheField?.GetValue(loaderInstance) as System.Collections.IDictionary)?.Clear();
                    }
                }

                // Reset IsInitialized so the next game can call GumService.Initialize()
                var isInitProp = gumServiceType.GetProperty("IsInitialized", BindingFlags.Instance | BindingFlags.Public);
                isInitProp?.SetValue(gumService, false);
            }
            catch (Exception e)
            {
                // Log but don't rethrow — partial cleanup is better than aborting the run.
                // This uses reflection against KniGum internals, so failures here are most
                // likely caused by a KniGum API change and will show up clearly in the console.
                Console.WriteLine($"[XnaFiddle] CleanUpGumService failed: {e}");
            }
        }

        private static void CleanUpGameWindowRegistry()
        {
            try
            {
                var field = typeof(BlazorGameWindow).GetField("_instances",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (field?.GetValue(null) is System.Collections.IDictionary dict)
                    dict.Clear();
            }
            catch
            {
                // Intentionally swallowed. This reflects a single well-known field on a type
                // in our own codebase (_instances on BlazorGameWindow). The only realistic
                // failure is a refactor that renames the field, which would be caught immediately
                // in development. There is nothing actionable to do if this fails at runtime.
            }
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
    }
}
