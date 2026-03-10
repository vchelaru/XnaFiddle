using System;
using System.Collections.Generic;
using System.IO;
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
        string _statusMessage = "";
        string _statusColor = "#4ec9b0";
        bool _isCompiling;
        bool _pendingCompile;
        bool _monacoReady;
        bool _assetsOpen;

        struct AssetInfo
        {
            public string FileName;
            public int Size;
        }

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
            StateHasChanged();
        }

        private async Task DoCompileAndRun()
        {
            try
            {
                string sourceCode = await JsRuntime.InvokeAsync<string>("monacoInterop.getValue");
                CompilationService.CompilationResult result = await CompilationService.CompileAsync(sourceCode);
                _diagnosticsOutput = result.Log;

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
                        // Dispose existing game if any
                        if (_game != null)
                        {
                            try { _game.Dispose(); } catch { }
                            _game = null;
                        }

                        // Safety: ensure KNI's static BlazorGameWindow registry is clean.
                        // Dispose may fail to fully clean up in single-threaded WASM.
                        CleanUpGameWindowRegistry();

                        _game = (Game)Activator.CreateInstance(gameType);
                        _game.Content = new InMemoryContentManager(_game.Services);
                        _game.Run();
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
                _statusMessage = "Error.";
                _statusColor = "#f48771";
            }

            _isCompiling = false;
            StateHasChanged();
        }

        private async Task OnExampleSelected(ChangeEventArgs e)
        {
            string name = e.Value?.ToString();
            if (string.IsNullOrEmpty(name) || !_monacoReady)
                return;

            string code = ExampleGallery.Load(name);
            if (code != null)
                await JsRuntime.InvokeVoidAsync("monacoInterop.setValue", code);
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
