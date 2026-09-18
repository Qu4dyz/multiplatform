#!/usr/bin/env bash
# Idempotent Cloud Agent setup for the GameAnalytics (.NET 9 / Avalonia) solution.
# Installs the .NET 9 SDK system-wide, the native graphical libraries Avalonia
# needs on Linux, then restores and builds the solution.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_INSTALL_DIR="/usr/lib/dotnet"

export DEBIAN_FRONTEND=noninteractive
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "==> Installing native dependencies (Avalonia rendering, fonts, Xvfb)"
sudo apt-get update -qq
sudo apt-get install -y --no-install-recommends \
    git curl ca-certificates \
    xvfb x11-utils \
    fontconfig fonts-liberation \
    libx11-6 libx11-xcb1 libxcursor1 libxi6 libxrandr2 \
    libfontconfig1 libice6 libsm6 libglib2.0-0 libgl1 libegl1

echo "==> Ensuring the .NET 9 SDK is installed"
if ! /usr/local/bin/dotnet --list-sdks 2>/dev/null | grep -q '^9\.'; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    sudo bash /tmp/dotnet-install.sh --channel 9.0 --install-dir "$DOTNET_INSTALL_DIR"
    sudo ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
fi

export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
export PATH="$DOTNET_INSTALL_DIR:$PATH"

echo "==> .NET SDK version: $(dotnet --version)"

echo "==> Restoring and building the solution (Release)"
cd "$REPO_ROOT"
dotnet restore GameAnalytics.sln
dotnet build GameAnalytics.sln --configuration Release --no-restore

echo "==> Setup complete"
