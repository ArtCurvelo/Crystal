using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// Ícone de cada minério: uma "foto" da forma 3D do próprio fragmento (OreShapes), tirada uma vez num
/// palco fora da tela (SubViewport com luz e fundo transparente) e guardada. O que cai no chão e o que
/// aparece no HUD/coleção são o mesmo objeto. Até a foto ficar pronta, usa o ícone desenhado antigo.
/// </summary>
public partial class OreIcons : Node
{
    public static OreIcons Instance { get; private set; }
    /// <summary>Um ícone ficou pronto (os ícones na tela se redesenham).</summary>
    public static event Action Ready;

    const int Size = 96;
    static readonly Dictionary<string, Texture2D> Done = new();
    readonly Queue<MaterialDef> _queue = new();
    readonly HashSet<string> _queued = new();
    SubViewport _vp;
    MeshInstance3D _mesh;
    Node3D _pivot;
    bool _busy;

    public static int ReadyCount => Done.Count;

    public static Texture2D Get(MaterialDef m)
    {
        if (m == null) return null;
        if (Done.TryGetValue(m.Id, out var t)) return t;
        Instance?.Request(m);
        return null;
    }

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // sem tela (testes): não há como fotografar; fica no ícone desenhado
        if (DisplayServer.GetName() == "headless") return;
        _vp = new SubViewport
        {
            Size = new Vector2I(Size, Size), OwnWorld3D = true, TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_vp);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.7f, 0.7f, 0.8f), AmbientLightEnergy = 0.25f, TonemapMode = Godot.Environment.ToneMapper.Aces,
            GlowEnabled = false,
        };
        _vp.AddChild(new WorldEnvironment { Environment = env });
        _vp.AddChild(new DirectionalLight3D { LightEnergy = 1.0f, Rotation = new Vector3(-0.9f, 0.6f, 0), LightColor = new Color(1f, 0.96f, 0.9f) });
        _vp.AddChild(new DirectionalLight3D { LightEnergy = 0.35f, Rotation = new Vector3(-0.2f, -2.4f, 0), LightColor = new Color(0.6f, 0.7f, 1f) });
        var cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 0.42f, Position = new Vector3(0, 0.02f, 1f), Current = true };
        _vp.AddChild(cam);
        _pivot = new Node3D { Rotation = new Vector3(0.45f, 0.6f, 0) };
        _vp.AddChild(_pivot);
        _mesh = new MeshInstance3D();
        _pivot.AddChild(_mesh);
        // já deixa os mais comuns na fila (ícones da mochila logo no começo)
        foreach (var m in GameData.Materials.Values) Request(m);
    }

    public void Request(MaterialDef m)
    {
        if (m == null || Done.ContainsKey(m.Id) || _queued.Contains(m.Id) || _vp == null) return;
        _queued.Add(m.Id);
        _queue.Enqueue(m);
    }

    public override async void _Process(double delta)
    {
        if (_busy || _queue.Count == 0 || _vp == null) return;
        _busy = true;
        var m = _queue.Dequeue();
        try
        {
            var mesh = OreShapes.For(m);
            _mesh.Mesh = mesh;
            _mesh.MaterialOverride = ShardManager.DropMaterial(m);
            // centraliza e enquadra pela caixa da forma
            var box = mesh.GetAabb();
            float s = 0.3f / Mathf.Max(0.05f, Mathf.Max(box.Size.X, Mathf.Max(box.Size.Y, box.Size.Z)));
            _mesh.Scale = Vector3.One * s;
            _mesh.Position = -box.GetCenter() * s;
            _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = _vp.GetTexture()?.GetImage();
            if (img != null && !img.IsEmpty())
            {
                Done[m.Id] = ImageTexture.CreateFromImage(img);
                Ready?.Invoke();
            }
        }
        catch (Exception e) { GD.PushWarning($"[OreIcons] {m.Id}: {e.Message}"); }
        finally { _busy = false; }
    }
}
