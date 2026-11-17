using System;
using System.Collections.Generic;
using System.Linq;
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
                if (f == null) { GD.PushError($"Salvamento: não foi possível abrir {tmp}: {FileAccess.GetOpenError()}"); return false; }
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
            GD.PushError($"Falha ao salvar: {e.Message}");
            return false;
        }
    }

    /// <summary>Carrega o save; se estiver corrompido tenta o backup. Retorna null se nada for válido.</summary>
    public static SaveData Load(out string message)
    {
        message = "";
        if (TryLoadFile(SavePath, out var data)) return data;
        if (FileAccess.FileExists(SavePath)) message = "Arquivo de salvamento principal corrompido. ";
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
            GD.Print($"[Salvamento] Versão mais nova ({d.Version}); carregando em modo compatível.");
    }

    // ------------------------------------------------------------ backups com histórico
    /// <summary>Pasta de backups: "user://backups" para o save do jogador; testes/bot usam pastas próprias.</summary>
    public static string BackupDir => SavePath == "user://save.json"
        ? "user://backups"
        : "user://backups_" + System.IO.Path.GetFileNameWithoutExtension(SavePath);
    public const int MaxSnapshots = 10;

    public sealed class SnapshotInfo
    {
        public string Path;
        public DateTime When;
        public string Reason;
        public SaveData Data;
    }

    /// <summary>
    /// Copia o save atual (se válido) para user://backups com data/hora. Ignora se for idêntico ao último backup.
    /// Mantém os últimos MaxSnapshots arquivos.
    /// </summary>
    public static string Snapshot(string reason)
    {
        try
        {
            string src = ProjectSettings.GlobalizePath(SavePath);
            if (!System.IO.File.Exists(src)) return null;
            string text = System.IO.File.ReadAllText(src);
            if (!TryParse(text, out var data) || IsEmpty(data)) return null; // não guarda saves vazios
            string dir = ProjectSettings.GlobalizePath(BackupDir);
            System.IO.Directory.CreateDirectory(dir);
            var existing = ListSnapshotFiles();
            if (existing.Count > 0 && System.IO.File.ReadAllText(existing[0]) == text) return existing[0];
            string safe = new string((reason ?? "backup").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '.').ToArray());
            string dst = System.IO.Path.Combine(dir, $"save_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{safe}.json");
            System.IO.File.WriteAllText(dst, text);
            var all = ListSnapshotFiles();
            for (int i = MaxSnapshots; i < all.Count; i++) System.IO.File.Delete(all[i]);
            return dst;
        }
        catch (Exception e)
        {
            GD.PushWarning($"Backup falhou: {e.Message}");
            return null;
        }
    }

    /// <summary>Save "vazio" = sem nenhum progresso (evita poluir backups com partidas recém-criadas).</summary>
    public static bool IsEmpty(SaveData d) => d.TotalEarned == 0 && d.CrystalsMined == 0 && d.PlayTimeSeconds < 30;

    static List<string> ListSnapshotFiles()
    {
        string dir = ProjectSettings.GlobalizePath(BackupDir);
        if (!System.IO.Directory.Exists(dir)) return new List<string>();
        return System.IO.Directory.GetFiles(dir, "save_*.json").OrderByDescending(f => f).ToList();
    }

    public static List<SnapshotInfo> ListSnapshots()
    {
        var r = new List<SnapshotInfo>();
        foreach (var f in ListSnapshotFiles())
        {
            if (!TryParse(System.IO.File.ReadAllText(f), out var d)) continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(f); // save_yyyyMMdd_HHmmss_fff_motivo
            var parts = name.Split('_');
            DateTime.TryParseExact(parts.Length > 3 ? parts[1] + parts[2] : "", "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var when);
            r.Add(new SnapshotInfo { Path = f, When = when, Reason = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "", Data = d });
        }
        return r;
    }

    /// <summary>Restaura um backup como save atual (o save atual vira backup antes).</summary>
    public static bool Restore(string snapshotPath)
    {
        if (!System.IO.File.Exists(snapshotPath)) return false;
        string text = System.IO.File.ReadAllText(snapshotPath);
        if (!TryParse(text, out _)) return false;
        Snapshot("antes-de-restaurar");
        string abs = ProjectSettings.GlobalizePath(SavePath);
        System.IO.File.WriteAllText(abs + ".tmp", text);
        System.IO.File.Move(abs + ".tmp", abs, true);
        return true;
    }

    /// <summary>"Novo jogo": arquiva o save atual nos backups e remove os arquivos ativos.</summary>
    public static void ArchiveAndDelete()
    {
        Snapshot("antes-do-novo-jogo");
        Delete();
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
