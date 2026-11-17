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
