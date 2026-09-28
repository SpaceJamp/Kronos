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
  ; MUI's directory page seeds $INSTDIR from the InstallDir directive, which is declared below as a
  ; per user path, so the page always starts from a location that is writable without elevation.
  ; .onVerifyInstDir then only ever validates that path, and never rewrites it, which is what
  ; finally stopped the install path from changing under the user.
  ;
  ; An existing install's location wins, so an upgrade keeps its folder. Read from both hives
  ; because the uninstall registry key is written to whichever the installer ran as.
  ClearErrors
  ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
  ${IfNot} ${Errors}
    StrCpy $INSTDIR "$0\"
    StrCpy $DEFAULT_INSTALL_PATH "$0"
  ${EndIf}
  ClearErrors
  ReadRegStr $0 HKCU "${UNINST_KEY}" "InstallLocation"
  ${IfNot} ${Errors}
    StrCpy $INSTDIR "$0\"
    StrCpy $DEFAULT_INSTALL_PATH "$0"
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

; Removed: DirectoryPagePre and OnInstFilesPre.
;
; Both were attached with "!define MUI_PAGE_CUSTOMFUNCTION_PRE", which does not exist in Modern UI 2.
; The MUI2 tree contains no reference to that directive anywhere, so defining it is silently ignored
; and the two functions it appeared to wire up were never called. They compiled with zero warnings
; and zero errors throughout, which is why a dead code path survived two rounds of fixes and kept
; looking like the thing that was running. Only .onInit, .onVerifyInstDir and .onSectionExit style
; callbacks, which NSIS itself invokes, are used now.

; InstallDir is mandatory, not cosmetic. MUI's directory page seeds $INSTDIR from it when the page
; is created, and it does so *after* .onInit, so anything .onInit set is discarded. With no
; InstallDir declared the page reset $INSTDIR to empty, and the append in the old .onVerifyInstDir
; then produced bare "\Kronos" and "Kronos\Kronos" paths relative to the drive root. It is a per
; user path, so it is writable without elevation, which is what allows the installer to run as a
; normal user now.
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"

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
  ; Validate only. This function must never rewrite $INSTDIR.
  ;
  ; It used to append a subfolder when the chosen path did not contain a hardcoded product name
  ; string, "dlss", which was the name before the rename. Two things went wrong from there. The
  ; string no longer matched anything, including the app's own folder, so the append fired on every
  ; install. And when $INSTDIR reached this function empty, which happened because no InstallDir was
  ; declared for the directory page to seed it from, the append produced a bare "\Kronos" and then
  ; "Kronos\Kronos": paths relative to the root of the current drive. Both were reported to the user
  ; as an unwritable folder, which sent them chasing administrator rights when the real problem was
  ; that the path was nonsense.
  ;
  ; Nothing needs appending now. InstallDir gives the page a per user default, and a user who
  ; deliberately chooses some other folder has chosen it, so it is used exactly as given. The only
  ; job left is to fail early and clearly if it cannot be written to, instead of extracting 600+
  ; files and dying on the first one.
  ;
  ; Create the folder before testing it. FileOpen on a path whose directory does not exist fails
  ; with "path not found" rather than "access denied", and NSIS surfaces both through the same
  ; error flag, so probing a folder that has not been created yet reported "Kronos cannot write to
  ; this folder" for a location that is perfectly writable. On a first install that is every install,
  ; because %LOCALAPPDATA%\Programs\Kronos does not exist beforehand.
  ClearErrors
  CreateDirectory "$INSTDIR"
  IfErrors folder_unusable

  ClearErrors
  FileOpen $1 "$INSTDIR\.__kronos_write_test" w
  IfErrors folder_unusable
  FileClose $1
  Delete "$INSTDIR\.__kronos_write_test"
  Goto path_ok

  folder_unusable:
  MessageBox MB_OK|MB_ICONEXCLAMATION "Kronos cannot use this folder:$\r$\n$INSTDIR$\r$\n$\r$\nIt could not be created, or is not writable. Pick a different folder, for example one under your user profile.$\r$\n$\r$\nIf you chose Program Files, it needs administrator rights, and this installer no longer requests them.$\r$\n$\r$\nNothing has been installed."
  Abort

  path_ok:
FunctionEnd


; Removed: OnInstFilesPre, which is where the "Install path updated to ..." message came from. It
; appended a subfolder and rewrote $INSTDIR during the install, which is exactly the behaviour that
; produced every wrong path reported. It was also never invoked.


; This is disabled until I can figure out how to make it launch as admin
; Used to launch Kronos after install is complete.
;Function LaunchLink
;  ExecShell "" "$SMPROGRAMS\Kronos.lnk"
;FunctionEnd


; Install as a normal user, no elevation.
;
; This was "highest", which raised a UAC prompt on every run. That bought nothing, because the
; default install location is %LOCALAPPDATA%\Programs\Kronos, which any user can write, and it
; actively hurt in two ways. A declined prompt left the installer running unelevated while it still
; believed it had rights, and the prompt itself became the thing standing between a user and a
; successful install, on a machine where nothing needed administrator rights at all.
;
; The writability check in .onVerifyInstDir still catches a user who deliberately picks somewhere
; unwritable, and tells them so in one sentence before anything is written.
RequestExecutionLevel user


; App version information
Name "Kronos"
!define MUI_ICON "..\..\src\Assets\icon.ico"
!define MUI_PRODUCT "Kronos"

; Defined once here rather than repeated per key, which is how the version used to drift out of
; step between ProductVersion, FileVersion and the uninstall registry entry. Keep in step with
; <Version> in the csproj and app_version in ..\config.cmd.
; NSIS requires VIProductVersion to be a 4 part number, and the assembly version is APP_VERSION
; with an implied trailing .0, so the two forms are both spelled out here.
!define APP_VERSION "1.45"
!define APP_VERSION_4PART "1.45.0.0"

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
; No MUI_PAGE_CUSTOMFUNCTION_PRE here. That directive does not exist in Modern UI 2, so defining it
; does nothing at all and the functions it appeared to attach to were never called. The path is
; seeded by InstallDir, upgraded by .onInit, and validated by .onVerifyInstDir, all of which NSIS
; itself invokes.
!insertmacro MUI_PAGE_DIRECTORY
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
