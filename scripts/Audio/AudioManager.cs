using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.Audio;

/// <summary>
/// Autoload de áudio. Todos os sons são sintetizados proceduralmente na inicialização
/// (nenhum arquivo externo), com pool de players para evitar alocação durante o jogo.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    const int MixRate = 22050;
    readonly Dictionary<string, AudioStreamWav> _sounds = new();
    readonly List<AudioStreamPlayer> _pool = new();
    readonly Dictionary<int, AudioStreamWav> _music = new();
    AudioStreamPlayer _musicA, _musicB;
    bool _musicOnA = true;
    int _currentTrack = -1;
    readonly Random _rng = new(1234);

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        EnsureBus("Music");
        EnsureBus("SFX");
    }

    public override void _Ready()
    {
        BuildSounds();
        for (int i = 0; i < 14; i++)
        {
            var p = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(p);
            _pool.Add(p);
        }
        var beam = BuildBeamLoop();
        _beam = new AudioStreamPlayer { Bus = "SFX", Stream = beam, VolumeDb = -80 };
        AddChild(_beam);
        _musicA = new AudioStreamPlayer { Bus = "Music", VolumeDb = -80 };
        _musicB = new AudioStreamPlayer { Bus = "Music", VolumeDb = -80 };
        AddChild(_musicA);
        AddChild(_musicB);
        Core.Settings.Apply(GetTree());
    }

    static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        AudioServer.AddBus();
        int idx = AudioServer.BusCount - 1;
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, "Master");
    }

    // ===================== API =====================
    public void Play(string id, float volumeDb = 0f, float pitch = 1f, float pitchJitter = 0f)
    {
        if (!_sounds.TryGetValue(id, out var s)) return;
        var p = GetFreePlayer();
        p.Stream = s;
        p.VolumeDb = volumeDb;
        p.PitchScale = Math.Max(0.1f, pitch + (float)(_rng.NextDouble() * 2 - 1) * pitchJitter);
        p.Play();
    }

    readonly List<AudioStreamPlayer3D> _pool3d = new();

    /// <summary>Som posicional (some com a distância).</summary>
    public void Play3D(string id, Vector3 pos, float volumeDb = 0f, float pitch = 1f)
    {
        if (!_sounds.TryGetValue(id, out var s)) return;
        AudioStreamPlayer3D p = null;
        foreach (var q in _pool3d) if (!q.Playing) { p = q; break; }
        if (p == null)
        {
            if (_pool3d.Count < 12) { p = new AudioStreamPlayer3D { Bus = "SFX", UnitSize = 5f, MaxDistance = 35f }; AddChild(p); _pool3d.Add(p); }
            else { p = _pool3d[0]; _pool3d.RemoveAt(0); _pool3d.Add(p); }
        }
        p.Stream = s;
        p.VolumeDb = volumeDb;
        p.PitchScale = Math.Max(0.1f, pitch + (float)(_rng.NextDouble() * 0.1 - 0.05));
        p.GlobalPosition = pos;
        p.Play();
    }

    AudioStreamPlayer GetFreePlayer()
    {
        foreach (var p in _pool) if (!p.Playing) return p;
        // rouba o player mais antigo
        var oldest = _pool[0];
        _pool.RemoveAt(0);
        _pool.Add(oldest);
        return oldest;
    }

    /// <summary>Troca a trilha ambiente com crossfade (uma por região, -1 = menu).</summary>
    AudioStreamPlayer _beam;
    float _beamTarget = -80f;

    /// <summary>Zumbido contínuo do feixe (loop). intensity 0..1 eleva o tom conforme o cristal racha.</summary>
    public void SetBeam(bool on, float intensity = 0f)
    {
        if (_beam == null) return;
        _beamTarget = on ? -15f : -80f;
        if (on)
        {
            if (!_beam.Playing) _beam.Play();
            _beam.PitchScale = 0.9f + intensity * 0.5f;
        }
    }

    public override void _Process(double delta)
    {
        if (_beam == null) return;
        _beam.VolumeDb = Mathf.MoveToward(_beam.VolumeDb, _beamTarget, (float)delta * 240f);
        if (_beamTarget < -70f && _beam.VolumeDb <= -79f && _beam.Playing) _beam.Stop();
    }

    public void PlayMusic(int track)
    {
        if (track == _currentTrack) return;
        _currentTrack = track;
        if (!_music.TryGetValue(track, out var stream))
        {
            stream = BuildAmbient(track);
            _music[track] = stream;
        }
        var from = _musicOnA ? _musicA : _musicB;
        var to = _musicOnA ? _musicB : _musicA;
        _musicOnA = !_musicOnA;
        to.Stream = stream;
        to.VolumeDb = -40;
        to.Play();
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(to, "volume_db", -6f, 2.5f);
        tw.TweenProperty(from, "volume_db", -60f, 2.5f);
        tw.Chain().TweenCallback(Callable.From(() => { if (from.VolumeDb < -50) from.Stop(); }));
    }

    /// <summary>Pico absoluto (0..1) de cada efeito sintetizado — usado nos testes para detectar saturação.</summary>
    public System.Collections.Generic.Dictionary<string, float> PeakLevels()
    {
        var r = new System.Collections.Generic.Dictionary<string, float>();
        foreach (var kv in _sounds)
        {
            var d = kv.Value.Data;
            int peak = 0;
            for (int i = 0; i + 1 < d.Length; i += 2)
                peak = Math.Max(peak, Math.Abs((int)(short)(d[i] | (d[i + 1] << 8))));
            r[kv.Key] = peak / 32000f;
        }
        return r;
    }

    // ===================== Síntese =====================
    void BuildSounds()
    {
        // golpe cristalino: parciais inarmônicas com decaimento rápido
        _sounds["hit"] = Synth(0.28f, (t, i) =>
        {
            float env = MathF.Exp(-t * 22f);
            float s = MathF.Sin(Tau * 1320 * t) * 0.5f + MathF.Sin(Tau * 2110 * t) * 0.3f + MathF.Sin(Tau * 3370 * t) * 0.18f;
            float click = Noise() * MathF.Exp(-t * 180f) * 0.6f;
            return (s * env + click) * 0.7f;
        });
        // golpe de rocha (sem dano / bloqueado)
        _sounds["thud"] = Synth(0.18f, (t, i) => (Noise() * 0.6f + MathF.Sin(Tau * 110 * t)) * MathF.Exp(-t * 30f) * 0.5f);
        // quebra: ruído + cascata de sinos
        _sounds["break"] = Synth(0.9f, (t, i) =>
        {
            float n = Noise() * MathF.Exp(-t * 9f) * 0.45f;
            float bells = 0;
            for (int k = 0; k < 5; k++)
            {
                float st = k * 0.06f;
                if (t > st) bells += MathF.Sin(Tau * (1500 + k * 380) * (t - st)) * MathF.Exp(-(t - st) * 7f) * 0.18f;
            }
            return n + bells;
        });
        // coleta de fragmento
        _sounds["pickup"] = Synth(0.12f, (t, i) =>
        {
            float f = 900 + t * 6000;
            return MathF.Sin(Tau * f * t) * MathF.Exp(-t * 28f) * 0.35f;
        });
        // venda: arpejo ascendente brilhante
        _sounds["sell"] = Arp(new[] { 784f, 988f, 1175f, 1568f }, 0.07f, 0.5f);
        // compra
        _sounds["buy"] = Arp(new[] { 523f, 659f, 784f, 1047f }, 0.05f, 0.4f);
        // desbloqueio de região: acorde grande e longo
        _sounds["unlock"] = Synth(2.2f, (t, i) =>
        {
            float env = MathF.Min(1f, t * 6f) * MathF.Exp(-t * 1.6f);
            float s = 0;
            foreach (var f in new[] { 261.6f, 329.6f, 392f, 523.2f, 784f })
                s += MathF.Sin(Tau * f * t + MathF.Sin(Tau * 3 * t) * 0.3f) * 0.17f;
            return s * env;
        });
        _sounds["rare"] = Arp(new[] { 1047f, 1319f, 1568f, 2093f, 2637f }, 0.06f, 0.9f);
        _sounds["error"] = Synth(0.22f, (t, i) => Square(180 * t) * MathF.Exp(-t * 12f) * 0.22f);
        _sounds["click"] = Synth(0.05f, (t, i) => MathF.Sin(Tau * 1800 * t) * MathF.Exp(-t * 90f) * 0.35f);
        _sounds["hover"] = Synth(0.04f, (t, i) => MathF.Sin(Tau * 2400 * t) * MathF.Exp(-t * 120f) * 0.15f);
        _sounds["respawn"] = Synth(0.5f, (t, i) => MathF.Sin(Tau * (400 + t * 900) * t) * MathF.Sin(MathF.PI * t / 0.5f) * 0.18f);
        _sounds["swing"] = Synth(0.12f, (t, i) => (MathF.Sin(Tau * 220 * t) * 0.5f + Square(440 * t) * 0.15f + Noise() * 0.2f) * MathF.Sin(MathF.PI * t / 0.12f) * 0.14f);
        _sounds["full"] = Arp(new[] { 440f, 349f }, 0.09f, 0.3f);
        // pulso de ressonância: sub-grave + varredura cristalina
        _sounds["pulse"] = Synth(1.1f, (t, i) =>
        {
            float boom = MathF.Sin(Tau * (70 - t * 30) * t) * MathF.Exp(-t * 4f) * 0.7f;
            float sweep = MathF.Sin(Tau * (600 + t * 2400) * t) * MathF.Exp(-t * 3.5f) * 0.25f;
            float shimmer = MathF.Sin(Tau * 2637 * t) * MathF.Sin(Tau * 7 * t) * MathF.Exp(-t * 2.5f) * 0.12f;
            return (boom + sweep + shimmer + Noise() * MathF.Exp(-t * 25f) * 0.3f) * 0.62f;
        });
        _sounds["charged"] = Synth(0.25f, (t, i) => (MathF.Sin(Tau * 1760 * t) + MathF.Sin(Tau * 2217 * t)) * MathF.Exp(-t * 10f) * 0.18f);
        _sounds["step"] = Synth(0.08f, (t, i) => Noise() * MathF.Exp(-t * 60f) * 0.12f);
        BuildOreSounds();
        // sons-guia (acessibilidade): ping do minério mais próximo e tom de mira
        _sounds["cue_ping"] = Synth(0.12f, (t, i) => MathF.Sin(Tau * 1400 * t) * MathF.Exp(-t * 30f) * 0.5f);
        _sounds["cue_aim"] = Synth(0.18f, (t, i) => (MathF.Sin(Tau * 880 * t) + MathF.Sin(Tau * 1320 * t) * 0.5f) * MathF.Exp(-t * 14f) * 0.35f);
    }

    /// <summary>Stream sintetizado por id (para tocadores 3D fora do gerenciador).</summary>
    public AudioStream GetStream(string id) => _sounds.TryGetValue(id, out var s) ? s : null;

    // ===================== Sons por tipo de minério =====================
    /// <summary>
    /// Cada formato tem timbre próprio para golpe e quebra; cada minério soa diferente
    /// pelo tom (derivado do id, estável entre execuções).
    /// </summary>
    void BuildOreSounds()
    {
        foreach (Data.OreArchetype a in Enum.GetValues(typeof(Data.OreArchetype)))
        {
            _sounds["hit_" + a] = OreHit(a);
            _sounds["brk_" + a] = OreBreak(a);
        }
    }

    public static float OrePitch(Data.MaterialDef m)
    {
        if (m.Special) return 0.7f;
        uint h = 2166136261;
        foreach (char ch in m.Id) { h ^= ch; h *= 16777619; }
        return 0.82f + (h % 41) / 100f; // 0,82 … 1,22
    }

    /// <summary>Som do minério sendo atingido (broke=false) ou quebrando (broke=true).</summary>
    public void PlayOre(Data.MaterialDef m, bool broke, float volumeDb, float pitchMul = 1f)
    {
        string id = (broke ? "brk_" : "hit_") + m.Archetype;
        Play(id, volumeDb, OrePitch(m) * pitchMul, broke ? 0.04f : 0.05f);
    }

    AudioStreamWav OreHit(Data.OreArchetype a) => a switch
    {
        // metal: "clink" com parciais inarmônicas longas
        Data.OreArchetype.Vein => Synth(0.45f, (t, i) =>
            (MathF.Sin(Tau * 523 * t) * 0.35f + MathF.Sin(Tau * 1391 * t) * 0.3f + MathF.Sin(Tau * 2761 * t) * 0.2f) * MathF.Exp(-t * 9f) * 0.75f
            + Noise() * MathF.Exp(-t * 220f) * 0.5f),
        // geodo: batida oca + brilho pequeno
        Data.OreArchetype.Geode => Synth(0.3f, (t, i) =>
            MathF.Sin(Tau * (320 - t * 400) * t) * MathF.Exp(-t * 18f) * 0.6f + MathF.Sin(Tau * 1980 * t) * MathF.Exp(-t * 26f) * 0.18f
            + Noise() * MathF.Exp(-t * 150f) * 0.35f),
        // pedra em faixas: estalo seco e grave
        Data.OreArchetype.Banded => Synth(0.22f, (t, i) =>
            (Noise() * 0.55f * MathF.Exp(-t * 35f) + MathF.Sin(Tau * 160 * t) * MathF.Exp(-t * 25f) * 0.5f) * 0.9f),
        // vidro/obsidiana: tique agudo e curtíssimo
        Data.OreArchetype.Shards => Synth(0.18f, (t, i) =>
            (MathF.Sin(Tau * 3150 * t) * 0.4f + MathF.Sin(Tau * 4410 * t) * 0.25f) * MathF.Exp(-t * 40f) + Noise() * MathF.Exp(-t * 260f) * 0.45f),
        // gelo: estalidos + "ping" frio
        Data.OreArchetype.Ice => Synth(0.3f, (t, i) =>
            (Noise() > 0.82f ? 0.6f : 0f) * MathF.Exp(-t * 20f) + MathF.Sin(Tau * 2600 * t) * MathF.Exp(-t * 16f) * 0.3f),
        // magma: chiado + baque grave
        Data.OreArchetype.Magma => Synth(0.35f, (t, i) =>
            Noise() * MathF.Exp(-t * 10f) * 0.28f + MathF.Sin(Tau * 90 * t) * MathF.Exp(-t * 20f) * 0.55f),
        // coral: "toc" de madeira
        Data.OreArchetype.Coral => Synth(0.16f, (t, i) =>
            (Square(720 * t) * 0.25f + MathF.Sin(Tau * 1440 * t) * 0.2f) * MathF.Exp(-t * 45f) + Noise() * MathF.Exp(-t * 200f) * 0.3f),
        // pérola: "blup" suave que desce
        Data.OreArchetype.Pearl => Synth(0.28f, (t, i) =>
            MathF.Sin(Tau * (950 - t * 1500) * t) * MathF.Exp(-t * 14f) * 0.5f),
        // cristal (padrão)
        _ => Synth(0.28f, (t, i) =>
            (MathF.Sin(Tau * 1320 * t) * 0.5f + MathF.Sin(Tau * 2110 * t) * 0.3f + MathF.Sin(Tau * 3370 * t) * 0.18f) * MathF.Exp(-t * 22f) * 0.7f
            + Noise() * MathF.Exp(-t * 180f) * 0.42f),
    };

    AudioStreamWav OreBreak(Data.OreArchetype a) => a switch
    {
        // metal: estrondo metálico com cauda de "ting"
        Data.OreArchetype.Vein => Synth(1.1f, (t, i) =>
        {
            float crash = Noise() * MathF.Exp(-t * 7f) * 0.35f;
            float ring = 0;
            foreach (var f in new[] { 410f, 987f, 1873f, 3021f }) ring += MathF.Sin(Tau * f * t) * 0.13f;
            return (crash + ring * MathF.Exp(-t * 3.5f) + MathF.Sin(Tau * 80 * t) * MathF.Exp(-t * 12f) * 0.35f) * 0.62f;
        }),
        // geodo: "pop" + cascata de brilhos rápidos
        Data.OreArchetype.Geode => Synth(1.0f, (t, i) =>
        {
            float pop = MathF.Sin(Tau * (220 - t * 200) * t) * MathF.Exp(-t * 14f) * 0.55f;
            float glitter = 0;
            for (int k = 0; k < 8; k++) { float st = 0.05f + k * 0.035f; if (t > st) glitter += MathF.Sin(Tau * (2200 + k * 310) * (t - st)) * MathF.Exp(-(t - st) * 14f) * 0.12f; }
            return pop + glitter + Noise() * MathF.Exp(-t * 30f) * 0.2f;
        }),
        // pedra: desmoronamento longo e grave
        Data.OreArchetype.Banded => Synth(1.1f, (t, i) =>
            Noise() * MathF.Exp(-t * 4f) * 0.4f * (0.6f + 0.4f * MathF.Sin(Tau * 18 * t)) + MathF.Sin(Tau * 55 * t) * MathF.Exp(-t * 5f) * 0.5f),
        // vidro: estilhaço com muitos "pings" agudos
        Data.OreArchetype.Shards => Synth(0.9f, (t, i) =>
        {
            float shatter = Noise() * MathF.Exp(-t * 12f) * 0.4f;
            float pings = 0;
            for (int k = 0; k < 7; k++) { float st = k * 0.045f; if (t > st) pings += MathF.Sin(Tau * (2900 + k * 530) * (t - st)) * MathF.Exp(-(t - st) * 18f) * 0.12f; }
            return shatter + pings;
        }),
        // gelo: rachadura + vento frio
        Data.OreArchetype.Ice => Synth(1.0f, (t, i) =>
            (Noise() > 0.7f ? 0.5f : 0f) * MathF.Exp(-t * 6f) + Noise() * 0.15f * MathF.Sin(MathF.PI * t) + MathF.Sin(Tau * 1760 * t) * MathF.Exp(-t * 6f) * 0.2f),
        // magma: explosão abafada + crepitar de brasas
        Data.OreArchetype.Magma => Synth(1.2f, (t, i) =>
            (MathF.Sin(Tau * (60 + 30 * MathF.Exp(-t * 5f)) * t) * MathF.Exp(-t * 3.5f) * 0.6f + Noise() * MathF.Exp(-t * 5f) * 0.25f
            + (Noise() > 0.93f ? 0.35f : 0f) * MathF.Exp(-t * 2f)) * 0.65f),
        // coral: galhos estalando em sequência
        Data.OreArchetype.Coral => Synth(0.8f, (t, i) =>
        {
            float s2 = 0;
            for (int k = 0; k < 5; k++) { float st = k * 0.09f; if (t > st) s2 += (Square(600 * (t - st)) * 0.18f + Noise() * 0.25f) * MathF.Exp(-(t - st) * 40f); }
            return s2;
        }),
        // pérola: acorde suave e bolhas
        Data.OreArchetype.Pearl => Synth(1.1f, (t, i) =>
        {
            float chord = (MathF.Sin(Tau * 784 * t) + MathF.Sin(Tau * 988 * t) + MathF.Sin(Tau * 1175 * t)) * 0.13f * MathF.Exp(-t * 3f);
            float bub = 0;
            for (int k = 0; k < 4; k++) { float st = 0.1f + k * 0.12f; if (t > st) bub += MathF.Sin(Tau * (600 + k * 150 + (t - st) * 2500) * (t - st)) * MathF.Exp(-(t - st) * 30f) * 0.2f; }
            return chord + bub;
        }),
        // cristal: ruído + cascata de sinos
        _ => Synth(0.9f, (t, i) =>
        {
            float n = Noise() * MathF.Exp(-t * 9f) * 0.45f;
            float bells = 0;
            for (int k = 0; k < 5; k++) { float st = k * 0.06f; if (t > st) bells += MathF.Sin(Tau * (1500 + k * 380) * (t - st)) * MathF.Exp(-(t - st) * 7f) * 0.18f; }
            return n + bells;
        }),
    };

    const float Tau = MathF.PI * 2f;
    uint _seed = 22222;
    float Noise()
    {
        _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
        return (_seed / (float)uint.MaxValue) * 2f - 1f;
    }
    static float Square(float phase) => (phase - MathF.Floor(phase)) < 0.5f ? 1f : -1f;

    AudioStreamWav Arp(float[] notes, float step, float length)
    {
        return Synth(length + notes.Length * step, (t, i) =>
        {
            float s = 0;
            for (int k = 0; k < notes.Length; k++)
            {
                float st = k * step;
                if (t < st) continue;
                float lt = t - st;
                s += (MathF.Sin(Tau * notes[k] * lt) + 0.3f * MathF.Sin(Tau * notes[k] * 2 * lt)) * MathF.Exp(-lt * 6f) * 0.2f;
            }
            return s;
        });
    }

    static AudioStreamWav Synth(float seconds, Func<float, int, float> f)
    {
        int n = (int)(seconds * MixRate);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)MixRate;
            float v = Math.Clamp(f(t, i), -1f, 1f);
            // fade-out curto para evitar estalos
            if (i > n - 200) v *= (n - i) / 200f;
            short s = (short)(v * 32000);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }

    /// <summary>Zumbido de feixe em loop perfeito (frequências múltiplas do período).</summary>
    AudioStreamWav BuildBeamLoop()
    {
        const float len = 0.5f;
        int n = (int)(len * MixRate);
        var data = new byte[n * 2];
        uint seed = 4242;
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)MixRate;
            // 110/220/330 Hz completam ciclos inteiros em 0,5 s → loop sem estalo
            float s = MathF.Sin(Tau * 110 * t) * 0.35f + MathF.Sin(Tau * 220 * t) * 0.22f + MathF.Sin(Tau * 330 * t + MathF.Sin(Tau * 6 * t)) * 0.12f;
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            lp += (((seed / (float)uint.MaxValue) * 2 - 1) - lp) * 0.3f;
            s = (s + lp * 0.12f) * (0.85f + 0.15f * MathF.Sin(Tau * 8 * t));
            short v = (short)(Math.Clamp(s * 0.8f, -1f, 1f) * 30000);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Stereo = false, Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = n,
        };
    }

    /// <summary>Trilha ambiente em loop: pad de acordes lentos com timbre por região.</summary>
    AudioStreamWav BuildAmbient(int track)
    {
        float[][] chords = track switch
        {
            0 => new[] { new[] { 220f, 277.2f, 329.6f }, new[] { 196f, 246.9f, 293.7f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 329.6f } },
            1 => new[] { new[] { 155.6f, 185f, 233.1f }, new[] { 138.6f, 174.6f, 207.7f }, new[] { 146.8f, 185f, 220f }, new[] { 130.8f, 164.8f, 196f } },
            2 => new[] { new[] { 110f, 130.8f, 164.8f }, new[] { 116.5f, 146.8f, 174.6f }, new[] { 103.8f, 130.8f, 155.6f }, new[] { 110f, 138.6f, 164.8f } },
            3 => new[] { new[] { 293.7f, 370f, 440f }, new[] { 246.9f, 311.1f, 370f }, new[] { 277.2f, 349.2f, 415.3f }, new[] { 220f, 277.2f, 329.6f } },
            _ => new[] { new[] { 261.6f, 329.6f, 392f }, new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f } },
        };
        float chordLen = 4f;
        float total = chordLen * chords.Length;
        int n = (int)(total * MixRate);
        var data = new byte[n * 2];
        uint seed = 777;
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)MixRate;
            int ci = (int)(t / chordLen) % chords.Length;
            float local = t - ci * chordLen;
            // crossfade suave entre acordes
            float fadeIn = Math.Min(1f, local / 1.2f);
            float fadeOut = Math.Min(1f, (chordLen - local) / 1.2f);
            float env = Math.Min(fadeIn, fadeOut);
            float s = 0;
            foreach (var f in chords[ci])
            {
                float vib = MathF.Sin(Tau * 0.2f * t) * 0.002f;
                s += MathF.Sin(Tau * f * (1 + vib) * t) * 0.16f;
                s += MathF.Sin(Tau * f * 2.003f * t) * 0.04f;
            }
            // brilho cristalino esparso
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            float noise = (seed / (float)uint.MaxValue) * 2 - 1;
            lp += (noise - lp) * 0.02f;
            s = s * env + lp * (track == 2 ? 0.25f : 0.12f);
            float sparkle = 0;
            float sp = (t * 1.5f) % 1f;
            float sf = chords[ci][(int)(t * 1.5f) % 3] * 4;
            sparkle = MathF.Sin(Tau * sf * t) * MathF.Exp(-sp * 6f) * 0.05f;
            s += sparkle;
            short v = (short)(Math.Clamp(s, -1f, 1f) * 26000);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = n,
        };
    }
}
