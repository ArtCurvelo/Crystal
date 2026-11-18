using System.Collections.Generic;

namespace CrystalExcavator.Core;

/// <summary>Tabelas de tradução (chave = texto em português).</summary>
public static partial class LocTables
{
    public static readonly Dictionary<string, string> En = new();
    public static readonly Dictionary<string, string> Es = new();

    static LocTables()
    {
        foreach (var (pt, en, es) in Entries())
        {
            En[pt] = en;
            Es[pt] = es;
        }
    }

    static partial void Fill(List<(string pt, string en, string es)> list);

    static IEnumerable<(string pt, string en, string es)> Entries()
    {
        var list = new List<(string, string, string)>();
        Fill(list);
        return list;
    }
}
