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
- Repositório remoto ainda não configurado: o container é temporário, então o código existe só aqui.

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
