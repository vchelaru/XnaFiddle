using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace XnaFiddle.Pages
{
    public partial class Index
    {
        Game _game;

        string _diagnosticsOutput = "";
        string _diagnosticsColor = "#888";
        string _statusMessage = "";
        string _statusColor = "#4ec9b0";
        bool _isCompiling;
        bool _pendingCompile;
        bool _monacoReady;
        bool _assetsOpen;
        bool _runLocallyOpen;
        int _compileProgress;
        int _compileTotal;
        DateTime _compileStartTime;
        bool _hasCompiledOnce;
        string _selectedExample = "";

        struct AssetInfo
        {
            public string FileName;
            public int Size;
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
                string defaultCode = ExampleGallery.Load("ColorCycle") ?? "";
                await JsRuntime.InvokeVoidAsync("monacoInterop.init", "monacoContainer", defaultCode);
                _monacoReady = true;
                var dotNetRef = DotNetObjectReference.Create(this);
                await JsRuntime.InvokeAsync<object>("initRenderJS", dotNetRef);
                await JsRuntime.InvokeVoidAsync("fileDropInterop.init", dotNetRef);

                // Check ?example= query param first, then #code= hash (hash wins)
                string search = await JsRuntime.InvokeAsync<string>("eval", "window.location.search");
                string exampleFromQuery = UrlCodec.ParseQueryParam(search, "example");
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

        [JSInvokable]
        public void OnFileDropped(string fileName, string base64Data)
        {
            byte[] data = Convert.FromBase64String(base64Data);
            InMemoryContentManager.AddFile(fileName, data);

            // Update UI list (replace if same name)
            _assets.RemoveAll(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            _assets.Add(new AssetInfo { FileName = fileName, Size = data.Length });
            _assetsOpen = true;

            _statusMessage = "Loaded: " + fileName;
            _statusColor = "#4ec9b0";
            StateHasChanged();
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
            _statusMessage = "Copied: " + text;
            _statusColor = "#4ec9b0";
            StateHasChanged();
        }

        private void RemoveAsset(string fileName)
        {
            InMemoryContentManager.RemoveFile(fileName);
            _assets.RemoveAll(a => string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
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

            if (_game == null)
                return;

            try
            {
                _game.Tick();
            }
            catch (Exception e)
            {
                _diagnosticsOutput = "Runtime error: " + e.Message;
                _diagnosticsColor = "#f48771";
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
            _statusColor = "#dcdcaa";
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
                CompilationService.CompilationResult result = await CompilationService.CompileAsync(sourceCode, (current, total) =>
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
                _diagnosticsOutput = $"Compiled in {compileSeconds:0.0}s" + failedNote +
                    (string.IsNullOrEmpty(result.Log) ? "" : "\n" + result.Log);
                _diagnosticsColor = result.Success ? "#888" : "#f48771";

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
                    _statusColor = "#4ec9b0";
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
                        _statusColor = "#4ec9b0";
                    }
                    else
                    {
                        _statusMessage = "No class extending Game found.";
                        _statusColor = "#f48771";
                    }
                }
                else
                {
                    _statusMessage = "Compilation failed.";
                    _statusColor = "#f48771";
                }
            }
            catch (Exception e)
            {
                _diagnosticsOutput = e.ToString();
                _diagnosticsColor = "#f48771";
                _statusMessage = "Error.";
                _statusColor = "#f48771";
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
                _selectedExample = "";
                await JsRuntime.InvokeVoidAsync("eval", $"history.replaceState(null,'','#code={encoded}')");
                await JsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", shareUrl);

                _statusMessage = "Link copied!";
                _statusColor = "#4ec9b0";
            }
            catch (Exception e)
            {
                _statusMessage = "Share failed.";
                _statusColor = "#f48771";
                _diagnosticsOutput = e.Message;
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
                _statusColor = "#4ec9b0";
            }
            catch (Exception e)
            {
                _statusMessage = "Failed to load snippet.";
                _statusColor = "#f48771";
                _diagnosticsOutput = e.Message;
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
                _statusColor = "#4ec9b0";
            }
            catch (Exception e)
            {
                _statusMessage = "Failed to load from link.";
                _statusColor = "#f48771";
                _diagnosticsOutput = e.Message;
            }

            StateHasChanged();
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
                if (_runLocallyOpen)
                {
                    RefreshRunLocallyPackages(code);
                    StateHasChanged();
                }
            }
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
            catch { }
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
            catch { }
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
