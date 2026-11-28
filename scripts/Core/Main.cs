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
    DebugScreen _debug;
    GoblinScreen _goblin;
    public BuildingManager Buildings { get; private set; }
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
        // processo de teste de renderizador: avisa logo que o motor e o C# iniciaram (o driver abriu)
        var probeOut = args.FirstOrDefault(a => a.StartsWith("--probe-out="));
        if (probeOut != null && args.Any(a => a.StartsWith("--render-probe=")))
        {
            // teste automático: simula o driver parado numa caixa de erro antes do jogo iniciar
            if (args.Contains("--probe-simulate=nostart")) return;
            using var alive = FileAccess.Open(probeOut.Substring("--probe-out=".Length) + ".alive", FileAccess.ModeFlags.Write);
            alive?.StoreString("ok");
        }
        // verificação das APIs gráficas num processo sem janela (pedido pelo RenderBootstrap)
        var gpuCheck = args.FirstOrDefault(a => a.StartsWith("--gpu-check="));
        if (gpuCheck != null)
        {
            Rendering.GpuCapabilities.RunAndWrite(gpuCheck.Substring("--gpu-check=".Length));
            GetTree().Quit(0);
            return;
        }
        // diagnóstico: qual versão é esta (jogadores x desenvolvedor) — sem abrir o jogo
        if (args.Contains("--build-info"))
        {
            var ico = FileAccess.GetFileAsBytes("res://icon.ico");
            GD.Print($"BUILD exportado={BuildFlavor.IsExported} steam={BuildFlavor.IsSteam} admin={BuildFlavor.DevTools} atualizador={UpdateManager.Supported} icone={ico?.Length ?? 0}");
            GetTree().Quit(0);
            return;
        }
        if (args.Contains("--run-tests"))
        {
            AddChild(new Tests.TestRunner());
            return;
        }
        // escolha do renderizador antes de montar o mundo (pode testar e reiniciar com outro)
        if (Rendering.RenderBootstrap.ShouldRun(args))
        {
            AddChild(new Rendering.RenderBootstrap { OnContinue = () => BuildGame(args) });
            return;
        }
        BuildGame(args);
    }

    /// <summary>Monta o mundo, as telas e inicia (depois da escolha do renderizador).</summary>
    void BuildGame(string[] args)
    {
        // teste automático de recuperação: simula o jogo caindo antes do menu neste renderizador
        if (args.Contains("--x-boot-crash=" + Rendering.RenderMethods.Current)) { OS.Kill(OS.GetProcessId()); return; }
        AddChild(new OreIcons { Name = "OreIcons" });
        ShortcutIcon.Apply(); // atalhos antigos passam a mostrar o ícone novo (só no Windows exportado)
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
        Buildings = new BuildingManager { Name = "Buildings", World = World, GetPlayer = () => Player };
        Buildings.CanUseBuildMode = () => CurrentMode == Mode.Playing && Player != null && Player.InputEnabled && !AnyModal() && !_fading && !PlayerInTown;
        world3d.AddChild(Buildings);
        _shards.FindSink = Buildings.FindSink;
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
            OnRescue = Rescue,
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
        _goblin = new GoblinScreen();
        AddChild(_goblin);
        _goblin.Closed += ReturnToGameplay;
        UiBus.OpenGoblin += camp =>
        {
            if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Player.InputEnabled = false;
            AudioManager.Instance?.SetBeam(false);
            _goblin.Show(camp);
        };
        _debug = new DebugScreen { Run = RunDebug };
        AddChild(_debug);
        _debug.Closed += () => { GetTree().Paused = false; ReturnToGameplay(); };
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
        _missions = new MissionScreen();
        _missions.Closed += ReturnToGameplay;
        AddChild(_missions);
        _forge = new ForgeScreen();
        _forge.Closed += ReturnToGameplay;
        AddChild(_forge);
        _diary = new DiaryScreen();
        _diary.Closed += ReturnToGameplay;
        AddChild(_diary);
        _map = new MapScreen
        {
            PlayerPos = () => Player?.GlobalPosition ?? Vector3.Zero, PlayerYaw = () => Player?.Yaw ?? 0f,
            PlayerRegion = () => Player == null || PlayerInTown ? -1 : WorldBuilder.RegionAt(Player.GlobalPosition).Index,
        };
        _map.Closed += () => { GetTree().Paused = false; ReturnToGameplay(); };
        AddChild(_map);
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
        UiBus.Door += UseHouseDoor;
        UiBus.OpenMissions += OpenMissions;
        UiBus.OpenForge += OpenForge;
        UiBus.OpenDiary += OpenDiary;
        UiBus.Talk += Talk;
        UiBus.Gate += UseGate;
        UiBus.DeepExit += ExitDeep;
        UiBus.Descend += Descend;
        UiBus.Plant += PlantSeed;
        UiBus.GoToMine += GoToMine;
        _shards.InventoryFullBlocked += () => { if (CurrentMode == Mode.Playing) Hud.FlashFull(); };
        Input.Singleton.JoyConnectionChanged += InputHints.OnJoyConnection;
        InputHints.PadConnection += OnPadConnection;
        if (Input.GetConnectedJoypads().Count > 0) InputHints.StartWithPad(); // controle já ligado ao abrir o jogo
        GameState.Instance.BeforeSave = WritePlayerToSave;
        Buildings.BuildBarChanged += (t, d, ok) => Hud.SetBuildBar(t, d, ok);
        GameState.Instance.Prestiged += () => Buildings.Rebuild();
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
            if (mm.HasMeta(ChunkedMulti.RangeMeta)) mm.VisibilityRangeEnd = (float)mm.GetMeta(ChunkedMulti.RangeMeta) * ChunkedMulti.RangeScale(Settings.Quality);
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
        double target = DynamicTarget(Settings.Data.MaxFps, Settings.Data.VSync ? DisplayServer.ScreenGetRefreshRate() : -1);
        _dynScale = NextDynamicScale(_dynScale, max, fps, target, Settings.Quality == GraphicsQuality.Low ? 0.5f : 0.6f);
        if (!Mathf.IsEqualApprox(vp.Scaling3DScale, _dynScale)) vp.Scaling3DScale = _dynScale;
    }

    public float CurrentRenderScale => GetViewport().Scaling3DScale;

    /// <summary>FPS que a resolução dinâmica tenta segurar: 120, ou menos se o limite/VSync não deixar passar disso.</summary>
    public static double DynamicTarget(int maxFps, double refresh)
    {
        double cap = maxFps > 0 ? maxFps : 1000;
        if (refresh > 0) cap = System.Math.Min(cap, refresh);
        return System.Math.Min(120.0, cap - 3);
    }

    /// <summary>Próxima escala 3D: baixa 5% quando o FPS fica abaixo do alvo (10% se estiver muito abaixo), sobe quando sobra folga.</summary>
    public static float NextDynamicScale(float scale, float max, double fps, double target, float min = 0.6f)
    {
        if (fps < target * 0.7 && scale > min) return Mathf.Max(min, scale - 0.1f);
        if (fps < target - 2 && scale > min) return Mathf.Max(min, scale - 0.05f);
        if (fps > target * 1.12 && scale < max) return Mathf.Min(max, scale + 0.05f);
        return scale;
    }

    async void Boot(string[] args)
    {
        // processo de teste de renderizador: mede a cena real e fecha (sem menu, sem atualizador)
        var probe = args.FirstOrDefault(a => a.StartsWith("--render-probe="));
        if (probe != null)
        {
            AddChild(new Rendering.RenderProbe { Main = this, Method = probe.Substring("--render-probe=".Length),
                OutPath = args.FirstOrDefault(a => a.StartsWith("--probe-out="))?.Substring("--probe-out=".Length) ?? "user://probe.json" });
            return;
        }
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
        Rendering.RenderBootstrap.MarkBootOk(this);
        _updater.CheckForUpdates();


        if (args.Contains("--autostart")) StartGame(false);
        if (args.Contains("--perf"))
        {
            // diagnóstico: tempos por quadro do motor a cada 3 s
            Prof.On = true;
            var t = new Timer { WaitTime = 3, Autostart = true, ProcessMode = ProcessModeEnum.Always };
            AddChild(t);
            t.Timeout += () => GD.Print($"[Perf] fps {Engine.GetFramesPerSecond():0} · process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0} ms · física {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0} ms · nós {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} · corpos {Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects):0} · colisões {Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs):0}\n   " + Prof.Report());
        }
        // reaberto pelo atualizador: continua a partida de onde parou
        else if (args.Contains("--continue") && SaveSystem.HasSave()) StartGame(true);
        var matrix = args.FirstOrDefault(a => a.StartsWith("--render-matrix="));
        if (matrix != null) AddChild(new Tests.RenderMatrix { Main = this, Dir = matrix.Substring("--render-matrix=".Length) });
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
        UiBus.Door -= UseHouseDoor;
        UiBus.OpenMissions -= OpenMissions;
        UiBus.OpenForge -= OpenForge;
        UiBus.OpenDiary -= OpenDiary;
        UiBus.Talk -= Talk;
        UiBus.Gate -= UseGate;
        UiBus.DeepExit -= ExitDeep;
        UiBus.Descend -= Descend;
        UiBus.Plant -= PlantSeed;
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
            _confirm.Ask(Loc.T("Iniciar um novo jogo apagará o progresso salvo atual. Deseja continuar?"), PlayIntroThenStart);
        else PlayIntroThenStart();
    }

    IntroComic _intro;
    /// <summary>Novo jogo: mostra a HQ de abertura (dívida com os ferreiros) e depois começa.</summary>
    void PlayIntroThenStart()
    {
        if (_intro == null)
        {
            _intro = new IntroComic { Name = "IntroComic" };
            AddChild(_intro);
            _intro.Finished += () => StartGame(false);
        }
        if (_intro.IsPlaying) return;
        _menu.Hide();
        _intro.Play();
    }

    public void StartGame(bool continueSave)
    {
        var gs = GameState.Instance;
        if (continueSave) { SaveSystem.Snapshot("ao-iniciar"); gs.LoadGame(); }
        else { SaveSystem.ArchiveAndDelete(); gs.NewGame(); }

        _shards.ClearAll();
        Events.ResetAll();
        Buildings.Rebuild();
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
                camp.PlayerInMine = () => CurrentMode == Mode.Playing && !PlayerInTown && !_fading;
                camp.Discovered += c => GetTree().CreateTimer(2.6f).Timeout += () => Hud.ShowBigMessage("GOBLIN ENCONTRADO!"); // depois de "PASSAGEM SECRETA!"
            }
            _shards.PlayerPosition = () => Player.BodyCenter;
            Rope.PlayerPos = () => Player.GlobalPosition;
            if (World.Town?.Life != null) World.Town.Life.PlayerPos = () => Player.GlobalPosition;
            Player.Enemies = World.Enemies;
            World.Enemies.Player = Player;
            World.Enemies.SpawnShards = (m, p, n) => _shards.SpawnShards(m, p, n);
            World.Enemies.Killed += e =>
            {
                GameState.Instance.Data.EnemiesKilled++;
                if (e.Def.Guardian) GameState.Instance.DefeatGuardian(e.Def.Region);
            };
            World.Enemies.DamageTakenMultiplier = () => GameState.Instance.DamageTakenMultiplier;
            World.Enemies.Killed += e => { if (e.Def.Boss) OnBossDefeated(); };
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
        World.PrepareRegionLists();
        UpdateRegionCulling();
        if (!continueSave) gs.Notify("Bem-vindo, escavador!", "Mire em um cristal e clique com o botão esquerdo para dar cada golpe.", NotifyKind.Info);
        gs.SaveGame(true);
    }

    void BackToMenu()
    {
        GameState.Instance.SaveGame(true);
        _pause.Close();
        if (Player != null) Player.InputEnabled = false;
        ShowMenu();
        // voltou ao menu no meio da sessão: procura atualização na hora (atualiza e volta para a partida)
        _updater.ResumeAfterUpdate = true;
        _updater.CheckForUpdates();
    }

    /// <summary>Preso em algum lugar? Volta para a entrada da região atual (sem custo).</summary>
    public void Rescue()
    {
        if (Player == null || CurrentMode != Mode.Playing) return;
        Resume();
        Player.StopClimb(false);
        int ri = WorldBuilder.RegionAt(Player.GlobalPosition).Index;
        var L = World.Layouts[ri];
        var to = ri == 0 || PlayerInTown ? World.SpawnPoint : L.OnGround(L.Entrance.X, L.Entrance.Z - 4f, 0.2f);
        if (PlayerInHouse) to = World.House.WakePoint;
        else if (PlayerInTown) to = World.Town.ArrivePoint;
        Player.GlobalPosition = to;
        Player.Velocity = Vector3.Zero;
        UpdateRegion(true);
        UpdateRegionCulling();
        GameState.Instance.Notify("Resgate", "Você voltou para a entrada da região.", NotifyKind.Info);
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
            PlaceTo(World.Town.ArrivePoint, Town.ArriveYaw);
            Hud.ShowBigMessage("VILA DOS FERREIROS");
            gs.Notify(Loc.F("Fim do Dia {0}", gs.Data.Day),
                Loc.F("Ganho no dia: {0}", GameState.FormatMoney(gs.Data.DayEarned)) + (bonus > 0 ? Loc.F(" (+{0} de bônus)", GameState.FormatMoney(bonus)) : "") +
                Loc.T(". Venda ao FERREIRO, veja as ENCOMENDAS e durma em CASA."), NotifyKind.Success);
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
            gs.Notify("Bom dia!", "Saia de casa e pegue o ELEVADOR da vila para voltar à mina.", NotifyKind.Info);
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
        bool deep = WorldBuilder.RegionAt(Player.GlobalPosition).Deep;
        int stolen = gs.StealFromInventory(0.25f);
        foreach (var camp in World.GoblinCamps) if (camp.Rebels.Count > 0) camp.EndRebellion(false);
        gs.EndDay(early: true);
        AudioManager.Instance?.SetBeam(false);
        FadeThrough(() =>
        {
            Player.RestoreHp();
            PlaceTo(World.House.WakePoint, Mathf.Pi);
            Hud.ShowBigMessage("VOCÊ DESMAIOU");
            if (deep) gs.Notify("Você desmaiou", Loc.F("As criaturas das Profundezas te derrubaram. Você perdeu {0} fragmentos e acordou em casa; durma para começar outro dia.", stolen), NotifyKind.Warning);
            else gs.Notify("Você desmaiou", Loc.F("Os goblins levaram {0} fragmentos. Você acordou em casa; durma para começar outro dia.", stolen), NotifyKind.Warning);
        });
    }

    public bool PlayerInHouse => Player != null && PlayerHouse.Contains(Player.GlobalPosition);
    /// <summary>Na superfície: Vila dos Ferreiros ou dentro de casa (mina escondida, sem construir, compras liberadas).</summary>
    public bool PlayerInTown => Player != null && (PlayerHouse.Contains(Player.GlobalPosition) || Town.Contains(Player.GlobalPosition));

    MissionScreen _missions;
    ForgeScreen _forge;
    DiaryScreen _diary;
    MapScreen _map;
    DialogueScreen _dialogue;
    IntroComic _flashback;
    public bool StoryPlaying => (_dialogue?.IsPlaying ?? false) || (_flashback?.IsPlaying ?? false);

    void BeginStory()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
    }

    void EndStory()
    {
        if (StoryPlaying) return;
        ReturnToGameplay();
    }

    void Say(System.Collections.Generic.IReadOnlyList<Line> lines, System.Action then = null)
    {
        if (_dialogue == null)
        {
            _dialogue = new DialogueScreen { Name = "Dialogue" };
            AddChild(_dialogue);
        }
        BeginStory();
        _dialogue.Play(lines, () => { then?.Invoke(); EndStory(); });
    }

    void Flashback(System.Action then)
    {
        if (_flashback == null)
        {
            _flashback = new IntroComic { Name = "Flashback" };
            AddChild(_flashback);
            _flashback.Finished += () => { var t = _flashbackThen; _flashbackThen = null; t?.Invoke(); EndStory(); };
        }
        BeginStory();
        _flashbackThen = then;
        _flashback.Play("bram_", StoryData.Flashback);
    }
    System.Action _flashbackThen;

    /// <summary>Conversa com o Velho Bram conforme a etapa do ato 2.</summary>
    void Talk(string who)
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading || StoryPlaying) return;
        var gs = GameState.Instance;
        bool cathedral = gs.IsUnlocked("cathedral");
        switch (gs.Story)
        {
            case StoryData.Locked:
                Say(new[] { new Line("bram", StoryData.Grumbles[gs.Data.Day % StoryData.Grumbles.Length]) });
                break;
            case StoryData.BramCalls:
                Say(StoryData.Intro1, () => Flashback(() => Say(cathedral ? StoryData.Intro2 : StoryData.Intro2.Concat(StoryData.NeedCathedral).ToArray(), () => gs.SetStory(StoryData.FindGate))));
                break;
            case StoryData.FindGate:
                Say(cathedral ? StoryData.RemindGate : StoryData.NeedCathedral);
                break;
            case StoryData.BringMold:
                Say(StoryData.Mold, () => gs.SetStory(StoryData.ForgeKey));
                break;
            case StoryData.ForgeKey:
                Say(StoryData.RemindKey);
                break;
            case StoryData.OpenGate:
                Say(StoryData.KeyReady);
                break;
            case StoryData.SeedFound:
                Say(StoryData.BramSeed, () => gs.SetStory(StoryData.PlantSeed));
                break;
            case StoryData.PlantSeed:
                Say(StoryData.RemindPlant);
                break;
            case StoryData.Freed:
                Say(StoryData.AfterEnding);
                break;
            default:
                Say(StoryData.AfterGate);
                break;
        }
    }

    /// <summary>Desce pelo portão aberto até uma Profundeza Proibida (primeira vez: libera a área).</summary>
    void EnterDeep(RegionDef r)
    {
        var gs = GameState.Instance;
        if (!World.DeepArrivals.TryGetValue(r.Id, out var at)) return;
        if (!gs.CanWork) { gs.RequireWork(); return; }
        bool first = !gs.IsUnlocked(r.Id);
        if (first) { gs.Data.UnlockedRegions.Add(r.Id); gs.SaveGame(true); }
        if (!gs.HasWeapon)
            gs.Notify("Desarmado", Loc.T("Você desce sem arma. Lá embaixo os monstros não se mineram: forje uma arma na forja da vila."), NotifyKind.Warning);
        FadeThrough(() =>
        {
            PlaceTo(at, 0f);
            if (gs.HasWeapon) Player.SetWeaponMode(true);
            Hud.ShowBigMessage(r.Name.ToUpper());
            if (first) gs.Notify("As Profundezas Proibidas", Loc.T("Cuidado: aqui os cristais têm dono. Troque entre ferramenta e arma quando precisar."), NotifyKind.Unlock);
        });
    }

    DeepTravelScreen _deepTravel;

    /// <summary>Chegada à arena do Mestre Corvo: conversa e depois a luta. Corvo já derrotado não volta.</summary>
    void CheckBoss()
    {
        var boss = World.Enemies?.Boss;
        if (boss == null || Player == null || CurrentMode != Mode.Playing) return;
        var gs = GameState.Instance;
        if (gs.Story >= StoryData.SeedFound) { if (boss.Alive) boss.Retire(); return; }
        if (!boss.Dormant || StoryPlaying || AnyModal() || _fading) return;
        if (Player.GlobalPosition.DistanceTo(World.BossArena) > World.BossArenaRadius + 3f) return;
        Hud.ShowBigMessage(Loc.T("MESTRE CORVO"));
        Say(StoryData.CorvoIntro, () => { boss.Dormant = false; if (gs.HasWeapon) Player.SetWeaponMode(true); });
    }

    void OnBossDefeated()
    {
        var gs = GameState.Instance;
        gs.SetStory(StoryData.SeedFound);
        Hud.ShowBigMessage(Loc.T("A SEMENTE DO POVO LÚMEN"));
        gs.Notify("Mestre Corvo derrotado!", Loc.T("Você pegou a Semente do Povo Lúmen. Leve-a ao Velho Bram, na vila."), NotifyKind.Unlock);
        GetTree().CreateTimer(1.6f).Timeout += () => { if (CurrentMode == Mode.Playing && !StoryPlaying) Say(StoryData.CorvoDefeated); };
    }

    /// <summary>Final: planta a Semente no monumento da praça (HQ final) e a Guilda perde o poder.</summary>
    void PlantSeed()
    {
        var gs = GameState.Instance;
        if (CurrentMode != Mode.Playing || AnyModal() || _fading || StoryPlaying || gs.Story != StoryData.PlantSeed) return;
        BeginStory();
        if (_flashback == null)
        {
            _flashback = new IntroComic { Name = "Flashback" };
            AddChild(_flashback);
            _flashback.Finished += () => { var t = _flashbackThen; _flashbackThen = null; t?.Invoke(); EndStory(); };
        }
        _flashbackThen = () =>
        {
            gs.SetStory(StoryData.Freed);
            World.Town?.SeedPlanted();
            Hud.ShowBigMessage(Loc.T("LIVRE!"));
            gs.Notify("Fim · A mina é de quem cava", Loc.T("A Guilda caiu. Sem a taxa dela, tudo vende 25% mais caro. O jogo continua: termine a Coleção, os diários e as Profundezas."), NotifyKind.Unlock);
        };
        _flashback.Play("end_", StoryData.Ending);
    }

    /// <summary>Passagem no fundo de uma Profundeza: só com o Guardião derrotado; leva à próxima área.</summary>
    void Descend(string regionId)
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
        var gs = GameState.Instance;
        if (!gs.GuardianDefeated(regionId))
        {
            var g = EnemyData.GuardianOf(regionId);
            gs.Notify("Passagem lacrada", Loc.F("O selo só se abre quando {0}, o Guardião desta área, cair.", g?.Name ?? "?"), NotifyKind.Info);
            return;
        }
        var list = GameData.Regions;
        int i = list.FindIndex(r => r.Id == regionId);
        var next = i >= 0 && i + 1 < list.Count ? list[i + 1] : null;
        if (next == null || !next.Deep)
        {
            gs.Notify("O Trono de Corvo", Loc.T("Lá embaixo, uma luz vermelha pulsa e alguém bate num martelo, sem parar. (O Trono de Corvo chega numa próxima atualização.)"), NotifyKind.Info);
            return;
        }
        EnterDeep(next);
    }

    /// <summary>Portal de saída de uma Profundeza: volta para a frente do portão da Catedral.</summary>
    void ExitDeep(string regionId)
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading || World.SealedGate == null) return;
        var g = World.SealedGate;
        var front = g.GlobalPosition + g.GlobalTransform.Basis.Z * 5f;
        var L = World.LayoutAt(front);
        var at = L.OnGround(front.X, front.Z, 0.1f);
        var dir = g.GlobalTransform.Basis.Z;
        FadeThrough(() => PlaceTo(at, Mathf.Atan2(-dir.X, -dir.Z)));
    }

    /// <summary>Portão selado da Catedral: molde do selo (etapa 2) e abertura com a chave falsa (etapa 5).</summary>
    void UseGate()
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading || StoryPlaying) return;
        var gs = GameState.Instance;
        switch (gs.Story)
        {
            case StoryData.FindGate:
                gs.SetStory(StoryData.BringMold);
                Hud.ShowBigMessage(Loc.T("MOLDE DO SELO"));
                gs.Notify("Molde do selo", Loc.T("Você apertou a cera no selo do Mestre. Leve o molde ao Velho Bram, na vila."), NotifyKind.Unlock);
                break;
            case StoryData.OpenGate:
                gs.SetStory(StoryData.GateOpen);
                World.SealedGate?.Open();
                AudioManager.Instance?.Play("unlock", -2f);
                Hud.ShowBigMessage(Loc.T("O PORTÃO SE ABRIU"));
                gs.Notify("As Profundezas Proibidas", Loc.T("Um vento frio sobe da escuridão. Seis lugares, um pior que o outro... e o Mestre Corvo no último. Forje uma arma na vila antes de descer."), NotifyKind.Unlock);
                break;
            case >= StoryData.GateOpen:
                // com mais de uma Profundeza aberta, escolhe o destino
                if (GameData.Deep.Count(r => gs.IsUnlocked(r.Id)) > 1)
                {
                    if (_deepTravel == null)
                    {
                        _deepTravel = new DeepTravelScreen { Chosen = EnterDeep };
                        _deepTravel.Closed += ReturnToGameplay;
                        AddChild(_deepTravel);
                    }
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    Player.InputEnabled = false;
                    _deepTravel.Open();
                }
                else EnterDeep(GameData.Deep.First());
                break;
            case StoryData.BringMold:
            case StoryData.ForgeKey:
                gs.Notify("Portão selado", Loc.T("O selo do Mestre não cede. Você precisa da chave falsa que o Velho Bram ensinou a forjar."), NotifyKind.Info);
                break;
            default:
                gs.Notify("Portão selado", Loc.T("Um portão antigo, mais velho que a Guilda, lacrado com correntes e o selo do Mestre."), NotifyKind.Info);
                break;
        }
    }

    void OpenDiary(string id)
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
        if (GameState.Instance.ReadDiary(id)) Hud.ShowBigMessage("DIÁRIO ENCONTRADO");
        _diary.Show(id);
    }

    /// <summary>Abre o mapa (M / Back). O jogo fica pausado enquanto ele está aberto.</summary>
    public bool OpenMap()
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading || Player == null) return false;
        UpdateExploration(true);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
        GetTree().Paused = true;
        _map.World = World;
        _map.Open();
        return true;
    }

    public MapScreen MapForTests => _map;

    float _exploreTimer;
    /// <summary>Revela no mapa o que está em volta do jogador (raio da lanterna).</summary>
    public void UpdateExploration(bool now = false)
    {
        if (Player == null || World == null || PlayerInTown) return;
        if (!now && (_exploreTimer -= (float)GetProcessDeltaTime()) > 0) return;
        _exploreTimer = 0.3f;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        ExploreMap.Reveal(GameState.Instance.Data, r, World.Layouts[r.Index], Player.GlobalPosition);
    }

    void OpenForge()
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
        _forge.Open();
    }

    void OpenMissions()
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Player.InputEnabled = false;
        AudioManager.Instance?.SetBeam(false);
        _missions.Open();
    }

    /// <summary>Porta da casa: entra (perto da porta, olhando para dentro) ou sai para a vila.</summary>
    void UseHouseDoor(bool toInside)
    {
        if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
        AudioManager.Instance?.SetBeam(false);
        FadeThrough(() =>
        {
            if (toInside) PlaceTo(PlayerHouse.Origin + new Vector3(-1.9f, 0.1f, 1.4f), -Mathf.Pi / 2);
            else PlaceTo(Town.HouseOutsidePoint, Town.HouseOutsideYaw);
        });
    }

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
        foreach (var sc in new ModalScreen[] { _debug, _goblin, _choice, _missions, _forge, _diary, _map, _deepTravel, _home, _shop, _sell, _pause, _settings, _confirm, _codex, _help, _completion, _updateScreen, _backups })
            sc?.CloseSilently();
        GetTree().Paused = false;
        if (Player != null) Player.InputEnabled = true;
    }

    bool AnyModal() => (_choice?.IsOpen ?? false) || (_missions?.IsOpen ?? false) || (_forge?.IsOpen ?? false) || (_diary?.IsOpen ?? false) || (_map?.IsOpen ?? false) || (_deepTravel?.IsOpen ?? false) || StoryPlaying || _home.IsOpen || _shop.IsOpen || _sell.IsOpen || _pause.IsOpen || _settings.IsOpen || _confirm.IsOpen || _codex.IsOpen || _help.IsOpen || _completion.IsOpen || _updateScreen.IsOpen || _backups.IsOpen || (_debug?.IsOpen ?? false) || (_goblin?.IsOpen ?? false);

    public override void _Input(InputEvent e) => InputHints.Observe(e);

    void OnPadConnection(bool connected, string name)
    {
        var gs = GameState.Instance;
        if (connected) gs.Notify("Controle conectado", string.IsNullOrEmpty(name) ? "" : name, NotifyKind.Info);
        else
        {
            gs.Notify("Controle desconectado", "Reconecte o controle ou use teclado e mouse.", NotifyKind.Warning);
            // desconectou no meio da partida: pausa para ninguém ficar parado no meio da caverna
            if (CurrentMode == Mode.Playing && !AnyModal() && !_fading) Pause();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // painel de admin: F9 ou Back (controle), durante a partida
        if (e.IsActionPressed("debug_panel") && BuildFlavor.DevTools) // versão dos jogadores: F9 não faz nada
        {
            GetViewport().SetInputAsHandled();
            if (_debug.IsOpen) { _debug.Close(); return; }
            if (CurrentMode != Mode.Playing || AnyModal() || _fading) return;
            GetTree().Paused = true;
            AudioManager.Instance?.SetBeam(false);
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Player.InputEnabled = false;
            _debug.Open();
            return;
        }
        if (e.IsActionPressed("toggle_map"))
        {
            if (_map.IsOpen) { GetViewport().SetInputAsHandled(); _map.Close(); return; }
            if (OpenMap()) { GetViewport().SetInputAsHandled(); return; }
        }
        if (e.IsActionPressed("pause"))
        {
            GetViewport().SetInputAsHandled();
            if (_debug.IsOpen) { _debug.Close(); return; }
            if (_goblin.IsOpen) { _goblin.Close(); return; }
            if (_confirm.IsOpen) { _confirm.Close(); return; }
            if (_choice.IsOpen) { _choice.Decline(); return; }
            if (_home.IsOpen) { _home.Close(); return; }
            if (_missions?.IsOpen ?? false) { _missions.Close(); return; }
            if (_forge?.IsOpen ?? false) { _forge.Close(); return; }
            if (_diary?.IsOpen ?? false) { _diary.Close(); return; }
            if (_map?.IsOpen ?? false) { _map.Close(); return; }
            if (_deepTravel?.IsOpen ?? false) { _deepTravel.Close(); return; }
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
            if (_debug.IsOpen) _debug.Close();
            else if (_goblin.IsOpen) _goblin.Close();
            else if (_confirm.IsOpen) _confirm.Close();
            else if (_choice.IsOpen) _choice.Decline();
            else if (_home.IsOpen) _home.Close();
            else if (_missions?.IsOpen ?? false) _missions.Close();
            else if (_forge?.IsOpen ?? false) _forge.Close();
            else if (_diary?.IsOpen ?? false) _diary.Close();
            else if (_map?.IsOpen ?? false) _map.Close();
            else if (_deepTravel?.IsOpen ?? false) _deepTravel.Close();
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
        long __t = Core.Prof.Start();
        _ProcessInner(delta);
        Core.Prof.End("main", __t);
    }

    void _ProcessInner(double delta)
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
        UpdateExploration();
        CheckBoss();
        UpdateDynamicResolution((float)delta);
        UpdateAccessibilityCues((float)delta);
        _cullTimer -= (float)delta;
        if (_cullTimer <= 0) { _cullTimer = 0.5f; UpdateRegionCulling(); CheckSecrets(); }
    }

    int _bannerPlace = -1;

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

    bool _townNightShown;
    void UpdateRegion(bool instant)
    {
        if (Player == null) return;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        bool inHouse = PlayerInHouse;
        bool inTown = PlayerInTown;
        int place = inHouse ? 2 : inTown ? 1 : 0;
        GameState.Instance.InTown = inTown;
        bool night = World.Town?.Night ?? true;
        Player.SetDaylight(inTown && !inHouse && !night);
        // céu da vila acompanha o fim do turno e o novo dia mesmo sem sair do lugar
        if (inTown && night != _townNightShown && r == _currentRegion && place == _bannerPlace) { _townNightShown = night; _env.SetTown(true, night); }
        if (r == _currentRegion && place == _bannerPlace) return;
        _townNightShown = night;
        _bannerPlace = place;
        _currentRegion = r;
        GameState.Instance.CurrentRegionId = r.Id;
        _env.SetRegion(r, instant);
        _env.SetTown(inTown, World.Town?.Night ?? true);
        if (inHouse) Hud.ShowHouseBanner(); else if (inTown) Hud.ShowTownBanner(); else Hud.ShowRegionBanner(r);
        AudioManager.Instance?.PlayMusic(r.Index);
        UiBus.EnterRegion(r.Id);
    }

    /// <summary>Esconde regiões distantes (não adjacentes) para economizar desenho na GPU.</summary>
    void UpdateRegionCulling()
    {
        if (Player == null || CurrentMode != Mode.Playing) return;
        var r = WorldBuilder.RegionAt(Player.GlobalPosition);
        bool inHouse = PlayerInTown; // casa e vila são a superfície: a mina some
        if (World.House.Visible != inHouse) World.House.Visible = inHouse;
        if (World.Town.Visible != inHouse) World.Town.Visible = inHouse;
        for (int i = 0; i < World.RegionRoots.Count; i++)
        {
            // a mina e as Profundezas não se enxergam (ficam longe e sem túnel)
            bool sameWorld = GameData.Regions[i].Deep == r.Deep;
            bool visible = !inHouse && sameWorld && Mathf.Abs(i - r.Index) <= 1;
            if (World.RegionRoots[i].Visible != visible) World.RegionRoots[i].Visible = visible;
            World.SetRegionActive(i, visible);
        }
        World.SetAmbientActive(World.Town, inHouse);
        World.SetAmbientActive(World.House, inHouse);
        Buildings.UpdateVisibility(r.Index, inHouse);
        World.Enemies?.ShowOnly(!inHouse && r.Deep ? r.Id : null);
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
        _confirm.Ask(Loc.F("Renascer recomeça a jornada: Lúmens, melhorias, mochila e regiões voltam ao início. ") +
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
        Buildings?.WriteInFlight(d);
        return true;
    }
}
