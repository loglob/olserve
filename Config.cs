namespace Olserve;

public record Config( Dictionary<string,string> Routes, ushort Port = 80 );
