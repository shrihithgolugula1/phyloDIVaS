#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_ENV="$ROOT/ClientApp/.env.local"
PIDS=()

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1"
    exit 1
  fi
}

assert_port_free() {
  local port="$1"

  if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port $port is already in use."
    echo "Stop the existing process using that port, then run this script again."
    exit 1
  fi
}

ensure_env_var() {
  local key="$1"
  local value="$2"

  mkdir -p "$(dirname "$CLIENT_ENV")"
  touch "$CLIENT_ENV"

  if ! grep -q "^${key}=" "$CLIENT_ENV"; then
    printf '%s=%s\n' "$key" "$value" >> "$CLIENT_ENV"
  fi
}

prefix_output() {
  local name="$1"
  sed -u "s/^/[${name}] /"
}

start_service() {
  local name="$1"
  shift

  echo "Starting $name..."
  (
    cd "$ROOT"
    "$@" 2>&1 | prefix_output "$name"
  ) &
  PIDS+=("$!")
}

cleanup() {
  echo
  echo "Stopping phyloDIVaS services..."
  for pid in "${PIDS[@]:-}"; do
    [ -n "$pid" ] || continue
    if kill -0 "$pid" >/dev/null 2>&1; then
      kill "$pid" >/dev/null 2>&1 || true
    fi
  done
  wait >/dev/null 2>&1 || true
}

trap cleanup EXIT INT TERM

require_command dotnet
require_command npm
require_command sed
require_command lsof
require_command curl
require_command open

ensure_env_var "VITE_ANALYSIS_API_URL" "http://localhost:7040"
ensure_env_var "VITE_SEARCH_API_URL" "https://localhost:7039"
ensure_env_var "VITE_SEARCH_HISTORY_API_URL" "https://localhost:7227"
ensure_env_var "VITE_REGISTRATION_API_URL" "https://localhost:7072"
ensure_env_var "VITE_ACCOUNT_API_URL" "https://localhost:7017"
ensure_env_var "VITE_LOGIN_API_URL" "https://localhost:7149"
ensure_env_var "VITE_FEEDBACK_API_URL" "https://localhost:7183"

echo "Starting phyloDIVaS from:"
echo "$ROOT"
echo

for port in 3000 7040 7039 7227 7072 7149 7017 7183; do
  assert_port_free "$port"
done

start_service "AnalysisAPI" env DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false dotnet run --project AnalysisAPI/AnalysisAPI.csproj
start_service "SearchAPI" dotnet run --project SearchAPI/SearchAPI.csproj --launch-profile https
start_service "SearchHistoryAPI" dotnet run --project SearchHistoryAPI/SearchHistoryAPI.csproj --launch-profile https
start_service "RegistrationAPI" dotnet run --project RegistrationControllerAPI/RegistrationControllerAPI.csproj --launch-profile https
start_service "LoginBffAPI" dotnet run --project LoginBffAPI/LoginBffAPI.csproj --launch-profile https
start_service "AccountAPI" dotnet run --project AccountAPI/AccountAPI.csproj --launch-profile https
start_service "FeedbackAPI" dotnet run --project FeedbackAPI/FeedbackAPI.csproj --launch-profile https

echo "Starting ClientApp..."
(
  cd "$ROOT/ClientApp"
  if [ ! -d node_modules ]; then
    npm install
  fi
  npm run dev -- --host 127.0.0.1 --port 3000 2>&1 | prefix_output "ClientApp"
) &
PIDS+=("$!")

echo
echo "All services are starting in this terminal."
echo "The app will open automatically when the ClientApp is ready."
echo "Press Ctrl+C here to stop every service."
echo

(
  for _ in {1..60}; do
    if curl -fsS http://127.0.0.1:3000 >/dev/null 2>&1; then
      open http://localhost:3000
      exit 0
    fi
    sleep 1
  done
  echo "ClientApp did not become ready in time; open http://localhost:3000 manually."
) &
PIDS+=("$!")

wait
