Unicode true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"

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

Var BootstrapAdmin
Var BootstrapAdminInput

VIProductVersion "0.1.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${COMPANY_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT_NAME} Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom BootstrapAdminPageCreate BootstrapAdminPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${GetParameters} $R0
  ${GetOptions} $R0 "/BOOTSTRAPADMIN=" $BootstrapAdmin
FunctionEnd

Function BootstrapAdminPageCreate
  IfFileExists "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\server.settings.json" 0 +2
    Abort

  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  !insertmacro MUI_HEADER_TEXT "Bootstrap Administrator" "Choose the initial WellSite AutoPilot administrator."

  ${NSD_CreateLabel} 0 0 100% 28u "Enter an explicit Windows identity (DOMAIN\user, MACHINE\user, or user@domain). This identity is used only while the system has no active WellSite AutoPilot Admin."
  Pop $0

  ${NSD_CreateText} 0 34u 100% 13u "$BootstrapAdmin"
  Pop $BootstrapAdminInput

  nsDialogs::Show
FunctionEnd

Function BootstrapAdminPageLeave
  ${NSD_GetText} $BootstrapAdminInput $BootstrapAdmin

  ${If} $BootstrapAdmin == ""
    MessageBox MB_ICONSTOP "A bootstrap Windows administrator identity is required for a fresh installation."
    Abort
  ${EndIf}
FunctionEnd

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

  SetOutPath "$INSTDIR\Infrastructure\NATS"
  File /r "${STAGING}\nats\*"

  SetOutPath "$INSTDIR\Infrastructure\NATSHost"
  File /r "${STAGING}\nats-host\*"

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

  IfFileExists "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\server.settings.json" ExistingSecuritySettings FreshSecuritySettings

FreshSecuritySettings:
  ${If} $BootstrapAdmin == ""
    Abort "Fresh WellSite AutoPilot installation requires /BOOTSTRAPADMIN=<WindowsIdentity> in silent mode."
  ${EndIf}
  ExecWait '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR" --bootstrap-admin "$BootstrapAdmin"' $0
  Goto ServicesInstalled

ExistingSecuritySettings:
  ExecWait '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR"' $0

ServicesInstalled:
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
