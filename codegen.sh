#!/usr/bin/env sh
set -eu

NO_RESTORE=0
if [ "${1-}" = "--no-restore" ]; then
  NO_RESTORE=1
fi

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
SERVER_PATH="$ROOT_DIR/Server/Server"
CLIENT_PATH="$ROOT_DIR/Client"

if [ "$NO_RESTORE" -ne 1 ]; then
  dotnet tool restore
fi

(
  cd "$SERVER_PATH"
  dotnet tool run ulinkrpc-codegen -- \
    --contracts "$ROOT_DIR/Shared" \
    --mode server \
    --server-output Generated \
    --server-namespace Server.Generated
)

(
  cd "$CLIENT_PATH"
  dotnet tool run ulinkrpc-codegen -- \
    --contracts "$ROOT_DIR/Shared" \
    --mode unity \
    --output "Assets/Scripts/Rpc/Generated" \
    --namespace Rpc.Generated
)
