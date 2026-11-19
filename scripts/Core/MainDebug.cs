using System;
using System.Globalization;
using System.Linq;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Comandos de administrador (painel F9 / Back no controle) para testar conteúdo novo
/// sem jogar a campanha inteira. Alteram o save atual.
/// </summary>
public partial class Main
{
    public static readonly string DebugHelp =
        "dinheiro N · tudo · regioes · melhorias · habilidades · casa · mochila · goblins · curar\n" +
        "ir 1-6 · ir coracao · ir acampamento · ir goblin · ir poco · hora HH · fimdia · novodia · velocidade N\n" +
        "rebeliao · evento · renascer · construcoes (libera esteira, funil, drone, robô) · ajuda";

    /// <summary>Executa um comando de admin e devolve o resultado (texto).</summary>
    public string RunDebug(string line)
    {
        var gs = GameState.Instance;
        var parts = (line ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        string cmd = parts[0], arg = parts.Length > 1 ? parts[1] : "";
        double Num(double def) => double.TryParse(arg.Replace("k", "000").Replace("m", "000000"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        bool playing = CurrentMode == Mode.Playing && Player != null;
        switch (cmd)
        {
            case "ajuda": case "help": return DebugHelp;
            case "dinheiro": case "money":
            {
                long v = (long)Math.Min(Num(1_000_000), 1e15);
                gs.AddMoney(v);
                return $"+{GameState.FormatMoney(v)}";
            }
            case "regioes": case "regions":
                foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
                gs.RaiseAll();
                return "Todas as regiões liberadas";
            case "melhorias": case "upgrades":
                foreach (var id in UpgradeIds.All) gs.Data.Upgrades[id] = GameData.Upgrades[id].MaxLevel;
                gs.RaiseAll();
                return "Melhorias no máximo";
            case "habilidades": case "skills":
                foreach (var s in HomeData.Skills) if (!gs.Data.Skills.Contains(s.Id)) gs.Data.Skills.Add(s.Id);
                gs.RaiseAll();
                return "Todas as habilidades";
            case "casa": case "house":
                foreach (var h in HomeData.House) gs.Data.House[h.Id] = h.MaxLevel;
                gs.RaiseAll();
                return "Casa no máximo";
            case "tudo": case "all":
                RunDebug("regioes"); RunDebug("melhorias"); RunDebug("habilidades"); RunDebug("casa"); RunDebug("goblins");
                gs.AddMoney(100_000_000);
                return "Tudo liberado + ◈ 100 mi";
            case "mochila": case "backpack":
            {
                var mats = GameData.Materials.Values.Where(m => !m.Special).ToList();
                int i = 0;
                while (gs.FreeSpace > 0 && i < 10000) gs.AddItem(mats[i++ % mats.Count].Id);
                return $"Mochila cheia ({gs.InventoryCount})";
            }
            case "goblins":
                foreach (var d in GoblinData.All) gs.MarkGoblinFound(d.RegionId);
                foreach (var c in World.GoblinCamps) c.SyncState();
                return "Todos os goblins encontrados (fale com eles para contratar)";
            case "curar": case "heal":
                Player?.RestoreHp();
                return "Vida cheia";
            case "ir": case "go":
            {
                if (!playing) return "Comece uma partida primeiro";
                Vector3 to;
                if (arg is "coracao" or "heart") to = World.Layouts[2].OnGround(World.Layouts[2].Heart.A.X, World.Layouts[2].Heart.A.Y + World.Layouts[2].Heart.RA - 4f);
                else if (arg is "acampamento" or "camp" or "") to = World.SpawnPoint;
                else if (arg is "poco" or "pit")
                {
                    var L = World.LayoutAt(Player.GlobalPosition);
                    if (L.Pits.Count == 0) return "Sem poço nesta região";
                    var pit = L.Pits.OrderBy(pt => pt.TopExit.DistanceTo(Player.GlobalPosition)).First();
                    to = pit.TopExit;
                }
                else if (arg is "goblin")
                {
                    int ri = WorldBuilder.RegionAt(Player.GlobalPosition).Index;
                    var camp = World.GoblinCamps.FirstOrDefault(c => c.RegionIndex == ri);
                    if (camp == null) return "Sem goblin nesta região";
                    to = camp.HideoutPos + new Vector3(1.5f, 0.1f, 0);
                }
                else if (int.TryParse(arg, out int n) && n >= 1 && n <= World.Layouts.Count)
                {
                    var L = World.Layouts[n - 1];
                    var e = n == 1 ? World.SpawnPoint : L.OnGround(L.Entrance.X, L.Entrance.Z - 3f);
                    to = e;
                    if (!gs.IsUnlocked(GameData.Regions[n - 1].Id)) RunDebug("regioes");
                }
                else return "Use: ir 1-6 · ir coracao · ir acampamento · ir goblin";
                Player.GlobalPosition = to + Vector3.Up * 0.2f;
                Player.Velocity = Vector3.Zero;
                UpdateRegion(true);
                UpdateRegionCulling();
                return $"Teleportado ({to.X:0}, {to.Z:0})";
            }
            case "hora": case "time":
            {
                double h = Math.Clamp(Num(12), 6, 17.99);
                gs.Data.DayOver = false;
                gs.Data.DayTimeSeconds = (h - 6) / 12.0 * gs.DayLength;
                return $"Hora: {h:0.#}h";
            }
            case "fimdia": case "endday":
                gs.EndDay(true);
                return "Turno encerrado";
            case "novodia": case "newday":
                gs.StartNewDay();
                return $"Dia {gs.Data.Day}";
            case "velocidade": case "speed":
            {
                float s = (float)Math.Clamp(Num(1), 0.1, 20);
                Engine.TimeScale = s;
                return $"Velocidade do tempo x{s:0.#}";
            }
            case "rebeliao": case "rebellion":
            {
                if (!playing) return "Comece uma partida primeiro";
                int ri = WorldBuilder.RegionAt(Player.GlobalPosition).Index;
                var camp = World.GoblinCamps.FirstOrDefault(c => c.RegionIndex == ri);
                if (camp == null) return "Sem goblin nesta região";
                gs.GoblinData(camp.RegionId).Found = true;
                gs.GoblinData(camp.RegionId).State = (int)GameState.GoblinMood.Rebel;
                camp.StartRebellion();
                return $"{camp.Def.Name} se rebelou";
            }
            case "evento": case "event":
            {
                var r = _currentRegion ?? GameData.Regions[0];
                Events.Begin(r.Id);
                return $"Evento: {RegionEvents.For(r.Id)?.Name}";
            }
            case "renascer": case "prestige":
                gs.Data.GameCompleted = true;
                gs.Data.RunEarned = Math.Max(gs.Data.RunEarned, 25_000_000);
                return $"Renascimento disponível (+{gs.PendingEssence} Essência) — use o Terminal";
            case "construcoes": case "buildings": case "drone":
                foreach (var id in new[] { SkillIds.BuildBelt, SkillIds.BuildHopper, SkillIds.BuildDrone, SkillIds.BuildRobot })
                    if (!gs.HasSkill(id)) gs.Data.Skills.Add(id);
                gs.AddMoney(500_000);
                gs.RaiseAll();
                return "Ramo Construção liberado + ◈ 500 mil (B para construir)";
            default:
                return $"Comando desconhecido: {cmd}. Digite ajuda.";
        }
    }
}
