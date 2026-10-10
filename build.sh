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

# ---------------------------------------------------------------------------
# Restore
#
# Several passes rather than one, because no single command works everywhere and
# a failure here is opaque: `dotnet restore` can fail without writing the assets
# file, and the `--no-restore` build that follows then reports "NETSDK1004: Assets
# file project.assets.json not found", which names the symptom and not the cause.
#
#   Pass 1  every target framework - preferred. The only pass that leaves the
#           committed lock file untouched, so it keeps builds reproducible.
#   Pass 2  net10.0 only - never evaluates the Windows target framework, which
#           is the most likely reason pass 1 fails on Linux.
#   Pass 3  net10.0 only plus the runtime identifier - adds the RID-specific
#           assets that the build needs when it is given -r.
#
# Passes 2 and 3 restore one framework, which rewrites the committed
# src/packages.lock.json to contain only that framework - 553 lines of it, as
# measured. So the lock file is copied aside first and put back afterwards, with
# a trap for the interrupted case. Quietly deleting the Windows target's
# dependency graph from a committed file would be worse than any restore failure.
# ---------------------------------------------------------------------------
LOCK_FILE_BACKUP=""

save_lock_file() {
    local lock_file="$SRC_DIR/packages.lock.json"
    LOCK_FILE_BACKUP=""

    if [ -f "$lock_file" ]; then
        LOCK_FILE_BACKUP="$(mktemp)"
        cp "$lock_file" "$LOCK_FILE_BACKUP" || LOCK_FILE_BACKUP=""
    fi
}

put_lock_file_back() {
    local lock_file="$SRC_DIR/packages.lock.json"

    if [ -n "$LOCK_FILE_BACKUP" ] && [ -f "$LOCK_FILE_BACKUP" ]; then
        cp "$LOCK_FILE_BACKUP" "$lock_file"
        rm -f "$LOCK_FILE_BACKUP"
        LOCK_FILE_BACKUP=""
    fi
}

# No restore path may leave the committed lock file modified, however it ends.
trap put_lock_file_back EXIT

RESTORE_PASS=""

restore_dependencies() {
    local src_dir=$1
    local runtime=$2
    local assets="$src_dir/obj/project.assets.json"

    local labels=(
        "every target framework"
        "net10.0 only"
        "net10.0 only, runtime $runtime"
    )
    local total=${#labels[@]}
    local pass status

    for pass in 0 1 2; do
        echo -e "${BLUE}Restore pass $((pass + 1)) of $total: ${labels[$pass]}${NC}"

        # An assets file left over from an earlier run would make a restore that
        # genuinely failed look like one that succeeded, so it goes first.
        rm -f "$assets"

        # Only passes 2 and 3 rewrite the lock file.
        if [ "$pass" -gt 0 ]; then
            save_lock_file
        fi

        # `|| status=$?` rather than a bare call: under `set -e` a failing command
        # here would abort the script before the next pass could be tried, which is
        # the opposite of what this function is for.
        status=0
        case "$pass" in
            0) dotnet restore "$src_dir/Kronos.csproj" || status=$? ;;
            1) dotnet restore "$src_dir/Kronos.csproj" -p:TargetFramework=net10.0 || status=$? ;;
            2) dotnet restore "$src_dir/Kronos.csproj" -p:TargetFramework=net10.0 -r "$runtime" || status=$? ;;
        esac

        put_lock_file_back

        if [ "$status" -eq 0 ] && [ -f "$assets" ]; then
            RESTORE_PASS="${labels[$pass]}"
            echo -e "${GREEN}Restore succeeded on pass $((pass + 1)): $RESTORE_PASS.${NC}"
            return 0
        fi

        if [ "$pass" -lt $((total - 1)) ]; then
            echo -e "${YELLOW}That did not produce the assets file. Trying the next approach.${NC}"
            echo ""
        fi
    done

    echo ""
    echo -e "${RED}Restore produced no assets file on any of the $total passes.${NC}"
    echo -e "${RED}The errors above are the real ones. Building with --no-restore would only have${NC}"
    echo -e "${RED}reported NETSDK1004, which names the symptom and hides the cause.${NC}"
    return 1
}

# Set by the build functions below. These used to be called in a command substitution,
# "$(build_linux ...)", which ran the whole build in a subshell and captured its standard output -
# so the progress messages and every compiler and restore error went into an array element instead
# of the terminal, and the user saw none of them.
BUILD_OUTPUT=""

build_linux() {
    local config=$1
    local runtime=$2
    local src_dir=$3

    echo -e "${GREEN}=== Building Linux CLI ===${NC}"

    echo -e "${YELLOW}Restoring dependencies...${NC}"
    restore_dependencies "$src_dir" "$runtime"

    echo -e "${YELLOW}Building Linux CLI ($config)...${NC}"
    dotnet build "$src_dir/Kronos.csproj" -f net10.0 -c "$config" -r "$runtime" --no-restore

    echo -e "${YELLOW}Publishing Linux CLI for $runtime...${NC}"
    local output_dir="$(dirname "$0")/Output/$runtime-$(date +"%Y%m%d-%H%M%S")"
    # No --no-build here: publish has to re-evaluate for the RID anyway (runtime pack, self-contained
    # layout), and pairing an RID-less build with an RID'd --no-build publish made this step look for
    # bin/<cfg>/<tfm>/<rid>/ output that the build had never produced.
    #
    # --no-restore, though. Without it publish runs its own restore, which would evaluate the Windows
    # target framework again on Linux - the thing the passes above exist to avoid - and would rewrite
    # the committed lock file behind the protection in restore_dependencies. The restore above already
    # covered this framework and this RID, which is exactly what the build step then consumed.
    dotnet publish "$src_dir/Kronos.csproj" -f net10.0 -c "$config" -r "$runtime" --self-contained --no-restore -o "$output_dir"

    echo -e "${GREEN}Linux CLI published to: $output_dir${NC}"
    BUILD_OUTPUT="$output_dir"
}

build_windows() {
    local config=$1
    local src_dir=$2

    echo -e "${GREEN}=== Building Windows GUI ===${NC}"

    echo -e "${YELLOW}Restoring dependencies...${NC}"
    restore_dependencies "$src_dir" "win-x64"

    echo -e "${YELLOW}Building Windows GUI ($config)...${NC}"
    dotnet build "$src_dir/Kronos.csproj" -f net10.0-windows10.0.26100.0 -c "$config" -r win-x64 --no-restore

    echo -e "${YELLOW}Publishing Windows Portable...${NC}"
    local output_dir="$(dirname "$0")/Output/win-x64-portable-$(date +"%Y%m%d-%H%M%S")"
    # --no-restore for the same reason as the Linux publish: it would otherwise restore again and
    # rewrite the committed lock file outside the protection in restore_dependencies.
    dotnet publish "$src_dir/Kronos.csproj" -f net10.0-windows10.0.26100.0 -c "$config" -r win-x64 --self-contained --no-restore -p:PublishSingleFile=true -o "$output_dir"

    echo -e "${GREEN}Windows Portable published to: $output_dir${NC}"
    BUILD_OUTPUT="$output_dir"
}

outputs=()

case "$TARGET" in
    Linux)
        build_linux "$CONFIGURATION" "$RUNTIME" "$SRC_DIR"
        outputs+=("$BUILD_OUTPUT")
        ;;
    Windows)
        if [[ "$OSTYPE" != "msys" && "$OSTYPE" != "cygwin" && "$OSTYPE" != "win32" ]]; then
            echo -e "${RED}Error: Windows build must run on Windows${NC}"
            exit 1
        fi
        build_windows "$CONFIGURATION" "$SRC_DIR"
        outputs+=("$BUILD_OUTPUT")
        ;;
    All)
        if [[ "$OSTYPE" != "msys" && "$OSTYPE" != "cygwin" && "$OSTYPE" != "win32" ]]; then
            echo -e "${RED}Error: 'All' target requires Windows (for Windows build)${NC}"
            exit 1
        fi
        build_linux "$CONFIGURATION" "$RUNTIME" "$SRC_DIR"
        outputs+=("$BUILD_OUTPUT")
        build_windows "$CONFIGURATION" "$SRC_DIR"
        outputs+=("$BUILD_OUTPUT")
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