#!/bin/sh
# ============================================================
#  AI2P - makeAsServise.sh  (Linux / macOS, T-271)
#  Turning an installation into an OS service. The full description is in the
#  help: ./makeAsServise.sh --help
#
#  THE TEXTS LIVE OUTSIDE THE SCRIPT (T-65-S0): the messages in
#  i18n/scripts.<lang>.txt, the help in i18n/help/makeAsServise.<lang>.txt.
#  The default language is en; --lang xx and AI2P_LANG override it, in that order.
# ============================================================
set -u

# ПЕРЕМЕННЫХ ОКРУЖЕНИЯ МОЖЕТ НЕ БЫТЬ ВОВСЕ (T-294): под «set -u» подстановка незаданной
# переменной обрывает скрипт («AI2P_HOME: parameter not set»), а не даёт пустую строку.
# AI2P_HOME в обычной установке не задан никогда — нормализуем внешние переменные один раз
AI2P_HOME="${AI2P_HOME:-}"
HOME="${HOME:-}"

SERVICE_NAME="AI2P"
TARGET=""
REMOVE=0
NOSTART=0
MANUAL=0
SYSTEM=0
LANG_OPT=""
HELP=0
BADOPT=""

while [ $# -gt 0 ]; do
    case "$1" in
        --target)   shift; TARGET="${1:-}" ;;
        --remove)   REMOVE=1 ;;
        --no-start) NOSTART=1 ;;
        --manual)   MANUAL=1 ;;
        --system)   SYSTEM=1 ;;
        --lang)     shift; LANG_OPT="${1:-}" ;;
        --lang=*)   LANG_OPT="${1#--lang=}" ;;
        -h|--help)  HELP=1 ;;
        *) BADOPT="$1" ;;
    esac
    shift
done

# ТЕКСТЫ СООБЩЕНИЙ ЛЕЖАТ СНАРУЖИ (T-65-S0); каталога рядом может не оказаться —
# тогда L отдаёт сам ключ, и скрипт всё равно работает
# годность загрузчика — по функциям, а не по наличию файла (T-319)
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
    ai2p_help() { printf 'No i18n folder next to makeAsServise.sh\n'; }
fi

if [ -n "$BADOPT" ]; then L scr.svc.62 "$BADOPT"; exit 1; fi
if [ "$HELP" -eq 1 ]; then ai2p_help makeAsServise; exit 0; fi

# --- каталог установки ---------------------------------------------------------------
if [ -z "$TARGET" ]; then
    TARGET="$(cd "$(dirname "$0")" && pwd)"
fi
case "$TARGET" in
    "~")   TARGET="$HOME" ;;
    # тильда в ОБРАЗЦЕ раскрывается оболочкой (T-294) — экранируем, иначе «~» останется в пути
    "~/"*) TARGET="$HOME/${TARGET#\~/}" ;;
esac
case "$TARGET" in
    /*) TARGET_DIR="$TARGET" ;;
    *)  TARGET_DIR="$(pwd)/$TARGET" ;;
esac
EXE="$TARGET_DIR/AI2P.Server"

# --- РАБОЧИЙ каталог: он не обязан совпадать с каталогом программы (T-287) -----------
# Установка в каталог программ системы (/usr, /opt, /Applications) уводит config.json,
# data и secrets в общий каталог данных компьютера (/var/lib/ai2p), а если и он закрыт —
# в каталог данных пользователя (~/.local/share/ai2p). Правило выбора живёт в приложении
# (AppHome) и здесь НЕ повторяется: два источника правды разъедутся молча. Мы только
# ИЩЕМ уже существующий config.json там, где приложение могло его завести.
#
# Каталог ПРОГРАММ пропускается намеренно: config.json может лежать и там, а приложение
# из каталога программ его НЕ читает — пометка ушла бы в мёртвый файл. Это единственная
# часть правила AppHome, повторённая здесь.
IN_PROGRAM_DIR=0
case "$TARGET_DIR/" in
    /usr/*|/opt/*|/Applications/*) IN_PROGRAM_DIR=1 ;;
esac

HOME_DIR="$TARGET_DIR"
OWN_DIR="$TARGET_DIR"
[ "$IN_PROGRAM_DIR" -eq 0 ] || OWN_DIR=""
for candidate in "$AI2P_HOME" "$OWN_DIR" "/var/lib/ai2p" "$HOME/.local/share/ai2p"; do
    [ -n "$candidate" ] || continue
    if [ -f "$candidate/config.json" ]; then HOME_DIR="$candidate"; break; fi
done
CONFIG="$HOME_DIR/config.json"
RELOCATED=0
[ "$HOME_DIR" = "$TARGET_DIR" ] || RELOCATED=1

# рабочий каталог прибивается гвоздями, если он не рядом с программой: системная служба
# работает от другой учётной записи, и запасной путь выбора (каталог данных ПОЛЬЗОВАТЕЛЯ)
# у неё был бы свой — служба завела бы пустую базу вместо рабочей
EXEC_ARGS="--service"
[ "$RELOCATED" -eq 0 ] || EXEC_ARGS="--service --config $CONFIG"

OS="$(uname -s 2>/dev/null || echo unknown)"

echo "=== AI2P makeAsServise ($OS) ==="
L scr.svc.2 "$TARGET_DIR"
[ "$RELOCATED" -eq 0 ] || L scr.svc.3 "$HOME_DIR"
L scr.svc.4 "$SERVICE_NAME"

if [ "$REMOVE" -eq 0 ] && [ ! -f "$EXE" ]; then
    L scr.svc.63
    L scr.svc.64
    exit 1
fi

# --- записка «эта установка настроена сервисом» (config.json) ------------------------
# Правится ТОЧЕЧНО: полный разбор и обратная запись JSON переформатировали бы весь файл
# пользователя ради одного значения
set_service_flag() {
    value="$1"
    [ -f "$CONFIG" ] || return 1
    if grep -q '"serviceMode"' "$CONFIG"; then
        tmp="$(mktemp)"
        sed 's/"serviceMode"[[:space:]]*:[[:space:]]*\(true\|false\)/"serviceMode": '"$value"'/' \
            "$CONFIG" > "$tmp" && cat "$tmp" > "$CONFIG"
        rm -f "$tmp"
    else
        # конфиг старой версии — ключа ещё нет; дописываем перед последней закрывающей
        # скобкой. Не нашли такой строки — файл нестандартный, лучше не трогать вовсе
        tmp="$(mktemp)"
        awk -v val="$value" '
            { lines[NR] = $0 }
            END {
                last = 0
                for (i = NR; i >= 1; i--)
                    if (lines[i] ~ /^[[:space:]]*}[[:space:]]*$/) { last = i; break }
                if (last == 0) { for (i = 1; i <= NR; i++) print lines[i]; exit 1 }
                for (i = 1; i <= NR; i++) {
                    line = lines[i]
                    # предыдущей строке нужна запятая, иначе получится битый JSON
                    if (i == last - 1 && line !~ /,[[:space:]]*$/ && line !~ /[\{\[][[:space:]]*$/)
                        line = line ","
                    if (i == last) print "  \"serviceMode\": " val
                    print line
                }
            }' "$CONFIG" > "$tmp" || { rm -f "$tmp"; return 1; }
        cat "$tmp" > "$CONFIG"
        rm -f "$tmp"
    fi
    return 0
}

# ============================ Linux: systemd =========================================
if [ "$OS" = "Linux" ]; then
    command -v systemctl >/dev/null 2>&1 || {
        L scr.svc.65
        L scr.svc.66
        echo "        $EXE --service"
        exit 1
    }

    if [ "$SYSTEM" -eq 1 ]; then
        UNIT_DIR="/etc/systemd/system"
        CTL="systemctl"
        if [ "$(id -u)" -ne 0 ]; then
            L scr.svc.67
            exit 1
        fi
    else
        UNIT_DIR="$HOME/.config/systemd/user"
        CTL="systemctl --user"
    fi
    UNIT="$UNIT_DIR/$SERVICE_NAME.service"

    if [ "$REMOVE" -eq 1 ]; then
        $CTL disable --now "$SERVICE_NAME.service" >/dev/null 2>&1
        if [ -f "$UNIT" ]; then
            rm -f "$UNIT"
            $CTL daemon-reload
            L scr.svc.68 "$UNIT"
        else
            L scr.svc.69 "$UNIT"
        fi
        set_service_flag false && L scr.svc.18
        exit 0
    fi

    mkdir -p "$UNIT_DIR" || exit 1
    # Type=simple, а НЕ notify: сообщение о готовности приложение отправляет только тогда,
    # когда обёртка UseSystemd опознала systemd (T-271). Опознание надёжное, но цена ошибки
    # у notify непомерная — systemd убил бы исправно работающий сервер по таймауту старта.
    # Ключ --service ставится явно: он не даёт режиму зависеть от опознания вовсе
    {
        echo "[Unit]"
        printf 'Description=%s\n' "$(ai2p_text scr.svc.70)"
        echo "Documentation=file://$TARGET_DIR/doc"
        echo "After=network-online.target"
        echo ""
        echo "[Service]"
        echo "Type=simple"
        echo "WorkingDirectory=$TARGET_DIR"
        echo "ExecStart=$EXE $EXEC_ARGS"
        echo "Restart=on-failure"
        echo "RestartSec=10"
        echo "KillSignal=SIGTERM"
        echo "TimeoutStopSec=60"
        echo ""
        echo "[Install]"
        if [ "$SYSTEM" -eq 1 ]; then echo "WantedBy=multi-user.target"; else echo "WantedBy=default.target"; fi
    } > "$UNIT" || exit 1
    chmod +x "$EXE" 2>/dev/null
    $CTL daemon-reload

    if [ "$MANUAL" -eq 1 ]; then
        L scr.svc.71
    else
        $CTL enable "$SERVICE_NAME.service" >/dev/null 2>&1
        # пользовательская служба без «задержанного входа» умирает вместе с сеансом
        if [ "$SYSTEM" -eq 0 ] && command -v loginctl >/dev/null 2>&1; then
            loginctl enable-linger "$(id -un)" >/dev/null 2>&1 ||
                L scr.svc.72
        fi
    fi

    set_service_flag true && L scr.svc.73

    if [ "$NOSTART" -eq 1 ]; then
        L scr.svc.74 "$CTL" "$SERVICE_NAME"
    else
        $CTL restart "$SERVICE_NAME.service" || {
            L scr.svc.75 "$CTL" "$SERVICE_NAME"
            exit 1
        }
        L scr.svc.49
    fi

    echo ""
    L scr.svc.54
    L scr.svc.76 "$UNIT"
    L scr.svc.77 "$CTL" "$SERVICE_NAME"
    L scr.svc.78
    L scr.svc.79
    exit 0
fi

# ============================ macOS: launchd =========================================
if [ "$OS" = "Darwin" ]; then
    PLIST_DIR="$HOME/Library/LaunchAgents"
    PLIST="$PLIST_DIR/$SERVICE_NAME.plist"

    if [ "$REMOVE" -eq 1 ]; then
        launchctl unload "$PLIST" >/dev/null 2>&1
        if [ -f "$PLIST" ]; then
            rm -f "$PLIST"
            L scr.svc.80 "$PLIST"
        else
            L scr.svc.81 "$PLIST"
        fi
        set_service_flag false && L scr.svc.18
        exit 0
    fi

    mkdir -p "$PLIST_DIR" || exit 1
    # launchd не даёт процессу признака «меня подняла система», поэтому режим задаётся
    # ключом --service явно (T-271): без него приложение считало бы запуск консольным
    RUNATLOAD="true"
    [ "$MANUAL" -eq 1 ] && RUNATLOAD="false"
    {
        echo '<?xml version="1.0" encoding="UTF-8"?>'
        echo '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">'
        echo '<plist version="1.0">'
        echo '<dict>'
        echo "    <key>Label</key><string>$SERVICE_NAME</string>"
        echo '    <key>ProgramArguments</key>'
        echo '    <array>'
        echo "        <string>$EXE</string>"
        echo '        <string>--service</string>'
        if [ "$RELOCATED" -eq 1 ]; then
            echo '        <string>--config</string>'
            echo "        <string>$CONFIG</string>"
        fi
        echo '    </array>'
        echo "    <key>WorkingDirectory</key><string>$TARGET_DIR</string>"
        echo "    <key>RunAtLoad</key><$RUNATLOAD/>"
        echo '    <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>'
        echo "    <key>StandardErrorPath</key><string>$HOME_DIR/logs/launchd.err.log</string>"
        echo '</dict>'
        echo '</plist>'
    } > "$PLIST" || exit 1
    chmod +x "$EXE" 2>/dev/null
    mkdir -p "$HOME_DIR/logs" 2>/dev/null

    set_service_flag true && L scr.svc.73

    launchctl unload "$PLIST" >/dev/null 2>&1
    if [ "$NOSTART" -eq 1 ]; then
        L scr.svc.82 "$PLIST"
    else
        launchctl load "$PLIST" || {
            L scr.svc.83 "$PLIST"
            exit 1
        }
        L scr.svc.84
    fi

    echo ""
    L scr.svc.54
    L scr.svc.85 "$PLIST"
    L scr.svc.86 "$PLIST" "$SERVICE_NAME"
    L scr.svc.87
    L scr.svc.79
    exit 0
fi

L scr.svc.88 "$OS"
L scr.svc.89
echo "        $EXE"
exit 1
