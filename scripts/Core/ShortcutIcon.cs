using System;
using Path = System.IO.Path;
using File = System.IO.File;
using FileAccess = Godot.FileAccess;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// O atalho da área de trabalho e do Menu Iniciar pega o ícone do .exe, e o atualizador só troca o .pck e a DLL
/// (nunca o .exe). Para quem já tinha o jogo instalado também ver o ícone novo, o próprio jogo, uma vez por
/// versão do ícone, grava o icone.ico ao lado do .exe e aponta os atalhos do usuário (que levam ao jogo) para ele.
/// Tudo na pasta do usuário, sem pedir administrador; qualquer falha é ignorada (o jogo segue normalmente).
/// </summary>
public static class ShortcutIcon
{
    public const int IconVersion = 2;
    static string FlagPath => $"user://atalho_icone_v{IconVersion}.ok";

    public static bool Supported => OS.HasFeature("windows") && BuildFlavor.IsExported && !BuildFlavor.IsSteam;

    public static void Apply()
    {
        if (!Supported || FileAccess.FileExists(FlagPath)) return;
        try
        {
            var bytes = FileAccess.GetFileAsBytes("res://icon.ico");
            if (bytes == null || bytes.Length < 100) return;
            string exe = OS.GetExecutablePath().Replace('/', '\\');
            string ico = Path.Combine(Path.GetDirectoryName(exe) ?? ".", "icone.ico");
            File.WriteAllBytes(ico, bytes);
            OS.CreateProcess("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-Command", Script(exe, ico) });
            using var f = FileAccess.Open(FlagPath, FileAccess.ModeFlags.Write);
            f?.StoreString(DateTime.Now.ToString("s"));
        }
        catch (Exception e) { GD.Print("[Ícone] não foi possível atualizar o atalho: " + e.Message); }
    }

    static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>Comando do PowerShell: troca o ícone dos atalhos .lnk que apontam para o executável do jogo.</summary>
    public static string Script(string exe, string ico) =>
        "$ws=New-Object -ComObject WScript.Shell; $exe=" + Q(exe) + "; $ico=" + Q(ico) + "; " +
        "$lnks=@(); " +
        "$d=[Environment]::GetFolderPath('Desktop'); if(Test-Path $d){ $lnks+=Get-ChildItem -Path $d -Filter *.lnk -ErrorAction SilentlyContinue }; " +
        "$p=[Environment]::GetFolderPath('Programs'); if(Test-Path $p){ $lnks+=Get-ChildItem -Path $p -Filter *.lnk -Recurse -ErrorAction SilentlyContinue }; " +
        "foreach($f in $lnks){ try{ $l=$ws.CreateShortcut($f.FullName); if($l.TargetPath -ieq $exe){ $l.IconLocation=$ico+',0'; $l.Save() } }catch{} }; " +
        "try{ & ie4uinit.exe -show }catch{}";
}
