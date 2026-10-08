#!/bin/bash
# Kronos Linux Build Script
# Builds the Kronos CLI for Linux

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Kronos Linux Build Script ===${NC}"
echo ""

# Check for .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK not found. Please install .NET 10 SDK.${NC}"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}Found .NET SDK: ${DOTNET_VERSION}${NC}"

# Navigate to src directory
cd "$(dirname "$0")/src"

echo -e "${YELLOW}Restoring dependencies...${NC}"
dotnet restore Kronos.csproj -f net10.0

echo -e "${YELLOW}Building for Linux (Release)...${NC}"
dotnet build Kronos.csproj -f net10.0 -c Release --no-restore

echo -e "${YELLOW}Publishing for Linux x64...${NC}"
dotnet publish Kronos.csproj -f net10.0 -c Release -r linux-x64 --self-contained -o ../../Output/linux-x64

echo -e "${GREEN}=== Build Complete ===${NC}"
echo -e "${GREEN}Output: $(pwd)/../../Output/linux-x64${NC}"
echo ""
echo -e "${YELLOW}To run: ./Output/linux-x64/Kronos --help${NC}"