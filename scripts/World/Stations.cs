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
            Text = Core.Loc.T(text),
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
        SetupBody(new Vector3(2.4f, 2.2f, 1.1f));
        var accent = new Color(0.3f, 1f, 0.75f);
        var cyan = new Color(0.35f, 0.9f, 1f);
        var orange = new Color(1f, 0.55f, 0.18f);
        var body = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.37f, 0.42f), Roughness = 0.6f, Metallic = 0.2f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.15f, 0.19f), Roughness = 0.7f };
        var trim = MeshFactory.Emissive(orange, orange, 1.6f);
        // balcão
        AddMesh(new BoxMesh { Size = new Vector3(2.4f, 0.95f, 1.0f) }, body, new Vector3(0, 0.475f, 0.05f));
        AddMesh(new BoxMesh { Size = new Vector3(2.5f, 0.08f, 1.1f) }, dark, new Vector3(0, 0.99f, 0.05f));
        for (int i = 0; i < 2; i++)
            AddMesh(new BoxMesh { Size = new Vector3(0.5f, 0.05f, 0.04f) }, trim, new Vector3(-0.55f + i * 1.1f, 0.3f, 0.56f));
        // estandarte laranja com picareta
        AddMesh(new BoxMesh { Size = new Vector3(0.5f, 0.62f, 0.015f) }, new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.42f, 0.12f), Roughness = 0.9f }, new Vector3(0.05f, 0.62f, 0.565f));
        var pickMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.85f, 0.7f), Roughness = 0.8f };
        AddMesh(new BoxMesh { Size = new Vector3(0.04f, 0.38f, 0.01f) }, pickMat, new Vector3(0.05f, 0.58f, 0.575f)).Rotation = new Vector3(0, 0, 0.6f);
        AddMesh(new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.01f) }, pickMat, new Vector3(0.12f, 0.69f, 0.575f)).Rotation = new Vector3(0, 0, -0.35f);
        // painel holográfico "cristal → moeda"
        var screenRoot = new Node3D { Position = new Vector3(0, 1.65f, -0.25f), Rotation = new Vector3(-0.2f, 0, 0) };
        AddChild(screenRoot);
        void S(Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
        {
            screenRoot.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        S(new BoxMesh { Size = new Vector3(1.9f, 1.0f, 0.12f) }, dark, Vector3.Zero);
        S(new BoxMesh { Size = new Vector3(1.76f, 0.86f, 0.02f) }, MeshFactory.Emissive(new Color(0.02f, 0.08f, 0.12f), new Color(0.03f, 0.18f, 0.25f), 1f), new Vector3(0, 0, 0.065f));
        S(new BoxMesh { Size = new Vector3(1.95f, 0.05f, 0.14f) }, trim, new Vector3(0, 0.52f, 0));
        var holo = MeshFactory.Emissive(cyan, cyan, 2f);
        S(MeshFactory.CrystalPrism(6, 1, 1, 0.4f), holo, new Vector3(-0.45f, -0.22f, 0.09f), null, new Vector3(0.12f, 0.45f, 0.04f));
        S(new BoxMesh { Size = new Vector3(0.3f, 0.05f, 0.01f) }, holo, new Vector3(0.02f, 0, 0.09f));
        S(MeshFactory.Spike(3, 0.08f, 0.12f), holo, new Vector3(0.2f, 0, 0.09f), new Vector3(0, 0, -Mathf.Pi / 2));
        var coin = MeshFactory.Emissive(new Color(1f, 0.8f, 0.3f), new Color(1f, 0.75f, 0.25f), 2f);
        for (int i = 0; i < 3; i++)
            S(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.05f, RadialSegments = 12, Rings = 1 }, coin, new Vector3(0.5f, -0.1f + i * 0.08f, 0.1f), new Vector3(0.25f, 0, 0));
        // cristais em pedestais
        var colors = new[] { cyan, new Color(0.7f, 0.4f, 1f), new Color(1f, 0.8f, 0.3f) };
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-0.9f + i * 0.34f, 1.03f, 0.3f);
            AddMesh(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.14f, Height = 0.1f, RadialSegments = 8, Rings = 1 }, dark, p + new Vector3(0, 0.05f, 0));
            AddMesh(new CylinderMesh { TopRadius = 0.125f, BottomRadius = 0.125f, Height = 0.015f, RadialSegments = 8, Rings = 1 }, trim, p + new Vector3(0, 0.1f, 0));
            AddMesh(MeshFactory.CrystalPrism(6, 1, 1, 0.35f), MeshFactory.Emissive(colors[i], colors[i], 2f), p + new Vector3(0, 0.1f, 0), new Vector3(0.08f, 0.3f - i * 0.04f, 0.08f));
        }
        // tablet com carrinho e caixotes ao lado
        var tab = AddMesh(new BoxMesh { Size = new Vector3(0.5f, 0.02f, 0.34f) }, dark, new Vector3(0.6f, 1.05f, 0.25f));
        tab.Rotation = new Vector3(0, -0.25f, 0);
        AddMesh(new BoxMesh { Size = new Vector3(0.44f, 0.005f, 0.28f) }, MeshFactory.Emissive(new Color(0.05f, 0.2f, 0.25f), cyan, 1.4f), new Vector3(0.6f, 1.063f, 0.25f)).Rotation = new Vector3(0, -0.25f, 0);
        Decor.Crate(this, new Vector3(-1.75f, 0, 0.2f), 0.2f, 0.85f);
        Decor.Crate(this, new Vector3(-1.75f, 0.68f, 0.25f), -0.15f, 0.65f);
        Ring = null;
        var light = new OmniLight3D { LightColor = accent, LightEnergy = 1.3f, OmniRange = 6f, Position = new Vector3(0, 2.2f, 1.2f) };
        AddChild(light);
        Label = MakeLabel("VENDA", accent, 2.75f);
        StaticBatcher.Batch(this);
    }

    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        return gs.InventoryCount > 0
            ? Loc.F("{0} Vender {1} fragmentos  ({2})", InputHints.Interact, gs.InventoryCount, GameState.FormatMoney(gs.InventoryValue()))
            : Loc.F("{0} Estação de Venda", InputHints.Interact);
    }

    public override void Interact(PlayerController player) => UiBus.RequestSell();
}

/// <summary>Elevador da mina: leva o jogador para casa e encerra o dia.</summary>
public partial class MineElevator : StationBase
{
    /// <summary>true = elevador dentro da casa (desce para a mina).</summary>
    public bool GoesDown;
    MeshInstance3D _lamp;
    StandardMaterial3D _lampMat;

    public override void _Ready()
    {
        SetupBody(new Vector3(3.4f, 4.2f, 1.8f));
        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.35f, 0.4f), Roughness = 0.55f, Metallic = 0.35f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.15f), Roughness = 0.8f };
        var yellow = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.72f, 0.12f), Roughness = 0.7f };
        var orange = new Color(1f, 0.55f, 0.18f);
        var trim = MeshFactory.Emissive(orange, orange, 1.4f);
        // poço (fundo escuro) e piso da cabine
        AddMesh(new BoxMesh { Size = new Vector3(3.0f, 4.0f, 0.2f) }, dark, new Vector3(0, 2.0f, -0.8f));
        AddMesh(new BoxMesh { Size = new Vector3(2.8f, 0.12f, 1.5f) }, steel, new Vector3(0, 0.06f, 0));
        // pilares e viga superior com faixas de alerta
        foreach (float x in new[] { -1.55f, 1.55f })
        {
            AddMesh(new BoxMesh { Size = new Vector3(0.3f, 4.2f, 0.4f) }, steel, new Vector3(x, 2.1f, 0.6f));
            for (int i = 0; i < 5; i++)
                AddMesh(new BoxMesh { Size = new Vector3(0.31f, 0.18f, 0.41f) }, i % 2 == 0 ? yellow : dark, new Vector3(x, 0.3f + i * 0.36f, 0.6f));
        }
        AddMesh(new BoxMesh { Size = new Vector3(3.5f, 0.45f, 0.5f) }, steel, new Vector3(0, 4.2f, 0.6f));
        for (int i = 0; i < 9; i++)
            AddMesh(new BoxMesh { Size = new Vector3(0.36f, 0.3f, 0.02f) }, i % 2 == 0 ? yellow : dark, new Vector3(-1.45f + i * 0.36f, 4.2f, 0.86f)).Rotation = new Vector3(0, 0, 0.5f);
        // grade da porta (meio aberta)
        for (int i = 0; i < 6; i++)
            AddMesh(new BoxMesh { Size = new Vector3(0.05f, 3.6f, 0.05f) }, steel, new Vector3(-1.35f + i * 0.14f, 1.9f, 0.75f));
        AddMesh(new BoxMesh { Size = new Vector3(0.8f, 0.06f, 0.06f) }, steel, new Vector3(-1.0f, 3.6f, 0.75f));
        AddMesh(new BoxMesh { Size = new Vector3(0.8f, 0.06f, 0.06f) }, steel, new Vector3(-1.0f, 0.4f, 0.75f));
        // cabos
        foreach (float x in new[] { -0.4f, 0.4f })
            AddMesh(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 3.8f, RadialSegments = 6, Rings = 1 }, dark, new Vector3(x, 2.1f, -0.5f));
        // painel com botão e lâmpada de status
        AddMesh(new BoxMesh { Size = new Vector3(0.36f, 0.6f, 0.12f) }, dark, new Vector3(1.95f, 1.3f, 0.7f));
        AddMesh(new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.13f) }, trim, new Vector3(1.95f, 1.62f, 0.7f));
        _lampMat = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 1f, 0.5f), EmissionEnabled = true, Emission = new Color(0.3f, 1f, 0.5f), EmissionEnergyMultiplier = 2f };
        _lamp = AddMesh(new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 10, Rings = 5 }, _lampMat, new Vector3(1.95f, 1.35f, 0.78f));
        AddMesh(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.05f, RadialSegments = 10, Rings = 1 }, MeshFactory.Emissive(orange, orange, 2f), new Vector3(1.95f, 1.12f, 0.78f)).Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.5f), LightEnergy = 1.1f, OmniRange = 6f, Position = new Vector3(0, 3.6f, 1.2f) };
        AddChild(light);
        Label = MakeLabel(GoesDown ? Loc.T("ELEVADOR · MINA") : Loc.T("ELEVADOR · CASA"), new Color(1f, 0.82f, 0.4f), GoesDown ? 3.75f : 5.25f);
        StaticBatcher.Batch(this);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        var gs = GameState.Instance;
        if (gs == null || _lampMat == null) return;
        // verde durante o turno, âmbar piscando quando é hora de voltar
        bool alert = GoesDown ? !gs.DayOver : gs.DayOver; // pisca quando é hora de usar
        var c = alert ? new Color(1f, 0.6f, 0.15f) * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time * 4f))) : new Color(0.3f, 1f, 0.5f);
        _lampMat.Emission = c;
        _lampMat.AlbedoColor = c;
    }

    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        if (GoesDown)
            return gs.DayOver ? Loc.T("Durma antes de descer para a mina.") : Loc.F("{0} Descer para a mina (Dia {1})", InputHints.Interact, gs.Data.Day);
        return gs.DayOver
            ? Loc.F("{0} Subir para casa (fim do turno)", InputHints.Interact)
            : Loc.F("{0} Elevador: encerrar o dia e ir para casa", InputHints.Interact);
    }

    public override void Interact(PlayerController player)
    {
        if (GoesDown) UiBus.RequestMine();
        else UiBus.RequestHome();
    }
}

/// <summary>Terminal de upgrades e desbloqueios.</summary>
public partial class ShopTerminal : StationBase
{


    public override void _Ready()
    {
        SetupBody(new Vector3(2.4f, 2.2f, 1.1f));
        var cyan = new Color(0.35f, 0.9f, 1f);
        var orange = new Color(1f, 0.55f, 0.18f);
        var body = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.37f, 0.42f), Roughness = 0.6f, Metallic = 0.2f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.15f, 0.19f), Roughness = 0.7f };
        var trim = MeshFactory.Emissive(orange, orange, 1.6f);
        var glow = MeshFactory.Emissive(new Color(0.05f, 0.2f, 0.25f), cyan, 1.4f);
        // mesa/console
        AddMesh(new BoxMesh { Size = new Vector3(2.4f, 0.95f, 1.0f) }, body, new Vector3(0, 0.475f, 0.05f));
        AddMesh(new BoxMesh { Size = new Vector3(2.5f, 0.08f, 1.1f) }, dark, new Vector3(0, 0.99f, 0.05f));
        for (int i = -1; i <= 1; i += 2)
            AddMesh(new BoxMesh { Size = new Vector3(0.06f, 0.5f, 0.04f) }, trim, new Vector3(i * 1.0f, 0.5f, 0.56f));
        AddMesh(new BoxMesh { Size = new Vector3(1.6f, 0.05f, 0.04f) }, trim, new Vector3(0, 0.82f, 0.56f));
        // tela inclinada com moldura
        var screenRoot = new Node3D { Position = new Vector3(0, 1.65f, -0.25f), Rotation = new Vector3(-0.25f, 0, 0) };
        AddChild(screenRoot);
        void S(Mesh m, Material mat, Vector3 pos)
        {
            screenRoot.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        S(new BoxMesh { Size = new Vector3(2.0f, 1.15f, 0.12f) }, dark, Vector3.Zero);
        S(new BoxMesh { Size = new Vector3(1.86f, 1.0f, 0.02f) }, MeshFactory.Emissive(new Color(0.02f, 0.08f, 0.12f), new Color(0.03f, 0.18f, 0.25f), 1f), new Vector3(0, 0, 0.065f));
        // "interface" desenhada com barras luminosas
        S(new BoxMesh { Size = new Vector3(0.42f, 0.16f, 0.01f) }, glow, new Vector3(-0.62f, 0.3f, 0.08f));
        S(new BoxMesh { Size = new Vector3(0.42f, 0.16f, 0.01f) }, MeshFactory.Emissive(new Color(0.05f, 0.1f, 0.15f), new Color(0.2f, 0.45f, 0.55f), 1f), new Vector3(-0.62f, 0.08f, 0.08f));
        for (int i = 0; i < 4; i++)
        {
            float y = 0.32f - i * 0.2f;
            S(new BoxMesh { Size = new Vector3(0.7f, 0.035f, 0.01f) }, glow, new Vector3(0.12f, y, 0.08f));
            S(new BoxMesh { Size = new Vector3(0.28f, 0.11f, 0.01f) }, MeshFactory.Emissive(new Color(0.2f, 0.15f, 0.05f), new Color(1f, 0.75f, 0.3f), 1.5f), new Vector3(0.7f, y, 0.08f));
        }
        S(new BoxMesh { Size = new Vector3(2.05f, 0.05f, 0.14f) }, trim, new Vector3(0, 0.6f, 0));
        // cristais em exibição sobre pedestais
        var colors = new[] { new Color(0.35f, 0.9f, 1f), new Color(0.7f, 0.4f, 1f), new Color(1f, 0.8f, 0.3f) };
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-0.8f + i * 0.32f, 1.03f, 0.35f);
            AddMesh(new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.12f, Height = 0.08f, RadialSegments = 8, Rings = 1 }, dark, p + new Vector3(0, 0.04f, 0));
            AddMesh(new CylinderMesh { TopRadius = 0.105f, BottomRadius = 0.105f, Height = 0.015f, RadialSegments = 8, Rings = 1 }, trim, p + new Vector3(0, 0.085f, 0));
            AddMesh(MeshFactory.CrystalPrism(6, 1, 1, 0.35f), MeshFactory.Emissive(colors[i], colors[i], 1.8f), p + new Vector3(0, 0.09f, 0), new Vector3(0.07f, 0.24f - i * 0.03f, 0.07f));
        }
        // tablet sobre a mesa
        var tab = AddMesh(new BoxMesh { Size = new Vector3(0.5f, 0.02f, 0.34f) }, dark, new Vector3(0.55f, 1.05f, 0.3f));
        tab.Rotation = new Vector3(0, 0.3f, 0);
        var tabScreen = AddMesh(new BoxMesh { Size = new Vector3(0.44f, 0.005f, 0.28f) }, glow, new Vector3(0.55f, 1.063f, 0.3f));
        tabScreen.Rotation = new Vector3(0, 0.3f, 0);
        var light = new OmniLight3D { LightColor = new Color(0.5f, 0.85f, 1f), LightEnergy = 1.3f, OmniRange = 6f, Position = new Vector3(0, 2f, 1.2f) };
        AddChild(light);
        Label = MakeLabel("MELHORIAS", new Color(1f, 0.75f, 0.3f), 2.75f);
        StaticBatcher.Batch(this);
    }

    public override string GetPrompt() => Loc.F("{0} Terminal de Melhorias", InputHints.Interact);
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
        Label.DoubleSided = false;
        Label.Position = new Vector3(0, _height - 0.9f, 0.3f);
        Label.RotationDegrees = new Vector3(0, 0, 0);
        // rótulo também no lado de trás
        var back = MakeLabel("", Colors.White, _height - 0.9f, 56);
        back.Billboard = BaseMaterial3D.BillboardModeEnum.Disabled;
        back.DoubleSided = false;
        back.Position = new Vector3(0, _height - 0.9f, -0.3f);
        back.RotationDegrees = new Vector3(0, 180, 0);
        _backLabel = back;
        BuildBeacon();
        GameState.Instance.RegionsChanged += Refresh;
        GameState.Instance.MoneyChanged += RefreshText;
        Refresh();
    }

    Label3D _backLabel;
    Node3D _beacon, _holo;
    MeshInstance3D _beam;
    StandardMaterial3D _padMat, _holoMat, _beamMat;

    /// <summary>Farol de passagem: plataforma com luzes, cristal holográfico e feixe ao desbloquear.</summary>
    void BuildBeacon()
    {
        _beacon = new Node3D { Name = "Beacon", Position = new Vector3(0, 0, 2.2f) };
        AddChild(_beacon);
        void M(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null)
            => parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.21f, 0.26f), Roughness = 0.6f };
        var orange = MeshFactory.Emissive(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.55f, 0.18f), 1.6f);
        _padMat = new StandardMaterial3D { AlbedoColor = Region.AccentColor, EmissionEnabled = true, Emission = Region.AccentColor, EmissionEnergyMultiplier = 0.6f };
        M(_beacon, new CylinderMesh { TopRadius = 2.1f, BottomRadius = 2.3f, Height = 0.12f, RadialSegments = 20, Rings = 1 }, dark, new Vector3(0, 0.06f, 0));
        M(_beacon, new TorusMesh { InnerRadius = 1.5f, OuterRadius = 1.62f, Rings = 28, RingSegments = 4 }, _padMat, new Vector3(0, 0.13f, 0));
        M(_beacon, new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 0.02f, RadialSegments = 20, Rings = 1 }, _padMat, new Vector3(0, 0.13f, 0));
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Tau * i / 10;
            M(_beacon, new BoxMesh { Size = new Vector3(0.36f, 0.04f, 0.14f) }, orange, new Vector3(Mathf.Cos(a) * 1.95f, 0.13f, Mathf.Sin(a) * 1.95f), new Vector3(0, -a, 0));
        }
        // faixas laranja nos pilares e antena
        foreach (float sx in new[] { -_width / 2, _width / 2 })
            for (int k = 0; k < 3; k++)
                M(this, new BoxMesh { Size = new Vector3(0.52f, 0.08f, 0.62f) }, orange, new Vector3(sx, 0.8f + k * 1.4f, 0));
        M(this, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 0.6f, RadialSegments = 6, Rings = 1 }, dark, new Vector3(_width / 2, _height + 0.3f, 0));
        M(this, new SphereMesh { Radius = 0.4f, Height = 0.32f, IsHemisphere = true, RadialSegments = 12, Rings = 3 }, dark, new Vector3(_width / 2, _height + 0.65f, 0), new Vector3(-2.2f, 0.4f, 0));
        // cristal holográfico flutuante
        _holo = new Node3D { Position = new Vector3(0, 3.4f, 0) };
        _beacon.AddChild(_holo);
        _holoMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(Region.AccentColor, 0.5f),
        };
        _holo.AddChild(new MeshInstance3D { Mesh = MeshFactory.CrystalPrism(6, 1, 1, 0.45f), MaterialOverride = _holoMat, Scale = new Vector3(0.35f, 1.0f, 0.35f), Position = new Vector3(0, -0.5f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var flip = new MeshInstance3D { Mesh = MeshFactory.CrystalPrism(6, 1, 1, 0.6f), MaterialOverride = _holoMat, Scale = new Vector3(0.35f, 0.5f, 0.35f), Position = new Vector3(0, -0.5f, 0), Rotation = new Vector3(Mathf.Pi, 0, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _holo.AddChild(flip);
        for (int i = 0; i < 2; i++)
            _holo.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.7f, OuterRadius = 0.76f, Rings = 24, RingSegments = 4 }, MaterialOverride = _holoMat, Position = new Vector3(0, -0.2f - i * 0.8f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _beamMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(Region.AccentColor, 0.16f),
        };
        _beam = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.85f, Height = _height, RadialSegments = 16, Rings = 1, CapTop = false, CapBottom = false }, MaterialOverride = _beamMat, Position = new Vector3(0, _height / 2, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _beacon.AddChild(_beam);
    }

    void UpdateBeacon()
    {
        if (_beacon == null) return;
        bool open = _open;
        _beam.Visible = open;
        _padMat.EmissionEnergyMultiplier = open ? 2.2f : 0.5f;
        _holoMat.AlbedoColor = new Color(Region.AccentColor, open ? 0.65f : 0.22f);
    }

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
        else if (!unlocked && _open)
        {
            // novo jogo / save carregado com a região bloqueada: restaura a barreira
            _open = false;
            _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
            foreach (Node c in GetChildren())
                if (c is MeshInstance3D m) m.Visible = true;
            Label.Visible = true;
            _backLabel.Visible = true;
            _barrierMat.AlbedoColor = new Color(Region.AccentColor, 0.35f);
        }
        RefreshText();
        UpdateBeacon();
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
        if (_holo != null)
        {
            _holo.RotateY((float)delta * (_open ? 1.2f : 0.3f));
            _holo.Position = new Vector3(0, 3.4f + Mathf.Sin(Time * 1.5f) * 0.12f, 0);
        }
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
        if (gs.CanUnlock(Region)) return Loc.F("{0} Desbloquear {1}  ({2})", InputHints.Interact, Region.Name, GameState.FormatMoney(Region.UnlockCost));
        long missing = Region.UnlockCost - gs.Data.Money;
        return Loc.F("Bloqueado: {0} — faltam {1}", Region.Name, GameState.FormatMoney(missing));
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
            gs.Notify("Moeda insuficiente", Loc.F("{0} custa {1}.", Region.Name, GameState.FormatMoney(Region.UnlockCost)), NotifyKind.Warning);
        }
    }

    public bool IsOpen => _open;
}
