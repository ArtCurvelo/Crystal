; Instalador do Crystal Excavator (NSIS 3, Modern UI 2)
; Gerado a partir de build/windows/. Uso: makensis installer/CrystalExcavator.nsi
Unicode true
!include "MUI2.nsh"

!define APPNAME "Crystal Excavator"
!define APPID "CrystalExcavator"
!ifndef VERSION
  !define VERSION "0.1.0"
!endif
!define PUBLISHER "Crystal Excavator"
!define SRC "..\build\windows"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"

Name "${APPNAME}"
OutFile "..\build\CrystalExcavator-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\${APPID}"
InstallDirRegKey HKCU "Software\${APPID}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${VERSION}"

!define MUI_ICON "..\icon.ico"
!define MUI_UNICON "..\icon.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\CrystalExcavator.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Jogar Crystal Excavator agora"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "PortugueseBR"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileDescription" "Instalador do ${APPNAME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "LegalCopyright" "${PUBLISHER}"

Section "Jogo" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  File "${SRC}\CrystalExcavator.exe"
  File "${SRC}\CrystalExcavator.console.exe"
  File "${SRC}\CrystalExcavator.pck"
  File "${SRC}\LEIA-ME.md"
  File /r "${SRC}\data_CrystalExcavator_windows_x86_64"

  WriteUninstaller "$INSTDIR\Desinstalar.exe"
  WriteRegStr HKCU "Software\${APPID}" "InstallDir" "$INSTDIR"

  ; atalhos
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\CrystalExcavator.exe"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Desinstalar ${APPNAME}.lnk" "$INSTDIR\Desinstalar.exe"
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\CrystalExcavator.exe"

  ; "Adicionar ou remover programas"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\CrystalExcavator.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  Delete "$INSTDIR\CrystalExcavator.exe"
  Delete "$INSTDIR\CrystalExcavator.console.exe"
  Delete "$INSTDIR\CrystalExcavator.pck"
  Delete "$INSTDIR\LEIA-ME.md"
  Delete "$INSTDIR\Desinstalar.exe"
  RMDir /r "$INSTDIR\data_CrystalExcavator_windows_x86_64"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\${APPID}"
  ; os saves em %APPDATA%\CrystalExcavator são preservados de propósito
SectionEnd
