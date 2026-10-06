Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef VERSION
  !define VERSION "0.1.0-dev"
!endif

!ifndef STAGING
  !define STAGING "..\staging"
!endif

!ifndef OUTFILE
  !define OUTFILE "WellSiteAutoPilot-Setup.exe"
!endif

!define PRODUCT_NAME "WellSite AutoPilot"
!define COMPANY_NAME "Weatherford"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\WellSiteAutoPilot"

Name "${PRODUCT_NAME}"
Caption "${PRODUCT_NAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\Weatherford\WellSite AutoPilot"
InstallDirRegKey HKLM "Software\Weatherford\WellSiteAutoPilot" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "0.1.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${COMPANY_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT_NAME} Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "WellSite AutoPilot" SEC_CORE
  SetShellVarContext all

  SetOutPath "$INSTDIR\Server"
  File /r "${STAGING}\server\*"

  SetOutPath "$INSTDIR\IntegrationGateway"
  File /r "${STAGING}\gateway\*"

  SetOutPath "$INSTDIR\Workers\DotNet"
  File /r "${STAGING}\worker-dotnet\*"

  SetOutPath "$INSTDIR\Workers\Python"
  File /r "${STAGING}\python-worker\*"

  SetOutPath "$INSTDIR\ProviderSimulator"
  File /r "${STAGING}\provider-simulator\*"

  SetOutPath "$INSTDIR\Tools\Bootstrapper"
  File /r "${STAGING}\bootstrapper\*"

  SetOutPath "$INSTDIR\Tools\DatabaseMigrator"
  File /r "${STAGING}\database-migrator\*"

  SetOutPath "$INSTDIR\Tools\Cli"
  File /r "${STAGING}\cli\*"

  CreateDirectory "$COMMONAPPDATA\Weatherford\WellSite AutoPilot"
  CreateDirectory "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\Logs"
  CreateDirectory "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\Diagnostics"
  CreateDirectory "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\Modules"

  WriteRegStr HKLM "Software\Weatherford\WellSiteAutoPilot" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "Software\Weatherford\WellSiteAutoPilot" "Version" "${VERSION}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${COMPANY_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1

  ExecWait '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR"' $0
  ${If} $0 != 0
    Abort "WellSite AutoPilot service registration failed with exit code $0."
  ${EndIf}
SectionEnd

Section "Uninstall"
  SetShellVarContext all

  IfFileExists "$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" 0 +2
    ExecWait '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" uninstall-services' $0

  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "Software\Weatherford\WellSiteAutoPilot"

  RMDir /r "$INSTDIR"

  ; Product data under ProgramData is deliberately preserved.
SectionEnd
