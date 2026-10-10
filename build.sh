#!/bin/bash
# Kronos Build Script (Bash)
# Builds Kronos CLI for Linux, or cross-compiles from Linux
# Usage: ./build.sh [linux|windows|all] [Debug|Release] [linux-x64] [license-key]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
TARGET="${1:-}"
CONFIGURATION="${2:-Release}"
RUNTIME="${3:-linux-x64}"
LICENSE_KEY="${4:-}"

# Auto-detect target if not specified
if [ -z "$TARGET" ]; then
    if [[ "$OSTYPE" == "linux-gnu"* ]]; then
        TARGET="Linux"
    elif [[ "$OSTYPE" == "darwin"* ]]; then
        TARGET="Linux"  # Can cross-compile Linux from macOS
    elif [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" || "$OSTYPE" == "win32" ]]; then
        TARGET="Windows"
    else
        echo -e "${RED}Error: Could not auto-detect platform. Specify target explicitly.${NC}"
        exit 1
    fi
fi

echo -e "${GREEN}=== Kronos Build Script (Bash) ===${NC}"
echo -e "${BLUE}Target: $TARGET | Configuration: $CONFIGURATION | Runtime: $RUNTIME${NC}"
echo ""

# ---------------------------------------------------------------------------
# .NET SDK
#
# The project targets net10.0, which needs the .NET 10 SDK specifically - an
# installed .NET 8 or 9 will not build it, so "is dotnet on PATH" is not a
# sufficient check. If the required SDK is missing we install it with
# Microsoft's official dotnet-install.sh into ~/.dotnet rather than system-wide,
# because a system-wide install needs root and this script should not ask for it.
# ---------------------------------------------------------------------------
REQUIRED_SDK_MAJOR=10
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"

# Prints the major version of the dotnet on PATH, or fails if there is none.
dotnet_sdk_major() {
    command -v dotnet >/dev/null 2>&1 || return 1
    local version
    version=$(dotnet --version 2>/dev/null) || return 1
    # "10.0.301" -> "10"
    printf '%s' "${version%%.*}"
}

has_required_sdk() {
    local major
    major=$(dotnet_sdk_major) || return 1
    [ "$major" -ge "$REQUIRED_SDK_MAJOR" ]
}

# Puts a ~/.dotnet install on PATH for this process and for the user's future
# shells, if it is not there already. Both halves matter: without the export the
# rest of this script cannot find it, and without the shell profile change the
# user's next build would fail the same way this one did.
use_local_dotnet() {
    case ":${PATH}:" in
        *":${DOTNET_INSTALL_DIR}:"*) : ;;
        *) export PATH="${DOTNET_INSTALL_DIR}:${PATH}" ;;
    esac

    # Append rather than prepend to the profile, and check first so repeat runs
    # do not keep adding the same line.
    local profile="${HOME}/.profile"
    if [ -f "$profile" ] && ! grep -qF "DOTNET_INSTALL_DIR" "$profile"; then
        {
            echo ""
            echo "# Added by Kronos build.sh"
            echo "export DOTNET_INSTALL_DIR=\"\${DOTNET_INSTALL_DIR:-$HOME/.dotnet}\""
            echo "export PATH=\"\$DOTNET_INSTALL_DIR:\$PATH\""
        } >> "$profile"
        echo -e "${BLUE}Added ~/.dotnet to your PATH in ~/.profile (applies to new shells).${NC}"
    fi
}

install_dotnet_sdk() {
    local script="${DOTNET_INSTALL_DIR}/dotnet-install.sh"

    mkdir -p "$DOTNET_INSTALL_DIR" || return 1

    if [ ! -f "$script" ]; then
        echo -e "${YELLOW}Downloading Microsoft's dotnet-install.sh...${NC}"
        if command -v curl >/dev/null 2>&1; then
            curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script" || return 1
        elif command -v wget >/dev/null 2>&1; then
            wget -qO "$script" https://dot.net/v1/dotnet-install.sh || return 1
        else
            echo -e "${RED}Neither curl nor wget is available, so the SDK cannot be downloaded.${NC}"
            return 1
        fi
        chmod +x "$script"
    fi

    echo -e "${YELLOW}Installing .NET ${REQUIRED_SDK_MAJOR} SDK into ${DOTNET_INSTALL_DIR}...${NC}"
    # --channel tracks the latest 10.0.x rather than pinning a patch, so a
    # security update in the SDK does not require changing this script. The
    # resulting apphost and framework are what the published self-contained
    # output is built against.
    "$script" --channel "${REQUIRED_SDK_MAJOR}.0" --install-dir "$DOTNET_INSTALL_DIR" --no-path || return 1
}

if ! has_required_sdk; then
    existing=$(dotnet_sdk_major || echo "none")

    if [ "$existing" = "none" ]; then
        echo -e "${YELLOW}No .NET SDK found.${NC}"
    else
        echo -e "${YELLOW}.NET ${existing} SDK found, but this project needs .NET ${REQUIRED_SDK_MAJOR}.${NC}"
    fi

    if ! install_dotnet_sdk; then
        echo ""
        echo -e "${RED}Could not install the .NET ${REQUIRED_SDK_MAJOR} SDK automatically.${NC}"
        echo -e "${RED}Install it manually and re-run:${NC}"
        echo "  https://dotnet.microsoft.com/download/dotnet/10.0"
        exit 1
    fi

    use_local_dotnet

    if ! has_required_sdk; then
        echo -e "${RED}SDK install reported success but .NET ${REQUIRED_SDK_MAJOR} is still not available.${NC}"
        exit 1
    fi

    echo ""
fi

DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}Using .NET SDK: ${DOTNET_VERSION}${NC}"

# Set license key env var if provided
if [ -n "$LICENSE_KEY" ]; then
    export IMAGESHARP_LICENSE_KEY="$LICENSE_KEY"
    echo -e "${GREEN}Using SixLabors ImageSharp license key${NC}"
fi

SRC_DIR="$(dirname "$0")/src"
TIMESTAMP=$(date +"%Y%m%d-%H%M%S")

build_linux() {
    local config=$1
    local runtime=$2
    local src_dir=$3
    
    echo -e "${GREEN}=== Building Linux CLI ===${NC}"
    
    echo -e "${YELLOW}Restoring dependencies...${NC}"
    dotnet restore "$src_dir/Kronos.csproj" -f net10.0
    
    echo -e "${YELLOW}Building Linux CLI ($config)...${NC}"
    dotnet build "$src_dir/Kronos.csproj" -f net10.0 -c "$config" -r "$runtime" --no-restore

    echo -e "${YELLOW}Publishing Linux CLI for $runtime...${NC}"
    local output_dir="$(dirname "$0")/Output/$runtime-$(date +"%Y%m%d-%H%M%S")"
    # No --no-build here: publish has to re-evaluate for the RID anyway (runtime pack, self-contained
    # layout), and pairing an RID-less build with an RID'd --no-build publish made this step look for
    # bin/<cfg>/<tfm>/<rid>/ output that the build had never produced.
    dotnet publish "$src_dir/Kronos.csproj" -f net10.0 -c "$config" -r "$runtime" --self-contained -o "$output_dir"
    
    echo -e "${GREEN}Linux CLI published to: $output_dir${NC}"
    echo "$output_dir"
}

build_windows() {
    local config=$1
    local src_dir=$2
    
    echo -e "${GREEN}=== Building Windows GUI ===${NC}"
    
    echo -e "${YELLOW}Restoring dependencies...${NC}"
    dotnet restore "$src_dir/Kronos.csproj"
    
    echo -e "${YELLOW}Building Windows GUI ($config)...${NC}"
    dotnet build "$src_dir/Kronos.csproj" -f net10.0-windows10.0.26100.0 -c "$config" -r win-x64 --no-restore

    echo -e "${YELLOW}Publishing Windows Portable...${NC}"
    local output_dir="$(dirname "$0")/Output/win-x64-portable-$(date +"%Y%m%d-%H%M%S")"
    dotnet publish "$src_dir/Kronos.csproj" -f net10.0-windows10.0.26100.0 -c "$config" -r win-x64 --self-contained -p:PublishSingleFile=true -o "$output_dir"
    
    echo -e "${GREEN}Windows Portable published to: $output_dir${NC}"
    echo "$output_dir"
}

outputs=()

case "$TARGET" in
    Linux)
        outputs+=("$(build_linux "$CONFIGURATION" "$RUNTIME" "$SRC_DIR")")
        ;;
    Windows)
        if [[ "$OSTYPE" != "msys" && "$OSTYPE" != "cygwin" && "$OSTYPE" != "win32" ]]; then
            echo -e "${RED}Error: Windows build must run on Windows${NC}"
            exit 1
        fi
        outputs+=("$(build_windows "$CONFIGURATION" "$SRC_DIR")")
        ;;
    All)
        if [[ "$OSTYPE" != "msys" && "$OSTYPE" != "cygwin" && "$OSTYPE" != "win32" ]]; then
            echo -e "${RED}Error: 'All' target requires Windows (for Windows build)${NC}"
            exit 1
        fi
        outputs+=("$(build_linux "$CONFIGURATION" "$RUNTIME" "$SRC_DIR")")
        outputs+=("$(build_windows "$CONFIGURATION" "$SRC_DIR")")
        ;;
    *)
        echo -e "${RED}Error: Unknown target '$TARGET'. Use: Linux, Windows, or All${NC}"
        exit 1
        ;;
esac

echo ""
echo -e "${GREEN}=== Build Complete ===${NC}"
for output in "${outputs[@]}"; do
    echo -e "${GREEN}Output: $output${NC}"
done
echo ""
echo -e "${YELLOW}To run Linux CLI: ./Kronos --help${NC}"
echo -e "${YELLOW}To run Windows: Double-click Kronos.exe${NC}"
echo ""
echo -e "${YELLOW}Note: Copy the entire output folder to target machine to run.${NC}"