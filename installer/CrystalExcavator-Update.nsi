; Atualizador do Crystal Excavator: substitui apenas o conteúdo do jogo (.pck) e o código C# (.dll)
; na instalação existente. O motor (.exe) e o runtime .NET não são reenviados.
Unicode true
!include "MUI2.nsh"

!define APPNAME "Crystal Excavator"
!define APPID "CrystalExcavator"
!ifndef VERSION
  !define VERSION "0.1.0"
!endif
!define SRC "..\build\windows"
!define DATA "data_CrystalExcavator_windows_x86_64"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"

Name "${APPNAME} - Atualização"
OutFile "..\build\CrystalExcavator-Atualizacao.exe"
InstallDir "$LOCALAPPDATA\Programs\${APPID}"
InstallDirRegKey HKCU "Software\${APPID}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"

!define MUI_ICON "..\icon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\CrystalExcavator.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Jogar Crystal Excavator agora"
!define MUI_WELCOMEPAGE_TITLE "Atualizar o ${APPNAME}"
!define MUI_WELCOMEPAGE_TEXT "Esta atualização substitui apenas os arquivos do jogo que mudaram. Seus saves e configurações são mantidos.$\r$\n$\r$\nFeche o jogo antes de continuar."

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "PortugueseBR"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileDescription" "Atualização do ${APPNAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductVersion" "${VERSION}"

Function .onInit
  ; exige uma instalação existente (feita pelo instalador completo)
  IfFileExists "$INSTDIR\CrystalExcavator.exe" found
    MessageBox MB_ICONEXCLAMATION|MB_OK "O ${APPNAME} não foi encontrado neste computador.$\r$\nInstale primeiro com CrystalExcavator-Setup.exe."
    Abort
  found:
FunctionEnd

Section "Atualizar"
  ; o jogo não pode estar aberto (arquivos em uso)
  retry:
  ClearErrors
  FileOpen $0 "$INSTDIR\CrystalExcavator.pck" a
  IfErrors 0 +3
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "O jogo parece estar aberto. Feche o ${APPNAME} e clique em Repetir." IDRETRY retry
    Abort
  FileClose $0

  SetOutPath "$INSTDIR"
  File "${SRC}\CrystalExcavator.pck"
  File "${SRC}\LEIA-ME.md"
  SetOutPath "$INSTDIR\${DATA}"
  File "${SRC}\${DATA}\CrystalExcavator.dll"
  File "${SRC}\${DATA}\CrystalExcavator.deps.json"
  File "${SRC}\${DATA}\CrystalExcavator.runtimeconfig.json"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  DetailPrint "Atualizado para a versão ${VERSION}."
SectionEnd
