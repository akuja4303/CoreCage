using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreCage.Core.Modes
{
    /// <summary>
    /// Central catalog of IModeModule instances: the built-in "Gaming" mode plus whatever future
    /// modes (Trading/Coding, or private modules shipped outside this repo) call Register() with.
    /// This is the seam -- a private module never needs to touch CoreCage.Core source, it just
    /// registers an IModeModule implementation at startup.
    /// </summary>
    public static class ModeRegistry
    {
        private static readonly List<IModeModule> _modules = new()
        {
            new GamingMode(),
        };

        /// <summary>All currently registered modules (built-ins + anything Register() has added).</summary>
        public static IReadOnlyList<IModeModule> Modules => _modules;

        /// <summary>
        /// Adds a module to the registry. If a module with the same Name (case-insensitive) is already
        /// registered, it is replaced -- lets a private module override a built-in, or a test re-register
        /// a fresh instance, without needing a separate Unregister API.
        /// </summary>
        public static void Register(IModeModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));

            int existingIndex = _modules.FindIndex(m => string.Equals(m.Name, module.Name, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0) _modules[existingIndex] = module;
            else _modules.Add(module);
        }

        /// <summary>Looks up a module by Name (case-insensitive). Null if not found.</summary>
        public static IModeModule? Get(string name) =>
            _modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
