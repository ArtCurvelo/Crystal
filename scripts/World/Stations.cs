using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>Base comum: corpo estático interagível com rótulo flutuante e brilho animado.</summary>
public abstract partial class StationBase : StaticBody3D, IInteractable
{
    protected Label3D Label;
    protected Node3D Ring;
    protected float Time;

    protected void SetupBody(Vector3 size)
    {
        CollisionLayer = PlayerController.WorldLayer | PlayerController.InteractLayer;
        CollisionMask = 0;
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, size.Y / 2, 0) };
        AddChild(shape);
    }

    protected Label3D MakeLabel(string text, Color color, float y, int size = 64)
    {
        var l = new Label3D
        {
            Text = text,
            FontSize = size,
            OutlineSize = 14,
            PixelSize = 0.005f,
            Modulate = color,
            OutlineModulate = new Color(0.02f, 0.01f, 0.05f, 0.9f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Position = new Vector3(0, y, 0),
            VisibilityRangeEnd = 30f,
        };
        AddChild(l);
        return l;
    }

    protected MeshInstance3D AddMesh(Mesh mesh, Material mat, Vector3 pos, Vector3? scale = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
            Scale = scale ?? Vector3.One,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        return mi;
    }

    public override void _Process(double delta)
    {
        Time += (float)delta;
        if (Ring != null)
        {
            Ring.RotateY((float)delta * 0.8f);
            Ring.Position = new Vector3(0, Ring.Position.Y, 0);
        }
        if (Label != null) Label.Position = new Vector3(0, Label.Position.Y + Mathf.Sin(Time * 2f) * 0.0015f, 0);
    }

    public abstract string GetPrompt();
    public abstract void Interact(PlayerController player);
}

/// <summary>Estação de venda: converte fragmentos em moeda.</summary>
public partial class SellStation : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(1.6f, 1.2f, 1.6f));
        var accent = new Color(0.3f, 1f, 0.75f);
        AddMesh(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.85f, Height = 0.9f, RadialSegments = 10, Rings = 1 },
            MeshFactory.Metal(new Color(0.22f, 0.24f, 0.3f), 0.5f), new Vector3(0, 0.45f, 0));
        AddMesh(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.4f, Height = 0.3f, RadialSegments = 10, Rings = 1 },
            MeshFactory.Emissive(new Color(0.1f, 0.3f, 0.25f), accent, 2.2f), new Vector3(0, 1.05f, 0));
        Ring = new Node3D { Position = new Vector3(0, 1.7f, 0) };
        AddChild(Ring);
        for (int i = 0; i < 3; i++)
        {
            var gem = new MeshInstance3D
            {
                Mesh = MeshFactory.CrystalPrism(6, 1, 1, 0.35f),
                MaterialOverride = MeshFactory.Emissive(accent, accent, 2.5f),
                Scale = new Vector3(0.08f, 0.22f, 0.08f),
                Position = new Vector3(Mathf.Cos(i * Mathf.Tau / 3) * 0.45f, 0, Mathf.Sin(i * Mathf.Tau / 3) * 0.45f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            Ring.AddChild(gem);
        }
        var light = new OmniLight3D { LightColor = accent, LightEnergy = 1.4f, OmniRange = 6f, Position = new Vector3(0, 2, 0) };
        AddChild(light);
        Label = MakeLabel("VENDA", accent, 2.45f);
    }

    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        return gs.InventoryCount > 0
            ? $"[E] Vender {gs.InventoryCount} fragmentos  ({GameState.FormatMoney(gs.InventoryValue())})"
            : "[E] Estação de Venda";
    }

    public override void Interact(PlayerController player) => UiBus.RequestSell();
}

/// <summary>Terminal de upgrades e desbloqueios.</summary>
public partial class ShopTerminal : StationBase
{
    MeshInstance3D _screen;

    public override void _Ready()
    {
        SetupBody(new Vector3(1.4f, 1.8f, 0.9f));
        var accent = new Color(1f, 0.75f, 0.3f);
        AddMesh(new BoxMesh { Size = new Vector3(1.3f, 1.1f, 0.8f) }, MeshFactory.Metal(new Color(0.25f, 0.22f, 0.3f), 0.5f), new Vector3(0, 0.55f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.12f) }, MeshFactory.Metal(new Color(0.18f, 0.16f, 0.22f), 0.5f), new Vector3(0, 1.45f, -0.25f));
        _screen = AddMesh(new BoxMesh { Size = new Vector3(1.0f, 0.62f, 0.02f) }, MeshFactory.Emissive(new Color(0.2f, 0.12f, 0.05f), accent, 1.8f), new Vector3(0, 1.45f, -0.18f));
        AddMesh(new BoxMesh { Size = new Vector3(1.0f, 0.06f, 0.5f) }, MeshFactory.Emissive(accent, accent, 1.2f), new Vector3(0, 1.12f, 0.05f));
        var light = new OmniLight3D { LightColor = accent, LightEnergy = 1.2f, OmniRange = 5.5f, Position = new Vector3(0, 1.8f, 0.8f) };
        AddChild(light);
        Label = MakeLabel("MELHORIAS", accent, 2.3f);
    }

    public override string GetPrompt() => "[E] Terminal de Melhorias";
    public override void Interact(PlayerController player) => UiBus.RequestShop();
}

/// <summary>Barreira energética que bloqueia uma região até ser desbloqueada.</summary>
public partial class RegionGate : StationBase
{
    public RegionDef Region;
    CollisionShape3D _shape;
    MeshInstance3D _barrier;
    StandardMaterial3D _barrierMat;
    bool _open;
    float _width = 7f, _height = 5f;

    public void Init(RegionDef region, float width, float height)
    {
        Region = region; _width = width; _height = height;
    }

    public override void _Ready()
    {
        CollisionLayer = PlayerController.WorldLayer | PlayerController.InteractLayer;
        CollisionMask = 0;
        _shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(_width, _height, 0.4f) },
            Position = new Vector3(0, _height / 2, 0),
        };
        AddChild(_shape);
        _barrierMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(Region.AccentColor, 0.35f),
        };
        _barrier = AddMesh(new QuadMesh { Size = new Vector2(_width, _height) }, _barrierMat, new Vector3(0, _height / 2, 0));
        // grade de linhas para dar textura sem imagens
        var lineMat = MeshFactory.Unshaded(Region.AccentColor.Lightened(0.3f));
        for (int i = 0; i < 6; i++)
        {
            AddMesh(new BoxMesh { Size = new Vector3(_width, 0.04f, 0.04f) }, lineMat, new Vector3(0, 0.4f + i * (_height - 0.6f) / 5f, 0));
        }
        // pilares
        var pillar = MeshFactory.Emissive(new Color(0.2f, 0.2f, 0.25f), Region.AccentColor, 0.8f);
        AddMesh(new BoxMesh { Size = new Vector3(0.5f, _height, 0.6f) }, pillar, new Vector3(-_width / 2, _height / 2, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.5f, _height, 0.6f) }, pillar, new Vector3(_width / 2, _height / 2, 0));
        Label = MakeLabel("", Colors.White, _height + 0.6f, 56);
        Label.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        Label.Position = new Vector3(0, _height - 0.9f, 0.3f);
        Label.RotationDegrees = new Vector3(0, 0, 0);
        // rótulo também no lado de trás
        var back = MakeLabel("", Colors.White, _height - 0.9f, 56);
        back.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        back.Position = new Vector3(0, _height - 0.9f, -0.3f);
        back.RotationDegrees = new Vector3(0, 180, 0);
        _backLabel = back;
        GameState.Instance.RegionsChanged += Refresh;
        GameState.Instance.MoneyChanged += RefreshText;
        Refresh();
    }

    Label3D _backLabel;

    public override void _ExitTree()
    {
        if (GameState.Instance == null) return;
        GameState.Instance.RegionsChanged -= Refresh;
        GameState.Instance.MoneyChanged -= RefreshText;
    }

    void Refresh()
    {
        bool unlocked = GameState.Instance.IsUnlocked(Region.Id);
        if (unlocked && !_open)
        {
            _open = true;
            _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
            if (IsInsideTree() && GetTree().CurrentScene != null && Time > 0.1f)
            {
                AudioManager.Instance?.Play("unlock", -2f);
                Effects.Instance?.Burst(GlobalPosition + Vector3.Up * 2.5f, Region.AccentColor, 3f);
                var tw = CreateTween();
                tw.TweenProperty(_barrierMat, "albedo_color:a", 0f, 1.2f);
                tw.TweenCallback(Callable.From(HideBarrier));
            }
            else HideBarrier();
        }
        RefreshText();
    }

    void HideBarrier()
    {
        foreach (Node c in GetChildren())
            if (c is MeshInstance3D m && m != null)
            {
                // mantém só os pilares visíveis
                if (m.Mesh is BoxMesh b && b.Size.X < 1f) continue;
                m.Visible = false;
            }
        Label.Visible = false;
        _backLabel.Visible = false;
    }

    void RefreshText()
    {
        if (_open || Label == null) return;
        var gs = GameState.Instance;
        bool prevUnlocked = Region.Index == 0 || gs.IsUnlocked(GameData.Regions[Region.Index - 1].Id);
        string cost = GameState.FormatMoney(Region.UnlockCost);
        string text = $"{Region.Name.ToUpper()}\n{cost}";
        Label.Text = text;
        _backLabel.Text = text;
        var col = gs.Data.Money >= Region.UnlockCost && prevUnlocked ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.85f, 0.85f);
        Label.Modulate = col;
        _backLabel.Modulate = col;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!_open && _barrierMat != null)
        {
            float a = 0.28f + 0.1f * Mathf.Sin(Time * 2.2f);
            _barrierMat.AlbedoColor = new Color(Region.AccentColor, a);
        }
    }

    public override string GetPrompt()
    {
        if (_open) return null;
        var gs = GameState.Instance;
        if (gs.CanUnlock(Region)) return $"[E] Desbloquear {Region.Name}  ({GameState.FormatMoney(Region.UnlockCost)})";
        long missing = Region.UnlockCost - gs.Data.Money;
        return $"Bloqueado: {Region.Name} — faltam {GameState.FormatMoney(missing)}";
    }

    public override void Interact(PlayerController player)
    {
        if (_open) return;
        var gs = GameState.Instance;
        if (gs.TryUnlockRegion(Region.Id))
        {
            player?.AddShake(1.2f);
        }
        else
        {
            AudioManager.Instance?.Play("error", -6f);
            gs.Notify("Moeda insuficiente", $"{Region.Name} custa {GameState.FormatMoney(Region.UnlockCost)}.", NotifyKind.Warning);
        }
    }

    public bool IsOpen => _open;
}
