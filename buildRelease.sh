#!/bin/sh
# ============================================================
#  AI2P - buildRelease.sh  (Linux / macOS)   [до T-285 назывался publish.sh]
#  Релизная выкладка сервера в отдельный каталог: самодостаточный
#  комплект файлов, который запускается без исходников и без dotnet run.
#
#  Использование:  ./buildRelease.sh [каталог] [Release|Debug] [--clean] [--self-contained] [--runtime RID]
#  По умолчанию:   builds/linux/release (каталог ОС — своя система), конфигурация Release
#
#  ПАКЕТ УСТАНОВКИ (T-285) собирается ОТДЕЛЬНЫМ шагом и уже из готовой выкладки:
#  в неё кладётся MakePackage.sh (в выкладку для Windows — MakePackage.ps1/.cmd),
#  запускать его надо ИЗ каталога выкладки, результат ложится в ../../packages.
#
#  ДВЕ ВЫКЛАДКИ (T-211). Обычная — без рантайма: маленькая, но требует на машине
#  установленный ASP.NET Core 8.x (её install.sh это проверяет и предлагает поставить).
#  Полная (--self-contained, он же --full) — рантайм лежит внутри неё самой, ставить на
#  машину нечего, зато выкладка привязана к платформе (--runtime, по умолчанию linux-x64
#  или osx-x64/osx-arm64) и весит на порядок больше. Каталоги разные и НЕ смешиваются.
#
#  КАТАЛОГ ОПЕРАЦИОННОЙ СИСТЕМЫ (T-243). Между builds и release/releasefull стоит
#  подкаталог ОС, под которую собрана выкладка, — windows, linux или macos:
#      builds/linux/release        — без рантайма (как было всегда, но в каталоге ОС)
#      builds/linux/releasefull    — с рантаймом
#      builds/macos/releasefull    — под macOS (--runtime osx-x64 / osx-arm64)
#      builds/windows/releasefull  — под Windows (--runtime win-x64)
#  САМ КАТАЛОГ builds ЛЕЖИТ ВНУТРИ РАБОЧЕГО КАТАЛОГА (T-131-S0) — рядом с AI2P.sln,
#  то есть в каталоге этого скрипта, а не на уровень выше него, как было до 1.114.
#  Выкладки разных систем больше не затирают друг друга в одном каталоге: у них разные
#  скрипты установки, разный рантайм внутри и разные исполняемые файлы.
#
#  Каталог данных сюда НЕ копируется: data/, logs/, config.json,
#  secrets.json и secrets/ (ключи моделей, по одному json на ключ — T-228)
#  переносятся руками и при повторной выкладке не трогаются.
# ============================================================
set -u
# внешние переменные нормализуются один раз (T-294): под «set -u» подстановка незаданной
# переменной обрывает скрипт, а HOME нужен только для раскрытия «~» в каталоге выкладки
HOME="${HOME:-}"
cd "$(dirname "$0")"
SCRIPT_DIR="$(pwd)"

OUTPUT=""
CONFIGURATION="Release"
CLEAN=0
SELF_CONTAINED=0
RUNTIME=""
LANG_OPT=""
HELP=0

first=1
while [ "$#" -gt 0 ]; do
    case "$1" in
        --clean) CLEAN=1 ;;
        --self-contained|--full) SELF_CONTAINED=1 ;;
        --runtime=*) RUNTIME="${1#--runtime=}" ;;
        --runtime) if [ "$#" -ge 2 ]; then shift; RUNTIME="$1"; fi ;;
        -h|--help) HELP=1 ;;
        --lang|-lang|-Lang) if [ "$#" -ge 2 ]; then shift; LANG_OPT="$1"; fi ;;
        --lang=*) LANG_OPT="${1#--lang=}" ;;
        Release|Debug) CONFIGURATION="$1" ;;
        *)
            if [ "$first" -eq 1 ]; then OUTPUT="$1"; fi
            ;;
    esac
    first=0
    shift
done

# --help печатается из отдельного файла i18n/help/buildRelease.<язык>.txt (T-65-S0),
# язык берётся из той же командной строки: --lang xx / --lang=xx, иначе лесенка
# loc.sh — переменная окружения AI2P_LANG, затем en
if [ "$HELP" -eq 1 ]; then
    if [ -f "$SCRIPT_DIR/i18n/loc.sh" ]; then
        AI2P_LOC_DIR="$SCRIPT_DIR/i18n"; . "$SCRIPT_DIR/i18n/loc.sh"
        ai2p_set_lang "$LANG_OPT"; ai2p_help buildRelease
    else
        echo "Usage: ./buildRelease.sh [dir] [Release|Debug] [--clean] [--self-contained] [--runtime RID]"
    fi
    exit 0
fi

# рантайм по умолчанию — платформа, на которой идёт сборка
if [ "$SELF_CONTAINED" -eq 1 ] && [ -z "$RUNTIME" ]; then
    case "$(uname -s)" in
        Darwin) case "$(uname -m)" in arm64|aarch64) RUNTIME="osx-arm64" ;; *) RUNTIME="osx-x64" ;; esac ;;
        *)      case "$(uname -m)" in aarch64|arm64) RUNTIME="linux-arm64" ;; *) RUNTIME="linux-x64" ;; esac ;;
    esac
fi

# ОС ЦЕЛИ (T-243): выкладка кладётся в подкаталог своей системы (windows/linux/macos).
# Платформу задаёт рантайм (--runtime) у полной выкладки, а у обычной — та система,
# на которой идёт сборка: у неё внутри скрипты установки этой системы и ничего больше
if [ "$SELF_CONTAINED" -eq 1 ]; then
    case "$RUNTIME" in
        win-*) TARGET_OS="windows" ;;
        osx-*) TARGET_OS="macos" ;;
        *)     TARGET_OS="linux" ;;
    esac
else
    case "$(uname -s)" in
        Darwin)               TARGET_OS="macos" ;;
        CYGWIN*|MINGW*|MSYS*) TARGET_OS="windows" ;;
        *)                    TARGET_OS="linux" ;;
    esac
fi

# каталог по умолчанию зависит от вида выкладки: смешивать их в одном каталоге нельзя —
# у полной внутри рантайм, и при переходе к обычной он остался бы там мусором
if [ -z "$OUTPUT" ]; then
    if [ "$SELF_CONTAINED" -eq 1 ]; then OUTPUT="builds/$TARGET_OS/releasefull"; else OUTPUT="builds/$TARGET_OS/release"; fi
fi

# «~/ai/AI2P» — обычный вид каталога сервера на Linux/macOS (T-135): оболочка раскрывает «~»
# не всегда (кавычки, переменная), поэтому раскрываем сами
case "$OUTPUT" in
    "~")   OUTPUT="$HOME" ;;
    # тильда в ОБРАЗЦЕ раскрывается оболочкой (T-294) — экранируем, иначе «~» останется в пути
    "~/"*) OUTPUT="$HOME/${OUTPUT#\~/}" ;;
esac
case "$OUTPUT" in
    /*) OUT_DIR="$OUTPUT" ;;
    *)  OUT_DIR="$SCRIPT_DIR/$OUTPUT" ;;
esac

echo "=== AI2P buildRelease ($CONFIGURATION) ==="
if [ "$SELF_CONTAINED" -eq 1 ]; then
    echo "Вид выкладки: с рантаймом внутри ($RUNTIME)"
else
    echo "Вид выкладки: без рантайма (нужен ASP.NET Core 8.x)"
fi
echo "Операционная система выкладки: $TARGET_OS"
echo "Каталог выкладки: $OUT_DIR"

case "$OUT_DIR" in
    "$SCRIPT_DIR"|"$SCRIPT_DIR/src"*)
        echo "[ERROR] Каталог выкладки не должен быть внутри исходников."
        exit 1 ;;
esac

if [ ! -d "$OUT_DIR" ]; then
    mkdir -p "$OUT_DIR"
elif [ "$CLEAN" -eq 1 ]; then
    echo "Очистка каталога (сохраняются: data, logs, config.json, secrets.json, secrets)..."
    for item in "$OUT_DIR"/* "$OUT_DIR"/.[!.]*; do
        [ -e "$item" ] || continue
        case "$(basename "$item")" in
            data|logs|config.json|secrets.json|secrets) continue ;;
        esac
        rm -rf "$item"
    done
else
    echo "Каталог не пуст — файлы выкладки будут перезаписаны (--clean для полной замены)."
fi

# 1. публикация сервера (тянет за собой Core/Storage/Connectors/UI и статику RCL)
#    PublishDir вместо -o: ключ -o задаёт OutputPath и для проектов-ссылок,
#    из-за чего рядом с src/ появляется мусорный каталог сборки
if [ "$SELF_CONTAINED" -eq 1 ]; then
    if ! dotnet publish src/AI2P.Server/AI2P.Server.csproj -c "$CONFIGURATION" \
            -r "$RUNTIME" --self-contained true "--property:PublishDir=$OUT_DIR/"; then
        echo "[ERROR] dotnet publish failed."
        exit 1
    fi
elif ! dotnet publish src/AI2P.Server/AI2P.Server.csproj -c "$CONFIGURATION" "--property:PublishDir=$OUT_DIR/"; then
    echo "[ERROR] dotnet publish failed."
    exit 1
fi

# 2. native C/C++ plugins (CMake) - nothing yet; the empty native/ folder is removed (T-207)

# 3. версия выкладки и скрипты установки (ТЗ гл. 4.3, этап 46).
#    version.json — то, по чему install.* сверяет версии; номер спрашиваем у самой
#    собранной программы (--version), чтобы он не разъезжался с AppInfo
VERSION=""
if [ -x "$OUT_DIR/AI2P.Server" ]; then
    VERSION="$("$OUT_DIR/AI2P.Server" --version 2>/dev/null | head -1)"
fi
if [ -z "$VERSION" ] && [ -f "$OUT_DIR/AI2P.Server.dll" ]; then
    VERSION="$(dotnet "$OUT_DIR/AI2P.Server.dll" --version 2>/dev/null | head -1)"
fi
if [ -z "$VERSION" ]; then
    # ВЫКЛАДКА ПОД ЧУЖУЮ ПЛАТФОРМУ (T-211): собранную для win-x64 программу на Linux
    # не запустить, и «dotnet AI2P.Server.dll» тоже не годится — у самодостаточной выкладки
    # dll привязана к своему рантайму. Спросить номер не у кого, поэтому берём его
    # из ТОГО ЖЕ исходника, из которого только что собрали
    APPINFO="$SCRIPT_DIR/src/AI2P.Core/AppInfo.cs"
    if [ -f "$APPINFO" ]; then
        VERSION="$(sed -n 's/.*Version[[:space:]]*=[[:space:]]*"\([^"]*\)".*/\1/p' "$APPINFO" | head -1)"
        if [ -n "$VERSION" ]; then
            echo "Программа собрана под другую платформу и здесь не запускается — версия взята из AppInfo.cs: $VERSION"
        fi
    fi
fi
if [ -z "$VERSION" ]; then
    echo "[ERROR] Не удалось определить версию собранной программы (--version)."
    exit 1
fi
BUILD="$(echo "$VERSION" | awk -F. '{ if (NF >= 2) print $2 + 0; else print 0 }')"
# selfContained в version.json — то, по чему УСТАНОВКА понимает, проверять ли рантайм (T-211):
# полной выкладке dotnet на машине не нужен вовсе, и спрашивать про него нечего
if [ "$SELF_CONTAINED" -eq 1 ]; then SC_JSON="true"; else SC_JSON="false"; fi
cat > "$OUT_DIR/version.json" <<VJSON
{
  "version": "$VERSION",
  "build": $BUILD,
  "selfContained": $SC_JSON,
  "runtime": "$RUNTIME",
  "published": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
VJSON
# 4. ДОКУМЕНТАЦИЯ (ТЗ гл. 14, задания todo47, todo47_2): каталог AI2P_app/doc переписывается
#    в КОРЕНЬ выкладки ЦЕЛИКОМ — приложение показывает эти документы прямо в UI (кнопка «i»
#    в форме модели и в форме источника импорта). Каталог doc/ в корне репозитория
#    (ТЗ, отчёты) в выкладку не входит — он про проект, а не про работу с программой
DOC_SOURCE="$SCRIPT_DIR/doc"
if [ -d "$DOC_SOURCE" ]; then
    rm -rf "$OUT_DIR/doc"
    mkdir -p "$OUT_DIR/doc"
    (cd "$DOC_SOURCE" && tar cf - .) | (cd "$OUT_DIR/doc" && tar xf -)
    DOC_COUNT="$(find "$OUT_DIR/doc" -type f | wc -l | tr -d ' ')"
    echo "Документация скопирована: $DOC_COUNT файлов в $OUT_DIR/doc"
else
    echo "[WARN] Каталог документации не найден: $DOC_SOURCE"
fi

# СКРИПТЫ УСТАНОВКИ КЛАДУТСЯ ТОЛЬКО СВОЕЙ ПЛАТФОРМЫ (T-211): в выкладке для Linux/macOS нечего
# делать install.ps1 и install.cmd, в выкладке для Windows — install.sh. Платформа — та же
# самая, по которой выбран каталог ОС ($TARGET_OS, T-243): рантайм (--runtime) у полной
# выкладки, система сборки — у обычной.
#
# СБОРЩИК ПАКЕТА УСТАНОВКИ (T-285) кладётся туда же и по тому же правилу платформы:
# MakePackage.sh — в выкладку для Linux/macOS, MakePackage.ps1/.cmd — в выкладку для Windows.
# Запускается ИЗ каталога выкладки и делает из неё один файл установщика в ../../packages.
#
# НАСТРОЙКА ЗАПУСКА СЕРВИСОМ ОС (T-271) кладётся по тому же правилу: makeAsServise.sh —
# в выкладку для Linux/macOS, makeAsServise.ps1/.cmd — в выкладку для Windows. В отличие
# от MakePackage этот скрипт нужен не сборщику, а тому, у кого программа установлена:
# install.* переносит его из выкладки в установку, и человек зовёт его там руками
case "$TARGET_OS" in
    windows) INSTALL_SCRIPTS="install.ps1 install.cmd"; PACKAGE_SCRIPTS="MakePackage.ps1 MakePackage.cmd"; SERVICE_SCRIPTS="makeAsServise.ps1 makeAsServise.cmd" ;;
    *)       INSTALL_SCRIPTS="install.sh";              PACKAGE_SCRIPTS="MakePackage.sh";                  SERVICE_SCRIPTS="makeAsServise.sh" ;;
esac
ALL_SCRIPTS="$INSTALL_SCRIPTS $PACKAGE_SCRIPTS $SERVICE_SCRIPTS"
# чужие скрипты из прежней выкладки в каталоге оставаться не должны
for script in install.ps1 install.cmd install.sh MakePackage.ps1 MakePackage.cmd MakePackage.sh makeAsServise.ps1 makeAsServise.cmd makeAsServise.sh; do
    case " $ALL_SCRIPTS " in
        *" $script "*) ;;
        *) rm -f "$OUT_DIR/$script" ;;
    esac
done
for script in $ALL_SCRIPTS; do
    if [ -f "$SCRIPT_DIR/$script" ]; then
        cp -f "$SCRIPT_DIR/$script" "$OUT_DIR/$script"
    fi
done
chmod +x "$OUT_DIR/install.sh" 2>/dev/null
chmod +x "$OUT_DIR/MakePackage.sh" 2>/dev/null
chmod +x "$OUT_DIR/makeAsServise.sh" 2>/dev/null
echo "Версия выкладки: $VERSION (билд $BUILD); скрипты установки: $INSTALL_SCRIPTS."
echo "Сборка пакета установки: $PACKAGE_SCRIPTS (запускать из каталога выкладки)."
echo "Запуск сервисом ОС: $SERVICE_SCRIPTS (запускать из каталога установки)."

echo ""
echo "=== BuildRelease OK ==="
# у выкладки под Windows рядом AI2P.Server.exe, у остальных — файл без расширения (T-243)
if [ "$TARGET_OS" = "windows" ]; then echo "Запуск: $OUT_DIR/AI2P.Server.exe"; else echo "Запуск: $OUT_DIR/AI2P.Server"; fi
case "$TARGET_OS" in
    windows) echo "Установка/обновление на другой машине: install.cmd <каталог> (из этой выкладки)"
             echo "Пакет установки одним файлом: cd \"$OUT_DIR\" и MakePackage.cmd (результат — в ../../packages)"
             echo "Сервис ОС (по желанию): в каталоге УСТАНОВКИ makeAsServise.cmd от имени администратора" ;;
    *)       echo "Установка/обновление на другой машине: ./install.sh <каталог> (из этой выкладки)"
             echo "Пакет установки одним файлом: cd \"$OUT_DIR\" и ./MakePackage.sh (результат — в ../../packages)"
             echo "Сервис ОС (по желанию): в каталоге УСТАНОВКИ ./makeAsServise.sh" ;;
esac
if [ "$SELF_CONTAINED" -eq 1 ]; then
    echo "Рантайм внутри выкладки — dotnet на машине ставить не нужно."
else
    echo "Рантайма внутри нет — на машине нужен ASP.NET Core 8.x (install проверит и предложит)."
fi
echo ""
echo "Перенести руками (сборкой не копируются и не перезаписываются):"
echo "  * data/        — база и файлы проектов"
echo "  * logs/        — при желании"
echo "  * secrets.json — ключи API (шаблон рядом: secrets.example.json)"
echo ""
echo "Две версии одновременно на одном порту не работают (проверка одного экземпляра):"
echo "поменяйте ui.port в $OUT_DIR/config.json, если нужно держать обе запущенными."
exit 0
