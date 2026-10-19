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
Var DbHost
Var DbPort
Var DbName
Var DbUser
Var DbPassword
Var DbHostInput
Var DbPortInput
Var DbNameInput
Var DbUserInput
Var DbPasswordInput

; Hands a value to the bootstrapper through its environment (never its command line, which other
; programs can read). The value goes through a register so quotes in a password cannot break the call.
!macro ExportEnvironment NAME VALUE
  StrCpy $0 "${VALUE}"
  System::Call 'Kernel32::SetEnvironmentVariable(t "${NAME}", t r0)i.r1'
!macroend

VIProductVersion "0.1.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${COMPANY_NAME}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT_NAME} Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
Page custom BootstrapAdminPageCreate BootstrapAdminPageLeave
Page custom DatabasePageCreate DatabasePageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${GetParameters} $R0
  ${GetOptions} $R0 "/BOOTSTRAPADMIN=" $BootstrapAdmin
  ; Silent installs: /DBUSER= and /DBPASSWORD= (or WSA_DATABASE_USER / WSA_DATABASE_PASSWORD already in
  ; the environment, which keeps the password off the command line), optionally /DBHOST= /DBPORT= /DBNAME=.
  ${GetOptions} $R0 "/DBHOST=" $DbHost
  ${GetOptions} $R0 "/DBPORT=" $DbPort
  ${GetOptions} $R0 "/DBNAME=" $DbName
  ${GetOptions} $R0 "/DBUSER=" $DbUser
  ${GetOptions} $R0 "/DBPASSWORD=" $DbPassword
FunctionEnd

Function DatabasePageCreate
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  !insertmacro MUI_HEADER_TEXT "PostgreSQL Database" "Tell WellSite AutoPilot how to reach its database."

  ${NSD_CreateLabel} 0 0 100% 36u "The PostgreSQL database must already exist, and this user must be able to create tables in it. The settings are stored in a protected machine settings file readable only by the WellSite AutoPilot Server. On an upgrade, leave the user and password empty to keep the stored connection."
  Pop $0

  ${If} $DbHost == ""
    StrCpy $DbHost "127.0.0.1"
  ${EndIf}
  ${If} $DbPort == ""
    StrCpy $DbPort "5432"
  ${EndIf}
  ${If} $DbName == ""
    StrCpy $DbName "wellsite_autopilot"
  ${EndIf}

  ${NSD_CreateLabel} 0 40u 30% 10u "Host"
  Pop $0
  ${NSD_CreateText} 32% 38u 40% 12u "$DbHost"
  Pop $DbHostInput
  ${NSD_CreateLabel} 74% 40u 8% 10u "Port"
  Pop $0
  ${NSD_CreateText} 83% 38u 17% 12u "$DbPort"
  Pop $DbPortInput

  ${NSD_CreateLabel} 0 56u 30% 10u "Database"
  Pop $0
  ${NSD_CreateText} 32% 54u 68% 12u "$DbName"
  Pop $DbNameInput

  ${NSD_CreateLabel} 0 72u 30% 10u "User"
  Pop $0
  ${NSD_CreateText} 32% 70u 68% 12u "$DbUser"
  Pop $DbUserInput

  ${NSD_CreateLabel} 0 88u 30% 10u "Password"
  Pop $0
  ${NSD_CreatePassword} 32% 86u 68% 12u ""
  Pop $DbPasswordInput

  nsDialogs::Show
FunctionEnd

Function DatabasePageLeave
  ${NSD_GetText} $DbHostInput $DbHost
  ${NSD_GetText} $DbPortInput $DbPort
  ${NSD_GetText} $DbNameInput $DbName
  ${NSD_GetText} $DbUserInput $DbUser
  ${NSD_GetText} $DbPasswordInput $DbPassword

  ; Leaving user and password empty means "keep the stored connection" (upgrade). The pre-filled host,
  ; port and database must then not be sent on their own, or they would look like a partial new connection.
  ${If} $DbUser == ""
  ${AndIf} $DbPassword == ""
    StrCpy $DbHost ""
    StrCpy $DbPort ""
    StrCpy $DbName ""
  ${EndIf}

  IfFileExists "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\server.settings.json" ExistingSettings FreshSettings

FreshSettings:
  ${If} $DbUser == ""
  ${OrIf} $DbPassword == ""
    MessageBox MB_ICONSTOP "A fresh installation needs the PostgreSQL user and password."
    Abort
  ${EndIf}
  Return

ExistingSettings:
  ; Upgrade: both empty keeps the stored connection; giving one without the other is a mistake.
  ${If} $DbUser != ""
  ${AndIf} $DbPassword == ""
    MessageBox MB_ICONSTOP "Enter the password for the PostgreSQL user, or clear the user to keep the stored connection."
    Abort
  ${EndIf}
  ${If} $DbUser == ""
  ${AndIf} $DbPassword != ""
    MessageBox MB_ICONSTOP "Enter the PostgreSQL user for this password, or clear the password to keep the stored connection."
    Abort
  ${EndIf}
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

  ; Upgrade: running services lock their executables and an old Server must not run against a migrated
  ; schema, so stop and remove them before any file is replaced. The bootstrapper re-creates them.
  IfFileExists "$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" StopExistingServices SkipStopServices
StopExistingServices:
  nsExec::ExecToLog '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" uninstall-services'
  Pop $0
SkipStopServices:

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

  ; Database connection for the bootstrapper. Empty values are not exported, so a silent install can
  ; supply WSA_DATABASE_USER / WSA_DATABASE_PASSWORD through the process environment instead.
  ${If} $DbHost != ""
    !insertmacro ExportEnvironment "WSA_DATABASE_HOST" "$DbHost"
  ${EndIf}
  ${If} $DbPort != ""
    !insertmacro ExportEnvironment "WSA_DATABASE_PORT" "$DbPort"
  ${EndIf}
  ${If} $DbName != ""
    !insertmacro ExportEnvironment "WSA_DATABASE_NAME" "$DbName"
  ${EndIf}
  ${If} $DbUser != ""
    !insertmacro ExportEnvironment "WSA_DATABASE_USER" "$DbUser"
  ${EndIf}
  ${If} $DbPassword != ""
    !insertmacro ExportEnvironment "WSA_DATABASE_PASSWORD" "$DbPassword"
  ${EndIf}

  IfFileExists "$COMMONAPPDATA\Weatherford\WellSite AutoPilot\server.settings.json" ExistingSecuritySettings FreshSecuritySettings

FreshSecuritySettings:
  ${If} $BootstrapAdmin == ""
    Abort "Fresh WellSite AutoPilot installation requires /BOOTSTRAPADMIN=<WindowsIdentity> in silent mode."
  ${EndIf}
  ; nsExec shows the bootstrapper's messages (for example a failed database migration) in the install log.
  nsExec::ExecToLog '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR" --bootstrap-admin "$BootstrapAdmin"'
  Pop $0
  Goto ServicesInstalled

ExistingSecuritySettings:
  nsExec::ExecToLog '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR"'
  Pop $0

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
