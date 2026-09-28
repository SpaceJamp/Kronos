; DLL compiled from https://github.com/aranor01/FindProcDLL
!addplugindir /x86-ansi "plugins\x86-ansi"
!addplugindir /x86-unicode "plugins\x86-unicode"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

; define name of installer
OutFile "installer.exe"

!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Kronos"

; The product folder name, defined once. The install path guard used to hardcode the old product
; name "dlss" as the string it searched the chosen directory for, so renaming the product stopped it
; matching the app's own directory and it appended a second folder every time, turning
; "C:\Program Files\Kronos" into "C:\Program Files\Kronos\Kronos".
!define APP_NAME "Kronos"
!define APP_EXE "${APP_NAME}.exe"

!define UninstLog "uninstall.log"
Var UninstLog

Var DEFAULT_INSTALL_PATH

Function .onInit
  ; Default to Program Files when we can actually write there, and to a per user location when we
  ; cannot. RequestExecutionLevel highest raises the UAC prompt, but if it is declined, or the
  ; account is not an Administrator, this installer still runs, just unelevated. Offering
  ; C:\Program Files to such a user means offering a location that is certain to fail, which is a
  ; poor first impression and used to end with the install dying hundreds of files in.
  StrCpy $DEFAULT_INSTALL_PATH "$PROGRAMFILES64\${APP_NAME}"
  ClearErrors
  FileOpen $1 "$PROGRAMFILES64\.__kronos_probe" w
  IfErrors use_per_user_path
  FileClose $1
  Delete "$PROGRAMFILES64\.__kronos_probe"
  StrCpy $INSTDIR "$DEFAULT_INSTALL_PATH\"
  Goto have_default_path

use_per_user_path:
  StrCpy $DEFAULT_INSTALL_PATH "$LOCALAPPDATA\Programs\${APP_NAME}"
  StrCpy $INSTDIR "$DEFAULT_INSTALL_PATH\"

have_default_path:
  ; The missing \ on $DEFAULT_INSTALL_PATH is intentional, it is compared against later.
  ClearErrors
  ReadRegStr $0 SHCTX "${UNINST_KEY}" "InstallLocation"
  ${If} ${Errors}
    ; No-op
  ${Else}
    StrCpy $INSTDIR "$0\"
  ${EndIf}

  FindProcDLL::FindProc "${APP_EXE}"

  StrCmp $R0 0 NotRunning
    MessageBox MB_OK|MB_ICONEXCLAMATION "Kronos is currently running. Please close it before continuing with installation." /SD IDOK
    ; This used to fall through and carry on to the directory page. The guard in the install
    ; section does stop before any files are written, so this was a warning that let the user walk
    ; on and only find out at the point of installing, rather than being a half install. Bail here
    ; instead, so the answer arrives before they have chosen anything.
    SetErrorLevel 1
    Quit
  NotRunning:
FunctionEnd

; On uninstall, confirm you want to remove downloaded/imported DLSS files.
Function un.onInit
  
  FindProcDLL::FindProc "${APP_EXE}"
  StrCmp $R0 0 NotRunning
    MessageBox MB_OK|MB_ICONSTOP "Kronos is currently running. Please close it before attempting to uninstall." /SD IDOK
    SetErrorLevel 2
    Quit
  NotRunning:

  MessageBox MB_YESNO "Are you sure you want to uninstall $(^Name)?$\r$\n$\r$\nThis will also remove downloaded and imported files. Changes to your games will remain as they are." /SD IDYES IDYES NoAbort
    Abort
  NoAbort:
FunctionEnd

; Install directory should be the app's own folder. If not, add it.
; See issue #169 for what the consequences are if a user selects a directory
; to install to which already contains other files.
;
; The check is on the last path component, not a substring search for a product name. It used to
; search for the literal "dlss", which was the product name before the rename; after renaming it no
; longer matched, so it fired even on the default "C:\Program Files\Kronos" and appended a second
; Kronos folder to it, announcing "Install path updated to C:\Program Files\Kronos\Kronos".
Function .onVerifyInstDir
  ${GetFileName} $0 $INSTDIR
  ${If} $0 != "${APP_NAME}"
    StrCpy $INSTDIR "$INSTDIR\${APP_NAME}\"
  ${EndIf}

  ; Check we can actually write here, now, before 600+ files are attempted. The usual cause is not
  ; being elevated: RequestExecutionLevel highest raises the UAC prompt, but if that is declined, or
  ; the account is not an Administrator, NSIS carries on unelevated rather than failing. The result
  ; used to be extraction dying partway with a bare "Error opening file for writing:
  ; \Kronos\SomeDependency.dll" and a half written install directory containing no uninstaller and
  ; no registry entry, which gives the user nothing to act on.
  ClearErrors
  FileOpen $1 "$INSTDIR\.__kronos_write_test" w
  IfErrors path_not_writable
  FileClose $1
  Delete "$INSTDIR\.__kronos_write_test"
  Goto path_ok

  path_not_writable:
  MessageBox MB_OK|MB_ICONEXCLAMATION "Kronos cannot write to this folder:$\r$\n$INSTDIR$\r$\n$\r$\nProgram Files needs administrator rights. Close this, right click the installer and choose 'Run as administrator', or pick a different folder such as your Downloads folder.$\r$\n$\r$\nNothing has been installed."
  Abort

  path_ok:
FunctionEnd


Function OnInstFilesPre
  ; Same reasoning as .onVerifyInstDir. This is idempotent with it, because after the first has run
  ; the last component is the app name and this becomes a no-op.
  ${GetFileName} $0 $INSTDIR
  ${If} $0 != "${APP_NAME}"
    StrCpy $INSTDIR "$INSTDIR\${APP_NAME}\"
    MessageBox MB_OK "Install path updated to $INSTDIR"
  ${EndIf}
FunctionEnd


; This is disabled until I can figure out how to make it launch as admin
; Used to launch Kronos after install is complete.
;Function LaunchLink
;  ExecShell "" "$SMPROGRAMS\Kronos.lnk"
;FunctionEnd


; For removing Start Menu shortcut in Windows 7
; RequestExecutionLevel user
RequestExecutionLevel highest


; App version information
Name "Kronos"
!define MUI_ICON "..\..\src\Assets\icon.ico"
!define MUI_PRODUCT "Kronos"

; Defined once here rather than repeated per key, which is how the version used to drift out of
; step between ProductVersion, FileVersion and the uninstall registry entry. Keep in step with
; <Version> in the csproj and app_version in ..\config.cmd.
; NSIS requires VIProductVersion to be a 4 part number, and the assembly version is APP_VERSION
; with an implied trailing .0, so the two forms are both spelled out here.
!define APP_VERSION "1.4.0"
!define APP_VERSION_4PART "1.4.0.0"

VIProductVersion "${APP_VERSION_4PART}"
VIAddVersionKey "ProductName" "Kronos"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileDescription" "Kronos installer"
VIAddVersionKey "FileVersion" "${APP_VERSION_4PART}"
; Shown as "Company" in Explorer's file Properties. Matches the Publisher written
; during install; this fork is not published by the original maintainer.
VIAddVersionKey "CompanyName" "SpaceJamp"
; GPL-3.0 requires the original copyright notices to be preserved, so attribute
; upstream here rather than leaving this standard key unset.
VIAddVersionKey "LegalCopyright" "Kronos is based on DLSS Swapper by beeradmoore, licensed under the GNU GPL v3.0 - see LICENSE."

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!define MUI_PAGE_CUSTOMFUNCTION_PRE OnInstFilesPre
!insertmacro MUI_PAGE_INSTFILES
 

; These indented statements modify settings for MUI_PAGE_FINISH
!define MUI_FINISHPAGE_NOAUTOCLOSE
;!define MUI_FINISHPAGE_RUN
;!define MUI_FINISHPAGE_RUN_CHECKED
;!define MUI_FINISHPAGE_RUN_TEXT "Launch now"
;!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchLink"
!insertmacro MUI_PAGE_FINISH


; Uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES


; Languages
!insertmacro MUI_LANGUAGE "English"


!macro CreateDirectoryToInstaller Path
  CreateDirectory "$INSTDIR\${Path}"
  FileWrite $UninstLog "${Path}$\r$\n"
!macroend


!macro AddFileToInstaller FileName FullFileName
  FileWrite $UninstLog "${FileName}$\r$\n"
  File "/oname=${FileName}" "${FullFileName}"
!macroend


Section -openlogfile
  CreateDirectory "$INSTDIR"
  IfFileExists "$INSTDIR\${UninstLog}" +3
    FileOpen $UninstLog "$INSTDIR\${UninstLog}" w
  Goto +4
    SetFileAttributes "$INSTDIR\${UninstLog}" NORMAL
    FileOpen $UninstLog "$INSTDIR\${UninstLog}" a
    FileSeek $UninstLog 0 END
SectionEnd

 
; start default section
Section

  FindProcDLL::FindProc "${APP_EXE}"
  StrCmp $R0 0 NotRunning
    MessageBox MB_OK|MB_ICONSTOP "Kronos is currently running. Please close it and run the installer again." /SD IDOK
    SetErrorLevel 2
    Quit
  NotRunning:

  ; set the installation directory as the destination for the following actions
  SetOutPath $INSTDIR
  
  ; Check if the install already directory exists
  ; We can't just check the directory exists as the directory is created by creating the uninstall.log file
  IfFileExists "$INSTDIR\${APP_EXE}" InstallProbablyExists Install

  InstallProbablyExists:

    ; If INSTDIR is the default, don't bother promoting to make the upgrade experience easier for existing users. We will just delete it.
    ; This is to fix issues with users using non-default locations and somehow
    ; set their install to C:\Windows\ or something
    StrCmp $INSTDIR $DEFAULT_INSTALL_PATH DeleteOldInstallFiles PromptToDeleteOldInstallFiles
    
    PromptToDeleteOldInstallFiles:
      ; Prompt if it is ok to delete existing directory. This is true by default on silent installs
      MessageBox MB_YESNO|MB_ICONEXCLAMATION 'The directory "$INSTDIR" already exists. Existing app will be uninstalled. Your existing imported and downloaded DLLs will remain. Do you want to continue?' /SD IDYES IDYES DeleteOldInstallFiles
      Quit

    ; Delete the existing install directory
    DeleteOldInstallFiles:
      RMDir /r "$INSTDIR"

  Install:

  ; Adds files from list that was auto-generated by build_Installer.ps1
  !include "FileList.nsh"
  
  ; create the uninstaller
  WriteUninstaller "$INSTDIR\uninstall.exe"
  FileWrite $UninstLog "uninstall.exe$\r$\n"

  ; Calculate install size. This will be updated in app to include data from LOCALAPPDATA\Kronos
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  
  # create a shortcut named "new shortcut" in the start menu programs directory
  # point the new shortcut at the program uninstaller
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteRegStr SHCTX "${UNINST_KEY}" "DisplayName" "Kronos"
  WriteRegStr SHCTX "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  ; Publisher shown in Windows "Installed apps" and Add/Remove Programs.
  ; Kronos is not published by the original DLSS Swapper maintainer, so this must
  ; not name them. Change the string below to your own name or handle.
  WriteRegStr SHCTX "${UNINST_KEY}" "Publisher" "SpaceJamp"
  WriteRegStr SHCTX "${UNINST_KEY}" "DisplayIcon" "$\"$INSTDIR\${APP_EXE}$\""
  WriteRegStr SHCTX "${UNINST_KEY}" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegStr SHCTX "${UNINST_KEY}" "QuietUninstallString" "$\"$INSTDIR\uninstall.exe$\" /S"
  WriteRegStr SHCTX "${UNINST_KEY}" "InstallLocation" $INSTDIR
  WriteRegDWORD SHCTX "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd


; Close the log file off and set it as a readonly hidden system file.
Section -closelogfile
  FileClose $UninstLog
  SetFileAttributes "$INSTDIR\${UninstLog}" READONLY|SYSTEM|HIDDEN
SectionEnd


; uninstaller section start
Section "Uninstall"

  ;Can't uninstall if uninstall log is missing!
  IfFileExists "$INSTDIR\${UninstLog}" +3
    MessageBox MB_OK|MB_ICONSTOP "${UninstLog} not found.$\r$\nUninstallation cannot proceed."
      Abort
 
  Push $R0
  Push $R1
  Push $R2
  SetFileAttributes "$INSTDIR\${UninstLog}" NORMAL
  FileOpen $UninstLog "$INSTDIR\${UninstLog}" r
  StrCpy $R1 -1
 
  GetLineCount:
    ClearErrors
    FileRead $UninstLog $R0
    IntOp $R1 $R1 + 1
    StrCpy $R0 $R0 -2
    Push $R0   
    IfErrors 0 GetLineCount
 
  Pop $R0
 
  LoopRead:
    StrCmp $R1 0 LoopDone
    Pop $R0
 
    IfFileExists "$INSTDIR\$R0\*.*" 0 +3
      RMDir "$INSTDIR\$R0"  #is dir
    Goto +3
    IfFileExists "$INSTDIR\$R0" 0 +2
      Delete "$INSTDIR\$R0" #is file

    IntOp $R1 $R1 - 1
    Goto LoopRead
  LoopDone:
  FileClose $UninstLog
  Delete "$INSTDIR\${UninstLog}"
  RMDir "$INSTDIR"
  Pop $R2
  Pop $R1
  Pop $R0

  ; Remove downloaded and imported DLSS dlls.
  ; Must match Storage.StoragePath in src\Storage.cs, or the uninstaller leaves the database behind.
RMDir /r "$LOCALAPPDATA\Kronos\"
  
  ; Remove registry keys
  DeleteRegKey SHCTX "${UNINST_KEY}"

  ; Remove start menu shortcut.
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"

SectionEnd
