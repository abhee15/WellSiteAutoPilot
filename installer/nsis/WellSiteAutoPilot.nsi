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
Var NetworkAccess
Var NetworkAccessInput
Var ServerPort
Var ServerPortInput
Var BootstrapperArgs

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
  ; The page functions look for the machine settings file before the install section runs, so resolve
  ; $APPDATA for all users here. Otherwise an upgrade is mistaken for a fresh install and a silent
  ; upgrade aborts for want of an administrator and database credentials.
  SetShellVarContext all
  ${GetParameters} $R0
  ${GetOptions} $R0 "/BOOTSTRAPADMIN=" $BootstrapAdmin
  ; Silent installs: /DBUSER= and /DBPASSWORD= (or WSA_DATABASE_USER / WSA_DATABASE_PASSWORD already in
  ; the environment, which keeps the password off the command line), optionally /DBHOST= /DBPORT= /DBNAME=.
  ${GetOptions} $R0 "/DBHOST=" $DbHost
  ${GetOptions} $R0 "/DBPORT=" $DbPort
  ${GetOptions} $R0 "/DBNAME=" $DbName
  ${GetOptions} $R0 "/DBUSER=" $DbUser
  ${GetOptions} $R0 "/DBPASSWORD=" $DbPassword
  ; /NETWORKACCESS=yes|no. Omitted: the first install allows network access and an upgrade keeps its earlier choice.
  ${GetOptions} $R0 "/NETWORKACCESS=" $NetworkAccess
  ; /PORT=<1-65535>: the Server's port. Omitted: the first install uses 5080 and an upgrade keeps its earlier port.
  ${GetOptions} $R0 "/PORT=" $ServerPort
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

  ${NSD_CreateCheckbox} 0 96u 100% 10u "Allow other computers on the network to connect"
  Pop $NetworkAccessInput
  ${If} $NetworkAccess != "no"
    ${NSD_Check} $NetworkAccessInput
  ${EndIf}
  ${NSD_CreateLabel} 10u 107u 95% 18u "Opens Windows Firewall for the WellSite AutoPilot Server on Domain and Private networks only. Clear this to allow connections from this computer only."
  Pop $0

  ; The Server port is remembered by an earlier install; the command line wins; otherwise 5080.
  ${If} $ServerPort == ""
    IfFileExists "$APPDATA\Weatherford\WellSite AutoPilot\server.settings.json" 0 ServerPortScanDone
    FileOpen $1 "$APPDATA\Weatherford\WellSite AutoPilot\server.settings.json" r
    ServerPortScan:
      FileRead $1 $2
      IfErrors ServerPortScanEnd
      ${WordFind} "$2" '"ServerPort": ' "+1}" $3
      IfErrors ServerPortScanNext 0
      IntOp $3 $3 + 0
      ${If} $3 > 0
        StrCpy $ServerPort $3
        Goto ServerPortScanEnd
      ${EndIf}
    ServerPortScanNext:
      ClearErrors
      Goto ServerPortScan
    ServerPortScanEnd:
    ClearErrors
    FileClose $1
    ServerPortScanDone:
  ${EndIf}
  ${If} $ServerPort == ""
    StrCpy $ServerPort "5080"
  ${EndIf}
  ${NSD_CreateLabel} 0 128u 30% 10u "Server port"
  Pop $0
  ${NSD_CreateText} 32% 126u 17% 12u "$ServerPort"
  Pop $ServerPortInput

  nsDialogs::Show
FunctionEnd

Function DatabasePageLeave
  ${NSD_GetText} $DbHostInput $DbHost
  ${NSD_GetText} $DbPortInput $DbPort
  ${NSD_GetText} $DbNameInput $DbName
  ${NSD_GetText} $DbUserInput $DbUser
  ${NSD_GetText} $DbPasswordInput $DbPassword
  ${NSD_GetText} $ServerPortInput $ServerPort
  IntOp $0 $ServerPort + 0
  ${If} $0 < 1
  ${OrIf} $0 > 65535
  ${OrIf} $0 == 5081
  ${OrIf} $0 == 5082
  ${OrIf} $0 == 4222
    MessageBox MB_ICONSTOP "The Server port must be a number from 1 to 65535 and not 5081, 5082 or 4222 (used by other WellSite AutoPilot services)."
    Abort
  ${EndIf}
  StrCpy $ServerPort $0
  ${NSD_GetState} $NetworkAccessInput $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $NetworkAccess "yes"
  ${Else}
    StrCpy $NetworkAccess "no"
  ${EndIf}

  ; Leaving user and password empty means "keep the stored connection" (upgrade). The pre-filled host,
  ; port and database must then not be sent on their own, or they would look like a partial new connection.
  ${If} $DbUser == ""
  ${AndIf} $DbPassword == ""
    StrCpy $DbHost ""
    StrCpy $DbPort ""
    StrCpy $DbName ""
  ${EndIf}

  IfFileExists "$APPDATA\Weatherford\WellSite AutoPilot\server.settings.json" ExistingSettings FreshSettings

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
  IfFileExists "$APPDATA\Weatherford\WellSite AutoPilot\server.settings.json" 0 +2
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

Function WriteInstallStep
  Exch $8
  Push $9
  CreateDirectory "$APPDATA\Weatherford\WellSite AutoPilot\Diagnostics"
  FileOpen $9 "$APPDATA\Weatherford\WellSite AutoPilot\Diagnostics\installer-steps.log" a
  FileSeek $9 0 END
  FileWrite $9 "$8$\r$\n"
  FileClose $9
  Pop $9
  Pop $8
FunctionEnd

Section "WellSite AutoPilot" SEC_CORE
  SetShellVarContext all

  ; Upgrade: running services lock their executables and an old Server must not run against a migrated
  ; schema, so stop and remove them before any file is replaced. The bootstrapper re-creates them.
  IfFileExists "$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" StopExistingServices SkipStopServices
StopExistingServices:
  nsExec::ExecToStack '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" uninstall-services'
  Pop $0
  Pop $1
  Push "uninstall-services exit $0: $1"
  Call WriteInstallStep
SkipStopServices:
  Push "files extracting"
  Call WriteInstallStep

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

  CreateDirectory "$APPDATA\Weatherford\WellSite AutoPilot"
  CreateDirectory "$APPDATA\Weatherford\WellSite AutoPilot\Logs"
  CreateDirectory "$APPDATA\Weatherford\WellSite AutoPilot\Diagnostics"
  CreateDirectory "$APPDATA\Weatherford\WellSite AutoPilot\Modules"

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

  StrCpy $BootstrapperArgs ""
  ${If} $NetworkAccess != ""
    StrCpy $BootstrapperArgs " --network-access $NetworkAccess"
  ${EndIf}
  ${If} $ServerPort != ""
    StrCpy $BootstrapperArgs "$BootstrapperArgs --port $ServerPort"
  ${EndIf}

  IfFileExists "$APPDATA\Weatherford\WellSite AutoPilot\server.settings.json" ExistingSecuritySettings FreshSecuritySettings

FreshSecuritySettings:
  ${If} $BootstrapAdmin == ""
    Abort "Fresh WellSite AutoPilot installation requires /BOOTSTRAPADMIN=<WindowsIdentity> in silent mode."
  ${EndIf}
  ; nsExec shows the bootstrapper's messages (for example a failed database migration) in the install log.
  nsExec::ExecToLog '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR" --bootstrap-admin "$BootstrapAdmin"$BootstrapperArgs'
  Pop $0
  Goto ServicesInstalled

ExistingSecuritySettings:
  Push "files extracted; running install-services"
  Call WriteInstallStep
  nsExec::ExecToStack '"$INSTDIR\Tools\Bootstrapper\WellSiteAutoPilot.Bootstrapper.exe" install-services --install-root "$INSTDIR"$BootstrapperArgs'
  Pop $0
  Pop $1
  Push "install-services exit $0: $1"
  Call WriteInstallStep

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
