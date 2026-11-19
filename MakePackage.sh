#!/bin/sh
# ============================================================
#  AI2P - MakePackage.sh  (Linux / macOS, T-285)
#  An installation package out of a ready release. The full description is in the
#  help: ./MakePackage.sh --help
#
#  THE TEXTS LIVE OUTSIDE THE SCRIPT (T-65-S0): the messages in
#  i18n/scripts.<lang>.txt, the help in i18n/help/MakePackage.<lang>.txt.
#  The default language is en; --lang xx and AI2P_LANG override it, in that order.
# ============================================================
set -u
# внешние переменные нормализуются один раз (T-294): под «set -u» подстановка незаданной
# переменной обрывает скрипт. HOME здесь нужен только для раскрытия «~» в каталогах
HOME="${HOME:-}"

SRC_DIR=""
OUT_DIR=""
LANG_OPT=""
HELP=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --source=*) SRC_DIR="${1#--source=}" ;;
        --source)   if [ "$#" -ge 2 ]; then shift; SRC_DIR="$1"; fi ;;
        --output=*) OUT_DIR="${1#--output=}" ;;
        --lang=*)   LANG_OPT="${1#--lang=}" ;;
        --lang)     if [ "$#" -ge 2 ]; then shift; LANG_OPT="$1"; fi ;;
        -h|--help)  HELP=1 ;;
        *)          if [ -z "$OUT_DIR" ]; then OUT_DIR="$1"; fi ;;
    esac
    shift
done

# ТЕКСТЫ СООБЩЕНИЙ ЛЕЖАТ СНАРУЖИ (T-65-S0); каталога рядом может не оказаться —
# тогда L отдаёт сам ключ, и скрипт всё равно работает.
#
# ГОДНОСТЬ ЗАГРУЗЧИКА ПРОВЕРЯЕТСЯ ПО ФУНКЦИЯМ, А НЕ ПО НАЛИЧИЮ ФАЙЛА (T-319). Проверка
# «-f» говорит только о том, что файл есть: пустой или обрезанный при копировании loc.sh
# её проходит, но НЕ ЗАДАЁТ НИ ОДНОЙ функции — а ветка «else» при этом не выполняется,
# и запасных определений не появляется тоже. Тогда каждая строка вывода превращается
# в «./MakePackage.sh: 250: L: not found» (жалоба T-319: пять таких строк в конце —
# это последние пять вызовов L, остальные такие же строки прошли выше по выводу).
# Поэтому: сначала пробуем загрузить каталог, потом СПРАШИВАЕМ, что из него получилось,
# и доопределяем недостающее. Без словаря скрипт обязан работать, а не сыпать ошибками
SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
AI2P_LOC_DIR="$SELF_DIR/i18n"
LOC_FILE=0
if [ -f "$AI2P_LOC_DIR/loc.sh" ]; then
    LOC_FILE=1
    . "$AI2P_LOC_DIR/loc.sh"
fi
if command -v ai2p_set_lang >/dev/null 2>&1; then
    ai2p_set_lang "$LANG_OPT"
elif [ "$LOC_FILE" -eq 1 ]; then
    printf 'AI2P: %s/loc.sh defines no messages (empty or truncated file) - texts are printed as keys\n' "$AI2P_LOC_DIR" >&2
fi
if ! command -v ai2p_text >/dev/null 2>&1; then
    ai2p_text() { printf '%s' "$1"; }
fi
if ! command -v L >/dev/null 2>&1; then
    L() { ai2p_text "$@"; printf '\n'; }
fi
if ! command -v ai2p_help >/dev/null 2>&1; then
    ai2p_help() { printf 'No i18n folder next to MakePackage.sh\n'; }
fi

if [ "$HELP" -eq 1 ]; then ai2p_help MakePackage; exit 0; fi

if [ -z "$SRC_DIR" ]; then SRC_DIR="$(cd "$(dirname "$0")" && pwd)"; fi
case "$SRC_DIR" in
    "~")   SRC_DIR="$HOME" ;;
    # тильда в ОБРАЗЦЕ раскрывается оболочкой (T-294) — экранируем, иначе «~» останется в пути
    "~/"*) SRC_DIR="$HOME/${SRC_DIR#\~/}" ;;
esac
SRC_DIR="$(cd "$SRC_DIR" 2>/dev/null && pwd)" || {
    L scr.pkg.34
    exit 1
}

echo "=== AI2P MakePackage (Linux/macOS) ==="
L scr.pkg.1 "$SRC_DIR"

# ---------- 1. версия выкладки ----------
VERSION_FILE="$SRC_DIR/version.json"
if [ ! -f "$VERSION_FILE" ]; then
    L scr.pkg.2
    L scr.pkg.3
    L scr.pkg.4 "--source"
    exit 1
fi
json_str() { sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$VERSION_FILE" | head -1; }
json_bool() { sed -n 's/.*"'"$1"'"[[:space:]]*:[[:space:]]*\([a-z]*\).*/\1/p' "$VERSION_FILE" | head -1; }
VERSION="$(json_str version)"
RUNTIME="$(json_str runtime)"
SELF_CONTAINED="$(json_bool selfContained)"
BUILD="$(echo "$VERSION" | awk -F. '{ if (NF >= 2) print $2 + 0; else print 0 }')"
if [ -z "$VERSION" ]; then
    L scr.pkg.5
    exit 1
fi

# ---------- 2. под какую систему и архитектуру собрана выкладка ----------
# у полной выкладки и система, и архитектура записаны рантаймом (linux-arm64, osx-arm64,
# win-x64), у обычной рантайма нет вовсе — тогда систему выдаёт каталог ОС
# (builds/<ОС>/release), а архитектуру берём у ТЕКУЩЕГО КОМПИЛЯТОРА: выкладка без RID
# собирается под ту машину, на которой её собирали
OS_NAME=""
ARCH_TAG=""
case "$RUNTIME" in
    linux-*) OS_NAME="linux" ;;
    osx-*)   OS_NAME="macos" ;;
    win-*)   OS_NAME="windows" ;;
esac
# архитектура RID — это его последняя часть: linux-x64 -> x64, osx-arm64 -> arm64
if [ -n "$RUNTIME" ]; then ARCH_TAG="${RUNTIME##*-}"; fi
if [ -z "$OS_NAME" ]; then
    case "$(basename "$(dirname "$SRC_DIR")")" in
        linux)   OS_NAME="linux" ;;
        macos)   OS_NAME="macos" ;;
        windows) OS_NAME="windows" ;;
        *)       case "$(uname -s)" in Darwin) OS_NAME="macos" ;; *) OS_NAME="linux" ;; esac ;;
    esac
fi
if [ -z "$ARCH_TAG" ]; then
    # x64, arm64, x86, arm — как их называет .NET, а не uname
    case "$(uname -m)" in
        x86_64|amd64)   ARCH_TAG="x64" ;;
        aarch64|arm64)  ARCH_TAG="arm64" ;;
        armv*|arm)      ARCH_TAG="arm" ;;
        i386|i686|x86)  ARCH_TAG="x86" ;;
        *)              ARCH_TAG="$(uname -m)" ;;
    esac
fi
if [ "$OS_NAME" = "windows" ]; then
    L scr.pkg.35
    L scr.pkg.36
    exit 1
fi

# ---------- 3. имя пакета и каталог результата ----------
# AI2P_v_1_133_full_linux_x64.run — полная выкладка; AI2P_v_1_133_linux_x64.run — обычная.
# «1_133» — это версия с точкой, заменённой на подчёркивание: вторая часть и есть билд NN;
# «full» стоит ПОСЛЕ номера версии, дальше система и архитектура (T-234-S0)
if [ "$SELF_CONTAINED" = "true" ]; then FULL_TAG="_full"; else FULL_TAG=""; fi
BASENAME="AI2P_v_$(echo "$VERSION" | tr '.' '_')${FULL_TAG}_${OS_NAME}_${ARCH_TAG}"

if [ -z "$OUT_DIR" ]; then OUT_DIR="$SRC_DIR/../../packages"; fi
case "$OUT_DIR" in
    "~")   OUT_DIR="$HOME" ;;
    "~/"*) OUT_DIR="$HOME/${OUT_DIR#\~/}" ;;      # T-294: тильду в образце экранируем
esac
case "$OUT_DIR" in
    /*) ;;
    *)  OUT_DIR="$SRC_DIR/$OUT_DIR" ;;
esac
mkdir -p "$OUT_DIR" || exit 1
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

if [ "$SELF_CONTAINED" = "true" ]; then
    L scr.pkg.10 "$VERSION" "$BUILD" "$(ai2p_text scr.pkg.8 "$RUNTIME")"
else
    L scr.pkg.10 "$VERSION" "$BUILD" "$(ai2p_text scr.pkg.9)"
fi
L scr.pkg.11 "$BASENAME.run"
L scr.pkg.12 "$OUT_DIR"

# ---------- 4. makeself ----------
MAKESELF=""
if command -v makeself >/dev/null 2>&1; then MAKESELF="makeself"
elif command -v makeself.sh >/dev/null 2>&1; then MAKESELF="makeself.sh"
fi
if [ -z "$MAKESELF" ]; then
    L scr.pkg.37
    L scr.pkg.38
    L scr.pkg.39
    exit 1
fi
L scr.pkg.40 "$(command -v "$MAKESELF")"

# ---------- 5. временный каталог с содержимым пакета ----------
# данные пользователя в дистрибутив не попадают (тот же список, что у install.sh)
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/ai2p_pkg_XXXXXX")" || exit 1
trap 'rm -rf "$STAGE"' EXIT INT TERM
PAYLOAD="$STAGE/payload"
mkdir -p "$PAYLOAD"

L scr.pkg.17
for item in "$SRC_DIR"/* "$SRC_DIR"/.[!.]*; do
    [ -e "$item" ] || continue
    case "$(basename "$item")" in
        data|logs|secrets|secrets.json|installed.json) continue ;;
        MakePackage.sh|MakePackage.ps1|MakePackage.cmd) continue ;;
    esac
    cp -R "$item" "$PAYLOAD/" || exit 1
done
if [ ! -f "$PAYLOAD/install.sh" ]; then
    L scr.pkg.41
    L scr.pkg.42
    exit 1
fi
chmod +x "$PAYLOAD/install.sh" 2>/dev/null
chmod +x "$PAYLOAD/AI2P.Server" 2>/dev/null
# настройка запуска сервисом (T-271) едет в пакете и попадает в установку: службу
# заводят руками, а вот остановку и запуск при обновлении делает сам install.sh
chmod +x "$PAYLOAD/makeAsServise.sh" 2>/dev/null
PAYLOAD_COUNT="$(find "$PAYLOAD" -type f | wc -l | tr -d ' ')"
L scr.pkg.21 "$PAYLOAD_COUNT"

# пусковой скрипт пакета: спрашивает каталог и отдаёт работу install.sh — со всеми
# его правилами обновления (опись, config.new.json, данные пользователя не трогаются)
cat > "$PAYLOAD/setup.sh" <<'SETUP'
#!/bin/sh
# The launcher of the AI2P installation package (T-285). makeself starts it inside
# the unpacked archive; all the work is done by install.sh next to it.
# The texts live outside (T-65-S0): i18n/scripts.<lang>.txt of the payload.
set -u
# T-294: under "set -u" an unset variable aborts the script, and HOME is not always set
HOME="${HOME:-}"
SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$SELF_DIR/i18n/loc.sh" ]; then
    AI2P_LOC_DIR="$SELF_DIR/i18n"
    . "$SELF_DIR/i18n/loc.sh"
fi
# T-319: годность загрузчика — по функциям, а не по наличию файла (пустой loc.sh
# проходит «-f», но не задаёт ничего, и тогда «ai2p_text: not found»)
if command -v ai2p_set_lang >/dev/null 2>&1; then
    ai2p_set_lang ""
fi
if ! command -v ai2p_text >/dev/null 2>&1; then
    ai2p_text() { printf 'AI2P installation folder [%s]:' "$2"; }
fi
if [ -n "$HOME" ]; then DEFAULT_TARGET="$HOME/ai/AI2P"; else DEFAULT_TARGET="$(pwd)/AI2P"; fi
TARGET=""
for arg in "$@"; do
    case "$arg" in
        -*) ;;
        *)  if [ -z "$TARGET" ]; then TARGET="$arg"; fi ;;
    esac
done
if [ -z "$TARGET" ]; then
    if [ -t 0 ]; then
        printf '%s ' "$(ai2p_text scr.setup.1 "$DEFAULT_TARGET")"
        read -r ANSWER
        if [ -n "$ANSWER" ]; then TARGET="$ANSWER"; else TARGET="$DEFAULT_TARGET"; fi
    else
        TARGET="$DEFAULT_TARGET"
    fi
fi
KEYS=""
for arg in "$@"; do
    case "$arg" in
        -*) KEYS="$KEYS $arg" ;;
    esac
done
# shellcheck disable=SC2086
exec sh ./install.sh "$TARGET" $KEYS
SETUP
chmod +x "$PAYLOAD/setup.sh"

# ---------- 6. сборка ----------
L scr.pkg.43
if ! "$MAKESELF" --gzip "$PAYLOAD" "$OUT_DIR/$BASENAME.run" "AI2P $VERSION" ./setup.sh; then
    L scr.pkg.44
    exit 1
fi
if [ ! -f "$OUT_DIR/$BASENAME.run" ]; then
    L scr.pkg.45 "$OUT_DIR/$BASENAME.run"
    exit 1
fi
chmod +x "$OUT_DIR/$BASENAME.run"
SIZE="$(du -h "$OUT_DIR/$BASENAME.run" | awk '{print $1}')"

echo ""
echo "=== MakePackage OK ==="
L scr.pkg.46 "$OUT_DIR/$BASENAME.run" "$SIZE"
L scr.pkg.47
L scr.pkg.48 "$BASENAME.run"
L scr.pkg.49 "$BASENAME.run"
L scr.pkg.50
exit 0
