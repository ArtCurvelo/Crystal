using System.Linq;
using CrystalExcavator.Audio;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using CrystalExcavator.UI;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Cena raiz: monta mundo, jogador e interfaces e controla o fluxo
/// Menu → Jogando ⇄ Pausa/Loja/Venda.
/// </summary>
public partial class Main : Node
{
    public enum Mode { Menu, Playing }
    public Mode CurrentMode { get; private set; } = Mode.Menu;

    public WorldBuilder World { get; private set; }
    public PlayerController Player { get; private set; }
    public Hud Hud { get; private set; }
    EnvironmentController _env;
    ShardManager _shards;
    Camera3D _menuCam;
    MainMenu _menu;
    ShopScreen _shop;
    SellScreen _sell;
    PauseScreen _pause;
    SettingsScreen _settings;
    ConfirmScreen _confirm;
    RegionDef _currentRegion;
    float _menuT;
    float _cullTimer;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--run-tests"))
        {
            AddChild(new Tests.TestRunner());
            return;
        }

        var world3d = new Node3D { Name = "World3D" };
        world3d.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(world3d);
        _env = new EnvironmentController { Name = "Environment" };
        world3d.AddChild(_env);
        world3d.AddChild(new Effects { Name = "Effects" });
        _shards = new ShardManager { Name = "Shards" };
        world3d.AddChild(_shards);
        World = new WorldBuilder { Name = "World" };
        world3d.AddChild(World);
        _menuCam = new Camera3D { Current = true, Fov = 70, Far = 120 };
        world3d.AddChild(_menuCam);

        Hud = new Hud { Visible = false };
        AddChild(Hud);
        _menu = new MainMenu
        {
            OnContinue = () => StartGame(true),
            OnNewGame = AskNewGame,
            OnSettings = () => _settings.Open(),
            OnQuit = QuitGame,
        };
        AddChild(_menu);
        _shop = new ShopScreen();
        _shop.Closed += ReturnToGameplay;
        AddChild(_shop);
        _sell = new SellScreen();
        _sell.Closed += ReturnToGameplay;
        AddChild(_sell);
        _pause = new PauseScreen
        {
            OnResume = Resume,
            OnSave = () => GameState.Instance.SaveGame(),
            OnSettings = () => _settings.Open(),
            OnMainMenu = BackToMenu,
            OnQuit = QuitGame,
        };
        AddChild(_pause);
        _settings = new SettingsScreen();
        AddChild(_settings);
        _confirm = new ConfirmScreen();
        AddChild(_confirm);

        UiBus.OpenShop += OpenShop;
        UiBus.OpenSell += OpenSell;
        _shards.InventoryFullBlocked += () => { if (CurrentMode == Mode.Playing) Hud.FlashFull(); };
        GameState.Instance.BeforeSave = WritePlayerToSave;
        GameState.Instance.RegionsChanged += UpdateRegionCulling;

        ShowMenu();

        if (args.Contains("--autostart")) StartGame(false);
        var shots = args.FirstOrDefault(a => a.StartsWith("--shots="));
        if (shots != null) AddChild(new Tests.ScreenshotTour { Main = this, Dir = shots.Substring("--shots=".Length) });
    }

    public override void _ExitTree()
    {
        UiBus.OpenShop -= OpenShop;
        UiBus.OpenSell -= OpenSell;
        if (GameState.Instance != null)
        {
            GameState.Instance.RegionsChanged -= UpdateRegionCulling;
            if (GameState.Instance.BeforeSave == WritePlayerToSave) GameState.Instance.BeforeSave = null;
        }
    }

    // ======================= Fluxo =======================
    void ShowMenu()
    {
        CurrentMode = Mode.Menu;
        GameState.Instance.IsPlaying = false;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Hud.Visible = false;
        _menuCam.Current = true;
        _env.SetRegion(GameData.Regions[0], true);
        foreach (var r in World.RegionRoots) r.Visible = true;
        World.RegionRoots[2].Visible = false;
        _menu.Show(SaveSystem.HasSave());
        AudioManager.Instance?.PlayMusic(-1);
    }

    void AskNewGame()
    {
        if (SaveSystem.HasSave())
            _confirm.Ask("Iniciar um novo jogo apagará o progresso salvo atual. Deseja continuar?", () => StartGame(false));
        else StartGame(false);
    }

    public void StartGame(bool continueSave)
    {
        var gs = GameState.Instance;
        if (continueSave) gs.LoadGame();
        else { SaveSystem.Delete(); gs.NewGame(); }

        _shards.ClearAll();
        if (Player == null)
        {
            Player = new PlayerController { Name = "Player" };
            World.GetParent().AddChild(Player);
            Player.CrystalHit += OnCrystalHit;
            _shards.PlayerPosition = () => Player.BodyCenter;
            Hud.Player = Player;
        }
        // posição: usa a salva se for válida e estiver numa região liberada
        var d = gs.Data;
        var pos = World.SpawnPoint;
        float yaw = 0;
        if (d.HasPosition)
        {
            var saved = new Vector3(d.PlayerX, Mathf.Max(0.1f, d.PlayerY), d.PlayerZ);
            var region = WorldBuilder.RegionAt(saved);
            if (gs.IsUnlocked(region.Id) && Mathf.Abs(saved.X) < 40) { pos = saved; yaw = d.PlayerYaw; }
        }
        Player.GlobalPosition = pos;
        Player.Velocity = Vector3.Zero;
        Player.SetLook(yaw);
        Player.Camera.Current = true;
        Player.InputEnabled = true;

        _menu.Hide();
        Hud.Visible = true;
        Hud.ResetMoneyDisplay();
        CurrentMode = Mode.Playing;
        gs.IsPlaying = true;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _currentRegion = null;
        UpdateRegion(true);
        UpdateRegionCulling();
        if (!continueSave) gs.Notify("Bem-vindo, escavador!", "Mire em um cristal e segure o botão esquerdo do mouse.", NotifyKind.Info);
        gs.SaveGame(true);
    }

    void BackToMenu()
    {
        GameState.Instance.SaveGame(true);
        _pause.Close();
        if (Player != null) Player.InputEnabled = false;
        ShowMenu();
    }

    void QuitGame()
    {
        if (CurrentMode == Mode.Playing) GameState.Instance.SaveGame(true);
        GetTree().Quit();
    }

    void Pause()
    {
        if (CurrentMode != Mode.Playing || _pause.IsOpen) return;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _pause.Open();
    }

    void Resume()
    {
        _pause.Close();
        _settings.Close();
        GetTree().Paused = false;
        if (CurrentMode == Mode.Playing) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    void OpenShop()
    {
        if (CurrentMode != Mode.Playing || AnyModal()) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        _shop.Open();
    }

    void OpenSell()
    {
        if (CurrentMode != Mode.Playing || AnyModal()) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        _sell.Open();
    }

    void ReturnToGameplay()
    {
        if (CurrentMode != Mode.Playing) return;
        if (Player != null) Player.InputEnabled = true;
        if (!_pause.IsOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    bool AnyModal() => _shop.IsOpen || _sell.IsOpen || _pause.IsOpen || _settings.IsOpen || _confirm.IsOpen;

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            GetViewport().SetInputAsHandled();
            if (_confirm.IsOpen) { _confirm.Close(); return; }
            if (_settings.IsOpen) { _settings.Close(); return; }
            if (CurrentMode != Mode.Playing) return;
            if (_shop.IsOpen) { _shop.Close(); return; }
            if (_sell.IsOpen) { _sell.Close(); return; }
            if (_pause.IsOpen) Resume(); else Pause();
            return;
        }
        if (e.IsActionPressed("toggle_fps"))
        {
            Settings.Data.ShowFps = !Settings.Data.ShowFps;
            Settings.Save();
        }
        if (CurrentMode != Mode.Playing) return;
        if (e.IsActionPressed("quick_save")) GameState.Instance.SaveGame();
        if (e.IsActionPressed("interact") && (_shop.IsOpen || _sell.IsOpen))
        {
            _shop.Close(); _sell.Close();
            GetViewport().SetInputAsHandled();
        }
        // clique recaptura o mouse caso tenha sido liberado (ex.: alt-tab)
        if (e is InputEventMouseButton mb && mb.Pressed && !AnyModal() && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && CurrentMode == Mode.Playing && !AnyModal() && DisplayServer.GetName() != "headless")
            Pause();
        if (what == NotificationWMCloseRequest && CurrentMode == Mode.Playing)
            GameState.Instance?.SaveGame(true);
    }

    // ======================= Loop =======================
    public override void _Process(double delta)
    {
        if (World == null) return;
        if (CurrentMode == Mode.Menu)
        {
            _menuT += (float)delta * 0.08f;
            var c = new Vector3(Mathf.Sin(_menuT) * 11f, 3.2f + Mathf.Sin(_menuT * 2.3f) * 0.6f, -2f + Mathf.Cos(_menuT) * 11f);
            _menuCam.GlobalPosition = c;
            _menuCam.LookAt(new Vector3(0, 1.4f, -2f), Vector3.Up);
            return;
        }
        UpdateRegion(false);
        _cullTimer -= (float)delta;
        if (_cullTimer <= 0) { _cullTimer = 0.5f; UpdateRegionCulling(); }
    }

    void UpdateRegion(bool instant)
    {
        if (Player == null) return;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        if (r == _currentRegion) return;
        _currentRegion = r;
        GameState.Instance.CurrentRegionId = r.Id;
        _env.SetRegion(r, instant);
        Hud.ShowRegionBanner(r);
        AudioManager.Instance?.PlayMusic(r.Index);
        UiBus.EnterRegion(r.Id);
    }

    /// <summary>Esconde regiões distantes (não adjacentes) para economizar desenho na GPU.</summary>
    void UpdateRegionCulling()
    {
        if (Player == null || CurrentMode != Mode.Playing) return;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        for (int i = 0; i < World.RegionRoots.Count; i++)
        {
            bool visible = Mathf.Abs(i - r.Index) <= 1;
            if (World.RegionRoots[i].Visible != visible) World.RegionRoots[i].Visible = visible;
        }
    }

    void OnCrystalHit(Crystal c, bool broke)
    {
        if (broke && GameState.Instance.InventoryFull) Hud.FlashFull();
    }

    bool WritePlayerToSave(SaveData d)
    {
        if (Player == null || CurrentMode != Mode.Playing) return false;
        var p = Player.GlobalPosition;
        d.PlayerX = p.X; d.PlayerY = p.Y; d.PlayerZ = p.Z;
        d.PlayerYaw = Player.Yaw;
        d.HasPosition = true;
        return true;
    }
}
