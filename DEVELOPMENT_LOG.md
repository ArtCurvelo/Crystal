# DEVELOPMENT_LOG — Crystal Excavator

> Registro de progresso por sessão. Horas são **informadas pelo diretor criativo**; nunca estimadas pelo desenvolvedor.

---

## Sessão 1 — Protótipo jogável (meta: horas 0–5)

- **Tempo consumido (informado):** 29 min (00:51 → 01:20)
- **Tempo restante (informado):** ~29h31min

### Ambiente
- Desenvolvimento em container Linux na nuvem (Ubuntu 24.04), não na máquina Windows do diretor.
- Godot **4.5.1 stable .NET** em `/home/user/tools/godot` (modo autocontido `._sc_`, modelos de exportação incluídos).
- .NET SDK **8.0.134** em `/home/user/tools/dotnet` (pacotes oficiais do Ubuntu extraídos localmente; o host dot.net está bloqueado pela rede).
- Renderizador **Compatibility (OpenGL 3.3)**.
- VS Code não está disponível no container. O projeto inclui `.sln` e `.csproj` padrão para abrir no VS Code do diretor.

### Concluído (e testado)
Além do escopo da Sessão 1, o loop completo já funciona:
- Projeto Godot e C# configurados, com arquitetura modular (`Core`, `Data`, `Player`, `Mining`, `World`, `UI`, `Audio`).
- Mundo procedural com 3 regiões, túneis, portões de desbloqueio, estações de venda e terminais de melhoria.
- Movimentação em primeira pessoa: WASD, mouse, pulo, corrida e colisões.
- Mineração: resistência por cristal, dano da ferramenta, cadência, anel de progresso, tremor, flash, partículas, som, quebra, fragmentos atraídos por ímã e renascimento controlado.
- Economia: 10 materiais (3 raros e 1 especial), inventário com capacidade, venda e moeda.
- 6 upgrades com custo exponencial que alteram o jogo de verdade: dano, cadência, mochila, velocidade, valor de venda e alcance/ímã.
- Ferramenta com 5 tiers visuais conforme o dano.
- Salvamento: automático (a cada 45 s e após vendas e desbloqueios), manual (F5 ou menu de pausa), backup, escrita atômica, saneamento de dados inválidos e migração de versão.
- UI: menu principal sobre cena 3D, HUD, loja, venda, pausa, configurações, confirmações, notificações, banner de região e guia de objetivos.
- Áudio 100% sintetizado (efeitos e trilha ambiente por região).
- Configurações: sensibilidade, FOV, volumes, qualidade (baixa/média/alta), escala 3D, limite de FPS, VSync, tela cheia, inverter Y e FPS na tela.
- Exportação para Windows funcionando (`.exe`, `.pck` e assemblies .NET).

### Testes realizados
- `tools/run_tests.sh`: **100 verificações, 0 falhas**. Cobre economia, upgrades, regiões, save/load, save corrompido, dados inválidos, compatibilidade de versões, integração 3D (mirar, minerar, coletar, renascer, mochila cheia, entrada simulada) e simulação de balanceamento.
- Tour de capturas de tela (`--shots=`) sob Xvfb: menu, gameplay, mineração, loja, venda, portão, as 3 regiões, Coração Ancestral e pausa, sem erros de script.
- Build Linux exportado executado com o tour completo, o que valida o pipeline de exportação. O `.exe` do Windows **não pôde ser executado aqui**.

### Bugs e pendências conhecidos
- O FPS medido aqui (16–21) é renderização por **software** (llvmpipe) e não representa a GPU real. O FPS na HD 4400 precisa ser medido pelo diretor no Windows.
- ~~Picareta escura e pouco legível~~ corrigido (materiais metálicos ficavam pretos sem reflexos no Compatibility).
- ~~Números de dano sobrepostos~~ corrigido (deslocamento aleatório).
- ~~Rótulos do portão espelhados através da barreira~~ corrigido.
- O ícone do `.exe` não é aplicado (exigiria rcedit e Wine).
- ~~Repositório remoto não configurado~~ resolvido: GitHub ArtCurvelo/Crystal.

### Próxima tarefa prioritária
Polimento visual (ferramenta, números de dano, iluminação da região 3), revisão da experiência inicial e o diretor validar o `.exe` no Windows.

---

## Sessão 1 (continuação) — profundidade e QA
- **Tempo:** sessão iniciada às 00:51. Em 01:20, 29 min consumidos, ~29h31min restantes (informado). Próxima atualização quando o diretor informar.

### Concluído (e testado)
- **Mecânica original de Ressonância:** golpes rítmicos carregam um medidor (arco violeta na mira). Cheio, o próximo golpe libera um pulso que causa dano em área a todos os cristais no raio, com onda de choque, som grave e tremor de câmera. Novo upgrade: Amplificador de Ressonância.
- **Coleção de materiais** (na pausa): descobertos com detalhes, desconhecidos como "???".
- **Autodetecção de GPU** na primeira execução: Intel HD/UHD ou renderização por software usam qualidade Baixa e escala 3D de 85%. A qualidade Baixa usa 2 luzes por região em vez de 4–5 (FPS em software: 16–21 → 29–36).
- **Correção:** portões não voltavam a fechar ao iniciar novo jogo depois de desbloquear (teste adicionado).
- **Correção:** aviso de mochila cheia repetido.
- **Bot de QA** (`tools/run_bot.sh`): joga a partida real usando as mesmas entradas do jogador. Mira, minera segurando o botão, usa as estações com E, aperta os botões de venda e de loja, abre portões e os atravessa andando, quebra o Coração e confere save/load ao final. Resultado: **SUCESSO, 0 erros**.
- **Rebalanceamento** com base no bot (antes tudo era maximizado em 24 min; agora cerca de 51 min no bot e cerca de 1,5–2 h estimadas para um humano).

### Testes
- `run_tests.sh`: 108 verificações, 0 falhas.
- `run_bot.sh`: SUCESSO (Região 2 em 9,5 min, Região 3 em 36 min, Coração em 46 min de jogo).
- Feedback de compra (destaque no card), venda (explosão dourada e texto flutuante no mundo), desbloqueio e evolução da ferramenta (banner central).
- Teste de caminhada real do acampamento ao Núcleo no bot: atravessa túneis e portões sem travar.
- Saves e configurações em pasta própria (`%APPDATA%\CrystalExcavator`). Execuções headless não gravam configurações.
- README com controles, instruções de execução e desenvolvimento.
- Build Windows exportada (com `.console.exe` para diagnóstico) e enviada ao diretor como zip (69 MB).

### Próxima tarefa prioritária
1. Receber o resultado do diretor rodando o `.exe` no Windows (abre? FPS com F3 na qualidade Baixa a 720p?).
2. ~~Configurar o repositório remoto~~ feito: https://github.com/ArtCurvelo/Crystal (`main`).
3. Continuar o polimento (Sessões 4–5): condições ambientais com impacto no jogo, refinamento de áudio e animações.

---

## Sessão 2 — condições ambientais e qualidade
- **Tempo (informado):** 01:20 → 02:36 (+1h16). Total consumido: **1h45min**. Restante: **~28h15min**.
- **Repositório:** https://github.com/ArtCurvelo/Crystal, com clone do diretor em `C:\Users\silvi\Documents\Crystal`.

### Concluído (e testado)
- **Eventos ambientais por região** (`World/RegionEvents.cs`), com efeito real no jogo e painel com contagem no topo do HUD:
  - Cavernas Luminosas, **Eco Luminoso**: todos os cristais quebrados da caverna renascem de uma vez.
  - Profundezas Prismáticas, **Maré Gélida** (22 s): névoa gelada densa e fragmentos x2.
  - Núcleo Ancestral, **Surto do Núcleo** (18 s): ambiente incandescente e dano +50%.
  - O primeiro evento ocorre aos 45 s de jogo; os seguintes a cada 70–110 s. O evento termina se o jogador sair da região.
- Teste de integração dos eventos (multiplicadores ligam e desligam; renascimento).

### Testes
- `run_tests.sh`: 115 verificações, 0 falhas.
- `run_bot.sh`: SUCESSO (Região 2 em 10,4 min, Região 3 em 36,5 min, Coração em 41,9 min; caminhada completa sem travar).
- Tour de capturas: sem erros. Maré Gélida verificada visualmente (névoa reduzida após a primeira captura ficar opaca demais).
- **Teste de áudio:** pico de cada um dos 17 sons sintetizados. Detectou saturação no som do pulso de ressonância (pico 1,00, com distorção); corrigido para 0,71.
- **Métricas de renderização** (tour, 720p):
  | Qualidade | Draw calls | Primitivas | FPS em software* |
  |---|---|---|---|
  | Baixa | 201–271 | 14–21 mil | 30–37 |
  | Alta | 194–389 | 18–35 mil | 16–21 |

  \*llvmpipe na CPU; não representa a GPU real. Os números de draw calls e primitivas são leves para uma Intel HD 4400.
- `run_tests.sh`: 150 verificações, 0 falhas.
- Tela **Como jogar** (no menu principal e na pausa) e resumo do save no botão Continuar (moeda e tempo de jogo).

### Direção de arte (a partir das imagens conceituais do diretor)
- **Escavadeira de feixe** substitui a picareta: modelo procedural laranja/cinza com tela ciano e bobinas, feixe de energia contínuo até o ponto atingido (com a cor do cristal), recuo e vibração. Tiers mudam cores e número de bobinas.
- **HUD sci-fi**: painéis chanfrados com borda ciano, etiqueta do título, etiqueta da região (nome + "Região X de 3"), Créditos com moeda e Capacidade com mochila (ícones desenhados por código), **barra de fragmentos** com gemas e contagem, painel de **Ressonância** (energia), mira de scanner tracejada e **painel do alvo com cantoneiras** (nome, raridade, % extraída, rendimento estimado).
- **Shader de cristal** (`shaders/crystal.gdshader`): facetas com brilho interno, borda fresnel e **rachaduras luminosas** (Voronoi) que se espalham conforme o dano.
- **Cenografia** (`World/Decor.cs`): posto avançado com janelas acesas e antena, cachoeiras animadas (shader próprio) com poça e névoa, cogumelos luminosos, samambaias, ruínas com pilares rúnicos, anel rúnico giratório e cristais flutuantes.
- **Paletas**: Cavernas com rocha quente e luzes alaranjadas; Núcleo em violeta/dourado.
- Correção técnica: `Basis.Scaled()` escala nos eixos globais; criado `ScaleLocal()` para transformações corretas.
- Custo (qualidade Baixa, 720p): 298–344 draw calls, 16–26 mil primitivas (antes 200–270).
- Testes: 150/150. Bot: SUCESSO (Região 2 em 7,3 min, Região 3 em 28,8 min, Coração em 34,5 min).
- **Terminal no estilo do conceito**: abas Melhorias/Vender, painel de inventário com gemas e valores, botão de venda rápida, linhas de upgrade com barra segmentada, "Nv X ▶ Nv Y" e preços em destaque dourado. A troca de abas está coberta pelo bot.
- **Tema global chanfrado** (menus, pausa, configurações) com paleta azul-marinho e ciano.
- **Formações gigantes de cristal** decorativas (MultiMesh com o shader de cristal) nas três regiões; Profundezas mais claras.
- Testes: 150/150. Bot: SUCESSO.
- **Terminal 3D** remodelado no estilo do conceito: console com tela inclinada (interface em barras luminosas), cristais em pedestais, tablet e acabamento laranja.

### Segunda leva de conceitos (estruturas, recursos, ferramentas, regiões)
- **Raridades** (Comum/Incomum/Raro/Épico/Lendário) com cores e selos no painel do alvo, na Coleção, na venda e nas notificações de descoberta.
- **Depósitos**: base de duas rochas mescladas + lascas de cristal ao redor.
- **Otimização**: todos os prismas de um cristal mesclados em uma única malha (UV codifica ângulo/altura para o shader). Draw calls na qualidade Baixa: R1 ~340 → 246, R2 ~308 → 245, R3 ~300 → 210.
- **6 ferramentas** com modelos distintos (Broca Inicial, Cortador de Pulso, Perfurador de Arco, Ressonador Cristalino, Lança Prismática, Quebra-Núcleo), trocando por nível de Potência (0/3/7/12/18/24). Tour gera folha comparativa `tools_sheet.png`.
- **Estruturas** (folha de conceito): estação de venda remodelada (balcão, holograma cristal → moedas, cristais em pedestais, estandarte laranja com picareta, caixotes); **farol de passagem** nos portões (plataforma com luzes, cristal holográfico girando, feixe de luz ao desbloquear); caixotes, **cápsula de refino**, **scanner de minério** com varredura animada e grades de piso. Posto avançado e refinaria também nas Profundezas.
- Pendência de otimização: R1 subiu para ~364 draw calls (Baixa). Planejado: agrupar malhas estáticas por material em cada região.
- **Agrupamento estático** (`World/StaticBatcher.cs`): na carga, mescla malhas estáticas de mesmo material por região e dentro das estações (143+ instâncias a menos). Draw calls na qualidade Baixa: R1 364 → 289, R2 302 → 237, R3 209 → 181. Verificado visualmente (sem diferenças).
- **Região bônus: Reservatório Abissal** (◈ 1,5 mi, após o Núcleo). Lago luminoso com shader próprio (ondulações e cáusticas), águas-vivas animadas, três cachoeiras, passarela iluminada, posto avançado, bolhas subindo e formações gigantes. Materiais: Cristal de Maré (Raro), Coral Prismático (Épico) e Lágrima Abissal (Lendário). Evento **Maré Luminosa**: fragmentos x1,5 e dano +25%. Trilha sonora própria.
- **Passarelas suspensas** com lanternas nas Profundezas (conceito "Cavern Bridge").
- Testes ajustados para 4 regiões: 156/156.
- Bot estendido: desbloqueia as **4 regiões** e caminha do acampamento até o Reservatório contornando o pedestal do Coração. Resultado: SUCESSO (Região 2 em 10,2 min, Região 3 em 32 min, Reservatório em 39,8 min, Coração em 45,7 min; caminhada de 18,9 s sem travar).
- **Correção de encerramento:** a suíte de testes passava, mas o processo abortava ao sair (código 134, "Leaked unsafe reference" seguido de FATAL em `csharp_script.cpp`), porque os caches estáticos ainda referenciavam recursos depois de vários mundos criados e destruídos. O jogo normal já saía limpo (código 0). Criado `GameState.QuitClean()`: limpa os caches, roda o GC e aguarda 2 quadros antes de sair. Usado no menu Sair, nos testes e no bot. Agora testes e bot retornam código 0, e os commits só são feitos com código de saída 0.
- **Teste de estabilidade no bot** (~50 min de jogo simulado): sem nós órfãos; nós 1.626 → 1.763 (notificações e textos flutuantes ativos); após o GC, objetos 5,9 mil → 5,4 mil e memória 92 → 77 MB. **Sem vazamento.** O bot falha se houver crescimento anormal.
- **Menos alocações por quadro no HUD:** estilos das barras e dos slots agora vêm de um cache (antes criavam StyleBox a cada quadro, gerando pausas de GC em CPUs fracas).
- **Resoluções testadas** (tour completo): 1366×768 (típica do notebook de referência), 1024×768 (4:3) e 1920×1080, sem sobreposições nem cortes. A interface escala proporcionalmente.
- **Configurações:** nova opção **Resolução da janela** (1280×720 recomendada para PCs modestos, 1366×768, 1600×900, 1920×1080, limitada ao tamanho da tela); layout compacto com rolagem; interruptores sem moldura; painéis modais opacos.
- **Marcador de objetivo 3D** (`World/ObjectiveMarker.cs`): seta dourada flutuante com distância, visível através das paredes e escalada pela distância. Aponta para o cristal mais próximo, depois para a venda (mochila cheia), o terminal (primeira compra), o portão (quando há dinheiro) e o Coração (quando viável). Teste de integração com as 4 etapas (161 verificações).

---

## Controle de tempo
- **02:36 → 04:12** (+1h36, informado). **Total consumido: 3h21min. Restante: ~26h39min.**
- Fases do cronograma já cobertas: protótipo, loop econômico, progressão completa (4 regiões), save/load, apresentação (direção de arte dos conceitos), boa parte da qualidade (testes, bot, estabilidade, otimização).
- Prioridades para o tempo restante: balanceamento fino, polimento, regressão, validação no Windows real (pendente com o diretor), documentação final e candidato 1.0.
- **Conclusão da jornada:** com as 4 regiões liberadas e o Coração Ancestral destruído, aparece uma única vez a tela **Mestre Escavador** com estatísticas (tempo, cristais, fragmentos, ganhos, materiais e melhorias no máximo). Persistida no save (`GameCompleted`). O jogo continua depois. O bot exige a tela de conclusão para passar (concluiu aos 45 min de jogo). Testes: 166/166.
- **Ícone do jogo** (cristal facetado na paleta do jogo): `icon_512.png` (janela) e `icon.ico` (6 tamanhos) embutido no `.exe` pelo próprio Godot (`modify_resources=true`, sem rcedit). Verificado: as 6 imagens estão presentes no executável.
- **Tempo de carga medido:** mundo construído em ~235 ms (92 cristais; 369 malhas mescladas).
- **Pré-aquecimento de shaders:** tela "Carregando cavernas…" por 5 quadros enquanto a câmera enxerga as 4 regiões, para o driver compilar os shaders antes do jogo (evita travadas na 1ª entrada em cada região no renderizador Compatibility).
- **Correção:** com save existente, o menu tinha 5 botões e o "Sair" era cortado na base da tela a 720p; layout compactado.
- **Suporte a controle (gamepad, layout Xbox/XInput):** analógico esquerdo move, analógico direito olha (curva quadrática e sensibilidade da configuração), RT/RB minera, X interage, A pula, LB/L3 corre, Start pausa, B volta nos menus; D-pad e analógico navegam nas telas, que focam o primeiro botão ao abrir. As dicas do HUD e das estações trocam entre teclado e controle conforme o último dispositivo usado (`InputHints`). Tela Como jogar e README atualizados.
- Teste de integração com eventos de controle simulados (girar câmera, minerar com RT, mover, troca de dicas): 178/178.
- **Instalador Windows** (`installer/CrystalExcavator.nsi`, NSIS 3.09 extraído em `/home/user/tools/nsis`; gerado por `tools/build_installer.sh`): `build/CrystalExcavator-Setup.exe` (53 MB, LZMA). Interface em pt-BR, instala em `%LOCALAPPDATA%\Programs\CrystalExcavator` sem exigir administrador, cria atalhos no Menu Iniciar e na Área de Trabalho, registra em "Adicionar ou remover programas" com desinstalador (preserva os saves) e oferece "Jogar agora" ao final. Validado como executável PE Nullsoft; a execução em Windows real depende do diretor.
- **Instalador versionado em `dist/`** (com `LEIA-ME.txt`), regenerado a partir do commit atual; `tools/build_installer.sh` copia automaticamente para `dist/`. A pasta `build/` e o cache `.godot/` continuam fora do Git.

---

## Controle de tempo
- **04:12 → 05:17** (+1h05, informado). **Total consumido: 4h26min. Restante: ~25h34min.**
- Entregue neste período: tela de conclusão, ícone embutido no `.exe`, pré-aquecimento de shaders com tela de carregamento, correção do menu a 720p, suporte a controle, instalador Windows (NSIS) versionado em `dist/`.
- **Desenvolvimento pausado** pelo diretor. Retomar somente após a instrução "Pode continuar".
- Pendente com o diretor: resultado de FPS no Windows real (720p, qualidade Baixa) e o conteúdo de `git diff project.godot` no clone local (o Godot alterou o arquivo ao abrir).

## Retomada ("Pode continuar")
- `project.godot` regravado pelo próprio Godot (`ProjectSettings.save()`) no formato oficial: cabeçalho, ordem das chaves e remoção de valores iguais ao padrão. Isso evita que o editor do diretor altere o arquivo ao abrir/salvar, o que causou o conflito no `git pull`.
- **Renascimento (prestígio):** após concluir a jornada, recomeça a partida (dinheiro, melhorias, mochila e regiões) e concede **Essência Ancestral** = ⌊√(ganhos da partida / 250 mil)⌋. Cada ponto dá **+10% permanente em dano e em valor de venda**. Mantém Coleção, estatísticas e Essência. Disponível no Terminal (seção Renascimento) e na tela de conclusão, sempre com confirmação. Saves antigos migram usando o total ganho. HUD mostra ✦Essência ao lado da ferramenta.
- Bot renasce **pela interface** (Terminal → Renascer → Confirmar) e joga a 2ª jornada: +6 Essência (x1,6) → **Região 2 em 3,6 min** (1ª jornada: 10,3 min). Testes: 192/192.

## Desempenho real (diretor): 13–15 FPS no Windows
- **Benchmark** novo (`tests/Benchmark.cs`, `tools/run_bench.sh`): 5 vistas fixas (R1 acampamento/mineração, R2, R3, R4), média de 3 s cada, com experimentos isolados por argumento.
- **Medição por fator** (renderização por software, qualidade Baixa, 720p; linha de base 19,6 FPS):
  | Experimento | FPS | Ganho |
  |---|---|---|
  | sem luzes dinâmicas | 28,3 | +44% |
  | escala 3D 60% | 26,4 | +35% |
  | shader simples nos cristais | 18,9 | 0 |
  | sem névoa | 19,9 | ~2% |
  | sem partículas | 19,5 | 0 |
  | sem luzes + escala 75% | 30,9 | +58% |
  | sem luzes + escala 60% | 35,1 | +79% |
- Causa: no Compatibility, cada OmniLight redesenha os objetos que alcança, e as malhas de parede mescladas recebem todas as luzes da região. Scripts e física medidos com Stopwatch: <0,25 ms por passo (não são o gargalo).
- **Aplicado:** qualidade Baixa **sem luzes dinâmicas** (luz ambiente x1,9 compensa; emissivos mantêm o brilho), escala 3D padrão 75% na autodetecção, e **resolução dinâmica** (nova opção, ligada por padrão): abaixo de ~28 FPS reduz a escala 3D em passos de 5% até 50%; acima de ~44 FPS volta ao valor configurado. O FPS na tela (F3) mostra a escala 3D atual.
- Resultado no benchmark (Baixa, escala fixa 75%): **19,6 → 29,8 FPS (+52%)**, antes da resolução dinâmica.
- **Atualizador** (`installer/CrystalExcavator-Update.nsi` → `dist/CrystalExcavator-Atualizacao.exe`, ~3,9 MB, contra 53 MB do Setup): localiza a instalação pelo registro, pede para fechar o jogo se estiver aberto e substitui só `.pck` + `CrystalExcavator.dll/.deps.json/.runtimeconfig.json`. O motor e o runtime .NET (~174 MB descompactados) não mudam entre versões do jogo. Versão do projeto passa a 0.2.0 (exibida no menu).
- **Atualização automática:** `Core/UpdateManager.cs` consulta `update.json` no ramo `updates` (raw.githubusercontent.com) ao abrir o jogo. Se a versão for mais nova, a tela "Nova versão disponível" oferece Atualizar/Depois. Baixa só os arquivos do jogo, confere o SHA-256, grava um `.bat` que espera o jogo fechar, copia os arquivos e reabre o jogo. Segurança: lista fechada de caminhos permitidos, só HTTPS, hash obrigatório. Se o motor mudar, pede o instalador completo. Opção "Procurar atualizações ao iniciar" nas Configurações. Só atua no executável Windows exportado.
- **Publicação:** `tools/publish_update.sh "notas"` roda os testes (aborta se falharem), sobe a versão (patch), exporta, gera o manifesto e força o envio do pacote ao ramo `updates` (só a versão mais recente; não infla o histórico do `main`).

## Feedback do diretor (após v0.2.2)
- Atualização automática funcionou (detecção um pouco lenta).
- **Bug: preset travado em 3D 50% / Baixa.** Causas: a resolução dinâmica ligada sobrepunha a escala escolhida; luzes, densidade e partículas eram decididas só na montagem do mundo. **Correções:** presets aplicam escala e resolução dinâmica juntos (Média/Alta → 3D 100%, dinâmica desligada; Baixa → 75%, ligada); mexer no 3D desliga a dinâmica e vale na hora; o mundo é sempre montado completo e a qualidade é aplicada em tempo real (luzes, partículas ambiente, fração da decoração via `VisibleInstanceCount`, distância de visão, brilho). Benchmark Baixa sem piora (variação de ±2 FPS entre execuções); Alta aplicada em tempo real (10,8 FPS no software).
- **Bug grave: save perdido na atualização.** O atualizador chamava `SaveGame` no menu, onde os dados em memória ainda eram um placeholder vazio, e sobrescrevia o save do jogador. **Correções:** (1) o atualizador só salva com partida em andamento; (2) trava geral `SessionActive`: nenhum save é gravado antes de NewGame/LoadGame; (3) backups com histórico em `user://backups` (10 mais recentes, sem duplicatas nem saves vazios), criados ao iniciar a partida, antes de atualizar, antes de restaurar e antes de um novo jogo; (4) "Novo jogo" arquiva em vez de apagar; (5) tela **Restaurar progresso** no menu. Teste de regressão reproduz o bug.
- Indicador no menu: "Procurando atualizações…" / "✓ Jogo atualizado" / "Nova versão disponível".
- Configurações compactas: todas as opções visíveis sem rolar.
- O diretor estendeu o prazo de 30 h; o desafio termina quando ele avisar.
- Testes: 214/214. Bot: SUCESSO.

## Nova direção (diretor): dia/casa/árvore de habilidades/esteiras/mapa maior/minérios únicos
Decisões do diretor:
- **Fim do dia:** o jogador só pode vender e ir para casa.
- **Moeda:** o mesmo dinheiro para tudo.
- **Construções:** a meu critério, com mais ideias além das esteiras.
- **Ordem:** minérios e mapa primeiro.

### Etapa 1: minérios com identidade própria
- `shaders/ore.gdshader`: 8 padrões procedurais (rocha, veios brilhantes, faixas, pepitas metálicas, iridescente, magma, gelo fosco, vidro negro) + rachaduras de dano + autoiluminação leve (legível sem luzes).
- `Mining/OreVisuals.cs`: 9 formatos (Cristal, Veio metálico, Geodo de cristais/esferas, Rocha em faixas, Lascas, Gelo, Magma, Coral ramificado, Pérolas), 1–2 malhas mescladas por depósito.
- **22 minérios** (antes 13): Cobre Nativo, Gelo Eterno, Prata Gélida, Malaquita, Obsidiana, Magmita, Ouro Ancestral, Titânio Abissal e Opala Abissal; Ametista vira geodo, Ancestralita faixas, Pérola do Vazio pérolas, Coral Prismático coral. Cada região tem 3–5 tipos comuns + raro.
- Ícones por formato (pepita, esfera, faixas, lasca, brasa, cristal) no HUD, terminal e Coleção.
- Galeria de minérios no tour (`16_ores_*`, `docs/screenshots/ores_sheet.png`) para revisão visual.
- Custo: benchmark Baixa na mesma ordem (24,2 → 23,1 FPS, dentro da variação do ambiente; trocar o shader por material simples não altera).
- Testes 214/214; bot SUCESSO (economia preservada: Região 2 em 7,3 min, Região 3 em 36,6 min).
- Publicado como **v0.2.4** (atualização automática).

### Etapa 2: mapa maior (galerias laterais)
- **8 galerias laterais** (2 por região, leste/oeste), ligadas à sala principal por um túnel curto com faixa luminosa e placa com o nome acima da entrada. Cada uma tem um **minério em destaque** (55% das ocorrências) e chance de raro x1,6: Veio de Cobre, Gruta das Ametistas, Gruta Congelada, Veio de Prata, Forja Antiga, Câmara de Obsidiana, Jardim de Coral e Poço de Opalas. Área de mineração ~+45%, 74 depósitos a mais.
- Dados em `RegionDef.Wings` / `WingDef` (GameData). A sala da galeria é montada num nó deslocado em X, reaproveitando decoração, luzes e distribuição de cristais das salas principais.
- Paredes leste/oeste agora têm abertura; rochas, cristais decorativos, formações gigantes e depósitos são mantidos longe das passagens (as formações gigantes nasciam na entrada e tapavam a visão — corrigido após revisar as capturas).
- **Oclusão por distância:** o renderizador Compatibility não tem oclusão, então os cristais das galerias eram desenhados através das paredes (R1: 260 → 315 draw calls, média 22,8 → 21,1 FPS). Cada galeria só fica visível com a câmera dentro dela ou a até 34 m da entrada → média de volta a **22,8 FPS** (Baixa, 75%).
- Teste novo "galerias laterais": cada uma existe, tem depósitos do minério em destaque, o jogador **entra andando** pela física real e a oclusão liga/desliga corretamente. Tour com capturas `10d_wing_entrance` e `10w_*`.
- Testes 270/270; bot SUCESSO (Região 2 em 9,4 min, Região 3 em 30,4 min, Coração em 45,5 min, Renascimento OK).
- Publicado como **v0.2.5**.

### Etapa 3: estação de venda única, turno de trabalho e elevador para casa
- **Uma única Estação de Venda** no mapa, no acampamento (as outras regiões ficaram só com o Terminal de Melhorias). Levar os minérios de volta passa a fazer parte do jogo.
- **Turno de trabalho:** 10 min reais = 06:00 → 18:00 no relógio do jogo (HUD: "Dia N · HH:MM" com barra). Aviso a 1 min do fim. No fim do turno ("FIM DO TURNO"), como pedido, o jogador **só pode vender e ir para casa**: mineração, loja, troca de aba para Melhorias e portões ficam bloqueados (com aviso).
- **Elevador da mina** no acampamento (pilares com faixas de alerta, grade, cabos, painel com lâmpada verde/âmbar piscando no fim do turno). Antes do fim do turno pede confirmação para encerrar o dia mais cedo.
- **Casa (primeira versão):** tela "FIM DO DIA N" com ganho do dia, depósitos quebrados, saldo e aviso de fragmentos não vendidos; "Dormir e começar o Dia N+1" devolve o jogador à frente do elevador. Esc não tira o jogador de casa. Melhorias da casa e árvore de habilidades vêm na próxima etapa.
- Marcador de objetivo: no fim do turno aponta para a venda (se houver minérios) ou para o elevador; em regiões fundas aponta o portão do caminho de volta. Objetivo do HUD e "Como jogar" atualizados.
- Save: `Day`, `DayTimeSeconds`, `DayOver`, `DayEarned`, `DayCrystals` (saneados; saves antigos começam no Dia 1).
- Testes: ciclo do dia (relógio, aviso, bloqueios, venda permitida, novo dia, volta antecipada, persistência, saneamento) e marcador/estação única/elevador → **291/291**. Bot: joga **7 dias**, testa que não minera fora do turno, a volta antecipada com confirmação, Esc na casa e dormir → SUCESSO (Região 2 em 9,4 min, Região 3 em 28,3 min, Coração em 49,7 min).
- Benchmark Baixa: 21,7 FPS (dentro da variação de ±2 FPS do ambiente).
- Publicado como **v0.2.6**.

### Etapa 4: casa com melhorias e árvore de habilidades (mesmo dinheiro)
- A tela de casa virou o centro do fim do dia: **maquete 3D da casa** (mundo próprio num SubViewport, desenhada só com a casa aberta), resumo do dia, duas abas e "Dormir".
- **Melhorias da casa** (permanentes, mantidas ao renascer), cada nível aparece como móvel na maquete:
  Cama (+1 min de turno/nível, 3), Bancada de ferramentas (+6% dano, 5), Armário de equipamento (+10% mochila, 4), Cozinha (+5% velocidade, 3), Estante de troféus (+4% venda, 5; cristais coloridos na estante).
- **Árvore de habilidades** (4 ramos × 3 níveis, cada nível exige o anterior; zera ao renascer):
  - Coleta: Ímã ampliado (+30% raio) → **Coleta direta** (fragmentos voam direto para a mochila de qualquer distância) → Compactador (+25% mochila).
  - Turno: Madrugador (+2 min) → Hora extra (+2 min) → Bônus do turno (+10% do ganho do dia ao chegar em casa).
  - Mineração: Golpe firme (+15% dano) → Cadência (+10% golpes/s) → Golpe crítico (12% de dano x3, texto "CRÍTICO!").
  - Comércio: Negociador (+10% venda) → Faro para raros (raros 50% mais frequentes) → Joalheiro (raros/especiais +50%).
- Save: `House` e `Skills` (saneados: níveis limitados, ids desconhecidos removidos, sem duplicatas).
- Testes: casa/habilidades (efeitos, limites, pré-requisitos, compra em casa fora do turno, bônus do turno uma vez, save, renascer, saneamento) e coleta direta a 40 m → **318/318**. Bot compra pela tela da casa (20 melhorias, 12 habilidades), confere o efeito do Golpe firme → SUCESSO (Região 2 em 11,4 min, Região 3 em 30,2 min, Coração em 49,8 min; o bot completa a árvore inteira antes de renascer — custos dos nós de nível 3 podem subir se o diretor achar rápido demais).
- Publicado como **v0.2.7**.

## Feedback do diretor (após v0.2.7)
- "No fim da noite ele não vai para uma casa, só aparece uma tela e depois não tem nada" → **casa onde se entra andando**.
- "Nas Profundezas Prismáticas ele destrói todos os minérios e não dá tempo de renascer" → **renascimento dinâmico**.

### Casa de verdade + renascimento dinâmico
- O elevador da mina agora **leva o jogador para dentro da casa** (transição com escurecimento). A casa é um cômodo fechado com colisões, iluminado mesmo na qualidade Baixa, com os móveis das melhorias (a maquete virou o interior real em escala 1:1).
- Dentro de casa: **Painel da casa** (melhorias e habilidades; Esc fecha), **Cama** (dormir → próximo dia; o jogador acorda em casa) e **Elevador · Mina** (só desce depois de dormir). Objetivo do HUD, marcador e faixa do topo ("Casa do Escavador") acompanham. Dentro de casa as regiões da mina não são desenhadas.
- **Renascimento dinâmico dos minérios:** a cada 0,5 s calcula a fração de depósitos quebrados em cada região (sala + galerias) e acelera o renascimento: taxa = 1 + 5·f² (região esgotada → até 6x mais rápido). Resolve o caso relatado em todas as regiões, não só nas Profundezas.
- Testes 324/324 (renascimento acelerado só na região esgotada; paredes da casa seguram o jogador). Bot: sobe, anda na casa, confere que o elevador não desce de noite, compra pelo painel, dorme na cama, desce pelo elevador → SUCESSO.
- Publicado como **v0.2.8**.

## Atualização grande (v0.3.0): mapa de verdade + minérios com personalidade
Pedido do diretor: regiões grandes e não quadradas, formações rochosas naturais, relevo, minérios nas paredes (alguns só alcançáveis subindo), caminhos secretos e agachar; mais minérios, cada um com som, efeito e fragmento próprios.

### Fase 1 — cavernas orgânicas
- `World/CaveLayout.cs`: caverna de cada região gerada num grid de 1 m (determinístico): caminho principal sinuoso (nós em alturas diferentes), câmaras laterais em níveis de −1,5 a +5 m ligadas por rampas (máx. ~25°), câmaras com nome e minério em destaque (as antigas galerias), câmara do Coração e lago do Reservatório, e **2 câmaras secretas por região** ligadas por túneis de 1,45 m de altura. Bordas irregulares por ruído; paredes sobem até o teto; estalactites.
- Produz chão (F), teto (C), distância às paredes (D) e uma **busca em largura** a partir da entrada que marca o que é alcançável a pé (inclinação e altura livre agachado). Todo depósito só é criado se tiver um ponto de mineração alcançável.
- `World/CaveTerrain.cs`: malhas em blocos de 32 m (recorte por visão; luzes só afetam blocos próximos), colisão por `HeightMapShape3D` e caixas sob o teto dos túneis baixos. `shaders/cave.gdshader`: rocha procedural com projeção pelo eixo dominante (3 leituras de textura; a versão triplanar com 7 custava ~10% de FPS).
- Regiões 4–5x maiores (R1 92×84 m … R4 120×108 m), ~250 depósitos, **~30% nas paredes** (orientados pela normal da rocha). Túneis entre regiões com escoramento de madeira e molduras que fecham as bocas.
- **Agachar** (C/Ctrl, B ou R3 no controle; alterna): cápsula 1,75 → 1,0 m, câmera baixa, metade da velocidade; só levanta se houver espaço (consulta de forma).
- Câmara secreta encontrada: "PASSAGEM SECRETA!", contagem no save (`Secrets`), depósitos raros garantidos.
- Posições salvas do mapa antigo que caem dentro da rocha voltam ao elevador (`IsSafeSpot`). Casa movida para z=110. Câmera do menu sobre o acampamento.

### Fase 2 — minérios com personalidade
- **32 minérios** (+10: Pirita, Jaspe Rubro, Água-marinha, Cobalto Glacial, Fluorita Dupla, Enxofre Vivo, Granada Profunda, Ferro Meteórico, Lápis-lazúli, Pérola Negra); tabelas de cada região refeitas.
- **Som próprio:** golpe e quebra sintetizados por formato (sino, metal, pedra, vidro, gelo, magma, madeira, bolha) com tom derivado do id → cada minério soa diferente.
- **Efeitos próprios:** faíscas e pepitas (metal), poeira e pedaços (pedra), estilhaços (vidro), névoa e flocos (gelo), brasas e fumaça (magma), bolhas (coral), orbes (pérola), brilho (geodo); golpes em metal/magma/pedra também soltam faíscas/brasas/poeira.
- **Fragmento próprio:** pepita metálica, lasca, cubo de gelo, brasa, galho, pérola, placa de pedra, pedaço de geodo ou prisma.

### Bugs encontrados e corrigidos na validação
- Depósitos de parede com o "olho" do jogador colado neles (mira começava dentro do colisor) → distância olho–centro exigida entre 1,4 e 3,3 m; teste novo faz a mira real (raio) de todos os depósitos.
- Pérola do Vazio impossível de mirar: o centro de mira ficava acima do colisor baixo das pérolas (o bot insistiu 2 h de jogo no mesmo depósito) → centro limitado pela altura real do colisor; teste força renascimentos em formatos baixos; o bot desiste de alvos impossíveis.
- Dois sons novos saturavam (metal e magma ao quebrar) → amplitude reduzida (teste de áudio).
- Consultas de física criadas a cada quadro → reaproveitadas.

### Resultados
- Testes **356/356** (cavernas: área, relevo, saída alcançável, câmaras, túneis secretos bloqueiam em pé e deixam passar agachado, não dá para levantar dentro deles, mira real de todos os depósitos, renascimento em outro formato).
- Bot: **SUCESSO** — caminhada física elevador → Reservatório (rampas, túneis, contorno do Coração) em 39 s; 5 dias, compras na casa, Coração em 49,7 min, renascimento.
- Benchmark Baixa/75%: 18,5–21,0 FPS (antes 21–23 no mapa antigo; o ambiente varia ±3 FPS e nenhum experimento isolou a diferença — partículas e shader dos minérios não são a causa). A resolução dinâmica compensa em PCs fracos; acompanhar no PC do diretor.
- Publicado como **v0.3.0**.

## Fase 3 (v0.3.1): Grub, o goblin minerador
Pedido do diretor: um goblin na área inicial que minera devagar; você coleta o que ele minerou; no fim do dia cobra uma taxa; sem pagar ele releva no início, mas se continuar pode fugir ou se rebelar; o jogador precisa conseguir se defender.
- **Acampamento do Grub** na borda leste do acampamento: barraca de lona com fogueira, baú e o próprio Grub (modelo procedural: capacete de mineiro com lanterna, orelhas pontudas, nariz comprido, picareta animada, saco de minério).
- **Contratar:** falar com ele (◈ 500). Ele escolhe o depósito de chão mais próximo na caverna inicial, anda até ele por **A\\* no grid da caverna** (rampas, sem atravessar rocha), minera a 4 de dano/s (bem devagar) e leva até 8 fragmentos de volta ao **baú**. O jogador pega do baú para vender. À noite ele dorme na frente da barraca.
- **Taxa:** ao subir pelo elevador, tela de cobrança — 1/4 do que ele minerou no dia (mínimo ◈ 30). Pagar zera as faltas. Sem pagar: 1ª falta ele perdoa, 2ª avisa (furioso), a partir da 3ª **foge** (volta só 3 dias depois, por ◈ 2.000) ou **se rebela** (sorteio estável pelo dia).
- **Rebelião:** no dia seguinte, quando o jogador aparece na caverna inicial, Grub + 2 comparsas (Snik e Brugo, olhos vermelhos) perseguem e atacam (12 de dano). O jogador agora tem **vida** (100, regenera depois de 4 s sem dano; barra no HUD com "⚠ REBELIÃO" e clarão vermelho ao levar dano). **O feixe atordoa e empurra** os goblins (Grub precisa de 6 acertos, os comparsas de 4; o pulso de ressonância conta 2). Vencendo, os comparsas fogem e Grub volta a trabalhar, envergonhado. **Desmaiando**, os goblins levam 25% da mochila e o baú, o dia acaba e o jogador acorda em casa.
- Fragmentos quebrados pelo goblin vão direto ao baú (destino no `Crystal.Hit`), sem contar como mineração do jogador.
- Testes 383/383: regras completas (contratar, baú, taxa, perdão, aviso, rebelião, fuga, recontratação, roubo, save, saneamento) e integração (Grub anda e minera de verdade com tempo acelerado; rebeldes atacam; o feixe real acerta; vencer encerra a rebelião). Bot: contrata o Grub, não paga uma vez (perdoado), paga as outras, tira 853 fragmentos do baú → SUCESSO.
- Publicado como **v0.3.1**.

## Atualização gráfica + mapa gigante (v0.4.0)
Diretor: desempenho não é mais problema (o jogo roda no PC do irmão: RTX 3060, i7 11ª, 180 FPS no Alto); quer mapa enorme no estilo das cavernas de jogos de mineração cooperativa, visual muito mais bonito, vibrante e "cyberpunk", cavernas escuras com lanterna no capacete e minérios cheios de luz. A história ficou para depois (decisão do diretor).
- **Renderizador Forward+ (Vulkan)** com queda automática para OpenGL (Compatibility) em GPUs sem Vulkan (`rendering_device/fallback_to_opengl3`). `Settings.Advanced` diz qual está ativo. Para testar aqui foi instalado (com autorização) o **Mesa lavapipe** (Vulkan por software) em `/home/user/tools/vk`; `env.sh` exporta `VK_ICD_FILENAMES`.
- **Ambiente:** tonemap ACES, bloom calibrado (limiar HDR 1,25, níveis 2–5), saturação +25%; **névoa volumétrica** (Alto), **SSAO** (Médio/Alto) e **SSIL** (Alto) quando Forward+; luz ambiente baixa (cavernas escuras).
- **Lanterna do capacete** (foco 36 m com sombras no Alto + brilho suave próximo), **F** / D-pad ↑ liga/desliga.
- **Minérios vibrantes:** emissão bem mais forte e **uma luz colorida por depósito** (some além de 30–40 m); o Coração e formações gigantes calibrados para não estourar.
- **Paleta cyberpunk** por região (rocha escura, acentos ciano/magenta/laranja/verde-neon) e **veios de neon pulsando** na rocha (shader da caverna).
- **Ferramenta nova:** corpo grafite anguloso, faixas neon, tela holográfica, núcleo de energia em vidro com luz, **anéis que giram** (mais rápido ao disparar), pontas emissoras (3–4), lente brilhante; cores neon por nível (ciano, magenta, verde, laranja, violeta, dourado). Feixe com núcleo, halo em cone e luzes no cano e no impacto.
- **Mapa gigante:** regiões ~2× maiores (R1 190×180 m … R4 230×210 m), caminho com 6 nós alternando **salões de até ~35 m de altura** e passagens, níveis de −6 a +14 m com rampas longas, 10–16 câmaras por região, 3 câmaras secretas por região, **676 depósitos**. Decoração proporcional à área.
- Testes 389/389 (o teste de fragmentos do evento dependia da sorte do sorteio e foi corrigido). Bot SUCESSO no mapa gigante: caminhada física elevador → Reservatório em 78,5 s, Grub, casa, habilidades, Coração e renascimento.
- Publicado como **v0.4.0**.

## v0.5.0 — goblins escondidos em todas as regiões, colisão do cenário, correções
Feedback do diretor: um goblin por área, o das Cavernas o mais barato; achado sem querer em cavernas secretas (sem nada mostrando antes); pedras e minérios do cenário sem colisão; jogo escuro demais e brilhante demais ("doendo os olhos"); textura das paredes (preto com azul) repetitiva e feia; fragmentos que voam e não descem; dava para vender em qualquer terminal de melhoria (o que tornaria as esteiras inúteis). Também: o PC do irmão (RTX 3060) passa a ser a base média do design.
- **Um goblin por região**, cada um escondido numa câmara secreta: Grub (Cavernas, ◈ 500), Frizz (Profundezas, ◈ 6.000), Krag (Núcleo, ◈ 60.000), Nixa (Reservatório, ◈ 250.000), cada um mais forte (4 → 1.500 de dano/s) e com comparsas próprios. Só aparece para quem explora: chegar perto do esconderijo dispara "GOBLIN ENCONTRADO!". Contratado, monta barraca e baú perto da entrada da região. Taxa, perdão, aviso, fuga (volta ao esconderijo) e rebelião por goblin; ao subir pelo elevador cada goblin cobra separadamente. Saves antigos: o Grub já contratado é migrado e marcado como encontrado.
- **Colisão do cenário:** rochas de parede, pedregulhos, estalagmites, cristais decorativos e formações gigantes ganharam colisão; os obstáculos entram no grid da caverna (caminhos a pé e pontos de mineração os evitam) e não são colocados perto de túneis secretos, estações nem onde estrangulariam um corredor. Depósitos só são criados com linha de mira livre (obstáculos, relevo do terreno e o teto invisível dos túneis baixos).
- **Fragmentos** caem até o chão real do relevo (antes paravam numa altura fixa e ficavam no ar) e quicam nas paredes.
- **Venda só na Estação de Venda:** terminais de melhoria não mostram a aba Vender nem o botão de venda.
- **Luz:** mais luz ambiente (tom neutro), lanterna mais forte e aberta, bloom bem mais contido, minérios e Coração menos estourados, névoa mais leve.
- **Rocha nova:** triplanar com distorção de domínio (sem repetição visível), camadas sedimentares, variação quente/fria, placas e rachaduras (ruído celular), chão de terra com cascalho; minerais e veios só como detalhes. Cores de rocha naturais por região.
- Testes 414/414. Bot SUCESSO (encontra o Grub na câmara secreta, contrata, paga; caminhada real elevador → Reservatório 98,5 s com os novos obstáculos).

## v0.6.0 — controle completo, configurações em abas, idiomas e acessibilidade
Feedback do diretor: com o controle não dava para iniciar um novo jogo nem comprar/vender; pediu configurações com abas (Geral com idioma, Vídeo, Áudio, Controle com mapeamento de teclado/mouse e de controle, Acessibilidade para daltônicos e para cegos) e o jogo em inglês e espanhol.
- **Controle em todos os menus:** o Godot só mapeia teclado nas ações de interface; agora A confirma, B volta, direcional e analógico esquerdo navegam, LB/RB (ou Q/R) trocam de aba. O terminal mantém o foco ao recriar a lista depois de uma compra e começa na primeira melhoria que dá para comprar. Diálogos começam com foco num botão.
- **Configurações em abas:** Geral (idioma, atualizações, FPS), Vídeo, Áudio, Teclado e mouse, Controle (aparece quando há um controle conectado ou em uso) e Acessibilidade. Novos ajustes: brilho, escala da interface e sensibilidade do analógico.
- **Remapeamento:** cada ação de teclado/mouse e de controle pode ser trocada (aperte a nova tecla/botão); fica salvo nas configurações, as dicas na tela usam a tecla atual e há "Restaurar padrões".
- **Idiomas:** português, inglês e espanhol (~510 textos: menus, HUD, terminais, casa, goblins, avisos, materiais, regiões, melhorias, habilidades, eventos e placas). Troca na aba Geral; alguns textos já criados só mudam depois de reiniciar. `--lang=en|es` na linha de comando para capturas.
- **Acessibilidade:** filtro de daltonismo (protanopia, deuteranopia, tritanopia, com intensidade) aplicado na tela inteira; narração por voz do sistema (botões em foco, o que está na mira, avisos); sons-guia (sonar 3D vindo do minério mais próximo, mais rápido quando perto, e tom quando a mira está num minério); reduzir tremor da câmera.
- Teste do controle estabilizado (eventos simulados agora são processados na hora).
- Testes **439/439** (novos: tabelas de tradução consistentes — mesmos marcadores `{n}` nos 3 idiomas —, troca de idioma, remapeamento de tecla/mouse/controle salvo e restaurado, filtro de daltonismo e narração sem falhar). Tours de capturas em inglês e em espanhol (com filtro de deuteranopia) conferidos.

## Atualização ao voltar para o menu (pronta para v0.6.1)
Pedido do diretor: quem está no meio de uma partida e volta ao menu já deve ver se há atualização, atualizar e continuar jogando, sem fechar e abrir o jogo.
- Voltar ao menu pela pausa salva e procura atualização na hora (antes só ao abrir o jogo). Se uma versão nova já tinha sido encontrada, a janela aparece de novo sem baixar o manifesto outra vez; verificação e download não rodam em paralelo.
- Atualizando nesse caso, o jogo fecha, aplica os arquivos e **reabre direto na partida salva** (`--continue`). Aberto do menu inicial, reabre no menu como antes.
- Os arquivos do jogo só podem ser trocados com ele fechado (o Windows trava arquivos em uso), por isso o reinício automático continua, mas sem passos manuais.
- Ambiente: `env.sh` não quebra mais com `set -u` (o script de publicação parava sem mensagem).
- Testes 441/441.

## v0.7.0 — construções e painel de admin
Pedido do diretor: construções (esteira, drone, poste de luz). Durante o trabalho, também pediu um comando de admin/debug para não precisar jogar 2 horas para testar algo novo.
- **Modo construção** (B / D-pad ↓): Q/R (D-pad ←/→) trocam o item, prévia verde/vermelha no chão com o motivo quando não pode (teto baixo, pedra no caminho, perto de estação/outra construção, sem moeda…), botão de minerar constrói, E desmonta (devolve 50%). Barra no HUD com custo e teclas; narração ao trocar.
- **Poste de luz** (◈ 150): poste neon com luz forte (16 m), acende piscando.
- **Funil + esteira** (◈ 2.500 + ◈ 12/m): o funil puxa fragmentos soltos num raio de 5 m e recebe a mochila (E). A esteira é traçada sozinha (A* no grid da caverna) até a Estação de Venda, atravessando os túneis de outras regiões; os fragmentos andam por ela (5 m/s, visíveis) e são vendidos ao chegar. A prévia mostra o trajeto tracejado e o custo total. O que está na esteira é salvo e volta para a fila ao carregar.
- **Drone coletor** (◈ 6.000, único, mantido no Renascimento): segue o jogador com luz própria, busca fragmentos à vista até 18 m, entrega na mochila e, com a mochila cheia, leva ao funil mais próximo.
- Renascimento remove as construções (o drone fica). Textos em PT/EN/ES; ajuda e rodapé mostram a tecla B.
- **Painel de admin** (F9 / Back no controle): botões para dinheiro, liberar tudo, melhorias/habilidades/casa no máximo, encher mochila, teleporte (regiões, Coração, goblin), achar goblins, rebelião, hora/fim do dia/novo dia, evento, velocidade do tempo, drone, curar, renascimento pronto; e campo de comando (`ajuda` lista tudo). Altera o save atual.
- Testes: integração das construções (regras de posição, custos, esteira na região 1 e do Núcleo até o acampamento, venda automática, funil puxando fragmentos, save com itens na esteira, desmontar, drone buscando fragmentos, renascimento). Bot: abre/fecha o modo construção pelos botões, constrói funil e drone, usa o funil; testa o painel de admin pelo atalho.

## v0.8.0 — duas regiões novas (fim de jogo)
Pedido do diretor: mais regiões (o mapa cresce a cada atualização).
- **Jardim Fúngico** (região 5, ◈ 8 mi): cogumelos gigantes bioluminescentes (caule com colisão, chapéus verde/magenta/ciano, lamelas), esporos flutuando, cachoeiras. Minérios: Esporita, Jade Vivo, Âmbar Fóssil, Pedra da Lua e a lendária **Semente Primordial**. Câmaras: Bosque de Esporos e Caverna de Âmbar. Goblin **Musgo** (◈ 1,2 mi). Evento **Chuva de Esporos** (fragmentos x2, dano +20%). Música própria.
- **Catedral de Cristal** (região 6, ◈ 40 mi): formações de cristal colossais (até ~60 m) com colisão, poeira de cristal. Minérios: Quartzo Fumê, Tanzanita, Alexandrita, Diamante Bruto e o lendário **Diamante Eterno**. Câmaras: Nave dos Prismas e Cripta dos Diamantes. Goblin **Zirko** (◈ 6 mi). Evento **Ressonância da Catedral** (dano x2). Música própria.
- A jornada principal continua sendo as 4 primeiras regiões + Coração (conclusão e Renascimento não mudam); as novas são conteúdo de fim de jogo.
- O Reservatório agora tem saída para a região seguinte (o mapa dele mudou um pouco). Construções salvas que ficarem em lugar inválido são removidas com reembolso total.
- Painel de admin: "Ir: Jardim Fúngico" / "Ir: Catedral" (`ir 5`, `ir 6`).
- Testes 515 (os testes de caverna, mira, câmaras secretas e goblins cobrem as regiões novas). Capturas das duas regiões conferidas.
- **Reflexos e água** (pedido do diretor): reflexos em tempo real (SSR) nas qualidades Média/Alta; chão das cavernas com poças e manchas molhadas que refletem; água nova (refração do fundo, cor pela profundidade, fresnel, ondulações, cáusticas, espuma nas bordas) no lago do Reservatório, nas poças das cachoeiras e das Profundezas.
- Bot: fica na jornada principal (as regiões de fim de jogo têm teste próprio de balanceamento); não acusa erro quando o turno acaba no meio das compras.
- Testes 549/549.

## Mapa colossal, poços e corda (em andamento para a v0.8.0)
Pedido do diretor: mapa colossal com muitos túneis, buracos e decoração, mas sempre com volta ("se o cara cair num buraco e não conseguir voltar é meio merda"); uma corda comprável para descer e subir, com animação.
- **Regiões ~1,8× maiores** (R1 250×240 m … Catedral 320×300 m), posições calculadas automaticamente; mais nós no caminho, mais câmaras (proporcional à área), **túneis em laço** ligando câmaras, mais câmaras secretas; ~1.730 depósitos.
- **Poços**: buracos de 7–13 m dentro de câmaras grandes, com tesouro no fundo (raro da região com mais chance) e **corda fixa** para subir. Um poço que isolaria qualquer parte da caverna é desfeito.
- **Nada fica sem volta**: depois de gerar a caverna, toda área isolada (câmara num degrau alto, bolsão fechado) ganha uma corda fixa ou um túnel escavado até a parte alcançável. Obstáculos do cenário sempre deixam 2,2 m de passagem entre si. Teste: todas as câmaras de todas as regiões têm volta.
- **Corda** (◈ 300, no modo construção): mira na beira de um buraco para descer ou numa borda acima para subir (até 25 m); a área ligada passa a contar como alcançável.
- **Escalar**: E na corda sobe/desce com animação em primeira pessoa (punhos alternando nos nós, braços, balanço da cabeça, rangido), lanterna suavizada perto da parede, e no alto o personagem sobe na borda; pular solta a corda.
- **Resgate** no menu de pausa: volta para a entrada da região (segurança para qualquer caso).
- Painel de admin: "Ir: poço".
- **Desempenho**: depósitos parados não rodam mais lógica a cada quadro (cortou mais da metade do tempo de scripts com ~1.730 depósitos); `--perf` imprime os tempos por sistema. Scripts do jogo ~0,6 ms/quadro em média.
- Testes 590/590. Bot: jornada completa no mapa maior (Coração aos 64,7 min de jogo, caminhada real até o Reservatório), funil, drone e painel de admin; a conta da taxa do goblin agora desconta o que a esteira vende sozinha no meio da checagem.

## Goblins com cabana e melhorias; casa que evolui (para a v0.8.0)
Pedido do diretor: nada de cabana/baú antes de contratar (spoiler); contratado, o goblin leva 1–2 dias montando a cabana antes de trabalhar; melhorias da cabana (mais tempo de trabalho no dia — base 3 de 10 min), do quanto ele carrega e da velocidade; visual sempre evoluindo. E a casa do jogador também precisa evoluir.
- **Montagem da cabana**: ~1,5 dia de turno, em 3 estágios visíveis (estacas e corda → estrutura → lona), com o goblin indo e voltando martelando; placa "MONTANDO A CABANA · X%". Sem trabalho e sem taxa enquanto monta. O baú só aparece com a cabana pronta. Saves antigos: quem já trabalhava fica com a cabana pronta.
- **Turno do goblin**: cabana nível 1 = 30% do turno (3 de 10 min); fora do horário ele volta e descansa.
- **Melhorias** (tela ao falar com ele, funciona com controle): Cabana (30% → 45% → 60% → 80% → 100% do turno), Mochila do goblin (8 → 30 fragmentos por viagem), Picareta (×1 → ×3,2 de dano). 5 níveis visuais da cabana: barraca → barraca com tapete/caixote/lampião → cabana de madeira com janela acesa → cabana com chaminé fumegante, bancada e placa → cabana neon (metal, contorno neon, antena piscando). A mochila cresce e a picareta vai de ferro a ouro e neon.
- **Casa**: além dos móveis por melhoria, o cômodo inteiro evolui em 5 patamares pelo total de melhorias (manchas e balde → rodapé, cortinas, planta → livros, quadros, arandelas → papel de parede, sofá, neon no teto → telão holográfico, aquário, lustre de cristal, frisos dourados).
- Testes: regras completas (montagem, sem taxa, turno 30%, melhorias, custos, save) e integração (goblin vai ao terreno, monta, baú aparece). Bot compra melhorias do goblin pela tela.

## Formações gigantes, mais minérios e detecção do controle (para a v0.9.0)
Pedido do diretor: formações gigantes e vários minérios; depois, "vê se a detecção do controle tá bugada".
- **Formações gigantes**: cada região (o Núcleo já tem o Coração) ganha uma **Câmara Colossal** com uma formação gigante minerável no centro, sobre pedestal com runas, com chuva de 18–28 fragmentos: Geodo Colossal (Cavernas), Pilar de Gelo Eterno (Profundezas), Coral Rei (Reservatório), Esfera-Mãe de Esporos (Jardim Fúngico) e Prisma Primordial (Catedral). Aparecem na Coleção.
- **18 minérios novos** (3 por região, cada um com um lendário raro): Hematita, Calcita Fluorescente, Olho de Dragão; Turmalina Bicolor, Celestina, Coração de Geleira; Cinábrio, Topázio Imperial, Rubi Estrela; Turquesa, Abalone Cristalino, Pérola do Leviatã; Peridoto, Crisocola, Raiz de Lúmen; Quartzo Arco-íris, Moissanita, Halo Celeste. Tabelas de chance de todas as regiões refeitas.
- Câmaras secretas ficam sempre livres de obstáculos do cenário (um obstáculo tinha caído no centro de uma).
- **Detecção do controle corrigida**: gatilhos que ficam em −1 quando soltos (comum no Windows) não fazem mais as dicas ficarem alternando; folga do analógico e soltar botões não contam; um esbarrão no mouse (ou mouse emulado pela Steam) não volta para teclado; conectar/desconectar o controle troca as dicas na hora e avisa; desconectar no meio da partida pausa o jogo.
- Testes 631/631 (novos: formação gigante alcançável em cada região; 10 casos de detecção de controle).
- **Loja (urgente, relato do diretor)**: comprar uma melhoria fazia a tela rolar até a área das regiões (com mouse e controle). Causa: a lista era recriada e os cartões antigos só sumiam no fim do quadro; o foco ia para o cartão novo, ainda posicionado embaixo de tudo, e a rolagem seguia. Agora os cartões antigos saem na hora, a rolagem é preservada e, no mouse, o foco não é puxado. Teste reproduz o bug no código antigo (rolagem 152 → 958) e passa com a correção.
- **Corda (relato do diretor)**: as mãos estavam presas à câmera (olhando para cima, a "corda" ia para o céu) e a escalada era automática. Agora as mãos ficam presas na corda de verdade, no mundo; o jogador controla com W/S ou analógico (sem apertar fica pendurado), pula para soltar, vira de frente para a corda ao começar, e a dica aparece na tela.
- Testes 637/637.

## Esteira sem atravessar paredes (pós v0.9.0)
- Rota da esteira refeita: A* com folga das paredes (`CaveLayout.FindClearPath`), desviando de depósitos, construções, estações, acampamentos e decoração bloqueante; trechos retos só onde a linha inteira é livre.
- A esteira acompanha o relevo (pontos a cada 0,5 m na altura do chão).
- Bug grave corrigido: a ligação entre regiões usava o fundo da região atual e o topo da anterior (invertido), e a esteira cruzava a região inteira em linha reta por dentro da rocha.
- Teste novo `RouteIsClean` (região 1 e Núcleo → acampamento): sem parede, obstáculo, depósito ou desnível. 641/641 testes.

## Ramo Construção, esteira arrastada, drone de área e robô mineiro
- Pedido do diretor: construções compradas na árvore de habilidades (ramo Construção), começando só pela esteira; esteira colocada marcando o início e arrastando até o fim; robôs mineradores mais lentos que o goblin, trabalhando só numa área, assim como o drone.
- Árvore: novo ramo **CONSTRUÇÃO** com 4 níveis — Esteira (◈ 1.500) → Funil (◈ 12 mil) → Drone de área (◈ 60 mil) → Robô mineiro (◈ 200 mil). Corda e poste continuam livres. Renascimento zera o ramo junto com a árvore.
- **Esteira**: no modo construção, apertar marca o início; segurando e soltando na ponta (ou clicando de novo) constrói. A ponta precisa ficar na Estação de Venda ou encostada em outra esteira (emenda: os fragmentos passam para a outra no ponto de encontro). Prévia do trajeto em verde/vermelho com custo e metros; E cancela o início. Entrada no começo da esteira recebe a mochila. Desmontar uma esteira deixa sem saída quem despejava nela (fragmentos ficam na fila, nada se perde).
- **Funil**: só ao lado de uma esteira; puxa fragmentos soltos e despeja nela.
- **Drone de área**: base fixa (até 4); recolhe fragmentos soltos num raio de 14 m e leva ao funil/entrada de esteira mais perto; sem nenhum, guarda na base (o jogador pega com E).
- **Robô mineiro**: base fixa (até 6); minera depósitos de chão num raio de 10 m com metade da força do goblin da região, carrega e entrega como o drone; para fora do turno. A área aparece como anel no modo construção.
- Saves antigos: funil com esteira automática vira funil + esteira separados (valor dividido); drone antigo é devolvido (◈ 6 mil) e o ramo é liberado até onde o jogador já tinha.
- Admin: `construcoes` libera o ramo inteiro + ◈ 500 mil.
- Testes 688/688 (construções: 77 verificações, incluindo arrastar a esteira com o botão de verdade, emenda, funil, área do drone e do robô, migração de save).

## Desempenho (meta 120 FPS) e acabamento visual
- **FPS travado em 60**: o padrão antigo era VSync + limite de 60 FPS, então o jogo nunca passava de 60 em nenhuma placa. Novo padrão: sem VSync, limite 144. Configurações antigas que estavam no padrão são migradas sozinhas (versão 2); escolhas do jogador são mantidas.
- **Resolução dinâmica** agora segura **120 FPS** (antes mirava 30), com reescala FSR (mínimo 60%).
- **Suavização de bordas**: o jogo não tinha nenhuma. Novo padrão SMAA; opções FXAA e temporal (FSR 2) em Vídeo. Debanding ligado (gradientes escuros sem faixas).
- **Placas RTX/RX 6000+ sobem para qualidade Alta** (primeira execução e migração de quem estava na Média padrão).
- **Cristais**: as rachaduras (Voronoi, caro) só são calculadas em cristal danificado; cintilância de gema (pontinhos que acendem com o ângulo).
- **Textura "estourada" (relato do diretor)**: as texturas de ruído da rocha eram 256 px em 8 bits; as rachaduras/cascalho recortam faixas estreitas e cada degrau virava pixel visível. Agora 512 px em ponto flutuante, suavizadas — mesmo desenho, sem serrilhado.
- **Arma brilhante demais (relato do diretor)**: neon de 3–6× caiu para ~0,4–1,6× (só passa do bloom ao atirar), sem brilho de borda no metal, e a lanterna do capacete não ilumina mais a arma (camada própria).
- Testado e descartado: relevo procedural na rocha (bump por derivadas) — com as texturas de ruído ampliadas gerava blocos visíveis no chão.
- Bot: corrigido o alvo do Coração (o bot tratava qualquer formação especial como o Coração). Jogo: fragmentos recolhidos por drone/robô/funil também contam como descobertos.
- Testes 703/703; bot SUCESSO. Não foi possível medir os 120 FPS numa RTX 3060 real (ambiente sem GPU): conferir com o contador (F3).

## Sistema inteligente de renderização (seleção automática Forward+/Mobile/Compatibility)
- Pedido do diretor (especificação completa): escolher sozinho, antes do jogo abrir, o renderizador mais avançado que roda bem na máquina; jogador nunca escolhe nem vê o renderizador; integrado ao atualizador; recuperação de falhas; perfis Baixa/Média/Alta nos três.
- Módulos em `scripts/Core/Rendering/`: `RenderFeatures` (9 perfis), `HardwareInfo`, `RenderProfile`/`RenderStore` (persistência local + override + controle de inicialização + log), `RendererSelector` (regras puras), `RenderProbe` (teste em processo separado fora da tela com a cena real), `RenderBootstrap` (orquestração antes do mundo).
- Mecanismo oficial da Godot: `application/config/project_settings_override = user://graphics_override.cfg` (testado: o motor lê na inicialização; linha de comando tem prioridade). Padrão do projeto agora é Compatibility.
- Sem executável de launcher separado de propósito: o atualizador das versões antigas só aceita `.pck`/DLL; tudo chega pela atualização normal.
- Testado de ponta a ponta com display virtual (primeira vez, inicializações seguintes, recuperação após 2 quedas, máquina sem Vulkan, teste que morre/trava, instalação antiga com save intacto) e matriz 3×3 de perfis sem erros. 758/758 testes. Detalhes e limites em `docs/RENDERIZACAO.md`.
- Não validado aqui: FPS reais das máquinas de referência (ambiente sem GPU) e comportamento visual da janela de teste no Windows.

## Correção do incidente da v0.11.0 ("OpenGL 3.3" ao abrir)
- Relato do diretor: o jogo não abriu (caixa "OpenGL 3.3"); reinstalou e caiu na 0.2.1, que não recebe a atualização.
- Causa provável (código-fonte da Godot 4.5.1): no Compatibility o Windows tenta o OpenGL nativo e só usa ANGLE se o driver declarar < 3.3; se declarar 3.3 e falhar, a Godot mostra a caixa e fecha antes do C#. Os testes da v0.11.0 ainda abriam janelas fora de todas as telas.
- Correções: Compatibility no Windows com ANGLE (DirectX 11) primeiro e OpenGL nativo de reserva; consulta de Vulkan/Direct3D 12 num processo sem janela antes de testar Forward+/Mobile (sem eles, nem abre o teste); janela de teste no canto da tela principal; teste que não inicia é encerrado em 20 s; override renomeado (`graphics_override_v2.cfg`) e reavaliação forçada; trava para uma avaliação por vez (duplo clique); instaladores de `dist/` passam a ser regerados a cada publicação (estavam na 0.2.1).
- Testes de ponta a ponta: GPU com Vulkan, sem Vulkan (Forward+/Mobile descartados em 1 s, sem abrir janela), driver travado antes do jogo, duas instâncias. 761/761.
