using System;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Diferença entre a versão dos jogadores e a de teste dos desenvolvedores.
/// - Exportado (jogadores): ferramentas de desenvolvedor (painel de admin F9) desligadas, a não ser que
///   exista o arquivo de chave de desenvolvedor na pasta do usuário com a chave certa. O código só guarda
///   o SHA-256 da chave, então ela não pode ser descoberta lendo o repositório.
/// - Rodando do código-fonte (editor, testes, bot): ferramentas sempre ligadas.
/// - Build da Steam (recurso "steam" na exportação): a Steam entrega as atualizações; o atualizador interno fica desligado.
/// </summary>
public static class BuildFlavor
{
    const string DevKeyHash = "d5115e9d38560d4e3b6ed119bc271b347b5746d96ac1ce320f8a2d9943396956";
    public const string DevKeyFile = "user://chave_dev.txt";

    public static bool IsExported => OS.HasFeature("template") && !OS.HasFeature("editor");
    public static bool IsSteam => OS.HasFeature("steam");

    static bool? _dev;
    /// <summary>Painel de admin e comandos de teste disponíveis.</summary>
    public static bool DevTools => _dev ??= !IsExported || HasDevKey();

    static bool HasDevKey()
    {
        try { return FileAccess.FileExists(DevKeyFile) && Matches(FileAccess.GetFileAsString(DevKeyFile)); }
        catch { return false; }
    }

    public static bool Matches(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant();
        return hash == DevKeyHash;
    }
}
