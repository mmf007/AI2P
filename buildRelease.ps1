# ============================================================
#  AI2P - buildRelease.ps1  (Windows)   [до T-285 назывался publish.ps1]
#  Релизная выкладка сервера в отдельный каталог: самодостаточный
#  комплект файлов, который запускается без исходников и без dotnet run.
#  Нужен, чтобы рядом с разрабатываемой версией всегда оставалась
#  работающая старая (ТЗ гл. 4.2).
#
#  Использование:
#    .\buildRelease.ps1                          # в builds\windows\release
#    .\buildRelease.ps1 -Output D:\AI2P_v1.45    # в указанный каталог
#    .\buildRelease.ps1 -Clean                   # очистить каталог (кроме данных) и выложить заново
#    .\buildRelease.ps1 -SelfContained           # С РАНТАЙМОМ ВНУТРИ, в builds\windows\releasefull
#
#  ПАКЕТ УСТАНОВКИ (T-285) собирается ОТДЕЛЬНЫМ шагом и уже из готовой выкладки:
#  в неё кладётся MakePackage.ps1/.cmd (для Linux/macOS — MakePackage.sh), запускать
#  его надо ИЗ каталога выкладки, результат ложится в ..\..\packages.
#
#  ДВЕ ВЫКЛАДКИ (T-211). Обычная — без рантайма: маленькая, но требует на машине
#  установленный ASP.NET Core 8.x (её install.ps1 это проверяет и предлагает поставить).
#  Полная (-SelfContained) — рантайм лежит внутри неё самой, ставить на машину нечего,
#  зато выкладка привязана к платформе (-Runtime, по умолчанию win-x64) и весит на
#  порядок больше. Каталоги разные и НЕ смешиваются.
#
#  КАТАЛОГ ОПЕРАЦИОННОЙ СИСТЕМЫ (T-243). Между builds и release/releasefull стоит
#  подкаталог ОС, под которую собрана выкладка, — windows, linux или macos:
#      builds\windows\release      — без рантайма (как было всегда, но в каталоге ОС)
#      builds\windows\releasefull  — с рантаймом
#      builds\linux\releasefull    — то же самое, собранное под Linux (-Runtime linux-x64)
#      builds\macos\releasefull    — под macOS (-Runtime osx-x64 / osx-arm64)
#  САМ КАТАЛОГ builds ЛЕЖИТ ВНУТРИ РАБОЧЕГО КАТАЛОГА (T-131-S0) — рядом с AI2P.sln,
#  то есть в каталоге этого скрипта, а не на уровень выше него, как было до 1.114.
#  Выкладки разных систем больше не затирают друг друга в одном каталоге: у них разные
#  скрипты установки, разный рантайм внутри и разные исполняемые файлы.
#
#  Каталог данных сюда НЕ копируется: data/, logs/, config.json,
#  secrets.json и secrets/ (ключи моделей, по одному json на ключ — T-228)
#  переносятся руками и при повторной выкладке не трогаются.
# ============================================================
param(
    [string]$Output = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$Clean,
    [string]$Lang = "",
    [switch]$Help
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# --help печатается из отдельного файла (T-65-S0): i18n/help/buildRelease.<язык>.txt.
# Собственный вывод этого скрипта — ярус B разбора T-64-S0, через каталог он пока не
# переведён (и в этом нет спешки: скрипт запускается на машине сборки)
$locFile = Join-Path $PSScriptRoot "i18n\loc.ps1"
if ($Help) {
    if (Test-Path -LiteralPath $locFile) { . $locFile; Set-Ai2pLang $Lang; Show-Ai2pHelp "buildRelease" }
    else { Write-Host "Usage: buildRelease.ps1 [-Output DIR] [-Configuration Release|Debug] [-SelfContained] [-Clean]" }
    exit 0
}

# ОС ЦЕЛИ (T-243): выкладка кладётся в подкаталог своей системы (windows/linux/macos).
# Платформу задаёт рантайм (-Runtime) у полной выкладки, а у обычной — та система,
# на которой идёт сборка: у неё внутри скрипты установки этой системы и ничего больше.
# ($IsLinux/$IsMacOS есть только в PowerShell 7+; в Windows PowerShell 5.1 их нет,
#  и переменная просто пуста — это и означает Windows.)
if ($SelfContained) {
    $targetOs = switch -Wildcard ($Runtime) {
        "win-*" { "windows"; break }
        "osx-*" { "macos"; break }
        default { "linux" }
    }
}
elseif ($IsLinux) { $targetOs = "linux" }
elseif ($IsMacOS) { $targetOs = "macos" }
else { $targetOs = "windows" }

# каталог по умолчанию зависит от вида выкладки: смешивать их в одном каталоге нельзя —
# у полной внутри рантайм, и при переходе к обычной он остался бы там мусором
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = if ($SelfContained) { "builds/$targetOs/releasefull" } else { "builds/$targetOs/release" }
}

# файлы и каталоги пользователя: не удаляются даже при -Clean.
# secrets — подкаталог с ключами моделей и источников импорта, по одному json на ключ (T-228)
$keep = @("data", "logs", "config.json", "secrets.json", "secrets")

# путь может быть относительным — считаем его от каталога скрипта (AI2P_app)
if ([System.IO.Path]::IsPathRooted($Output)) { $outDir = $Output }
else { $outDir = Join-Path $PSScriptRoot $Output }
$outDir = [System.IO.Path]::GetFullPath($outDir)

$kind = if ($SelfContained) { "с рантаймом внутри ($Runtime)" } else { "без рантайма (нужен ASP.NET Core 8.x)" }
Write-Host "=== AI2P buildRelease ($Configuration) ===" -ForegroundColor Cyan
Write-Host "Вид выкладки: $kind"
Write-Host "Операционная система выкладки: $targetOs"
Write-Host "Каталог выкладки: $outDir"

# защита от выкладки внутрь исходников: снесёт src/ при -Clean и запутает сборку
$srcRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "src"))
if ($outDir -eq $PSScriptRoot -or $outDir.StartsWith($srcRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "[ERROR] Каталог выкладки не должен быть внутри исходников." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
elseif ($Clean) {
    Write-Host "Очистка каталога (сохраняются: $($keep -join ', '))..." -ForegroundColor Yellow
    Get-ChildItem -Path $outDir -Force | Where-Object { $keep -notcontains $_.Name } |
        Remove-Item -Recurse -Force -Confirm:$false
}
else {
    $existing = @(Get-ChildItem -Path $outDir -Force)
    if ($existing.Count -gt 0) {
        Write-Host "Каталог не пуст — файлы выкладки будут перезаписаны." -ForegroundColor Yellow
        Write-Host "Не трогаются: $($keep -join ', '). Полная замена — ключ -Clean." -ForegroundColor Yellow
    }
}

# 1. публикация сервера (тянет за собой Core/Storage/Connectors/UI и статику RCL)
#    PublishDir вместо -o: ключ -o задаёт OutputPath и для проектов-ссылок,
#    из-за чего рядом с src/ появляется мусорный каталог сборки
#    прямые слэши и завершающий '/': PowerShell при передаче в native-программу
#    экранирует кавычку идущим перед ней '\', и путь приезжает в MSBuild искажённым
$publishDir = ($outDir -replace '\\', '/').TrimEnd('/') + '/'
$publishArgs = @(
    "publish", "src/AI2P.Server/AI2P.Server.csproj",
    "-c", $Configuration,
    "--property:PublishDir=$publishDir"
)
if ($SelfContained) { $publishArgs += @("-r", $Runtime, "--self-contained", "true") }

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] dotnet publish failed." -ForegroundColor Red
    exit 1
}

# 2. native C/C++ plugins (CMake) - nothing yet; the empty native/ folder is removed (T-207)

# 3. версия выкладки и скрипты установки (ТЗ гл. 4.3, этап 46).
#    version.json — то, по чему install.* сверяет версии; номер спрашиваем у самой
#    собранной программы (--version), чтобы он не разъезжался с AppInfo
$builtExe = Join-Path $outDir "AI2P.Server.exe"
$version = $null
if (Test-Path $builtExe) {
    try { $version = (& $builtExe --version 2>$null | Select-Object -First 1) } catch { }
}
if (-not $version) {
    # выкладка без .exe (framework-dependent на не-Windows) — спросим через dotnet
    $builtDll = Join-Path $outDir "AI2P.Server.dll"
    if (Test-Path $builtDll) {
        try { $version = (& dotnet $builtDll --version 2>$null | Select-Object -First 1) } catch { }
    }
}
if (-not $version) {
    # ВЫКЛАДКА ПОД ЧУЖУЮ ПЛАТФОРМУ (T-211): собранную для linux-x64 программу на Windows
    # не запустить — рядом с ней не .exe, а ELF-файл, и «dotnet AI2P.Server.dll» тоже не
    # годится: у самодостаточной выкладки dll привязана к своему рантайму. Спросить номер
    # не у кого, поэтому берём его из ТОГО ЖЕ исходника, из которого только что собрали
    $appInfo = Join-Path $PSScriptRoot "src/AI2P.Core/AppInfo.cs"
    if (Test-Path $appInfo) {
        $found = [regex]::Match((Get-Content $appInfo -Raw), 'Version\s*=\s*"([^"]+)"')
        if ($found.Success) {
            $version = $found.Groups[1].Value
            Write-Host "Программа собрана под другую платформу и здесь не запускается — версия взята из AppInfo.cs: $version" -ForegroundColor Yellow
        }
    }
}
if (-not $version) {
    Write-Host "[ERROR] Не удалось определить версию собранной программы (--version)." -ForegroundColor Red
    exit 1
}
$version = $version.Trim()
$build = 0
$parts = $version.Split('.')
if ($parts.Length -ge 2) { [void][int]::TryParse($parts[1], [ref]$build) }
# selfContained в version.json — то, по чему УСТАНОВКА понимает, проверять ли рантайм (T-211):
# полной выкладке dotnet на машине не нужен вовсе, и спрашивать про него нечего
[ordered]@{
    version       = $version
    build         = $build
    selfContained = [bool]$SelfContained
    runtime       = if ($SelfContained) { $Runtime } else { "" }
    published     = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json | Set-Content -Path (Join-Path $outDir "version.json") -Encoding UTF8

# 4. ДОКУМЕНТАЦИЯ (ТЗ гл. 14, задания todo47, todo47_2): каталог AI2P_app/doc
#    переписывается в КОРЕНЬ выкладки ЦЕЛИКОМ — приложение показывает эти документы
#    прямо в UI (кнопка «i» в форме модели и в форме источника импорта).
#    Каталог doc/ в корне репозитория (ТЗ, отчёты) в выкладку не входит — он про проект,
#    а не про работу с программой
$docSource = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "doc"))
if (Test-Path $docSource) {
    $docTarget = Join-Path $outDir "doc"
    if (Test-Path $docTarget) { Remove-Item -Path $docTarget -Recurse -Force }
    Copy-Item -Path $docSource -Destination $docTarget -Recurse -Force
    $docCount = @(Get-ChildItem -Path $docTarget -Recurse -File).Count
    Write-Host "Документация скопирована: $docCount файлов в $docTarget" -ForegroundColor Cyan
}
else {
    Write-Host "[WARN] Каталог документации не найден: $docSource" -ForegroundColor Yellow
}

# СКРИПТЫ УСТАНОВКИ КЛАДУТСЯ ТОЛЬКО СВОЕЙ ПЛАТФОРМЫ (T-211): в выкладке для Windows нечего
# делать install.sh, в выкладке для Linux/macOS — install.ps1 и install.cmd. Платформа —
# та же самая, по которой выбран каталог ОС ($targetOs, T-243): рантайм (-Runtime) у полной
# выкладки, система сборки — у обычной.
# install.cmd — обёртка над install.ps1 (у .ps1 в Windows ассоциация «редактировать»,
# и из проводника/FAR по Enter он не запускается)
#
# СБОРЩИК ПАКЕТА УСТАНОВКИ (T-285) кладётся туда же и по тому же правилу платформы:
# MakePackage.ps1/.cmd — в выкладку для Windows, MakePackage.sh — в выкладку для Linux/macOS.
# Запускается ИЗ каталога выкладки и делает из неё один файл установщика в ..\..\packages.
$targetWindows = ($targetOs -eq "windows")
$installScripts = if ($targetWindows) { @("install.ps1", "install.cmd") } else { @("install.sh") }
$packageScripts = if ($targetWindows) { @("MakePackage.ps1", "MakePackage.cmd") } else { @("MakePackage.sh") }
# НАСТРОЙКА ЗАПУСКА СЕРВИСОМ (T-271) — по тому же правилу платформы. В отличие от
# MakePackage этот скрипт нужен не сборщику, а тому, у кого программа установлена:
# install.* переносит его из выкладки в установку, и человек зовёт его там руками
$serviceScripts = if ($targetWindows) { @("makeAsServise.ps1", "makeAsServise.cmd") } else { @("makeAsServise.sh") }
# @(...) вокруг КАЖДОГО списка — не украшение (T-271). Ветка else отдаёт ОДИН элемент,
# а PowerShell разворачивает массив из одного элемента в саму строку: без @() «+» здесь
# складывает не списки, а СТРОКИ, и получается одно слово «install.shMakePackage.sh».
# Тогда копировать нечего, а цикл уборки чужих скриптов сносит из выкладки все свои —
# выкладка под Linux/macOS, собранная на Windows, оставалась вообще без install.sh
$allScripts = @($installScripts) + @($packageScripts) + @($serviceScripts)
# чужие скрипты из прежней выкладки в каталоге оставаться не должны
foreach ($script in @("install.ps1", "install.cmd", "install.sh", "MakePackage.ps1", "MakePackage.cmd", "MakePackage.sh",
                      "makeAsServise.ps1", "makeAsServise.cmd", "makeAsServise.sh")) {
    if ($allScripts -notcontains $script) {
        $old = Join-Path $outDir $script
        if (Test-Path $old) { Remove-Item -Path $old -Force }
    }
}
foreach ($script in $allScripts) {
    $from = Join-Path $PSScriptRoot $script
    if (Test-Path $from) { Copy-Item -Path $from -Destination (Join-Path $outDir $script) -Force }
}
Write-Host "Версия выкладки: $version (билд $build); скрипты установки: $($installScripts -join ', ')." -ForegroundColor Cyan
Write-Host "Сборка пакета установки: $($packageScripts -join ', ') (запускать из каталога выкладки)." -ForegroundColor Cyan
Write-Host "Запуск сервисом ОС: $($serviceScripts -join ', ') (запускать из каталога установки)." -ForegroundColor Cyan

# у выкладки под Linux/macOS рядом не .exe, а исполняемый файл без расширения:
# показывать «AI2P.Server.exe» для каталога linux — врать человеку (T-243)
$exeName = if ($targetWindows) { "AI2P.Server.exe" } else { "AI2P.Server" }
$exe = Join-Path $outDir $exeName
$cfg = Join-Path $outDir "config.json"
$sec = Join-Path $outDir "secrets.json"

Write-Host ""
Write-Host "=== BuildRelease OK ===" -ForegroundColor Green
Write-Host "Запуск: $exe"
if ($targetWindows) {
    Write-Host "Установка/обновление на другой машине: .\install.cmd <каталог> (или install.ps1)"
    Write-Host "Пакет установки одним файлом: cd `"$outDir`" и .\MakePackage.cmd (результат — в ..\..\packages)"
    Write-Host "Сервис ОС (по желанию): в каталоге УСТАНОВКИ .\makeAsServise.cmd от имени администратора"
}
else {
    Write-Host "Установка/обновление на другой машине: ./install.sh <каталог>"
    Write-Host "Пакет установки одним файлом: cd $outDir и ./MakePackage.sh (результат — в ../../packages)"
    Write-Host "Сервис ОС (по желанию): в каталоге УСТАНОВКИ ./makeAsServise.sh"
}
if ($SelfContained) {
    Write-Host "Рантайм внутри выкладки — dotnet на машине ставить не нужно." -ForegroundColor Cyan
}
else {
    Write-Host "Рантайма внутри нет — на машине нужен ASP.NET Core 8.x (install проверит и предложит)." -ForegroundColor Cyan
}
Write-Host ""
Write-Host "Перенести руками (сборкой не копируются и не перезаписываются):" -ForegroundColor Cyan
Write-Host "  * data\      — база и файлы проектов"
Write-Host "  * logs\      — при желании"
if (-not (Test-Path $sec)) {
    Write-Host "  * secrets.json — ключи API (без него облачные модели неактивны); шаблон рядом: secrets.example.json" -ForegroundColor Yellow
}
Write-Host ""
$port = (Get-Content $cfg -Raw | ConvertFrom-Json).ui.port
Write-Host "Порт в config.json этой копии: $port" -ForegroundColor Cyan
Write-Host "Две версии одновременно на одном порту не работают (проверка одного экземпляра):"
Write-Host "поменяйте ui.port в этой копии, если нужно держать обе запущенными."
exit 0
