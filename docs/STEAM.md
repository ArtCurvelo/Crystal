# Publicação na Steam (planejamento)

- Na Steam, quem entrega o jogo e as atualizações é a própria Steam: cada versão nova é enviada
  pelo SteamPipe (ferramenta `steamcmd`/ContentBuilder do Steamworks) e todo jogador recebe sempre a
  última build publicada, seja na primeira instalação ou depois.
- Por isso existe a predefinição de exportação **"Windows Steam"** (recurso `steam`): nela o atualizador
  interno fica desligado (`BuildFlavor.IsSteam`), para não brigar com a Steam. O resto do jogo é idêntico.
- Passos quando for a hora: conta Steamworks (taxa por jogo), criar o App ID, configurar o depot,
  exportar com `godot --headless --export-release "Windows Steam" build/steam/CrystalExcavator.exe`
  e enviar a pasta `build/steam` pelo SteamPipe.
- Fora da Steam continua valendo: o link `dist/CrystalExcavator-Setup.exe` é regerado a cada publicação
  e o jogo instalado se atualiza sozinho ao abrir.
