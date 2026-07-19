using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CoreCage.Core.Profiles
{
    /// <summary>Persists the user's per-game profiles to %LOCALAPPDATA%\CoreCage\profiles.json.</summary>
    public static class ProfileStore
    {
        private static readonly string PathJson = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreCage", "profiles.json");

        public static List<GameProfile> Load()
        {
            try
            {
                if (!File.Exists(PathJson)) return new List<GameProfile>();
                return JsonConvert.DeserializeObject<List<GameProfile>>(File.ReadAllText(PathJson))
                       ?? new List<GameProfile>();
            }
            catch (Exception ex) { Logger.LogError("Loading profiles.json failed", ex); return new List<GameProfile>(); }
        }

        public static void Save(List<GameProfile> profiles)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathJson)!);
                File.WriteAllText(PathJson, JsonConvert.SerializeObject(profiles, Formatting.Indented));
                Logger.Log($"Saved {profiles.Count} game profiles");
            }
            catch (Exception ex) { Logger.LogError("Saving profiles.json failed", ex); }
        }
    }
}
