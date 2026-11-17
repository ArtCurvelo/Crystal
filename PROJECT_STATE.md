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
- **4 regiões** em sequência no eixo −Z: Cavernas Luminosas (z≈0), Profundezas Prismáticas (z≈−58, ◈ 6.000), Núcleo Ancestral (z≈−124, ◈ 400.000) e Reservatório Abissal (bônus, z≈−194, ◈ 1,5 mi).
- **13 materiais** com raridade (Comum → Lendário), incluindo a formação especial Coração Ancestral.
- **7 upgrades**: Potência do Feixe, Ritmo de Extração, Mochila Reforçada, Botas, Lapidação Fina, Ímã de Coleta e Amplificador de Ressonância.
- **6 ferramentas visuais** conforme o nível de Potência: 0, 3, 7, 12, 18 e 24.
- **Ressonância**: 10 golpes rítmicos carregam um pulso em área.
- **Eventos por região**: Eco Luminoso, Maré Gélida, Surto do Núcleo e Maré Luminosa.
- **Marcador de objetivo 3D** e guia de objetivos no HUD.
- **Tela de conclusão** (Mestre Escavador): todas as regiões liberadas + Coração destruído.

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
- Turno de trabalho em `GameState` (`TickDay`, `EndDay`, `StartNewDay`, `CanWork`/`RequireWork`; `DayLengthOverride` para testes). Fora do turno só vender e ir para casa. Única `SellStation` e `MineElevator` no acampamento (`WorldBuilder.SellStation/Elevator`); `HomeScreen` (Menus.cs) aberta pelo elevador via `UiBus.RequestHome`.
- Cada região tem galerias laterais (`RegionDef.Wings`): sala extra a leste/oeste ligada por túnel curto, montada num nó deslocado em X. `WorldBuilder._Process` oculta galerias distantes (sem oclusão no Compatibility).
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
- `./tools/publish_update.sh "notas da versão"`: testes → versão +1 (patch) → exportação → `update.json` com SHA-256 → envio forçado ao ramo `updates` → commit da versão no `main`.
- O jogo (exportado, Windows) lê `https://raw.githubusercontent.com/ArtCurvelo/Crystal/updates/update.json` ao abrir.
- Instaladores manuais (`tools/build_installer.sh` → `dist/`) só para a 1ª instalação ou troca de versão do Godot.
- Versão publicada atual: ver `config/version` em `project.godot`.

## Procedimento de teste
1. `./tools/run_tests.sh` → código 0 e `0 falharam`.
2. `./tools/run_bot.sh 150` → `BOT: SUCESSO`.
3. Tour de capturas (inclui folha das ferramentas e métricas de draw calls) → inspecionar os PNGs.
4. Exportar para Windows e enviar o zip ao diretor; no Windows: F3 a 720p na qualidade Baixa.

## Balanceamento atual (bot: teleporta e mira perfeitamente)
Região 2 em cerca de 8–10 min · Região 3 em cerca de 30–36 min · Reservatório em cerca de 38–44 min · Coração em cerca de 44–50 min · conclusão em cerca de 45 min.
Para um humano, a estimativa é 1,5–2,5× mais lento (cerca de 1,5–2 h).

## Métricas (qualidade Baixa, 720p, software)
Draw calls: R1 ~290, R2 ~240, R3 ~180. Sem vazamento em cerca de 50 min de jogo simulado.

## Problemas pendentes
- **FPS real na HD 4400 ainda não medido** (o diretor recebeu builds; aguardando o resultado).

## Próxima ação recomendada
Polimento final e regressão; aguardar o resultado do teste no Windows e ajustar o desempenho conforme o FPS real.
