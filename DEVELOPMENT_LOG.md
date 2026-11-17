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
