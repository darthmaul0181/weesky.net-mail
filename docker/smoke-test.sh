#!/usr/bin/env bash
# Starts IMAGE the way docker-compose.yml runs it, beside a throwaway MariaDB, and checks that it
# migrates, turns healthy and serves the pages. Usage: docker/smoke-test.sh IMAGE [SHORT_COMMIT]
set -euo pipefail

IMAGE=$1
COMMIT=${2:-}
HERE=$(cd "$(dirname "$0")" && pwd)
VERSION=$(tr -d '[:space:]' < "$HERE/VERSION")
RUN=scotty-smoke-$$
PORT=${SMOKE_PORT:-18080}
BASE=http://127.0.0.1:$PORT
WORK=$(mktemp -d "${TMPDIR:-/tmp}/scotty-smoke.XXXXXX")

fail() { echo "::error::$*" >&2; exit 1; }
cleanup() {
  echo "--- container log (last 60 lines) ---"
  docker logs "$RUN-app" 2>&1 | tail -n 60 || true
  docker rm -fv "$RUN-app" "$RUN-db" >/dev/null 2>&1 || true
  docker network rm "$RUN" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

docker network create "$RUN" >/dev/null
docker run -d --name "$RUN-db" --network "$RUN" -e MARIADB_ROOT_PASSWORD=root mariadb:11.4 >/dev/null
# Over TCP: the entrypoint's first, temporary server listens on the socket only, before root's
# password exists.
for _ in $(seq 60); do
  docker exec "$RUN-db" mariadb -h127.0.0.1 -uroot -proot -e 'SELECT 1' >/dev/null 2>&1 && break
  sleep 1
done
sed -e 's/__HOST__/%/g' -e 's/__PASSWORD__/app-secret/g' -e 's/__SCHEMA_PASSWORD__/schema-secret/g' \
  "$HERE/../install/install.sql" | docker exec -i "$RUN-db" mariadb -uroot -proot >/dev/null

DB="Server=$RUN-db;Port=3306;Database=scotty_webmail"
docker run -d --name "$RUN-app" --network "$RUN" -p "127.0.0.1:$PORT:8080" \
  --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true \
  -e "ConnectionStrings__WebmailPreferencesDatabase=$DB;User=scotty_webmail;Password=app-secret" \
  -e "ConnectionStrings__WebmailSchema=$DB;User=scotty_webmail_schema;Password=schema-secret" \
  -e Mail__ImapHost=imap.example.test -e Mail__SmtpHost=smtp.example.test -e Sieve__Host=imap.example.test \
  -e ForwardedHeaders__KnownNetworks__0=172.16.0.0/12 \
  "$IMAGE" >/dev/null

STATUS=
for _ in $(seq 90); do
  STATUS=$(docker inspect -f '{{.State.Status}} {{.State.Health.Status}}' "$RUN-app")
  [ "$STATUS" = 'running healthy' ] && break
  [ "${STATUS%% *}" = running ] || fail "the container stopped: $STATUS"
  sleep 1
done
[ "$STATUS" = 'running healthy' ] || fail "not healthy after 90 s: $STATUS"

# Captured to files first: grep -q stopping early would break the pipe under pipefail.
docker exec "$RUN-db" mariadb -uroot -proot -N -e 'SELECT scriptname FROM scotty_webmail.schema_migrations' > "$WORK/migrations"
grep -qx '0001_initial.sql' "$WORK/migrations" || fail "the database was not migrated"
curl -fsS "$BASE/mail/inbox" -o "$WORK/page" || fail "/mail/inbox did not answer"
grep -q '<div id="root">' "$WORK/page" || fail "/mail/inbox did not answer the index"
[ "$(curl -fsS "$BASE/config.js")" = 'window.SCOTTY_CONFIG = {"apiBase":""};' ] || fail "/config.js is wrong"
[ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/Version")" = 401 ] || fail "/api/Version did not ask to sign in"
docker logs "$RUN-app" > "$WORK/log" 2>&1
grep -q "image $VERSION" "$WORK/log" || fail "the start-up log does not name image $VERSION"

# Every file of the bundle must come back as itself: a type the server does not know would
# fall back to the index, and the browser would get HTML instead.
docker cp "$RUN-app:/app/frontend" "$WORK/frontend"
(cd "$WORK/frontend" && find . -type f ! -name index.html) | while read -r file; do
  curl -fsS "$BASE/${file#./}" -o "$WORK/got" || fail "$file did not download"
  cmp -s "$WORK/frontend/$file" "$WORK/got" || fail "$file came back different"
done

if [ -n "$COMMIT" ]; then
  grep -q "commit $COMMIT" "$WORK/log" || fail "the API does not carry commit $COMMIT"
  grep -rqF "$COMMIT" "$WORK/frontend/assets" || fail "the web app does not carry commit $COMMIT"
fi

echo "Smoke test passed: $IMAGE"
