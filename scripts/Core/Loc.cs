using System.Collections.Generic;

namespace CrystalExcavator.Core;

/// <summary>
/// Tradução do jogo. O texto-fonte é o português (chave); as tabelas trazem inglês e espanhol.
/// Textos ainda sem tradução aparecem em português. Use T("...") nos textos fixos e
/// F("... {0} ...", args) nos que têm valores.
/// </summary>
public static class Loc
{
    public static readonly string[] Languages = { "pt", "en", "es" };
    public static readonly string[] LanguageNames = { "Português", "English", "Español" };
    public static string Lang => Settings.Data?.Language ?? "pt";

    public static string T(string pt)
    {
        if (string.IsNullOrEmpty(pt) || Lang == "pt") return pt;
        var table = Lang == "en" ? LocTables.En : LocTables.Es;
        return table.TryGetValue(pt, out var tr) ? tr : pt;
    }

    public static string F(string pt, params object[] args) => string.Format(T(pt), args);
}
