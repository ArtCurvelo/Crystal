# PROJECT_STATE — Crystal Excavator

Documento de continuidade. Leia isto primeiro ao retomar o projeto em uma nova conversa.

## Visão geral
Jogo incremental 3D em primeira pessoa: minerar cristais com uma escavadeira de feixe, vender fragmentos, comprar melhorias e desbloquear regiões.
Godot **4.5.1 .NET**, C# (.NET 8), renderizador **Compatibility**. Todo o conteúdo (malhas, materiais, shaders, sons, música) é **gerado proceduralmente em código**, sem assets externos.
A direção de arte segue as imagens conceituais do diretor: HUD sci-fi chanfrado, ferramenta laranja/cinza, cristais facetados com rachaduras, postos avançados, cachoeiras.

## Tempo (informado pelo diretor)
4h26 consumidas às 05:17, ~25h34 restantes. **Pausado: aguardar "Pode continuar".** Histórico em `DEVELOPMENT_LOG.md`.

## Repositório
- GitHub: https://github.com/ArtCurvelo/Crystal (branch `main`). **Fonte da verdade.**
- O desenvolvimento acontece num container Linux na nuvem (`/home/user/crystal-excavator`), e cada etapa é enviada com `git push`.
- No Windows do diretor: clone em `C:\Users\silvi\Documents\Crystal`. Atualizar com `git pull` nessa pasta.
- Ao retomar num container novo: clonar o repositório e reinstalar as ferramentas (ver "Comandos").

## Conteúdo do jogo
- **6 regiões** em sequência no eixo −Z: Cavernas Luminosas (z≈0), Profundezas Prismáticas (◈ 6.000), Núcleo Ancestral (◈ 400 mil), Reservatório Abissal (◈ 1,5 mi), Jardim Fúngico (◈ 8 mi) e Catedral de Cristal (◈ 40 mi). Jornada principal = 4 primeiras + Coração (`GameData.JourneyRegions`); as outras são fim de jogo.
- **13 materiais** com raridade (Comum → Lendário), incluindo a formação especial Coração Ancestral.
- **5 upgrades no Terminal**: Mochila Reforçada, Botas, Lapidação Fina, Ímã de Coleta e Amplificador de Ressonância (dano e velocidade saíram da loja).
- **12 ferramentas compradas** (`Data/ToolData.cs`): picaretas de madeira → cobre → hematita → ametista → prata → safira → obsidiana → rubi, brocas de titânio e peridoto, lasers de diamante e prisma (só no fim). Cada uma tem dano, golpes/s, durabilidade e região mínima; quebrada → mãos (1 de dano) até comprar outra. Lasers gastam **munição** (células, pacote de 200 comprado no Terminal).
- **Raridade por profundidade**: `CaveLayout.Depth` (distância andando desde a entrada) limita a raridade (`WorldBuilder.MaxRarityAt`); região 1 vai no máximo até Épico; desvios/salas secretas dão +1.
- **Identidade por minério**: `Mining/OreShapes.cs` gera uma forma 3D própria por minério (aglomerado, lascas, geodo, pepita, bandado, gelo, brasa, coral, pérola) com duas cores; `UI/OreIcons.cs` fotografa cada forma para os ícones do HUD, Terminal, venda e códice.
- **Casa** (`World/PlayerHouse.cs`, `World/HouseDiorama.cs`): 9 melhorias — Cama, Bancada, Armário, Cozinha, Troféus, **Suporte de ferramentas** (+12% durabilidade; nível 1 libera comprar ferramentas/munição na bancada da casa), **Baú do comprador** (vende a mochila em casa; +5% capacidade), **Lampiões** (+12% alcance da lanterna) e **Carregador de células** (+30 células por manhã com laser). Cada uma tem móvel próprio; o patamar visual (0–4) usa referência fixa de 20 níveis.
- **Árvore de habilidades**: 8 ramos, 30 habilidades (`UI/SkillTreeView.cs`). Ramo TÉCNICA: Golpe triplo (clique = 3 golpes) → Mineração contínua (segurar). Sem elas, cada clique é um golpe (`PlayerController._queuedSwings`).
- **Ressonância**: só com ferramenta a laser (`GameState.ResonanceActive`).
- **Eventos por região**: Eco Luminoso, Maré Gélida, Surto do Núcleo e Maré Luminosa.
- **Vila dos Ferreiros** (`World/Town.cs`): fim do elevador da mina; casa do jogador, ferreiro (vende minérios e ferramentas) e Quadro de Encomendas (3 pedidos por dia, `GameState.EnsureContracts`, `UI/MissionScreen.cs`). Próximas etapas do plano: forja (criar equipamentos com minérios), níveis mais fundos com inimigos pelo elevador, história e calibrar a duração para 4–6 h.
- **Forja** (`Data/GearData.cs`, `UI/ForgeScreen.cs`, bigorna `ForgeAnvil` na vila): capacete/luvas/botas/bolsa em 6 níveis com minérios de cada região + taxa; conserto de ferramentas. Zera ao renascer.
- **Missões com prazo** (`GameState.TryAccept/FailActiveContract`, `UI/MissionScreen.cs`): uma missão aceita por vez, marcada na mina; não entregou até dormir → multa (metade da recompensa) e o que faltar vira dívida (`SaveData.Debt`).
- **Mapa (M)** (`Core/ExploreMap.cs`, `UI/MapScreen.cs`): planta de cada região só com as partes já vistas (névoa em quadrados de 4 m, salva em `SaveData.Explored`).
- **HQs animadas** (`UI/IntroComic.cs`, `assets/story/*_bg/_fg/_fx.png` gerados por `tools/comic2.py`): abertura (dívida com a Guilda), flashback do Bram e final. Camadas com paralaxe, partículas, tremida e transições variadas.
- **Vila viva** (`World/Humanoid.cs`, `World/TownLife.cs`, `World/TownVillage.cs`): moradores com rotina dia/noite, vigia, feirantes, galinhas, pássaros; casas enxaimel, feira, poço, árvores, bandeirolas.
- **Diários de escravos** (`Data/DiaryData.cs`, `World/DiaryNote.cs`, `UI/DiaryScreen.cs`): 12 em câmaras secretas contando a história do Coração, do Povo Lúmen e da Guilda (gancho: "quebrar a Guilda").
- **Ato 2 — A Chave Falsa** (`docs/STORY.md`, `Data/StoryData.cs`): Velho Bram (vila) → flashback em HQ → portão selado no fundo da Catedral (`World/SealedGate.cs`) → molde do selo → chave falsa forjada à noite → portão aberto (as Profundezas Proibidas ainda não existem).
- **Ato 3 — Profundezas Proibidas**: `RegionDef.Deep` (5 áreas depois das 6 da mina, sem túnel), inimigos com comportamentos (`World/Enemies.cs`), Guardião por área que abre a passagem para a próxima, armas e armaduras por combinação de minérios (`Data/WeaponData.cs`, tecla X). Trono de Corvo com o chefão Mestre Corvo (3 fases) e o final: a Semente plantada no monumento da praça (`SeedAltar`), HQ final e venda +25%.
- **Marcador de objetivo 3D** e guia de objetivos no HUD.
- **Tela de conclusão** (Mestre Escavador): todas as regiões liberadas + Coração destruído.
- **Idiomas:** português (texto-fonte), inglês e espanhol — `Core/Loc.cs` (`Loc.T`/`Loc.F`, chave = texto em português) e `Core/LocTablesData.cs` (gerado). Texto novo precisa de tradução nas duas tabelas; o teste confere os marcadores `{n}`.
- **Construções** (`World/Buildings.cs` = gerenciador/modo construção/trajetos; `World/BuildingUnits.cs` = funil, entrada da esteira, base, drone, robô; `World/BuildingParts.cs` = poste e esteira; `Data/BuildData.cs`): modo construção (B). Corda e poste sempre liberados; o resto vem do **ramo Construção** da árvore de habilidades (Esteira → Funil → Drone de área → Robô mineiro). Esteira: marca o início e arrasta (ou clique-clique) até a Estação de Venda ou até outra esteira (emenda em `JoinS`); trajeto por `RouteBetween` (A* com folga, atravessa regiões nos dois sentidos) salvo em `BuildingSave.Path`; `Feeds` = 0 venda, id = outra esteira, −1 sem saída. Funil só ao lado de esteira. Drone e robô têm base fixa e só trabalham na área (14 m / 10 m), entregam no funil/entrada de esteira mais perto ou guardam na base; robô minera com 50% da força do goblin da região e para fora do turno. Saves antigos migram (`Ver` 0 → funil vira funil + esteira; drone antigo é devolvido). Salvas em `SaveData.Buildings`.
- **Painel de admin** (F9 / Back): `Core/MainDebug.cs` (comandos) e `UI/DebugScreen.cs`.
- **Acessibilidade** (`Core/Accessibility.cs`): filtro de daltonismo, narração (TTS do sistema), sons-guia, reduzir tremor. Remapeamento de teclado/mouse/controle em `InputSetup`.

## Arquitetura
```
project.godot            Configuração (autoloads, renderizador, janela, pasta de usuário "CrystalExcavator")
CrystalExcavator.csproj  Projeto C# (Godot.NET.Sdk/4.5.1, net8.0)
CrystalExcavator.sln     Necessário para a exportação .NET
export_presets.cfg       "Windows Desktop" (principal, com .console.exe) e "Linux (QA)"
scenes/Main.tscn         Única cena; tudo é montado por código em Main.cs
shaders/
  crystal.gdshader     Cristal facetado (UV = ângulo/altura do prisma), fresnel e rachaduras Voronoi controladas por `damage`.
  waterfall.gdshader   Cachoeira aditiva com faixas animadas.
  lake.gdshader        Lago com ondulações e cáusticas (Reservatório).
scripts/
  Core/
    GameState.cs   Autoload. Dados, economia, upgrades, regiões, eventos (C#), autosave, conclusão, QuitClean().
    SaveData.cs    Modelo persistente + Sanitize() (corrige dados inválidos).
    SaveSystem.cs  JSON em user://save.json, escrita atômica, backup .bak, migração.
    Settings.cs    user://settings.json, autodetecção de GPU na 1ª execução, Apply() (janela/resolução/áudio/FPS).
    InputSetup.cs  Mapa de entradas registrado por código.
    UiBus.cs       Eventos mundo → UI (abrir loja/venda).
    Main.cs        Fluxo Menu/Jogo/Pausa, telas modais, região atual, culling de regiões, marcador de objetivo.
  Data/GameData.cs Materiais (com Rarity), regiões, upgrades. Balanceamento centralizado aqui.
  Player/          PlayerController (FPS, mineração, ressonância, feixe, interação), ToolView (6 modelos + feixe), IInteractable.
  Mining/          Crystal (malha mesclada, vida, reações, quebra, renascimento), ShardManager (fragmentos + ímã),
                   Effects (pool de partículas, textos, ondas de choque e clarões).
  World/           WorldBuilder (mundo procedural), Decor (posto, cachoeiras, flora, ruínas, formações gigantes, caixotes,
                   refinaria, scanner, passarelas, lago, águas-vivas), Stations (Venda, Terminal, Portão + farol),
                   RegionEvents, EnvironmentController, StaticBatcher (mescla malhas estáticas), ObjectiveMarker,
                   MeshFactory (+ BasisExt.ScaleLocal).
  UI/              UiTheme (estilo chanfrado + caches), Hud (HUD + Crosshair), HudIcon (ícones vetoriais + BracketFrame),
                   Terminal (ShopScreen/SellScreen com abas, SegmentBar), Menus (Configurações, Pausa, Confirmação,
                   Coleção, Como jogar, Conclusão), MainMenu.
  Audio/AudioManager.cs  Autoload. Síntese de efeitos, zumbido do feixe em loop e 5 trilhas ambiente.
tests/
  TestRunner.cs     Suíte automatizada (--run-tests): 166 verificações.
  ScreenshotTour.cs Tour de capturas, FPS e draw calls (--shots=<pasta>).
  PlaythroughBot.cs Bot que joga a partida real via entradas e telas (--bot[=min]): 4 regiões, conclusão, caminhada, estabilidade.
tools/run_tests.sh  Compila e roda os testes (código de saída 0 = sucesso).
tools/run_bot.sh    Roda o bot (cerca de 1,5 min real).
tools/build_installer.sh  Exporta para Windows e gera build/CrystalExcavator-Setup.exe (NSIS em /home/user/tools/nsis).
installer/          Script NSIS do instalador.
docs/screenshots/   Capturas do jogo real.
```

### Regras técnicas
- A região é detectada pela coordenada Z (`WorldBuilder.RegionAt`). Os portões ficam nos túneis.
- Turno de trabalho em `GameState` (`TickDay`, `EndDay`, `StartNewDay`, `CanWork`/`RequireWork`; `DayLengthOverride` para testes). Fora do turno só vender e ir para casa. Única `SellStation` e `MineElevator` no acampamento (`WorldBuilder.SellStation/Elevator`); `HomeScreen` (UI/HomeScreen.cs) aberta pelo elevador via `UiBus.RequestHome`: maquete `World/HouseDiorama.cs`, melhorias da casa e árvore de habilidades (`Data/HomeData.cs`; efeitos aplicados nos atributos de `GameState`).
- Cavernas orgânicas: `CaveLayout` (planejamento + grid de 1 m: chão F, teto C, distância D, alcançável a pé) e `CaveTerrain` (malhas em blocos de 32 m, HeightMapShape3D, caixas nos túneis baixos). `RegionDef.Wings` viraram câmaras com minério em destaque. Depósitos têm `StandPoint` (onde o jogador fica) e `Center` (alvo de mira). Agachar no `PlayerController`. Sons/efeitos/fragmentos por `OreArchetype` (AudioManager.PlayOre, Effects.BreakFx/HitFx, ShardManager.DropMesh).
- Camadas de física: 1 mundo, 2 jogador, 3 (valor 4) cristais, 4 (valor 8) interagíveis.
- `Basis.Scaled()` escala nos eixos **globais**; usar `ScaleLocal()` para transformações compostas.
- Malhas estáticas sob Node3D "puros" são mescladas na carga (`StaticBatcher`); nós com script não são tocados.
- Commits só com `run_tests.sh` retornando 0.

## Comandos
Ferramentas do container (fora do repositório, precisam ser reinstaladas num container novo):
- Godot 4.5.1 mono (zip oficial do GitHub) em `/home/user/tools/godot`, com o arquivo `._sc_` (modo autocontido) e os modelos em `editor_data/export_templates/4.5.1.stable.mono/`.
- .NET 8: `apt-get download` dos pacotes `dotnet-*`, `aspnetcore-*` e `netstandard-*` do Ubuntu, `dpkg -x` e mover `usr/lib/dotnet` para `/home/user/tools/dotnet`.
- `/home/user/tools/env.sh` exporta `DOTNET_ROOT`, `PATH` e `GODOT`.

```bash
source /home/user/tools/env.sh
dotnet build
./tools/run_tests.sh
./tools/run_bot.sh 150
$GODOT --headless --path . --import
$GODOT --headless --path . --export-release "Windows Desktop" build/windows/CrystalExcavator.exe
xvfb-run -a -s "-screen 0 1280x720x24" $GODOT --path . --resolution 1280x720 -- --shots=/caminho/saida
```
Argumentos de usuário (após `--`): `--run-tests`, `--shots=<pasta>`, `--bot[=minutos]`, `--autostart`.

## Publicar atualizações (automáticas para o jogador)
- **Autorização permanente do diretor (v0.13.0 em diante):** publicar todas as atualizações sem pedir autorização, desde que os testes passem e o bot dê SUCESSO. `BUMP=minor` para atualizações com conteúdo novo; patch para correções.
- `./tools/publish_update.sh "notas da versão"`: testes → versão +1 (patch) → exportação → `update.json` com SHA-256 → envio forçado ao ramo `updates` → commit da versão no `main`.
- O jogo (exportado, Windows) lê `https://raw.githubusercontent.com/ArtCurvelo/Crystal/updates/update.json` ao abrir.
- Instaladores manuais (`tools/build_installer.sh` → `dist/`) só para a 1ª instalação ou troca de versão do Godot.
- Versão publicada atual: ver `config/version` em `project.godot`.

## Procedimento de teste
1. `./tools/run_tests.sh` → código 0 e `0 falharam`.
2. `./tools/run_bot.sh 150` → `BOT: SUCESSO`.
3. Tour de capturas (inclui folha das ferramentas e métricas de draw calls) → inspecionar os PNGs.
4. Exportar para Windows e enviar o zip ao diretor; no Windows: F3 a 720p na qualidade Baixa.

## Balanceamento atual (economia nerfada, `Economy` em GameData.cs)
Simulação de jogador (teste "balanceamento"): região 2 ≈ 33 min, região 3 ≈ 3 h, Coração ≈ 5,7 h.
O bot multiplica a venda por 2,6 (`Economy.SellValue`) só para cobrir a jornada inteira em ~85 min.

## Métricas (qualidade Baixa, 720p, software)
Draw calls: R1 ~290, R2 ~240, R3 ~180. Sem vazamento em cerca de 50 min de jogo simulado.

## Problemas pendentes
- **FPS na RTX 3060 do irmão do diretor (Alta)**: era 85; meta 120. Otimizações da v0.22 publicadas, aguardando a medição (F3).
- **HD 4400 (notebook do diretor)**: 12–19 FPS na Baixa (v0.27/0.28). Rocha leve + resolução dinâmica até 50% na v0.29; aguardando nova medição com o F3 (mostra ms de CPU e GPU).

## Próxima ação recomendada
Desempenho em PCs fracos (aguardando dados do diretor sobre a máquina das capturas a 12–19 FPS na Baixa) e calibrar a duração total (meta 4–6 h) (as peças da forja já preparam o capacete como proteção). Depois: continuar a história (final ao quebrar o Coração = liberdade) e calibrar a duração para 4–6 h. Aguardar o FPS real da RTX 3060.

## Renderização (v0.4.0)
- Forward+ (Vulkan) com fallback automático para OpenGL. `Settings.Advanced` = Forward+ ativo. Testes/capturas locais usam Mesa lavapipe (`/home/user/tools/vk`, variáveis em `env.sh`).
- Tour de capturas força qualidade Alta (`--shots-quality=0..2` para comparar).
- Luzes: lanterna (SpotLight "HelmetLamp"), uma OmniLight por depósito (fade por distância), luzes de câmara; na Baixa as OmniLights são ocultadas.

## Plataforma-alvo (decisão do diretor, v0.5)
- O PC de referência é o do irmão do diretor (RTX 3060, i7 11ª geração, 16–32 GB DDR5, ~180 FPS no Alto) e deve ser tratado como **máquina média**: todo o design gráfico, sonoro e audiovisual parte dele. PCs fracos ficam apenas com o fallback automático (OpenGL/Baixa).

- **Vídeo/desempenho** (`Core/Settings.cs`, `Main.UpdateDynamicResolution`): `SettingsData.Version` (2), padrão sem VSync e limite 144, `AntiAliasing` (0 off, 1 FXAA, 2 SMAA, 3 FSR 2) aplicado em `Settings.ApplyAntiAliasing`; resolução dinâmica mira `Main.DynamicTarget` (120 ou o limite) com FSR; `Settings.StrongGpu` sobe RTX/RX 6000+ para Alta. Texturas de ruído da rocha em `CaveTerrain.Smooth` (512 px, float).

- **Renderizador automático** (`Core/Rendering/`, doc em `docs/RENDERIZACAO.md`): `RenderBootstrap` roda antes de montar o mundo (só no jogo exportado ou com `--render-select`), testa Forward+ → Mobile → Compatibility em processos fora da tela (`--render-probe=`), grava `user://graphics_override.cfg` (override oficial) e `graphics_profile.json`, reinicia no escolhido; recuperação por `render_boot.json`. Perfis por renderizador × qualidade em `RenderFeatures` (usado por ambiente, AA e lanterna). `--render-matrix=` verifica os 9 perfis. Ferramentas (tour/bench) precisam de `--rendering-method forward_plus`, pois o padrão do projeto é Compatibility.
