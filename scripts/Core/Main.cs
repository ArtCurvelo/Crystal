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
        _sell.SwitchToShop = () => { if (!GameState.Instance.RequireWork()) return; _sell.CloseSilently(); _shop.Open(); };
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
            Loc.F("Restaurar o backup de {0} ({1})? O progresso atual também é guardado como backup antes.", (snap.When == default ? Loc.T("data desconhecida") : snap.When.ToString("dd/MM/yyyy HH:mm")), GameState.FormatMoney(snap.Data.Money)),
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
        _choice = new ChoiceScreen();
        _choice.Closed += ReturnToGameplay;
        AddChild(_choice);
        _home = new HomeScreen { OnSleep = () => { _home.CloseSilently(); SleepAtHome(); } };
        _home.Closed += ReturnToGameplay;
        AddChild(_home);
        _fade = new CanvasLayer { Layer = 60 };
        _fadeRect = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _fade.AddChild(_fadeRect);
        AddChild(_fade);
        _confirm.Closed += ReturnToGameplay;
        _completion = new CompletionScreen();
        _completion.Closed += ReturnToGameplay;
        AddChild(_completion);
        GameState.Instance.Completed += OnCompleted;

        UiBus.OpenShop += OpenShop;
        UiBus.PrestigeRequested += AskPrestige;
        UiBus.OpenSell += OpenSell;
        UiBus.OpenHome += OpenHome;
        UiBus.OpenHomePanel += OpenHomePanel;
        UiBus.Sleep += SleepAtHome;
        UiBus.GoToMine += GoToMine;
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
            if (l.GetParent() is not Mining.Effects && !l.HasMeta(HouseDiorama.AlwaysOnMeta) && l.Name != "HelmetLamp") l.Visible = on; // clarões do Effects controlam a si mesmos
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
        UiBus.OpenHome -= OpenHome;
        UiBus.OpenHomePanel -= OpenHomePanel;
        UiBus.Sleep -= SleepAtHome;
        UiBus.GoToMine -= GoToMine;
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
            _confirm.Ask(Loc.T("Iniciar um novo jogo apagará o progresso salvo atual. Deseja continuar?"), () => StartGame(false));
        else StartGame(false);
    }

    public void StartGame(bool continueSave)
    {
        var gs = GameState.Instance;
        if (continueSave) { SaveSystem.Snapshot("ao-iniciar"); gs.LoadGame(); }
        else { SaveSystem.ArchiveAndDelete(); gs.NewGame(); }

        _shards.ClearAll();
        Events.ResetAll();
        foreach (var camp in World.GoblinCamps) camp.SyncState();
        if (Player == null)
        {
            Player = new PlayerController { Name = "Player" };
            World.GetParent().AddChild(Player);
            Player.CrystalHit += OnCrystalHit;
            Player.ResonancePulse += n => Hud.ShowPulse(n);
            Player.Damaged += _ => Hud.FlashDamage();
            Player.Fainted += OnFainted;
            foreach (var camp in World.GoblinCamps)
            {
                camp.PlayerPos = () => Player.GlobalPosition;
                camp.DamagePlayer = (d, from) => { if (CurrentMode == Mode.Playing && !_fading) Player.TakeDamage(d, from); };
                camp.PlayerInMine = () => CurrentMode == Mode.Playing && !PlayerInHouse && !_fading;
                camp.Discovered += c => GetTree().CreateTimer(2.6f).Timeout += () => Hud.ShowBigMessage("GOBLIN ENCONTRADO!"); // depois de "PASSAGEM SECRETA!"
            }
            _shards.PlayerPosition = () => Player.BodyCenter;
            _shards.GroundAt = World.GroundAt;
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
            // posições de versões antigas (mapa retangular) podem estar dentro da rocha: volta ao elevador
            if (gs.IsUnlocked(region.Id) && World.IsSafeSpot(saved)) { pos = saved; yaw = d.PlayerYaw; }
        }
        Player.GlobalPosition = pos;
        Player.Velocity = Vector3.Zero;
        Player.SetLook(yaw);
        PlayerController.SafePoint = World.SpawnPoint + Vector3.Up * 0.5f;
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
        if (!GameState.Instance.RequireWork()) return;
        TerminalScreen.AtSellStation = false; // terminal de melhorias: sem venda aqui
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        _shop.Open();
    }

    void OpenSell()
    {
        if (CurrentMode != Mode.Playing || AnyModal()) return;
        TerminalScreen.AtSellStation = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        _sell.Open();
    }

    // ======================= Casa / ciclo do dia =======================
    HomeScreen _home;

    void OpenHome()
    {
        if (CurrentMode != Mode.Playing || AnyModal()) return;
        var gs = GameState.Instance;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
        if (gs.DayOver) GoHome();
        else _confirm.Ask(Loc.F("Encerrar o turno agora ({0}) e subir para casa? O dia termina mais cedo.", gs.ClockText), GoHome);
    }

    CanvasLayer _fade;
    ColorRect _fadeRect;
    bool _fading;

    /// <summary>Escurece a tela, executa a troca (teleporte) e clareia de novo.</summary>
    void FadeThrough(System.Action mid)
    {
        _fading = true;
        Player.InputEnabled = false;
        var tw = CreateTween();
        tw.TweenProperty(_fadeRect, "color:a", 1f, 0.25f);
        tw.TweenCallback(Callable.From(() => { mid(); }));
        tw.TweenInterval(0.15f);
        tw.TweenProperty(_fadeRect, "color:a", 0f, 0.35f);
        tw.TweenCallback(Callable.From(() => { _fading = false; ReturnToGameplay(); }));
    }

    void PlaceTo(Vector3 pos, float yaw)
    {
        Player.GlobalPosition = pos;
        Player.Velocity = Vector3.Zero;
        Player.SetLook(yaw);
        _currentRegion = null;
        UpdateRegion(true);
        UpdateRegionCulling();
    }

    ChoiceScreen _choice;

    /// <summary>Elevador da mina → casa (encerra o dia). Antes, o goblin cobra a taxa do dia.</summary>
    void GoHome()
    {
        var gs = GameState.Instance;
        if (CurrentMode != Mode.Playing) return;
        var due = gs.DueGoblins();
        if (due.Count > 0)
        {
            var def = due[0];
            string id = def.RegionId;
            long fee = gs.GoblinFee(id);
            string threat = gs.GoblinData(id).Strikes switch
            {
                0 => Loc.T("Ele está de bom humor."),
                1 => Loc.T("Ele lembra que você não pagou da última vez."),
                _ => Loc.T("Ele está furioso. Se não pagar de novo, pode fugir ou se rebelar!"),
            };
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Player.InputEnabled = false;
            _choice.Ask(Loc.F("{0} QUER SEU PAGAMENTO", def.Name.ToUpperInvariant()),
                Loc.F("{0}, {1}, minerou {2} hoje (está no baú dele). A taxa é {3}. {4}", def.Name, def.Title, GameState.FormatMoney(gs.GoblinData(id).DayValue), GameState.FormatMoney(fee), threat),
                Loc.F("Pagar {0}", GameState.FormatMoney(fee)), "Não pagar", gs.CanAfford(fee), pay =>
                {
                    var r = gs.SettleGoblinFee(id, pay);
                    switch (r)
                    {
                        case GameState.FeeOutcome.Paid: gs.Notify(def.Name, "\"Negócio justo, chefe!\"", NotifyKind.Success); break;
                        case GameState.FeeOutcome.Forgiven: gs.Notify(def.Name, "\"Hmpf. Releva... desta vez.\"", NotifyKind.Info); break;
                        case GameState.FeeOutcome.Warned: gs.Notify(Loc.F("{0} está irritado", def.Name), "\"Mais uma vez, e ninguém responde por mim!\"", NotifyKind.Warning); break;
                        case GameState.FeeOutcome.Fled: gs.Notify(Loc.F("{0} fugiu!", def.Name), "Cansado de trabalhar de graça, ele sumiu. Talvez volte ao esconderijo em alguns dias.", NotifyKind.Warning); break;
                        case GameState.FeeOutcome.Rebel: gs.Notify(Loc.F("{0} jurou vingança!", def.Name), "Quando você voltar à região dele, ele e os comparsas vão cobrar à força. Prepare o feixe!", NotifyKind.Warning); break;
                    }
                    foreach (var c in World.GoblinCamps) c.SyncState();
                    GoHome();
                });
            return;
        }
        gs.EndDay(early: true);
        long bonus = gs.PayShiftBonus();
        _shards.ClearAll();
        AudioManager.Instance?.SetBeam(false);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        FadeThrough(() =>
        {
            PlaceTo(World.House.ArrivePoint, 0);
            Hud.ShowBigMessage("CASA");
            gs.Notify(Loc.F("Fim do Dia {0}", gs.Data.Day),
                Loc.F("Ganho no dia: {0}", GameState.FormatMoney(gs.Data.DayEarned)) + (bonus > 0 ? Loc.F(" (+{0} de bônus)", GameState.FormatMoney(bonus)) : "") +
                Loc.T(". Use o PAINEL para melhorias e a CAMA para dormir."), NotifyKind.Success);
        });
    }

    void OpenHomePanel()
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
        _home.Open();
    }

    /// <summary>Cama: começa o próximo dia; o jogador acorda em casa.</summary>
    void SleepAtHome()
    {
        var gs = GameState.Instance;
        if (CurrentMode != Mode.Playing || _fading) return;
        if (!gs.DayOver) { gs.Notify("Ainda é dia", "Você já descansou. Desça pelo elevador para trabalhar.", NotifyKind.Info); return; }
        Input.MouseMode = Input.MouseModeEnum.Captured;
        FadeThrough(() =>
        {
            gs.StartNewDay();
            Events.ResetAll();
            PlaceTo(World.House.WakePoint, Mathf.Pi);
            Hud.ShowBigMessage(Loc.F("DIA {0}", gs.Data.Day));
            gs.Notify("Bom dia!", "Desça pelo ELEVADOR para voltar à mina.", NotifyKind.Info);
        });
    }

    /// <summary>Elevador da casa → mina (só depois de dormir).</summary>
    void GoToMine()
    {
        var gs = GameState.Instance;
        if (CurrentMode != Mode.Playing || _fading) return;
        if (gs.DayOver) { gs.Notify("Hora de descansar", "Durma na cama antes de descer para a mina.", NotifyKind.Info); return; }
        FadeThrough(() => PlaceTo(World.SpawnPoint, 0));
    }

    /// <summary>Desmaio (rebelião goblin): perde parte da mochila e o resto do dia; acorda em casa.</summary>
    void OnFainted()
    {
        var gs = GameState.Instance;
        int stolen = gs.StealFromInventory(0.25f);
        foreach (var camp in World.GoblinCamps) if (camp.Rebels.Count > 0) camp.EndRebellion(false);
        gs.EndDay(early: true);
        AudioManager.Instance?.SetBeam(false);
        FadeThrough(() =>
        {
            Player.RestoreHp();
            PlaceTo(World.House.WakePoint, Mathf.Pi);
            Hud.ShowBigMessage("VOCÊ DESMAIOU");
            gs.Notify("Você desmaiou", Loc.F("Os goblins levaram {0} fragmentos. Você acordou em casa; durma para começar outro dia.", stolen), NotifyKind.Warning);
        });
    }

    public bool PlayerInHouse => Player != null && PlayerHouse.Contains(Player.GlobalPosition);

    public bool HomeOpen => _home.IsOpen;

    void ReturnToGameplay()
    {
        if (CurrentMode != Mode.Playing) return;
        if (AnyModal()) return; // outra janela ainda aberta (ex.: confirmação sobre a loja)
        if (_pendingCompletion && !AnyModal())
        {
            _pendingCompletion = false;
            _completion.Open();
            return;
        }
        if (_fading) return;
        if (Player != null) { Player.InputEnabled = true; Player.SuppressInput(); }
        if (!_pause.IsOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void OpenCodexForTour() => _codex.Open();
    public void OpenHelpForTour() => _help.Open();
    public void OpenSettingsForTour() => _settings.Open();
    public void OpenCompletionForTour() => _completion.Open();

    /// <summary>Fecha qualquer janela e volta ao jogo (usado pelo tour de capturas).</summary>
    public void CloseAllForTour()
    {
        foreach (var sc in new ModalScreen[] { _choice, _home, _shop, _sell, _pause, _settings, _confirm, _codex, _help, _completion, _updateScreen, _backups })
            sc.CloseSilently();
        GetTree().Paused = false;
        if (Player != null) Player.InputEnabled = true;
    }

    bool AnyModal() => (_choice?.IsOpen ?? false) || _home.IsOpen || _shop.IsOpen || _sell.IsOpen || _pause.IsOpen || _settings.IsOpen || _confirm.IsOpen || _codex.IsOpen || _help.IsOpen || _completion.IsOpen || _updateScreen.IsOpen || _backups.IsOpen;

    public override void _Input(InputEvent e) => InputHints.Observe(e);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            GetViewport().SetInputAsHandled();
            if (_confirm.IsOpen) { _confirm.Close(); return; }
            if (_choice.IsOpen) { _choice.Decline(); return; }
            if (_home.IsOpen) { _home.Close(); return; }
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
            else if (_choice.IsOpen) _choice.Decline();
            else if (_home.IsOpen) _home.Close();
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
            // câmera do menu gira devagar sobre o acampamento
            var L0 = World.Layouts[0];
            var cc = L0.OnGround(L0.Camp.A.X, L0.Camp.A.Y);
            var c = cc + new Vector3(Mathf.Sin(_menuT) * 8f, 3.4f + Mathf.Sin(_menuT * 2.3f) * 0.5f, Mathf.Cos(_menuT) * 8f);
            _menuCam.GlobalPosition = c;
            _menuCam.LookAt(cc + new Vector3(0, 1.6f, -3f), Vector3.Up);
            return;
        }
        UpdateRegion(false);
        UpdateDynamicResolution((float)delta);
        UpdateAccessibilityCues((float)delta);
        _cullTimer -= (float)delta;
        if (_cullTimer <= 0) { _cullTimer = 0.5f; UpdateRegionCulling(); CheckSecrets(); }
    }

    bool _bannerHouse;

    // ---------------- acessibilidade: sons-guia e narração do que a mira encontra ----------------
    AudioStreamPlayer3D _cuePlayer;
    float _cueTimer;
    Mining.Crystal _lastAimed;
    Player.IInteractable _lastPrompt;

    void UpdateAccessibilityCues(float dt)
    {
        if (Player == null || CurrentMode != Mode.Playing || AnyModal()) return;
        var d = Settings.Data;
        // narração: minério ou estação na mira
        if (d.Narration || d.AudioCues)
        {
            if (Player.TargetCrystal != _lastAimed)
            {
                _lastAimed = Player.TargetCrystal;
                if (_lastAimed != null)
                {
                    if (d.AudioCues) AudioManager.Instance?.Play("cue_aim", -4f, 1f + (_lastAimed.Material.Rare ? 0.3f : 0f));
                    Accessibility.Speak(Loc.T(_lastAimed.Material.Name), true);
                }
            }
            if (Player.TargetInteractable != _lastPrompt)
            {
                _lastPrompt = Player.TargetInteractable;
                if (_lastPrompt != null) Accessibility.Speak(_lastPrompt.GetPrompt(), true);
            }
        }
        if (!d.AudioCues) return;
        // sonar: ping 3D vindo do minério mineável mais próximo (mais rápido quando perto)
        _cueTimer -= dt;
        if (_cueTimer > 0) return;
        Mining.Crystal best = null; float bd = 25f;
        var p = Player.GlobalPosition;
        foreach (var c in World.Crystals)
        {
            if (!c.IsMineable) continue;
            float dist = c.GlobalPosition.DistanceTo(p);
            if (dist < bd) { bd = dist; best = c; }
        }
        if (best == null) { _cueTimer = 1.5f; return; }
        if (_cuePlayer == null)
        {
            _cuePlayer = new AudioStreamPlayer3D { Bus = "SFX", UnitSize = 6f, MaxDistance = 40f, Stream = AudioManager.Instance?.GetStream("cue_ping") };
            World.GetParent().AddChild(_cuePlayer);
        }
        _cuePlayer.GlobalPosition = best.Center;
        _cuePlayer.PitchScale = 0.8f + (1f - bd / 25f) * 0.7f;
        _cuePlayer.Play();
        _cueTimer = Mathf.Lerp(0.18f, 1.3f, bd / 25f);
    }

    /// <summary>Descoberta de câmaras secretas (uma vez por save).</summary>
    void CheckSecrets()
    {
        if (Player == null || CurrentMode != Mode.Playing) return;
        var gs = GameState.Instance;
        var secret = World.LayoutAt(Player.GlobalPosition).SecretAt(Player.GlobalPosition);
        if (secret == null || gs.Data.Secrets.Contains(secret.Id)) return;
        gs.Data.Secrets.Add(secret.Id);
        int total = 0;
        foreach (var L in World.Layouts) total += L.Secrets.Count;
        Hud.ShowBigMessage("PASSAGEM SECRETA!");
        gs.Notify("Câmara secreta encontrada", Loc.F("Depósitos raros escondidos aqui. Câmaras secretas: {0}/{1}", gs.Data.Secrets.Count, total), NotifyKind.Rare);
        AudioManager.Instance?.Play("rare", -2f);
        gs.SaveGame(true);
    }

    void UpdateRegion(bool instant)
    {
        if (Player == null) return;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        bool inHouse = PlayerInHouse;
        if (r == _currentRegion && inHouse == _bannerHouse) return;
        _bannerHouse = inHouse;
        _currentRegion = r;
        GameState.Instance.CurrentRegionId = r.Id;
        _env.SetRegion(r, instant);
        if (inHouse) Hud.ShowHouseBanner(); else Hud.ShowRegionBanner(r);
        AudioManager.Instance?.PlayMusic(r.Index);
        UiBus.EnterRegion(r.Id);
    }

    /// <summary>Esconde regiões distantes (não adjacentes) para economizar desenho na GPU.</summary>
    void UpdateRegionCulling()
    {
        if (Player == null || CurrentMode != Mode.Playing) return;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        bool inHouse = PlayerInHouse;
        if (World.House.Visible != inHouse) World.House.Visible = inHouse;
        for (int i = 0; i < World.RegionRoots.Count; i++)
        {
            bool visible = !inHouse && Mathf.Abs(i - r.Index) <= 1;
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
        _confirm.Ask(Loc.F("Renascer recomeça a jornada: dinheiro, melhorias, mochila e regiões voltam ao início. ") +
                     Loc.F("Você recebe +{0} Essência Ancestral (bônus permanente de +10% em dano e venda por ponto). ", gs.PendingEssence) +
                     Loc.T("A Coleção e as estatísticas são mantidas. Continuar?"), DoPrestige);
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
