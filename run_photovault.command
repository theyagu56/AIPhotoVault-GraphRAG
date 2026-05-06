#!/usr/bin/env zsh
# ════════════════════════════════════════════════════════
#  PhotoVault — Run Script
#  Double-click this file to launch the API + Frontend.
# ════════════════════════════════════════════════════════

# Change to the script's directory
cd "$(dirname "$0")"

echo ""
echo "╔══════════════════════════════════════════╗"
echo "║       PhotoVault Launcher (Full)         ║"
echo "║   API → :5050   Frontend → :3000         ║"
echo "╚══════════════════════════════════════════╝"
echo ""

# ── 1. Check for .NET ────────────────────────────────────
if ! command -v dotnet &>/dev/null; then
  echo "❌  .NET SDK not found."
  echo ""
  echo "Installing via Homebrew..."
  if ! command -v brew &>/dev/null; then
    echo "Homebrew not found either. Please install .NET manually:"
    echo "  https://dotnet.microsoft.com/download"
    read "?Press Enter to exit..."
    exit 1
  fi
  brew install --cask dotnet-sdk
fi

DOTNET_VER=$(dotnet --version 2>/dev/null)
echo "✅  .NET SDK: $DOTNET_VER"

# ── 2. Check for Node / npm ──────────────────────────────
if ! command -v node &>/dev/null; then
  echo "❌  Node.js not found. Installing via Homebrew..."
  brew install node
fi
NODE_VER=$(node --version 2>/dev/null)
echo "✅  Node: $NODE_VER"

# ── 3. Init MediaRoot folder structure ───────────────────
MEDIAROOT="/Volumes/LaCie/ONE PHOTOS/AI Photo & Video Album/MediaRoot"
if [ ! -d "$MEDIAROOT" ]; then
  echo ""
  echo "📁  Creating MediaRoot at:"
  echo "    $MEDIAROOT"
  bash scripts/init_mediaroot.sh "$MEDIAROOT"
else
  echo "✅  MediaRoot exists: $MEDIAROOT"
fi

# ── 4. Restore NuGet packages ────────────────────────────
echo ""
echo "📦  Restoring .NET packages..."
dotnet restore src/PhotoVault.API/PhotoVault.API.csproj --nologo -q

# ── 5. Build API ─────────────────────────────────────────
echo "🔨  Building PhotoVault API..."
dotnet build src/PhotoVault.API/PhotoVault.API.csproj -c Debug --no-restore --nologo -q

if [ $? -ne 0 ]; then
  echo ""
  echo "❌  API build failed. See errors above."
  read "?Press Enter to exit..."
  exit 1
fi
echo "✅  API build succeeded"

# ── 6. Install frontend npm packages (if needed) ─────────
if [ ! -d "frontend/node_modules" ]; then
  echo ""
  echo "📦  Installing frontend npm packages..."
  (cd frontend && npm install)
fi

# ── 7. Free ports ────────────────────────────────────────
echo ""
echo "🔌  Freeing ports 5050 and 3000..."
for PORT in 5050 3000; do
  lsof -ti:$PORT 2>/dev/null | xargs kill -9 2>/dev/null || true
done
sleep 2
# Second attempt if still occupied
for PORT in 5050 3000; do
  if lsof -ti:$PORT &>/dev/null; then
    echo "⚠️  Port $PORT still in use — retrying..."
    lsof -ti:$PORT | xargs kill -9 2>/dev/null || true
    sleep 2
  fi
done
echo "✅  Ports 5050 and 3000 are free"

# ── 8. Launch API in background ──────────────────────────
echo ""
echo "🚀  Starting PhotoVault API on http://localhost:5050 ..."
ASPNETCORE_ENVIRONMENT=Development AllowDevLogin=true \
dotnet run --project src/PhotoVault.API/PhotoVault.API.csproj \
           --no-build \
           --urls "http://localhost:5050" &
API_PID=$!
echo "    API PID: $API_PID"

# Give the API a moment to bind
sleep 3

# Verify API startup before launching frontend
if ! curl -sf "http://localhost:5050/health" >/dev/null; then
  echo ""
  echo "❌  API failed to start on :5050. Not launching frontend."
  kill $API_PID 2>/dev/null || true
  read "?Press Enter to exit..."
  exit 1
fi
echo "✅  API is UP"

# ── 9. Launch Next.js frontend ───────────────────────────
echo ""
echo "🌐  Starting PhotoVault Frontend on http://localhost:3000 ..."
(cd frontend && npm run dev -- --port 3000) &
FE_PID=$!
echo "    Frontend PID: $FE_PID"

# ── 10. Summary ──────────────────────────────────────────
echo ""
echo "════════════════════════════════════════════"
echo "  PhotoVault is running!"
echo ""
echo "  📷  App       →  http://localhost:3000"
echo "  🔧  API       →  http://localhost:5050"
echo "  📖  Swagger   →  http://localhost:5050/swagger"
echo ""
echo "  Press Ctrl+C to stop both servers."
echo "════════════════════════════════════════════"
echo ""

# ── 11. Wait and cleanup on Ctrl+C ───────────────────────
trap "echo ''; echo 'Stopping servers…'; kill $API_PID $FE_PID 2>/dev/null; exit 0" INT TERM

wait $API_PID $FE_PID
