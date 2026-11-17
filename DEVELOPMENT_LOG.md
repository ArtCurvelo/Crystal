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
