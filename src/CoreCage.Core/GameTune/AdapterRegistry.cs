using System;

namespace CoreCage.Core.GameTune
{
    /// <summary>Maps a profile's `graphics.format` string to the adapter that handles it.</summary>
    public static class AdapterRegistry
    {
        public static IGraphicsConfigAdapter For(string format) => format switch
        {
            "unreal-ini"         => new UnrealIniAdapter(),
            "frostbite-profsave" => new KeyValueAdapter("frostbite-profsave", ' ', quoteValues: false),
            "stingray-config"    => new KeyValueAdapter("stingray-config", '=', quoteValues: false),
            "source-cfg"         => new KeyValueAdapter("source-cfg", ' ', quoteValues: true),
            _ => throw new NotSupportedException($"No GameTune adapter for format '{format}'.")
        };
    }
}
