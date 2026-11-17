using System;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Condições ambientais periódicas por região, com efeito real no jogo:
/// Cavernas: Eco Luminoso (renascimento imediato) · Profundezas: Maré Gélida (fragmentos x2, névoa densa)
/// · Núcleo: Surto do Núcleo (dano +50%, ambiente incandescente).
/// </summary>
public partial class RegionEvents : Node
{
    public sealed class EventDef
    {
        public string Name, Description;
        public Color Color;
        public float Duration;
    }

    public static EventDef For(string regionId) => regionId switch
    {
        "caves" => new EventDef { Name = "Eco Luminoso", Description = "Todos os cristais da caverna renascem!", Color = new Color(0.4f, 0.95f, 1f), Duration = 6f },
        "depths" => new EventDef { Name = "Maré Gélida", Description = "O frio fragmenta os cristais: fragmentos x2", Color = new Color(0.75f, 0.92f, 1f), Duration = 22f },
        "core" => new EventDef { Name = "Surto do Núcleo", Description = "Energia do núcleo: dano +50%", Color = new Color(1f, 0.45f, 0.2f), Duration = 18f },
        _ => null,
    };

    public EnvironmentController Env;
    public WorldBuilder World;
    public Func<RegionDef> CurrentRegion;

    /// <summary>Evento ativo (null se nenhum), região e tempo restante.</summary>
    public EventDef Active { get; private set; }
    public string ActiveRegion { get; private set; }
    public float Remaining { get; private set; }
    public float NextIn { get; private set; }
    public event Action<EventDef> Started;
    public event Action<EventDef> Ended;

    readonly Random _rng = new();
    public float MinInterval = 70f, MaxInterval = 110f;

    public override void _Ready()
    {
        NextIn = 45f; // primeiro evento cedo para apresentar a mecânica
    }

    public override void _Process(double delta)
    {
        var gs = GameState.Instance;
        if (gs == null || !gs.IsPlaying || GetTree().Paused) return;
        float dt = (float)delta;
        var region = CurrentRegion?.Invoke();
        if (region == null) return;

        if (Active != null)
        {
            Remaining -= dt;
            // sai do evento se o jogador mudar de região
            if (Remaining <= 0 || region.Id != ActiveRegion) Stop();
            return;
        }
        NextIn -= dt;
        if (NextIn <= 0) Begin(region.Id);
    }

    public void Begin(string regionId)
    {
        var def = For(regionId);
        if (def == null) return;
        Active = def;
        ActiveRegion = regionId;
        Remaining = def.Duration;
        var gs = GameState.Instance;
        switch (regionId)
        {
            case "caves":
                int n = 0;
                var root = World.RegionRoots[0];
                foreach (var c in World.Crystals)
                    if (c.GetParent() == root && c.State == Crystal.CrystalState.Broken) { c.DebugRespawnNow(); n++; }
                def.Description = n > 0 ? $"{n} cristais renascem de uma vez!" : "A caverna brilha intensamente!";
                Env.OverlayAmbient = def.Color; Env.OverlayFog = new Color(0.1f, 0.3f, 0.45f); Env.OverlayFogDensity = 0.012f;
                break;
            case "depths":
                gs.EventShardMultiplier = 2f;
                Env.OverlayFog = new Color(0.32f, 0.45f, 0.55f); Env.OverlayFogDensity = 0.05f; Env.OverlayAmbient = def.Color;
                break;
            case "core":
                gs.EventDamageMultiplier = 1.5f;
                Env.OverlayFog = new Color(0.45f, 0.08f, 0.02f); Env.OverlayFogDensity = 0.03f; Env.OverlayAmbient = new Color(1f, 0.4f, 0.2f);
                break;
        }
        Env.OverlayAmount = 1f;
        AudioManager.Instance?.Play("rare", -3f, 0.8f);
        Started?.Invoke(def);
    }

    public void Stop()
    {
        if (Active == null) return;
        var def = Active;
        Active = null;
        ActiveRegion = null;
        var gs = GameState.Instance;
        gs.EventDamageMultiplier = 1f;
        gs.EventShardMultiplier = 1f;
        Env.OverlayAmount = 0f;
        NextIn = MinInterval + (float)_rng.NextDouble() * (MaxInterval - MinInterval);
        Ended?.Invoke(def);
    }

    /// <summary>Reinicia (novo jogo / volta ao menu).</summary>
    public void ResetAll()
    {
        Stop();
        NextIn = 45f;
    }
}
