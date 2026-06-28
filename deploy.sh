#!/usr/bin/env bash
#
# Развёртывание VPN Dashboard на чистой Ubuntu 22.04 VPS.
# Запускать из корня репозитория после `git clone`:
#   sudo bash deploy.sh
#
# Что делает:
#   1. Ставит Docker (если нет).
#   2. Спрашивает домен и пароль администратора, генерирует секреты, пишет .env.
#   3. Поднимает приложение + Caddy (reverse-proxy с автоматическим TLS на 443).
#
set -euo pipefail
cd "$(dirname "$0")"

# --- 0. Права ---
if [ "$(id -u)" -ne 0 ]; then
    echo "Запустите через sudo:  sudo bash deploy.sh" >&2
    exit 1
fi

# --- 1. Docker ---
if ! command -v docker >/dev/null 2>&1; then
    echo "==> Устанавливаю Docker..."
    curl -fsSL https://get.docker.com | sh
fi
if ! docker compose version >/dev/null 2>&1; then
    echo "==> Устанавливаю docker compose plugin..."
    apt-get update -y && apt-get install -y docker-compose-plugin
fi

# --- 2. Открыть порты в ufw (если включён) ---
if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    echo "==> Открываю порты 80/443 в ufw..."
    ufw allow 80/tcp  >/dev/null || true
    ufw allow 443/tcp >/dev/null || true
fi

# --- 3. Конфигурация (.env) ---
need_config=1
if [ -f .env ] && grep -q "^Admin__PasswordHash=." .env; then
    echo "==> .env уже настроен — пропускаю генерацию."
    need_config=0
fi

if [ "$need_config" -eq 1 ]; then
    read -rp "Домен (A-запись уже должна указывать на этот сервер), напр. vpn.example.com: " DOMAIN
    if [ -z "${DOMAIN:-}" ]; then echo "Домен обязателен." >&2; exit 1; fi
    read -rsp "Пароль администратора: " ADMIN_PASSWORD; echo
    if [ -z "${ADMIN_PASSWORD:-}" ]; then echo "Пароль обязателен." >&2; exit 1; fi

    SEG1=$(cat /proc/sys/kernel/random/uuid)
    SEG2=$(cat /proc/sys/kernel/random/uuid)
    export DOMAIN

    # Предварительный .env (без хеша) — нужен compose'у для сборки.
    cat > .env <<EOF
DOMAIN=$DOMAIN
Admin__SecretPathSegment1=$SEG1
Admin__SecretPathSegment2=$SEG2
Admin__PasswordHash=
Admin__PasswordSalt=
EOF

    echo "==> Собираю образ..."
    docker compose build vpndashboard

    echo "==> Генерирую хеш пароля..."
    HASH_OUT=$(docker compose run --rm --no-deps -T vpndashboard hash-password "$ADMIN_PASSWORD")
    HASH=$(printf '%s\n' "$HASH_OUT" | sed -n 's/^Admin__PasswordHash=//p' | tr -d '\r')
    SALT=$(printf '%s\n' "$HASH_OUT" | sed -n 's/^Admin__PasswordSalt=//p' | tr -d '\r')
    if [ -z "$HASH" ] || [ -z "$SALT" ]; then
        echo "Не удалось сгенерировать хеш пароля." >&2
        echo "$HASH_OUT" >&2
        exit 1
    fi

    # Финальный .env.
    cat > .env <<EOF
DOMAIN=$DOMAIN
Admin__SecretPathSegment1=$SEG1
Admin__SecretPathSegment2=$SEG2
Admin__PasswordHash=$HASH
Admin__PasswordSalt=$SALT
EOF
    chmod 600 .env
    echo "==> .env создан."
fi

# --- 4. Запуск ---
echo "==> Поднимаю сервисы..."
docker compose up -d --build

# --- 5. Итог ---
# shellcheck disable=SC1091
set -a; . ./.env; set +a
echo
echo "============================================================"
echo " Готово. Caddy получит TLS-сертификат при первом обращении."
echo
echo " Админка:  https://${DOMAIN}/${Admin__SecretPathSegment1}/${Admin__SecretPathSegment2}/"
echo
echo " Если сертификат не выдаётся — проверьте, что A-запись домена"
echo " указывает на этот сервер и порты 80/443 открыты."
echo " Логи:  docker compose logs -f"
echo "============================================================"
