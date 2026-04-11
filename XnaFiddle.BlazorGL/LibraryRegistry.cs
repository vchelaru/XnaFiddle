using System;
using System.Collections.Generic;

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
    }
}
