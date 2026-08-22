using System;
using System.Collections.Generic;
using XnaFiddle.Plugins;

namespace XnaFiddle
{
    public class LibraryRegistry
    {
        private readonly List<ILibraryPlugin> _plugins = new();

        public IReadOnlyList<ILibraryPlugin> Plugins => _plugins;

        public void Register(ILibraryPlugin plugin) => _plugins.Add(plugin);

        /// <summary>
        /// Calls CleanUp() on every registered plugin. Each call is wrapped in
        /// try/catch so one failure doesn't prevent other plugins from cleaning up.
        /// </summary>
        public void RunAllCleanups()
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                try
                {
                    _plugins[i].CleanUp();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[XnaFiddle] Cleanup failed ({_plugins[i].Name}): {e}");
                }
            }
        }

        /// <summary>
        /// Calls GameWindowPlugin.ClearCanvasElementCache(), if a GameWindowPlugin is registered.
        /// NOT part of RunAllCleanups() — call this only when the &lt;canvas&gt; element is
        /// actually about to be recreated (a Reach&lt;-&gt;HiDef profile switch), never on an
        /// ordinary restart. See ClearCanvasElementCache's doc comment for why.
        /// </summary>
        public void ClearCanvasElementCache()
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                if (_plugins[i] is GameWindowPlugin gameWindowPlugin)
                {
                    try
                    {
                        gameWindowPlugin.ClearCanvasElementCache();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[XnaFiddle] ClearCanvasElementCache failed: {e}");
                    }
                }
            }
        }
    }
}
