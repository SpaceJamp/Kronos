# Package instructions
This document assumes you have a correct development environment setup. Each package step may require additional tools. 

Final output for all 3 build scripts is `Output/`.

## build_Portable.cmd
Builds a portable zipped app. This requires [powershell](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows) installed to complete the actual zip step.

## build_Installer.cmd
Builds an installable app, also referred to as an unpackaged app. This requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed. The script that is run to build the installer is in `Installer.iss`.
