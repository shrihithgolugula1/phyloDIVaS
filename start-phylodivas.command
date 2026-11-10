#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_ENV="$ROOT/ClientApp/.env.local"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1"
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

escape_for_applescript() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '%s' "$value"
}

launch_terminal() {
  local title="$1"
  local run_command="$2"
  local root_quoted
  local full_command
  local escaped_command

  printf -v root_quoted '%q' "$ROOT"
  full_command="printf '\\033]0;${title}\\007'; cd ${root_quoted}; ${run_command}; status=\$?; echo; echo '${title} exited with status '\$status; exec bash -l"
  escaped_command="$(escape_for_applescript "$full_command")"

  osascript \
    -e 'tell application "Terminal" to activate' \
    -e "tell application \"Terminal\" to do script \"$escaped_command\""
}

require_command dotnet
require_command npm
require_command osascript

ensure_env_var "VITE_ANALYSIS_API_URL" "http://localhost:7040"
ensure_env_var "VITE_SEARCH_API_URL" "https://localhost:7039"
ensure_env_var "VITE_SEARCH_HISTORY_API_URL" "https://localhost:7227"
ensure_env_var "VITE_REGISTRATION_API_URL" "https://localhost:7072"
ensure_env_var "VITE_ACCOUNT_API_URL" "https://localhost:7017"
ensure_env_var "VITE_LOGIN_API_URL" "https://localhost:7149"
ensure_env_var "VITE_FEEDBACK_API_URL" "https://localhost:7183"

echo "Starting phyloDIVaS services from:"
echo "$ROOT"
echo
echo "Opening one Terminal window per service..."

launch_terminal "phyloDIVaS AnalysisAPI" "DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false dotnet run --project AnalysisAPI/AnalysisAPI.csproj"
sleep 1
launch_terminal "phyloDIVaS SearchAPI" "dotnet run --project SearchAPI/SearchAPI.csproj --launch-profile https"
sleep 1
launch_terminal "phyloDIVaS SearchHistoryAPI" "dotnet run --project SearchHistoryAPI/SearchHistoryAPI.csproj --launch-profile https"
sleep 1
launch_terminal "phyloDIVaS RegistrationAPI" "dotnet run --project RegistrationControllerAPI/RegistrationControllerAPI.csproj --launch-profile https"
sleep 1
launch_terminal "phyloDIVaS LoginBffAPI" "dotnet run --project LoginBffAPI/LoginBffAPI.csproj --launch-profile https"
sleep 1
launch_terminal "phyloDIVaS AccountAPI" "dotnet run --project AccountAPI/AccountAPI.csproj --launch-profile https"
sleep 1
launch_terminal "phyloDIVaS FeedbackAPI" "dotnet run --project FeedbackAPI/FeedbackAPI.csproj --launch-profile https"
sleep 1
launch_terminal "phyloDIVaS ClientApp" "cd ClientApp && if [ ! -d node_modules ]; then npm install; fi && npm run dev -- --host 127.0.0.1 --port 3000"

echo
echo "When the Vite terminal reports it is ready, open:"
echo "http://localhost:3000"
echo
echo "Stop the app by pressing Ctrl+C in each service terminal."
