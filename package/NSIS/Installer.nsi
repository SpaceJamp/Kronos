; DLL compiled from https://github.com/aranor01/FindProcDLL
!addplugindir /x86-ansi "plugins\x86-ansi"
!addplugindir /x86-unicode "plugins\x86-unicode"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "StrContains.nsh"
!include "FileFunc.nsh"

; define name of installer
OutFile "installer.exe"

!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Unofficial DLSS Swapper"

!define UninstLog "uninstall.log"
Var UninstLog

Var DEFAULT_INSTALL_PATH

Function .onInit
  ; Set default install location
  StrCpy $INSTDIR "$PROGRAMFILES64\Unofficial DLSS Swapper\"
  ; The missing \ is intentional
  StrCpy $DEFAULT_INSTALL_PATH "$PROGRAMFILES64\Unofficial DLSS Swapper"
  ClearErrors
  ReadRegStr $0 SHCTX "${UNINST_KEY}" "InstallLocation"
  ${If} ${Errors}
    ; No-op
  ${Else}
    StrCpy $INSTDIR "$0\"
  ${EndIf}

  FindProcDLL::FindProc "Unofficial DLSS Swapper.exe"

  StrCmp $R0 0 NotRunning
    MessageBox MB_OK|MB_ICONEXCLAMATION "Unofficial DLSS Swapper is currently running. Please close it before continuing with installation." /SD IDOK
  NotRunning:
FunctionEnd

; On uninstall, confirm you want to remove downloaded/imported DLSS files.
Function un.onInit
  
  FindProcDLL::FindProc "Unofficial DLSS Swapper.exe"
  StrCmp $R0 0 NotRunning
    MessageBox MB_OK|MB_ICONSTOP "Unofficial DLSS Swapper is currently running. Please close it before attempting to uninstall." /SD IDOK
    SetErrorLevel 2
    Quit
  NotRunning:

  MessageBox MB_YESNO "Are you sure you want to uninstall $(^Name)?$\r$\n$\r$\nThis will also remove downloaded and imported files. Changes to your games will remain as they are." /SD IDYES IDYES NoAbort
    Abort
  NoAbort:
FunctionEnd

; Install directory should have "dlss" in it, if not we should add it. 
; See issue #169 for what the consequences are if a user selects a directory
; to install to which already contains other files.
Function .onVerifyInstDir
  ${StrContains} $0 "dlss" $INSTDIR
  StrCmp $0 "" badPath
    Goto done
  badPath:
    StrCpy $INSTDIR "$INSTDIR\Unofficial DLSS Swapper\"
  done:
FunctionEnd


Function OnInstFilesPre
  ; If the install directory does not contain "dlss" in it we should
  ; probably add it to keep the user safe. See issue #169 as to why
  ; this is useful.
  ${StrContains} $0 "dlss" $INSTDIR
  StrCmp $0 "" badPath
    Goto done
  badPath:
    StrCpy $INSTDIR "$INSTDIR\Unofficial DLSS Swapper\"
    MessageBox MB_OK "Install path updated to $INSTDIR"
  done:
FunctionEnd


; This is disabled until I can figure out how to make it launch as admin
; Used to launch Unofficial DLSS Swapper after install is complete.
;Function LaunchLink
;  ExecShell "" "$SMPROGRAMS\Unofficial DLSS Swapper.lnk"
;FunctionEnd


; For removing Start Menu shortcut in Windows 7
; RequestExecutionLevel user
RequestExecutionLevel highest


; App version information
Name "Unofficial DLSS Swapper"
!define MUI_ICON "..\..\src\Assets\icon.ico"
!define MUI_PRODUCT "Unofficial DLSS Swapper"

; Defined once here rather than repeated per key, which is how the version used to drift out of
; step between ProductVersion, FileVersion and the uninstall registry entry. Keep in step with
; <Version> in the csproj and app_version in ..\config.cmd.
; NSIS requires VIProductVersion to be a 4 part number, and the assembly version is APP_VERSION
; with an implied trailing .0, so the two forms are both spelled out here.
!define APP_VERSION "1.3.0"
!define APP_VERSION_4PART "1.3.0.0"

VIProductVersion "${APP_VERSION_4PART}"
VIAddVersionKey "ProductName" "Unofficial DLSS Swapper"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileDescription" "Unofficial DLSS Swapper installer"
VIAddVersionKey "FileVersion" "${APP_VERSION_4PART}"
; Shown as "Company" in Explorer's file Properties. Matches the Publisher written
; during install; this fork is not published by the original maintainer.
VIAddVersionKey "CompanyName" "SpaceJamp"
; GPL-3.0 requires the original copyright notices to be preserved, so attribute
; upstream here rather than leaving this standard key unset.
VIAddVersionKey "LegalCopyright" "Unofficial DLSS Swapper is based on DLSS Swapper by beeradmoore. Licensed under the GNU GPL v3.0 - see LICENSE."

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

  FindProcDLL::FindProc "Unofficial DLSS Swapper.exe"
  StrCmp $R0 0 NotRunning
    MessageBox MB_OK|MB_ICONSTOP "Unofficial DLSS Swapper is currently running. Please close it and run the installer again." /SD IDOK
    SetErrorLevel 2
    Quit
  NotRunning:

  ; set the installation directory as the destination for the following actions
  SetOutPath $INSTDIR
  
  ; Check if the install already directory exists
  ; We can't just check the directory exists as the directory is created by creating the uninstall.log file
  IfFileExists "$INSTDIR\Unofficial DLSS Swapper.exe" InstallProbablyExists Install

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

  ; Calculate install size. This will be updated in app to include data from LOCALAPPDATA\DLSS Swapper
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  
  # create a shortcut named "new shortcut" in the start menu programs directory
  # point the new shortcut at the program uninstaller
  CreateShortcut "$SMPROGRAMS\Unofficial DLSS Swapper.lnk" "$INSTDIR\Unofficial DLSS Swapper.exe"

  WriteRegStr SHCTX "${UNINST_KEY}" "DisplayName" "Unofficial DLSS Swapper"
  WriteRegStr SHCTX "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  ; Publisher shown in Windows "Installed apps" and Add/Remove Programs.
  ; This is an unofficial fork, so it must not claim to be published by the
  ; original maintainer. Change the string below to your own name or handle.
  WriteRegStr SHCTX "${UNINST_KEY}" "Publisher" "SpaceJamp"
  WriteRegStr SHCTX "${UNINST_KEY}" "DisplayIcon" "$\"$INSTDIR\Unofficial DLSS Swapper.exe$\""
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
  ; NOTE: storage folder intentionally still "Unofficial DLSS Swapper" so existing users keep their data.
; NOTE: storage folder intentionally still "DLSS Swapper" so existing users keep their data.
RMDir /r "$LOCALAPPDATA\DLSS Swapper\"
  
  ; Remove registry keys
  DeleteRegKey SHCTX "${UNINST_KEY}"

  ; Remove start menu shortcut.
  Delete "$SMPROGRAMS\Unofficial DLSS Swapper.lnk"

SectionEnd
