# Sistema inteligente de renderização

O jogador instala, atualiza e joga: o Crystal Excavator escolhe sozinho, nos bastidores, o
renderizador da Godot que funciona melhor naquele computador. Não existe opção de renderizador
no menu — só as opções convencionais (qualidade, resolução, escala 3D, limite de FPS, VSync etc.).

## Ambiente analisado

| Item | Situação encontrada |
|---|---|
| Motor | Godot 4.5.1 .NET (C#) |
| Renderizador antes | Forward+ fixo, com queda automática para OpenGL (`fallback_to_opengl3`) |
| Launcher | Não existe um executável separado: o atalho e o instalador abrem `CrystalExcavator.exe` |
| Atualizador | Dentro do jogo (`Core/UpdateManager.cs`): baixa `.pck` + DLL do C# (lista fechada), um `.bat` aplica e reabre o jogo |
| Configurações | `Core/Settings.cs` → `user://settings.json` |

**Decisão de arquitetura:** o atualizador de versões antigas (ex.: 0.2.1) só aceita `.pck` e a DLL;
um executável de launcher novo seria recusado por essas instalações e elas nunca mais atualizariam.
Por isso o "launcher" é o próprio jogo, numa etapa que roda **antes de montar o mundo**, e toda a
lógica chega pelo `.pck`/DLL — qualquer instalação antiga recebe o sistema pela atualização normal.

## Incidente da v0.11.0 e correções (v0.11.1)

Na v0.11.0, um computador mostrou a caixa da Godot "Your video card drivers seem not to support the
required OpenGL 3.3 version" e o jogo não abriu (a 0.2.1 abria). Essa caixa vem do **motor**, antes de
qualquer código do jogo. Pelo código-fonte da Godot 4.5.1 (`platform/windows/display_server_windows.cpp`):
no Compatibility o Windows tenta o OpenGL nativo e só usa o ANGLE (OpenGL sobre DirectX 11) se o driver
*declarar* menos que 3.3; se declarar 3.3 mas falhar ao criar o contexto, desiste com essa caixa. Na v0.11.0
os testes também abriam janelas totalmente fora das telas, o que pode impedir o driver OpenGL de criar o
contexto. Correções:

- **Compatibility no Windows usa ANGLE (DirectX 11) primeiro** (`rendering/gl_compatibility/driver.windows =
  "opengl3_angle"`), com volta para o OpenGL nativo se o ANGLE falhar (`fallback_to_native`). Nunca fica pior
  que a 0.2.1: o ANGLE está embutido no executável (conferido no binário exportado).
- **Antes de testar Forward+/Mobile**, um processo **sem janela** (`--headless -- --gpu-check=`) consulta se há
  Vulkan com GPU real (via `vulkan-1.dll`) ou Direct3D 12 (`d3d12.dll`). Sem nenhum, esses testes nem são abertos
  (nenhuma caixa de erro em máquinas antigas). Se a própria consulta falhar, só o Compatibility é usado.
- A janela de teste fica **no canto da tela principal** (1 pixel visível, atrás da barra de tarefas), e não
  fora de todas as telas.
- Teste que não chega a iniciar o jogo (driver parado numa caixa de erro) é encerrado em **20 s** (aviso
  `.alive` gravado assim que o C# começa).
- Override renomeado para `graphics_override_v2.cfg` e regras na versão 2: o arquivo antigo da v0.11.0 é
  ignorado e todos reavaliam.
- A publicação passou a **regerar os instaladores** em `dist/` (estavam parados na 0.2.1; quem reinstalava
  recebia a 0.2.1).

## Como funciona

1. **Sem avaliação**, o jogo abre no **Compatibility** (OpenGL), o renderizador que abre em praticamente
   qualquer máquina (padrão em `project.godot`).
2. `RenderBootstrap` (antes do mundo) confere o perfil salvo. Se não existe ou não vale mais, mostra só
   a tela de carregamento do jogo ("Preparando o jogo para o seu computador…") e testa os renderizadores.
3. Cada teste roda **num processo separado**, com contexto gráfico real, numa janela **fora da área
   visível** (à direita de todas as telas), sem som, sem tocar no save nem nas configurações.
   O teste (`RenderProbe`) usa a cena real: mineração com partículas, luz do feixe e minérios, e
   deslocamento pelo caminho principal. Mede FPS médio, tempo médio e **percentil 99** por quadro,
   CPU e GPU, conta erros do motor (shader/driver = críticos) e confere se a imagem tem conteúdo.
   Se não bater a meta na Média, mede também na Baixa.
4. Ordem: **Forward+ → Mobile → Compatibility**, parando no primeiro aprovado (sem espera artificial).
5. A escolha é gravada no arquivo oficial de override da Godot
   (`application/config/project_settings_override = "user://graphics_override_v2.cfg"`), lido pelo motor
   ao iniciar. Se o escolhido não é o que está aberto, o jogo **reinicia uma vez** já nele
   (os argumentos, como `--continue` do atualizador, são mantidos).
6. Inicializações seguintes leem o perfil e abrem direto no renderizador certo, sem teste.

### Classificação e escolha (`RendererSelector`)

- **APROVADO**: iniciou o renderizador pedido (se a Godot caiu para outro, não conta), desenhou a cena,
  sem erros críticos, média ≥ 55 FPS e p99 ≤ 33 ms na qualidade Média.
- **LIMITADO**: funciona, mas não segura a meta.
- **INCOMPATÍVEL**: não iniciou, caiu para outro renderizador, imagem vazia, erro crítico, travou ou
  passou do tempo limite (50 s por teste).

Escolha: o primeiro aprovado na ordem de preferência (Alta se passar de 100 FPS); sem aprovado na
Média, o primeiro que segura a meta na Baixa; nenhum na meta → o que funcionou mais rápido, em
**perfil de recuperação** (Baixa + resolução dinâmica a partir de 75%) — a máquina não é tratada como
incompatível; nada funcionou → Compatibility no modo seguro. Exemplo do pedido: Forward+ 35 FPS e
Mobile 60 FPS → Mobile.

### Quando reavalia

Driver de vídeo, processador, núcleos, RAM, sistema ou resolução da tela mudaram; versão do motor
mudou; ou `RendererSelector.EvalVersion` foi aumentada (requisitos do jogo mudaram). Atualizar só o
jogo não refaz o teste.

### Recuperação de falhas

- Cada inicialização marca "pendente" e só é confirmada depois de 3 s no menu.
- Duas inicializações seguidas sem chegar ao menu no mesmo renderizador → ele é marcado como falho
  nesta máquina e o sistema **reavalia sem ele** (só troca para algo validado). Compatibility é o fim
  da linha: se até ele falhar, segue nele sem reiniciar (não há ciclo).
- Teste que morre, trava ou passa do tempo é encerrado e vira INCOMPATÍVEL; o processo de teste também
  se encerra sozinho se o jogo que o abriu fechar, e força a saída se o driver demorar a fechar.
- Erro inesperado no próprio bootstrap: registra e segue no renderizador atual (nunca impede o jogo de abrir).
- Saves nunca são alterados pelo sistema gráfico.

## Perfis gráficos (`RenderFeatures`)

Tabela única para os 9 perfis (3 renderizadores × Baixa/Média/Alta), usada pelo ambiente, pela
suavização de bordas e pela lanterna:

| | Baixa | Média | Alta |
|---|---|---|---|
| Forward+ | brilho, SMAA | + SSAO, SSR | + SSIL, névoa volumétrica, sombra da lanterna |
| Mobile | brilho, SMAA, luz ambiente compensada | idem | + sombra da lanterna |
| Compatibility | brilho, luz ambiente compensada | + MSAA 2x (no lugar do SMAA) | + sombra da lanterna |

O jogador pode escolher Alta mesmo no Compatibility: o renderizador não muda, só liga o que ele tem.
A qualidade sugerida pela avaliação só é aplicada se o jogador ainda não escolheu a qualidade no menu
(`SettingsData.QualityManual`); escolhas manuais nunca são revertidas.

## Arquivos (pasta do usuário, `%APPDATA%\CrystalExcavator`)

| Arquivo | Conteúdo |
|---|---|
| `graphics_override_v2.cfg` | renderizador escolhido (lido pela Godot ao iniciar) |
| `graphics_profile.json` | perfil: renderizador, qualidade inicial, impressão do hardware, driver, versão do motor e da avaliação, resultado resumido de cada teste, renderizadores que falharam |
| `render_boot.json` | controle de inicializações (recuperação) |
| `logs/render.log` | registro interno (até 200 KB) |

Nada disso é enviado para fora.

## Testes executados (neste ambiente: Linux, sem GPU, Mesa lavapipe/llvmpipe)

- Os três renderizadores iniciam por override e por linha de comando (Forward+/Vulkan, Mobile/Vulkan, Compatibility/OpenGL).
- 9 perfis com contexto gráfico real (`--render-matrix`): imagem com conteúdo, perfil aplicado, 0 erros de shader/driver.
- Ponta a ponta com display virtual:
  1. primeira inicialização → teste → escolha Forward+ → reinício nele → menu ✓ (≈ 29 s);
  2. inicialização seguinte direto no Forward+, sem teste ✓;
  3. Forward+ caindo antes do menu 2× → marcado como falho → reavaliação → Mobile → menu ✓;
  4. máquina sem Vulkan → Forward+/Mobile descartados em ~10 s cada, Compatibility medido e escolhido ✓ (45 s);
  5. teste que morre e teste que trava → registrados e encerrados, jogo segue ✓;
  6. instalação existente (save, backups e configurações antigas) → save intacto (mesmo SHA-256), backups mantidos, configurações do jogador mantidas ✓.
- 55 testes automáticos da lógica (classificação, ordem, escolha, validade, migração, escolha manual).

## O que NÃO foi possível validar aqui

- **Desempenho real** nas máquinas de referência (RTX 3050, i5 11ª geração integrada, ThinkPad T440):
  este ambiente não tem GPU (tudo roda na CPU a ~3 FPS), então nenhum número de FPS daqui vale.
  As regras foram calibradas pelos números informados; os resultados reais ficam em `logs/render.log`.
- **Windows**: a janela fora da tela, o ícone na barra de tarefas durante o teste e o fechamento dos
  processos foram testados no Linux. No Windows a janela de teste pode aparecer brevemente na barra de tarefas.
- **Queda nativa do driver antes do C# iniciar** (o processo morre antes de qualquer código do jogo):
  os testes em processos separados evitam escolher um renderizador assim, e a Godot já cai sozinha
  para OpenGL quando o Vulkan não inicializa; mas se um driver passar a travar *depois* da escolha e
  antes do C#, só uma reinstalação/atualização de driver resolve (o arquivo `graphics_override_v2.cfg`
  pode ser apagado para voltar ao Compatibility).
- Fluxo real do `.bat` do atualizador no Windows (a parte do jogo após reabrir foi testada).

## Comandos úteis

- Ver a matriz de perfis: `godot --path . --rendering-method mobile -- --render-matrix=pasta`
- Forçar a seleção fora do jogo exportado: `godot --path . -- --render-select`
- Simulações de falha para testes: `--x-probe-nostart=<r>`, `--x-probe-start-timeout=<s>`, `--x-gpu-allow-cpu`, `--x-probe-crash=<r>`, `--x-probe-hang=<r>`, `--x-probe-black=<r>`,
  `--x-probe-slow=<r>`, `--x-probe-fast=<r>`, `--x-boot-crash=<r>`, `--x-probe-timeout=<s>`
- Benchmark: `RENDER=mobile ./tools/run_bench.sh 1`
