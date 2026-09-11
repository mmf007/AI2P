#!/bin/sh
# ============================================================
#  AI2P - install.sh  (Linux / macOS)
#  Installing and updating a release (spec ch. 4.3). The full description is in
#  the help: ./install.sh --help
#
#  THE TEXTS LIVE OUTSIDE THE SCRIPT (T-65-S0): the messages in
#  i18n/scripts.<lang>.txt, the help in i18n/help/install.<lang>.txt. The script is
#  never duplicated per language - a new language is a new pair of files.
#  The default language is en; --lang xx and AI2P_LANG override it, in that order.
# ============================================================
set -u

# ПЕРЕМЕННЫХ ОКРУЖЕНИЯ МОЖЕТ НЕ БЫТЬ ВОВСЕ (T-294). Под «set -u» подстановка незаданной
# переменной — это не пустая строка, а ОБРЫВ: /bin/sh Ubuntu (dash) отвечает
# «AI2P_HOME: parameter not set» и бросает установку на середине, а человек видит только
# то, что версия не поменялась. AI2P_HOME задаёт рабочий каталог руками (T-287) и в обычной
# установке не задан НИКОГДА, поэтому нормализуем внешние переменные один раз, здесь,
# а не в каждом месте использования
AI2P_HOME="${AI2P_HOME:-}"
HOME="${HOME:-}"

# каталоги, целиком принадлежащие дистрибутиву: переписываются НАЧИСТО (ТЗ гл. 14, todo47)
WIPE_DIRS="doc wwwroot runtimes"
# опись установки: по ней следующая версия узнаёт, чего в ней не стало
INVENTORY="installed.json"
# СЛУЖБА ОС (T-271): установку можно сделать сервисом (./makeAsServise.sh). Тогда её надо
# остановить перед копированием и вернуть в работу после. Настоящее состояние знает
# СИСТЕМА — её и спрашиваем; пометка serviceMode в config.json это только записка
SERVICE_NAME="AI2P"
SERVICE_KIND=""            # systemd-user | systemd-system | launchd | пусто
SERVICE_UNIT=""
SERVICE_WAS_RUNNING=0

SOURCE="$(cd "$(dirname "$0")" && pwd)"
TARGET=""
FORCE=0
WHATIF=0
RUNTIME_CHECK=1

LANG_OPT=""
HELP=0
BADOPT=""
WANT_LANG=0
for arg in "$@"; do
    if [ "$WANT_LANG" -eq 1 ]; then LANG_OPT="$arg"; WANT_LANG=0; continue; fi
    case "$arg" in
        --force)            FORCE=1 ;;
        --what-if)          WHATIF=1 ;;
        --no-runtime-check) RUNTIME_CHECK=0 ;;
        --lang)             WANT_LANG=1 ;;
        --lang=*)           LANG_OPT="${arg#--lang=}" ;;
        -h|--help)          HELP=1 ;;
        -*)        BADOPT="$arg" ;;
        *)         if [ -z "$TARGET" ]; then TARGET="$arg"; fi ;;
    esac
done

# ТЕКСТЫ СООБЩЕНИЙ ЛЕЖАТ СНАРУЖИ (T-65-S0). Каталога рядом может не оказаться (кто-то
# унёс один install.sh из выкладки) — тогда L отдаёт сам ключ, и скрипт всё равно
# работает: из-за отсутствия перевода установка падать не должна
AI2P_LOC_DIR="$SOURCE/i18n"
if [ -f "$AI2P_LOC_DIR/loc.sh" ]; then
    . "$AI2P_LOC_DIR/loc.sh"
    ai2p_set_lang "$LANG_OPT"
else
    L() { printf '%s\n' "$1"; }
    ai2p_text() { printf '%s' "$1"; }
    ai2p_help() { printf 'No i18n folder next to install.sh\n'; }
fi

if [ -n "$BADOPT" ]; then
    L scr.inst.59 "$BADOPT"
    exit 1
fi
if [ "$HELP" -eq 1 ]; then
    ai2p_help install
    exit 0
fi
if [ -z "$TARGET" ]; then
    L scr.inst.60 "$(basename "$0")"
    ai2p_help install
    exit 1
fi

# --- версия каталога: version.json, а если его нет — спросить саму программу ---
version_of() {
    dir="$1"
    if [ -f "$dir/version.json" ]; then
        sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$dir/version.json" | head -1
        return
    fi
    if [ -x "$dir/AI2P.Server" ]; then
        "$dir/AI2P.Server" --version 2>/dev/null | head -1
        return
    fi
    echo ""
}

# «1.46» -> 46
build_of() {
    echo "$1" | awk -F. '{ if (NF >= 2) print $2 + 0; else print -1 }'
}

# --- СЛУЖБА ЭТОЙ УСТАНОВКИ (T-271) ------------------------------------------------------
# Ищем службу, которая ведёт ИМЕННО СЮДА: чужую (другой каталог) не трогаем никогда.
# Признак — путь установки в ExecStart юнита systemd или в plist launchd
find_service() {
    dir="$1"
    SERVICE_KIND=""; SERVICE_UNIT=""
    unit_user="$HOME/.config/systemd/user/$SERVICE_NAME.service"
    unit_sys="/etc/systemd/system/$SERVICE_NAME.service"
    plist="$HOME/Library/LaunchAgents/$SERVICE_NAME.plist"
    if [ -f "$unit_user" ] && grep -qF "$dir/AI2P.Server" "$unit_user"; then
        SERVICE_KIND="systemd-user"; SERVICE_UNIT="$unit_user"; return 0
    fi
    if [ -f "$unit_sys" ] && grep -qF "$dir/AI2P.Server" "$unit_sys"; then
        SERVICE_KIND="systemd-system"; SERVICE_UNIT="$unit_sys"; return 0
    fi
    if [ -f "$plist" ] && grep -qF "$dir/AI2P.Server" "$plist"; then
        SERVICE_KIND="launchd"; SERVICE_UNIT="$plist"; return 0
    fi
    return 1
}

service_ctl() {
    case "$SERVICE_KIND" in
        systemd-user)   systemctl --user "$@" "$SERVICE_NAME.service" ;;
        systemd-system) systemctl "$@" "$SERVICE_NAME.service" ;;
    esac
}

service_running() {
    case "$SERVICE_KIND" in
        systemd-user)   systemctl --user is-active --quiet "$SERVICE_NAME.service" ;;
        systemd-system) systemctl is-active --quiet "$SERVICE_NAME.service" ;;
        launchd)        launchctl list 2>/dev/null | grep -q "[[:space:]]$SERVICE_NAME\$" ;;
        *)              return 1 ;;
    esac
}

# Пометка «эта установка настроена сервисом» лежит в РАБОЧЕМ config.json, а он не обязан
# быть рядом с программой (T-287): установка в каталог программ системы уводит рабочие
# файлы в /var/lib/ai2p или в ~/.local/share/ai2p. Правило выбора живёт в приложении
# (AppHome) и здесь НЕ повторяется — смотрим в те же места, что и makeAsServise.sh
service_flag_set() {
    # каталог ПРОГРАММ пропускается: config.json может лежать и там, а приложение оттуда
    # его не читает (см. makeAsServise.sh, AppHome)
    own_dir="$TARGET_DIR"
    case "$TARGET_DIR/" in
        /usr/*|/opt/*|/Applications/*) own_dir="" ;;
    esac
    for place in "$AI2P_HOME" "$own_dir" "/var/lib/ai2p" "$HOME/.local/share/ai2p" "$TARGET_DIR"; do
        [ -n "$place" ] || continue
        [ -f "$place/config.json" ] || continue
        grep -q '"serviceMode"[[:space:]]*:[[:space:]]*true' "$place/config.json"
        return $?
    done
    return 1
}

# --- РАНТАЙМ .NET (T-211) --------------------------------------------------------------
# Обычная выкладка собрана БЕЗ рантайма и не запустится, пока на машине нет ASP.NET Core 8.x.
# Именно 8.x, а не «любой посвежее»: приложение net8.0 на рантайме 9/10 стартует, но
# интерактивность Blazor молча умирает — клиентский blazor.web.js версии 8 против сервера
# другой версии (опыт проекта). Поэтому проверяем ровно ветку 8

# признак полной выкладки: "selfContained": true в version.json
self_contained_of() {
    if [ -f "$1/version.json" ] &&
       grep -q '"selfContained"[[:space:]]*:[[:space:]]*true' "$1/version.json"; then
        echo 1
    else
        echo 0
    fi
}

have_aspnet8() {
    command -v dotnet >/dev/null 2>&1 || return 1
    dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft\.AspNetCore\.App 8\.'
}

install_aspnet8() {
    SUDO=""
    [ "$(id -u)" -ne 0 ] && SUDO="sudo"
    if command -v apt-get >/dev/null 2>&1; then
        $SUDO apt-get update && $SUDO apt-get install -y aspnetcore-runtime-8.0
    elif command -v dnf >/dev/null 2>&1; then
        $SUDO dnf install -y aspnetcore-runtime-8.0
    else
        # macOS и дистрибутивы без пакета: официальный установщик, рядом с тем, что уже есть
        DOTNET_HOME="$HOME/.dotnet"
        if command -v dotnet >/dev/null 2>&1; then
            RESOLVED="$(dirname "$(command -v dotnet)")"
            [ -n "$RESOLVED" ] && [ -w "$RESOLVED" ] && DOTNET_HOME="$RESOLVED"
        fi
        L scr.inst.61 "$DOTNET_HOME"
        if curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/ai2p-dotnet-install.sh; then
            sh /tmp/ai2p-dotnet-install.sh --channel 8.0 --runtime aspnetcore \
                --install-dir "$DOTNET_HOME"
            rm -f /tmp/ai2p-dotnet-install.sh
        else
            L scr.inst.62
            return 1
        fi
    fi
    if have_aspnet8; then
        L scr.inst.5
        return 0
    fi
    L scr.inst.63
    L scr.inst.64
    return 1
}

# Проверка того, без чего выкладка не запустится. Полной выкладке (рантайм внутри)
# проверять нечего — она ровно для того и собирается
confirm_runtime() {
    [ "$RUNTIME_CHECK" -eq 1 ] || return 0
    if [ "$SELF_CONTAINED" -eq 1 ]; then
        L scr.inst.8
        return 0
    fi
    if have_aspnet8; then
        L scr.inst.9
        return 0
    fi

    echo ""
    L scr.inst.10
    L scr.inst.11
    if [ "$WHATIF" -eq 1 ]; then
        L scr.inst.12 "--what-if"
        return 0
    fi
    if [ "$FORCE" -eq 1 ]; then
        install_aspnet8 || true
        return 0
    fi
    ANSWER=""
    if [ -t 0 ]; then
        printf '%s ' "$(ai2p_text scr.inst.13)"
        read -r ANSWER || ANSWER="n"
    else
        ANSWER="n"
    fi
    case "$ANSWER" in
        ""|y|Y|yes|YES|д|Д|да|ДА) install_aspnet8 || true ;;
        *)
            L scr.inst.14
            L scr.inst.15
            ;;
    esac
}

NEW_VERSION="$(version_of "$SOURCE")"
if [ -z "$NEW_VERSION" ]; then
    L scr.inst.16
    exit 1
fi
NEW_BUILD="$(build_of "$NEW_VERSION")"

# «~/ai/AI2P» — обычный вид каталога сервера на Linux/macOS (T-135). Оболочка раскрывает «~»
# не всегда: в кавычках, в переменной, в юните systemd он доходит сюда как обычный символ
case "$TARGET" in
    "~"|"~/"*)
        if [ -z "$HOME" ]; then
            L scr.inst.65
            L scr.inst.66
            exit 1
        fi ;;
esac
case "$TARGET" in
    "~")    TARGET="$HOME" ;;
    # тильду в ОБРАЗЦЕ надо экранировать (T-294): «${TARGET#~/}» оболочка раскрывает
    # прямо в образце, тот превращается в «/home/вы/», префикс не совпадает и в пути
    # остаётся сама тильда — получалось «/home/вы/~/ai/AI2P»
    "~/"*)  TARGET="$HOME/${TARGET#\~/}" ;;
esac
case "$TARGET" in
    /*) TARGET_DIR="$TARGET" ;;
    *)  TARGET_DIR="$(pwd)/$TARGET" ;;
esac

SELF_CONTAINED="$(self_contained_of "$SOURCE")"

echo "=== AI2P install ==="
L scr.inst.17 "$SOURCE" "$NEW_VERSION"
if [ "$SELF_CONTAINED" -eq 1 ]; then
    L scr.inst.18
else
    L scr.inst.19
fi
L scr.inst.20 "$TARGET_DIR"

if [ "$TARGET_DIR" = "$SOURCE" ]; then
    L scr.inst.21
    exit 1
fi

# то, без чего скопированные файлы не запустятся, проверяем ДО копирования (T-211)
confirm_runtime

HAS_FILES=0
if [ -d "$TARGET_DIR" ] && [ -n "$(ls -A "$TARGET_DIR" 2>/dev/null)" ]; then
    HAS_FILES=1
fi

if [ "$HAS_FILES" -eq 0 ]; then
    L scr.inst.22
else
    OLD_VERSION="$(version_of "$TARGET_DIR")"
    if [ -z "$OLD_VERSION" ] && [ "$FORCE" -eq 0 ]; then
        L scr.inst.23
        L scr.inst.24 "--force"
        exit 1
    fi
    OLD_BUILD="$(build_of "$OLD_VERSION")"
    L scr.inst.25 "${OLD_VERSION:-$(ai2p_text scr.inst.74)}"
    if [ "$OLD_BUILD" -gt "$NEW_BUILD" ] && [ "$FORCE" -eq 0 ]; then
        L scr.inst.26 "$OLD_VERSION" "$NEW_VERSION"
        L scr.inst.27
        L scr.inst.28 "--force"
        exit 1
    fi
    if [ "$OLD_BUILD" -eq "$NEW_BUILD" ]; then
        L scr.inst.29
    fi
    # СЛУЖБА ЭТОЙ УСТАНОВКИ (T-271): останавливаем её сами и вернём в работу после
    # копирования. Отказываться, как от запущенного вручную приложения, нельзя:
    # обновление сервера-службы — обычное дело
    if find_service "$TARGET_DIR"; then
        L scr.inst.67 "$SERVICE_NAME" "$SERVICE_KIND" "$SERVICE_UNIT"
        if [ "$WHATIF" -eq 1 ]; then
            L scr.inst.68
        elif service_running; then
            L scr.inst.31
            case "$SERVICE_KIND" in
                launchd) launchctl unload "$SERVICE_UNIT" >/dev/null 2>&1 ;;
                *)       service_ctl stop ;;
            esac
            SERVICE_WAS_RUNNING=1
        fi
    elif service_flag_set; then
        L scr.inst.69 "$SERVICE_NAME"
        L scr.inst.34 "./makeAsServise.sh"
    fi

    # запущенное приложение держит свои файлы: обновлять его на ходу нельзя
    if [ "$WHATIF" -eq 0 ] && ps ax 2>/dev/null | grep -F "$TARGET_DIR/AI2P.Server" | grep -qv grep; then
        L scr.inst.70
        exit 1
    fi
fi

# --- состав новой выкладки (опись будущей установки) ---
# Пути относительные, разделитель '/' — описи Windows и Linux читаются одинаково
PAYLOAD="$(mktemp)"
(cd "$SOURCE" && find . -type f -print) | sed 's|^\./||' \
    | grep -v -e '^data/' -e '^logs/' -e '^secrets\.json$' -e '^secrets/' \
    | LC_ALL=C sort > "$PAYLOAD"
# опись ПРОШЛОЙ установки: строки-пути в ней записаны по одному в строке
STALE="$(mktemp)"
: > "$STALE"
OLD_LISTED=0
if [ "$HAS_FILES" -eq 1 ] && [ -f "$TARGET_DIR/$INVENTORY" ]; then
    OLD_FILES="$(mktemp)"
    sed -n 's/^[[:space:]]*"\([^"]*\)",\{0,1\}$/\1/p' "$TARGET_DIR/$INVENTORY" \
        | LC_ALL=C sort > "$OLD_FILES"
    OLD_LISTED=$(wc -l < "$OLD_FILES")
    # устарело = было в прошлой описи и нет в новой выкладке. Данные пользователя и сама
    # опись не удаляются никогда, даже если кто-то впишет их в опись руками
    LC_ALL=C comm -23 "$OLD_FILES" "$PAYLOAD" \
        | grep -v -e '^data/' -e '^logs/' -e '^secrets\.json$' -e '^secrets/' \
                  -e '^config\.json$' -e "^config\.new\.json" -e "^$INVENTORY$" > "$STALE"
    rm -f "$OLD_FILES"
fi

if [ "$WHATIF" -eq 1 ]; then
    echo ""
    L scr.inst.37 "--what-if"
    L scr.inst.38 "$(wc -l < "$PAYLOAD")" "data, logs, secrets.json, secrets, config.json"
    if [ "$HAS_FILES" -eq 1 ]; then
        if [ "$OLD_LISTED" -eq 0 ]; then
            L scr.inst.39 "$INVENTORY"
        else
            L scr.inst.71 "$(wc -l < "$STALE")"
        fi
    fi
    rm -f "$PAYLOAD" "$STALE"
    exit 0
fi

mkdir -p "$TARGET_DIR" || exit 1

# каталоги дистрибутива переписываются ЦЕЛИКОМ (ТЗ гл. 14, задание todo47): иначе в установке
# остались бы документы и статика, которых в новой версии уже нет. Файлов пользователя там нет
for d in $WIPE_DIRS; do
    if [ -d "$SOURCE/$d" ] && [ -d "$TARGET_DIR/$d" ]; then
        rm -rf "$TARGET_DIR/$d"
    fi
done

# --- копирование ---
COPIED=0
while IFS= read -r rel; do
    dest="$TARGET_DIR/$rel"
    if [ "$rel" = "config.json" ] && [ "$HAS_FILES" -eq 1 ] && [ -f "$TARGET_DIR/config.json" ]; then
        # рабочий конфиг не трогаем: приложение сольёт его с новым при первом старте
        dest="$TARGET_DIR/config.new.json"
    fi
    mkdir -p "$(dirname "$dest")"
    cp -f "$SOURCE/$rel" "$dest" || exit 1
done < "$PAYLOAD"

# while работает в подоболочке — счётчик считаем отдельно
COPIED=$(wc -l < "$PAYLOAD")

# --- уборка того, чего в новой версии больше нет (T-183) ---
REMOVED=0
while IFS= read -r rel; do
    [ -n "$rel" ] || continue
    if [ -f "$TARGET_DIR/$rel" ]; then
        rm -f "$TARGET_DIR/$rel" && REMOVED=$((REMOVED + 1))
        # опустевший каталог убираем вслед за файлом, но не выше корня установки
        dir="$(dirname "$TARGET_DIR/$rel")"
        while [ "$dir" != "$TARGET_DIR" ] && [ -d "$dir" ] && [ -z "$(ls -A "$dir" 2>/dev/null)" ]; do
            rmdir "$dir" 2>/dev/null || break
            dir="$(dirname "$dir")"
        done
    fi
done < "$STALE"

# опись НОВОЙ установки (формат тот же, что у install.ps1: один путь в строке)
{
    printf '{\n'
    printf '    "version":  "%s",\n' "$NEW_VERSION"
    printf '    "build":  %s,\n' "$NEW_BUILD"
    printf '    "installed":  "%s",\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf '    "files":  [\n'
    awk 'NR>1 { printf ",\n" } { printf "                    \"%s\"", $0 } END { if (NR) printf "\n" }' "$PAYLOAD"
    printf '    ]\n'
    printf '}\n'
} > "$TARGET_DIR/$INVENTORY"
rm -f "$PAYLOAD" "$STALE"
chmod +x "$TARGET_DIR/AI2P.Server" 2>/dev/null
chmod +x "$TARGET_DIR/install.sh" 2>/dev/null
chmod +x "$TARGET_DIR/makeAsServise.sh" 2>/dev/null
# клиент командной строки для ИИ-агента (T-34-S0): в выкладку он приезжает обычным
# файлом без права на запуск — его зовёт агент, значит бит выставляем мы
chmod +x "$TARGET_DIR/ai2p" 2>/dev/null

# --- служба возвращается в работу (T-271) ---
# Путь к программе не менялся (обновление кладётся в тот же каталог), поэтому службу
# достаточно запустить. Не запустилась — об этом надо сказать: молча оставить сервер
# лежать нельзя, человек ждёт, что он поднимается сам
if [ "$SERVICE_WAS_RUNNING" -eq 1 ]; then
    L scr.inst.42 "$SERVICE_NAME"
    case "$SERVICE_KIND" in
        launchd) launchctl load "$SERVICE_UNIT" >/dev/null 2>&1 ;;
        *)       service_ctl start ;;
    esac
    if service_running; then
        L scr.inst.43 "$SERVICE_NAME"
    else
        L scr.inst.72 "$SERVICE_NAME"
        L scr.inst.73 "$TARGET_DIR/logs/"
    fi
fi

echo ""
L scr.inst.46 "$COPIED"
if [ "$REMOVED" -gt 0 ]; then
    L scr.inst.47 "$REMOVED"
elif [ "$HAS_FILES" -eq 1 ] && [ "$OLD_LISTED" -eq 0 ]; then
    L scr.inst.48 "$INVENTORY"
fi
L scr.inst.49 "$NEW_VERSION"
if [ "$HAS_FILES" -eq 1 ]; then
    echo ""
    L scr.inst.50 "data/, logs/, secrets.json, secrets/, config.json"
    if [ -f "$TARGET_DIR/config.new.json" ]; then
        L scr.inst.51
        L scr.inst.52
    fi
    L scr.inst.53
else
    echo ""
    L scr.inst.54 "$TARGET_DIR/AI2P.Server"
    L scr.inst.55
    L scr.inst.56 "./makeAsServise.sh"
    L scr.inst.58 "$SERVICE_NAME"
fi
exit 0
