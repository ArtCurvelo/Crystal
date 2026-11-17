# PROJECT_STATE — Crystal Excavator

Documento de continuidade. Leia isto primeiro ao retomar o projeto em uma nova conversa.

## Visão geral
Jogo incremental 3D em primeira pessoa: minerar cristais, vender, comprar upgrades e desbloquear regiões.
Godot **4.5.1 .NET**, C# (.NET 8), renderizador **Compatibility**. Todo o conteúdo (malhas, materiais, sons, música) é **gerado proceduralmente em código**, sem assets externos.

## Arquitetura
```
project.godot            Configuração (autoloads, renderizador, janela)
CrystalExcavator.csproj  Projeto C# (Godot.NET.Sdk/4.5.1, net8.0)
CrystalExcavator.sln     Necessário para a exportação .NET
export_presets.cfg       "Windows Desktop" (principal) e "Linux (QA)"
scenes/Main.tscn         Única cena; tudo é montado por código em Main.cs
scripts/
  Core/
    GameState.cs   Autoload. Dados do jogador, economia, upgrades, regiões, eventos, autosave.
    SaveData.cs    Modelo persistente + Sanitize() (corrige dados inválidos).
    SaveSystem.cs  JSON em user://save.json, escrita atômica, backup .bak, migração.
    Settings.cs    Configurações em user://settings.json + Apply().
    InputSetup.cs  Mapa de entradas registrado por código.
    UiBus.cs       Eventos mundo → UI (abrir loja/venda).
    Main.cs        Fluxo Menu/Jogo/Pausa, região atual, culling de regiões.
  Data/GameData.cs Definições: materiais, regiões, upgrades (balanceamento centralizado aqui).
  Player/          PlayerController (FPS, mineração, interação), ToolView (picareta procedural), IInteractable.
  Mining/          Crystal (vida, reações, quebra, renascimento), ShardManager (fragmentos + ímã), Effects (pool de partículas e textos).
  World/           WorldBuilder (mundo procedural), Stations (Venda, Terminal, Portão), EnvironmentController (névoa/ambiente por região), MeshFactory.
  UI/              UiTheme (estilo), Hud, Menus (Loja, Venda, Configurações, Pausa, Confirmação), MainMenu.
  Audio/AudioManager.cs  Autoload. Síntese de efeitos e trilhas em loop.
tests/
  TestRunner.cs    Suíte automatizada (--run-tests).
  ScreenshotTour.cs Tour de capturas e FPS (--shots=<pasta>).
tools/run_tests.sh Compila e roda os testes.
```

### Regras do mundo
- Regiões em sequência no eixo −Z: Cavernas Luminosas (z≈0), Profundezas Prismáticas (z≈−58), Núcleo Ancestral (z≈−124).
- Portões ficam nos túneis entre regiões. A região é detectada pela coordenada Z (`WorldBuilder.RegionAt`).
- Camadas de física: 1 mundo, 2 jogador, 3 (valor 4) cristais, 4 (valor 8) interagíveis.
- Cada região tem estação de venda e terminal de melhorias na entrada.

## Comandos
Ambiente do container (ferramentas fora do repositório):
```bash
source /home/user/tools/env.sh          # define DOTNET_ROOT, PATH e $GODOT
dotnet build                            # compila C#
./tools/run_tests.sh                    # testes automatizados
$GODOT --headless --path . --import     # (re)importa após clonar
$GODOT --headless --path . --export-release "Windows Desktop" build/windows/CrystalExcavator.exe
xvfb-run -a -s "-screen 0 1280x720x24" $GODOT --path . -- --shots=/caminho/saida   # capturas
```
No Windows (máquina do diretor): abrir `project.godot` no Godot 4.5.1 .NET e pressionar F5, ou executar `build/windows/CrystalExcavator.exe`.

Argumentos de usuário (após `--`): `--run-tests`, `--shots=<pasta>`, `--autostart`.

## Procedimento de teste
1. `./tools/run_tests.sh` → deve terminar com `0 falharam`.
2. Tour de capturas → inspecionar os PNGs.
3. Exportar o build Linux de QA e executá-lo com `--shots` para validar o pipeline de exportação.
4. No Windows: executar o `.exe`, ativar FPS (F3) e testar na qualidade Baixa a 720p.

## Balanceamento atual (simulação de jogador eficiente)
- Região 2 (◈ 4.000): cerca de 9 min · Região 3 (◈ 150.000): cerca de 31 min.
- Quartzo: 3,0 s no início, cerca de 0,03 s com upgrades no nível 12.

## Problemas pendentes
- FPS real na HD 4400 ainda não medido (só renderização por software aqui).
- O modelo da picareta e os números de dano sobrepostos precisam de polimento.
- O ícone do `.exe` não é aplicado (exigiria rcedit; `application/modify_resources=false`).

## Próxima ação recomendada
Polimento visual (Sessão 4) e validação do `.exe` no Windows pelo diretor.
