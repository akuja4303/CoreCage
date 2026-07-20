using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CoreCage.Core.Ledger
{
    /// <summary>
    /// Well-known <see cref="LedgerEntry.TweakId"/> values shared across the ledger, the Prove-It
    /// benchmark recorder, and the App-layer display mapping. Single source of truth for the
    /// "gaming-stack" string, which was previously duplicated as separate literals in
    /// <c>EngineOptimizeService</c> and <c>OptimizeViewModel</c> (review MINOR finding).
    /// </summary>
    public static class TweakIds
    {
        /// <summary>TweakId for the single whole-stack row Prove It records its A/B benchmark to.</summary>
        public const string GamingStack = "gaming-stack";

        private static readonly Lazy<IReadOnlySet<string>> _all = new(() =>
            (IReadOnlySet<string>)typeof(TweakIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToHashSet(StringComparer.Ordinal));

        /// <summary>Every known tweak id (every public const string on this class), reflected once
        /// and cached. Used by <see cref="IsKnown"/> and by community-profile load-time validation
        /// so new ids never need a second place to be registered.</summary>
        public static IReadOnlySet<string> All => _all.Value;

        /// <summary>True if <paramref name="id"/> is a recognized tweak id. Null/empty is never known.</summary>
        public static bool IsKnown(string? id) => !string.IsNullOrEmpty(id) && All.Contains(id);
    }
}
