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
    public RegionEvents Events { get; private set; }
    EnvironmentController _env;
    ShardManager _shards;
    Camera3D _menuCam;
    MainMenu _menu;
    ShopScreen _shop;
    SellScreen _sell;
    PauseScreen _pause;
    SettingsScreen _settings;
    ConfirmScreen _confirm;
    CodexScreen _codex;
    ObjectiveMarker _marker;
    HelpScreen _help;
    CompletionScreen _completion;
    BackupScreen _backups;
    UpdateManager _updater;
    UpdateScreen _updateScreen;
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
        Events = new RegionEvents { Name = "RegionEvents", Env = _env, World = World, CurrentRegion = () => _currentRegion };
        world3d.AddChild(Events);
        _menuCam = new Camera3D { Current = true, Fov = 70, Far = 120 };
        world3d.AddChild(_menuCam);

        Hud = new Hud { Visible = false, Events = Events };
        AddChild(Hud);
        _menu = new MainMenu
        {
            OnContinue = () => StartGame(true),
            OnNewGame = AskNewGame,
            OnHelp = () => _help.Open(),
            OnSettings = () => _settings.Open(),
            OnBackups = () => _backups.Open(),
            OnQuit = QuitGame,
        };
        AddChild(_menu);
        _shop = new ShopScreen();
        _shop.Closed += ReturnToGameplay;
        AddChild(_shop);
        _sell = new SellScreen();
        _sell.Closed += ReturnToGameplay;
        AddChild(_sell);
        _shop.SwitchToSell = () => { _shop.CloseSilently(); _sell.Open(); };
        _sell.SwitchToShop = () => { _sell.CloseSilently(); _shop.Open(); };
        _pause = new PauseScreen
        {
            OnResume = Resume,
            OnSave = () => GameState.Instance.SaveGame(),
            OnSettings = () => _settings.Open(),
            OnCodex = () => _codex.Open(),
            OnHelp = () => _help.Open(),
            OnMainMenu = BackToMenu,
            OnQuit = QuitGame,
        };
        AddChild(_pause);
        _settings = new SettingsScreen();
        AddChild(_settings);
        _confirm = new ConfirmScreen();
        AddChild(_confirm);
        _codex = new CodexScreen();
        AddChild(_codex);
        _help = new HelpScreen();
        AddChild(_help);
        _updater = new UpdateManager { Name = "Updater" };
        AddChild(_updater);
        _updateScreen = new UpdateScreen { Updater = _updater };
        AddChild(_updateScreen);
        _updater.UpdateFound += m => { if (CurrentMode == Mode.Menu) _updateScreen.Show(m); };
        _updater.Progress += (p, t) => _updateScreen.OnProgress(p, t);
        _updater.Failed += msg => _updateScreen.OnFailed(msg);
        _updater.Status += t => _menu.SetUpdateStatus(t);
        _backups = new BackupScreen();
        _backups.OnRestore = snap => _confirm.Ask(
            $"Restaurar o backup de {(snap.When == default ? "data desconhecida" : snap.When.ToString("dd/MM/yyyy HH:mm"))} ({GameState.FormatMoney(snap.Data.Money)})? O progresso atual também é guardado como backup antes.",
            () =>
            {
                if (SaveSystem.Restore(snap.Path))
                {
                    _backups.Close();
                    StartGame(true);
                    GameState.Instance.Notify("Progresso restaurado", "Backup carregado com sucesso.", NotifyKind.Success);
                }
            });
        AddChild(_backups);
        _completion = new CompletionScreen();
        _completion.Closed += ReturnToGameplay;
        AddChild(_completion);
        GameState.Instance.Completed += OnCompleted;

        UiBus.OpenShop += OpenShop;
        UiBus.PrestigeRequested += AskPrestige;
        UiBus.OpenSell += OpenSell;
        _shards.InventoryFullBlocked += () => { if (CurrentMode == Mode.Playing) Hud.FlashFull(); };
        GameState.Instance.BeforeSave = WritePlayerToSave;
        GameState.Instance.RegionsChanged += UpdateRegionCulling;
        GameState.Instance.Sold += OnSold;
        GameState.Instance.RegionsChanged += () => GameState.Instance.CheckCompletion();
        GameState.Instance.MaterialDiscovered += _ => GameState.Instance.CheckCompletion();

        Boot(args);
    }

    /// <summary>
    /// Inicialização: tela "Carregando…" enquanto todas as regiões são desenhadas por alguns quadros
    /// (pré-compila shaders na GPU e evita travadas ao entrar em regiões novas), depois o menu.
    /// </summary>
    bool _booting = true;

    /// <summary>Na qualidade Baixa desliga todas as luzes dinâmicas (maior custo no renderizador Compatibility).</summary>
    void ApplyLightQuality()
    {
        bool on = Settings.Quality != GraphicsQuality.Low;
        foreach (var l in FindAll<OmniLight3D>(World.GetParent()))
            if (l.GetParent() is not Mining.Effects) l.Visible = on; // clarões do Effects controlam a si mesmos
        // decoração por MultiMesh: na Baixa desenha só uma fração (as posições já são aleatórias)
        foreach (var mm in FindAll<MultiMeshInstance3D>(World))
        {
            if (mm.Multimesh == null || !mm.HasMeta(WorldBuilder.LowFractionMeta)) continue;
            float frac = (float)mm.GetMeta(WorldBuilder.LowFractionMeta);
            int visible = on ? -1 : Mathf.Max(1, (int)(mm.Multimesh.InstanceCount * frac));
            if (mm.Multimesh.VisibleInstanceCount != visible) mm.Multimesh.VisibleInstanceCount = visible;
        }
        int amount = WorldBuilder.AmbientAmount();
        foreach (var n in GetTree().GetNodesInGroup(WorldBuilder.AmbientGroup))
            if (n is CpuParticles3D p && p.Amount != amount) p.Amount = amount;
        _dynScale = -1f; // nova escala/qualidade vale imediatamente
    }

    static System.Collections.Generic.IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
    }

    // ---------- resolução dinâmica ----------
    float _dynTimer;
    float _dynScale = -1f;

    void UpdateDynamicResolution(float dt)
    {
        var vp = GetViewport();
        float max = Settings.Data.RenderScale;
        if (!Settings.Data.DynamicResolution || CurrentMode != Mode.Playing)
        {
            if (!Mathf.IsEqualApprox(vp.Scaling3DScale, max)) vp.Scaling3DScale = max;
            _dynScale = -1f;
            return;
        }
        if (_dynScale < 0) _dynScale = max;
        _dynTimer += dt;
        if (_dynTimer < 2f) return;
        _dynTimer = 0;
        double fps = Engine.GetFramesPerSecond();
        int cap = Settings.Data.MaxFps > 0 ? Settings.Data.MaxFps : 1000;
        double target = Mathf.Min(30, cap - 2);
        if (fps < target - 2 && _dynScale > 0.5f) _dynScale = Mathf.Max(0.5f, _dynScale - 0.05f);
        else if (fps > target + 14 && _dynScale < max) _dynScale = Mathf.Min(max, _dynScale + 0.05f);
        if (!Mathf.IsEqualApprox(vp.Scaling3DScale, _dynScale)) vp.Scaling3DScale = _dynScale;
    }

    public float CurrentRenderScale => GetViewport().Scaling3DScale;

    async void Boot(string[] args)
    {
        var loading = BuildLoadingOverlay();
        foreach (var r in World.RegionRoots) r.Visible = true;
        _menuCam.Current = true;
        _menuCam.Far = 420f;
        _menuCam.GlobalPosition = new Vector3(0, 9f, 34f);
        _menuCam.LookAt(new Vector3(0, 2f, -150f), Vector3.Up);
        Effects.Instance?.Burst(new Vector3(0, 2, 10), Colors.White, 1f); // compila partículas
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _menuCam.Far = 120f;
        ApplyLightQuality();
        Settings.Changed += ApplyLightQuality;
        loading.QueueFree();
        _booting = false;
        ShowMenu();
        _updater.CheckForUpdates();


        if (args.Contains("--autostart")) StartGame(false);
        var shots = args.FirstOrDefault(a => a.StartsWith("--shots="));
        if (shots != null) AddChild(new Tests.ScreenshotTour { Main = this, Dir = shots.Substring("--shots=".Length) });
        if (args.Contains("--bench"))
        {
            var q = args.FirstOrDefault(a => a.StartsWith("--bench-quality="));
            AddChild(new Tests.Benchmark { Main = this, Quality = q != null ? int.Parse(q.Split('=')[1]) : 0 });
        }
        var bot = args.FirstOrDefault(a => a.StartsWith("--bot"));
        if (bot != null)
        {
            var b = new Tests.PlaythroughBot { Main = this };
            if (bot.Contains('=') && float.TryParse(bot.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture, out float mins)) b.MaxGameMinutes = mins;
            AddChild(b);
        }
    }

    CanvasLayer BuildLoadingOverlay()
    {
        var layer = new CanvasLayer { Layer = 50 };
        AddChild(layer);
        var bg = new ColorRect { Color = new Color(0.02f, 0.03f, 0.06f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(center);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        center.AddChild(v);
        var icon = HudIcon.Make(HudIcon.Kind.Gem, UiTheme.Cyan, 64);
        icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        v.AddChild(icon);
        v.AddChild(UiTheme.Label("CRYSTAL EXCAVATOR", 34, UiTheme.Cyan, true, HorizontalAlignment.Center));
        v.AddChild(UiTheme.Label("Carregando cavernas…", 18, UiTheme.Muted, false, HorizontalAlignment.Center));
        return layer;
    }

    public override void _ExitTree()
    {
        UiBus.OpenShop -= OpenShop;
        UiBus.PrestigeRequested -= AskPrestige;
        Settings.Changed -= ApplyLightQuality;
        UiBus.OpenSell -= OpenSell;
        if (GameState.Instance != null)
        {
            GameState.Instance.RegionsChanged -= UpdateRegionCulling;
            GameState.Instance.Sold -= OnSold;
            if (GameState.Instance.BeforeSave == WritePlayerToSave) GameState.Instance.BeforeSave = null;
        }
    }

    // ======================= Fluxo =======================
    void ShowMenu()
    {
        CurrentMode = Mode.Menu;
        GameState.Instance.IsPlaying = false;
        AudioManager.Instance?.SetBeam(false);
        Events?.ResetAll();
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Hud.Visible = false;
        _menuCam.Current = true;
        _env.SetRegion(GameData.Regions[0], true);
        for (int i = 0; i < World.RegionRoots.Count; i++) World.RegionRoots[i].Visible = i < 2;
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
        if (continueSave) { SaveSystem.Snapshot("ao-iniciar"); gs.LoadGame(); }
        else { SaveSystem.ArchiveAndDelete(); gs.NewGame(); }

        _shards.ClearAll();
        Events.ResetAll();
        if (Player == null)
        {
            Player = new PlayerController { Name = "Player" };
            World.GetParent().AddChild(Player);
            Player.CrystalHit += OnCrystalHit;
            Player.ResonancePulse += n => Hud.ShowPulse(n);
            _shards.PlayerPosition = () => Player.BodyCenter;
            _marker = new ObjectiveMarker { Name = "ObjectiveMarker", World = World, PlayerPos = () => Player.GlobalPosition, CurrentRegion = () => _currentRegion };
            World.GetParent().AddChild(_marker);
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
        GameState.Instance.QuitClean();
    }

    void Pause()
    {
        if (CurrentMode != Mode.Playing || _pause.IsOpen) return;
        GetTree().Paused = true;
        AudioManager.Instance?.SetBeam(false);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _pause.Open();
    }

    void Resume()
    {
        _pause.Close();
        _settings.Close();
        _codex.Close();
        _help.Close();
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
        if (_pendingCompletion && !AnyModal())
        {
            _pendingCompletion = false;
            _completion.Open();
            return;
        }
        if (Player != null) Player.InputEnabled = true;
        if (!_pause.IsOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void OpenCodexForTour() => _codex.Open();
    public void OpenHelpForTour() => _help.Open();
    public void OpenSettingsForTour() => _settings.Open();
    public void OpenCompletionForTour() => _completion.Open();

    /// <summary>Fecha qualquer janela e volta ao jogo (usado pelo tour de capturas).</summary>
    public void CloseAllForTour()
    {
        foreach (var sc in new ModalScreen[] { _shop, _sell, _pause, _settings, _confirm, _codex, _help, _completion, _updateScreen, _backups })
            sc.CloseSilently();
        GetTree().Paused = false;
        if (Player != null) Player.InputEnabled = true;
    }

    bool AnyModal() => _shop.IsOpen || _sell.IsOpen || _pause.IsOpen || _settings.IsOpen || _confirm.IsOpen || _codex.IsOpen || _help.IsOpen || _completion.IsOpen || _updateScreen.IsOpen || _backups.IsOpen;

    public override void _Input(InputEvent e) => InputHints.Observe(e);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            GetViewport().SetInputAsHandled();
            if (_confirm.IsOpen) { _confirm.Close(); return; }
            if (_updateScreen.IsOpen) { _updateScreen.Close(); return; }
            if (_backups.IsOpen) { _backups.Close(); return; }
            if (_settings.IsOpen) { _settings.Close(); return; }
            if (_codex.IsOpen) { _codex.Close(); return; }
            if (_help.IsOpen) { _help.Close(); return; }
            if (_completion.IsOpen) { _completion.Close(); return; }
            if (CurrentMode != Mode.Playing) return;
            if (_shop.IsOpen) { _shop.Close(); return; }
            if (_sell.IsOpen) { _sell.Close(); return; }
            if (_pause.IsOpen) Resume(); else Pause();
            return;
        }
        if (e is InputEventJoypadButton jb && jb.Pressed && jb.ButtonIndex == JoyButton.B && AnyModal())
        {
            GetViewport().SetInputAsHandled();
            if (_confirm.IsOpen) _confirm.Close();
            else if (_settings.IsOpen) _settings.Close();
            else if (_codex.IsOpen) _codex.Close();
            else if (_help.IsOpen) _help.Close();
            else if (_completion.IsOpen) _completion.Close();
            else if (_shop.IsOpen) _shop.Close();
            else if (_sell.IsOpen) _sell.Close();
            else if (_pause.IsOpen) Resume();
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
        if (_booting) return;
        if (CurrentMode == Mode.Menu)
        {
            _menuT += (float)delta * 0.08f;
            var c = new Vector3(Mathf.Sin(_menuT) * 11f, 3.2f + Mathf.Sin(_menuT * 2.3f) * 0.6f, -2f + Mathf.Cos(_menuT) * 11f);
            _menuCam.GlobalPosition = c;
            _menuCam.LookAt(new Vector3(0, 1.4f, -2f), Vector3.Up);
            return;
        }
        UpdateRegion(false);
        UpdateDynamicResolution((float)delta);
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

    void OnSold(long amount)
    {
        if (Player == null || CurrentMode != Mode.Playing) return;
        var p = Player.Camera.GlobalPosition - Player.Camera.GlobalTransform.Basis.Z * 1.6f + Vector3.Down * 0.4f;
        Effects.Instance?.Burst(p, UiTheme.Gold, 1.2f);
        Effects.Instance?.FloatText(p + Vector3.Up * 0.3f, "+" + GameState.FormatMoney(amount), UiTheme.Gold, 1.6f);
    }

    void AskPrestige()
    {
        var gs = GameState.Instance;
        if (!gs.CanPrestige) return;
        _confirm.Ask($"Renascer recomeça a jornada: dinheiro, melhorias, mochila e regiões voltam ao início. " +
                     $"Você recebe +{gs.PendingEssence} Essência Ancestral (bônus permanente de +10% em dano e venda por ponto). " +
                     "A Coleção e as estatísticas são mantidas. Continuar?", DoPrestige);
    }

    void DoPrestige()
    {
        var gs = GameState.Instance;
        if (!gs.Prestige()) return;
        _shop.CloseSilently();
        _sell.CloseSilently();
        _completion.CloseSilently();
        _shards.ClearAll();
        Events.ResetAll();
        if (Player != null)
        {
            Player.GlobalPosition = World.SpawnPoint;
            Player.Velocity = Vector3.Zero;
            Player.SetLook(0);
        }
        _currentRegion = null;
        UpdateRegion(true);
        UpdateRegionCulling();
        Hud.ResetMoneyDisplay();
        ReturnToGameplay();
    }

    void OnCompleted()
    {
        if (CurrentMode != Mode.Playing) return;
        // pequena pausa dramática antes da tela
        GetTree().CreateTimer(1.5f).Timeout += () =>
        {
            if (CurrentMode != Mode.Playing || AnyModal()) { _pendingCompletion = true; return; }
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Player.InputEnabled = false;
            AudioManager.Instance?.SetBeam(false);
            _completion.Open();
        };
    }

    bool _pendingCompletion;

    void OnCrystalHit(Crystal c, bool broke)
    {
        if (broke && c.Material.Special) GameState.Instance.CheckCompletion();
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
