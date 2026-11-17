using System;
using System.Text.Json;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Salvamento em JSON em user://. Usa escrita atômica (arquivo temporário + renomeação)
/// e mantém um backup do último save válido.
/// </summary>
public static class SaveSystem
{
    public static string SavePath = "user://save.json";
    public static string BackupPath => SavePath + ".bak";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public static bool HasSave() => FileAccess.FileExists(SavePath) || FileAccess.FileExists(BackupPath);

    public static bool Save(SaveData data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, Options);
            string tmp = SavePath + ".tmp";
            using (var f = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
            {
                if (f == null) { GD.PushError($"Save: não foi possível abrir {tmp}: {FileAccess.GetOpenError()}"); return false; }
                f.StoreString(json);
            }
            string abs = ProjectSettings.GlobalizePath(SavePath);
            string absTmp = ProjectSettings.GlobalizePath(tmp);
            string absBak = ProjectSettings.GlobalizePath(BackupPath);
            if (System.IO.File.Exists(abs))
            {
                // só promove a backup se o save atual for válido
                if (TryParse(System.IO.File.ReadAllText(abs), out _)) System.IO.File.Copy(abs, absBak, true);
            }
            System.IO.File.Move(absTmp, abs, true);
            return true;
        }
        catch (Exception e)
        {
            GD.PushError($"Save falhou: {e.Message}");
            return false;
        }
    }

    /// <summary>Carrega o save; se estiver corrompido tenta o backup. Retorna null se nada for válido.</summary>
    public static SaveData Load(out string message)
    {
        message = "";
        if (TryLoadFile(SavePath, out var data)) return data;
        if (FileAccess.FileExists(SavePath)) message = "Save principal corrompido. ";
        if (TryLoadFile(BackupPath, out data))
        {
            message += "Backup restaurado.";
            return data;
        }
        if (message.Length > 0) message += "Nenhum backup válido: novo jogo iniciado.";
        return null;
    }

    static bool TryLoadFile(string path, out SaveData data)
    {
        data = null;
        if (!FileAccess.FileExists(path)) return false;
        try
        {
            string text = FileAccess.GetFileAsString(path);
            return TryParse(text, out data);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Falha ao ler {path}: {e.Message}");
            return false;
        }
    }

    public static bool TryParse(string text, out SaveData data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            data = JsonSerializer.Deserialize<SaveData>(text, Options);
            if (data == null) return false;
            Migrate(data);
            data.Sanitize();
            return true;
        }
        catch (Exception)
        {
            data = null;
            return false;
        }
    }

    /// <summary>Migração entre versões. Campos novos recebem os padrões da classe automaticamente.</summary>
    static void Migrate(SaveData d)
    {
        if (d.Version <= 0) d.Version = 1;
        // Futuro: if (d.Version < 2) { ... }
        if (d.Version > SaveData.CurrentVersion)
            GD.Print($"[Save] Versão mais nova ({d.Version}); carregando em modo compatível.");
    }

    public static void Delete()
    {
        foreach (var p in new[] { SavePath, BackupPath })
        {
            string abs = ProjectSettings.GlobalizePath(p);
            if (System.IO.File.Exists(abs)) System.IO.File.Delete(abs);
        }
    }
}
