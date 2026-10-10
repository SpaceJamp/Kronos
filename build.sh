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

# Check for .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK not found. Please install .NET 10 SDK.${NC}"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}Found .NET SDK: ${DOTNET_VERSION}${NC}"

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
    dotnet build "$src_dir/Kronos.csproj" -f net10.0 -c "$config" --no-restore
    
    echo -e "${YELLOW}Publishing Linux CLI for $runtime...${NC}"
    local output_dir="$(dirname "$0")/Output/linux-$runtime-$(date +"%Y%m%d-%H%M%S")"
    dotnet publish "$src_dir/Kronos.csproj" -f net10.0 -c "$config" -r "$runtime" --self-contained -o "$output_dir" --no-build
    
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
    dotnet build "$src_dir/Kronos.csproj" -f net10.0-windows10.0.26100.0 -c "$config" --no-restore
    
    echo -e "${YELLOW}Publishing Windows Portable...${NC}"
    local output_dir="$(dirname "$0")/Output/win-x64-portable-$(date +"%Y%m%d-%H%M%S")"
    dotnet publish "$src_dir/Kronos.csproj" -f net10.0-windows10.0.26100.0 -c "$config" -r win-x64 --self-contained -p:PublishSingleFile=true -o "$output_dir" --no-build
    
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