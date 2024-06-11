namespace Olserve;

public record Config( Dictionary<string, BuildConfig> Routes, ushort Port = 80 );

public record BuildConfig( string Link, string? MainFile = null );
