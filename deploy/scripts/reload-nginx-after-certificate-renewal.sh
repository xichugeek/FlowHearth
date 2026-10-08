#!/usr/bin/env bash
set -euo pipefail

nginx_binary="${NGINX_BINARY:-nginx}"

"${nginx_binary}" -t
"${nginx_binary}" -s reload
