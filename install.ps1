# ============================================================
#  AI2P - install.ps1  (Windows)
#  Installing and updating a release (spec ch. 4.3). The full description is in
#  the help: install.cmd --help  /  install.ps1 -Help.
#
#  THE TEXTS LIVE OUTSIDE THE SCRIPT (T-65-S0): the messages in
#  i18n/scripts.<lang>.txt, the help in i18n/help/install.<lang>.txt. The script is
#  never duplicated per language - a new language is a new pair of files.
#  The default language is en; -Lang xx and AI2P_LANG override it, in that order.
# ============================================================
param(
    [Parameter(Position = 0)]
    [string]$Target = "",
    [switch]$Force,
    [switch]$WhatIf,
    [switch]$NoRuntimeCheck,
    [string]$Lang = "",
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$source = $PSScriptRoot

# ТЕКСТЫ СООБЩЕНИЙ ЛЕЖАТ СНАРУЖИ (T-65-S0). Каталога рядом может не оказаться (кто-то
# унёс один install.ps1 из выкладки) — тогда L отдаёт сам ключ, и скрипт всё равно
# работает: из-за отсутствия перевода установка падать не должна
$locFile = Join-Path $PSScriptRoot "i18n\loc.ps1"
if (Test-Path -LiteralPath $locFile) { . $locFile }
else {
    function L { param([string]$Key) return $Key }
    function Set-Ai2pLang([string]$lang) { }
    function Show-Ai2pHelp([string]$name) { Write-Host "No i18n folder next to install.ps1" }
}
Set-Ai2pLang $Lang

if ($Help -or [string]::IsNullOrWhiteSpace($Target)) {
    Show-Ai2pHelp "install"
    if ($Help) { exit 0 }
    exit 1
}
# служба этой установки была остановлена нами и должна вернуться в работу (T-271)
$serviceWasRunning = $false

# файлы и каталоги ПОЛЬЗОВАТЕЛЯ: при обновлении не трогаются никогда.
# secrets — подкаталог с ключами моделей и источников импорта, по одному json на ключ (T-228):
# повторная установка его не удаляет и не переписывает, в опись он не попадает
$keep = @("data", "logs", "secrets.json", "secrets", "config.json")
# что не копируется вовсе. config.json стоит особняком и в этот список не входит:
# при первичной установке он копируется как есть, а при обновлении кладётся рядом
# как config.new.json — рабочий не трогаем, сливает его приложение (ConfigMerge)
$skip = @("data", "logs", "secrets.json", "secrets")
# каталоги, целиком принадлежащие дистрибутиву: переписываются НАЧИСТО (ТЗ гл. 14, todo47).
# Иначе в установке остаются документы, статика и нативные библиотеки, которых в новой
# версии уже нет, — а после прыжка через несколько версий их набирается тем больше,
# чем дальше прыжок. Файлов пользователя тут не бывает
$wipe = @("doc", "wwwroot", "runtimes")
# ОПИСЬ УСТАНОВКИ (T-183): что именно положила сюда прошлая установка. По ней видно,
# каких файлов в новой версии не стало, — и только они удаляются
$inventoryName = "installed.json"

function Read-Version([string]$dir) {
    $file = Join-Path $dir "version.json"
    if (Test-Path $file) {
        try { return (Get-Content $file -Raw | ConvertFrom-Json) } catch { return $null }
    }
    # version.json нет (старая выкладка) — спросим саму программу
    $exe = Join-Path $dir "AI2P.Server.exe"
    if (Test-Path $exe) {
        try {
            $printed = & $exe --version 2>$null | Select-Object -First 1
            if ($printed) { return [pscustomobject]@{ version = $printed.Trim(); build = 0 } }
        } catch { }
    }
    return $null
}

# --- РАНТАЙМ .NET (T-211) --------------------------------------------------------------
# Обычная выкладка собрана БЕЗ рантайма и не запустится, пока на машине нет ASP.NET Core 8.x.
# Именно 8.x, а не «любой посвежее»: приложение net8.0 на рантайме 9/10 стартует, но
# интерактивность Blazor молча умирает — клиентский blazor.web.js версии 8 против сервера
# другой версии (опыт проекта). Поэтому проверяем ровно ветку 8

function Test-AspNet8 {
    try {
        $lines = & dotnet --list-runtimes 2>$null
    }
    catch {
        return $false
    }
    if (-not $lines) { return $false }
    return @($lines | Where-Object { $_ -like "Microsoft.AspNetCore.App 8.*" }).Count -gt 0
}

function Install-AspNet8 {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Host (L 'scr.inst.1') -ForegroundColor Red
        Write-Host (L 'scr.inst.2') -ForegroundColor Red
        Write-Host (L 'scr.inst.3') -ForegroundColor Red
        return $false
    }
    Write-Host (L 'scr.inst.4') -ForegroundColor Cyan
    & winget install --id Microsoft.DotNet.AspNetCore.8 -e --silent `
        --accept-package-agreements --accept-source-agreements
    if (Test-AspNet8) {
        Write-Host (L 'scr.inst.5') -ForegroundColor Green
        return $true
    }
    Write-Host (L 'scr.inst.6') -ForegroundColor Yellow
    Write-Host (L 'scr.inst.7') -ForegroundColor Yellow
    return $false
}

# Проверка того, без чего выкладка не запустится. Полной выкладке (рантайм внутри)
# проверять нечего — она ровно для того и собирается
function Confirm-Runtime($info) {
    if ($NoRuntimeCheck) { return }
    if ($info.selfContained -eq $true) {
        Write-Host (L 'scr.inst.8') -ForegroundColor Cyan
        return
    }
    if (Test-AspNet8) {
        Write-Host (L 'scr.inst.9') -ForegroundColor Green
        return
    }

    Write-Host ""
    Write-Host (L 'scr.inst.10') -ForegroundColor Yellow
    Write-Host (L 'scr.inst.11') -ForegroundColor Yellow
    if ($WhatIf) {
        Write-Host (L 'scr.inst.12' '-WhatIf') -ForegroundColor Yellow
        return
    }
    if ($Force) {
        [void](Install-AspNet8)
        return
    }
    # вопрос печатается Write-Host, а не подсказкой Read-Host: подсказку Read-Host пишет
    # прямо в консоль мимо потока вывода, и в перенаправленном в файл журнале установки
    # («install.ps1 … > install.log») от вопроса не остаётся ни следа
    Write-Host (L 'scr.inst.13') -ForegroundColor Yellow
    $answer = ""
    try { $answer = Read-Host } catch { $answer = "n" }
    if ($answer -match '^\s*(y|yes|д|да)?\s*$') {
        [void](Install-AspNet8)
    }
    else {
        Write-Host (L 'scr.inst.14') -ForegroundColor Yellow
        Write-Host (L 'scr.inst.15') -ForegroundColor Yellow
    }
}

# --- СЛУЖБА ОС (T-271) -----------------------------------------------------------------
# Установку можно сделать сервисом (makeAsServise.cmd), и тогда обновлять её «как обычно»
# нельзя: служба работает, держит свои файлы, и копирование поверх сорвётся. Настоящее
# состояние знает СИСТЕМА — её и спрашиваем; пометка serviceMode в config.json — только
# записка для человека и для UI, и на неё нельзя опираться как на истину.
$serviceName = "AI2P"

function Get-Ai2pService {
    try { return Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop }
    catch { return $null }
}

# путь исполняемого файла из PathName службы: он записан в кавычках и может иметь ключи
function Get-ServiceExe([string]$pathName) {
    if ([string]::IsNullOrWhiteSpace($pathName)) { return "" }
    $value = $pathName.Trim()
    if ($value.StartsWith('"')) {
        $end = $value.IndexOf('"', 1)
        if ($end -gt 0) { return $value.Substring(1, $end - 1) }
    }
    return ($value -split ' ')[0]
}

# служба ЭТОЙ установки: чужую (другой каталог) не трогаем ни при каких условиях
function Get-ServiceOfTarget([string]$dir) {
    $svc = Get-Ai2pService
    if ($null -eq $svc) { return $null }
    $svcExe = Get-ServiceExe $svc.PathName
    if ($svcExe -and $svcExe.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)) { return $svc }
    return $null
}

# пометка «эта установка настроена сервисом» — её ставит makeAsServise.
# Рабочий config.json не обязан лежать рядом с программой (T-287): установка в каталог
# программ уводит его в общий каталог данных компьютера или в каталог данных пользователя.
# Правило выбора живёт в приложении (AppHome) и здесь НЕ повторяется — мы просто смотрим
# в те же места и берём первый найденный файл, как это делает makeAsServise.ps1
function Test-ServiceFlag([string]$dir) {
    # каталог ПРОГРАММ пропускается: пакет установки кладёт config.json и туда, а
    # приложение оттуда его не читает (см. makeAsServise.ps1, AppHome)
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $inProgramDir = $false
    foreach ($root in $roots) {
        $r = $root.TrimEnd('\')
        if ($dir -ieq $r -or $dir.StartsWith($r + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            $inProgramDir = $true
        }
    }
    $places = @()
    if (-not [string]::IsNullOrWhiteSpace($env:AI2P_HOME)) { $places += $env:AI2P_HOME }
    if (-not $inProgramDir) { $places += $dir }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) { $places += (Join-Path $env:ProgramData "AI2P") }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $places += (Join-Path $env:LOCALAPPDATA "AI2P") }
    $places += $dir
    foreach ($place in $places) {
        $cfg = Join-Path $place "config.json"
        if (-not (Test-Path $cfg)) { continue }
        try { return ([System.IO.File]::ReadAllText($cfg) -match '"serviceMode"\s*:\s*true') } catch { return $false }
    }
    return $false
}

function Build-Of($info) {
    if ($null -eq $info) { return -1 }
    if ($info.build -and $info.build -gt 0) { return [int]$info.build }
    # «1.46» -> 46
    $parts = "$($info.version)".Split('.')
    if ($parts.Length -ge 2 -and [int]::TryParse($parts[1], [ref]$null)) { return [int]$parts[1] }
    return 0
}

$newInfo = Read-Version $source
if ($null -eq $newInfo) {
    Write-Host (L 'scr.inst.16') -ForegroundColor Red
    exit 1
}
$newBuild = Build-Of $newInfo

if ([System.IO.Path]::IsPathRooted($Target)) { $targetDir = $Target }
else { $targetDir = Join-Path (Get-Location) $Target }
$targetDir = [System.IO.Path]::GetFullPath($targetDir)

Write-Host "=== AI2P install ===" -ForegroundColor Cyan
Write-Host (L 'scr.inst.17' $source $newInfo.version)
Write-Host (L $(if ($newInfo.selfContained -eq $true) { 'scr.inst.18' } else { 'scr.inst.19' }))
Write-Host (L 'scr.inst.20' $targetDir)

# КАТАЛОГ УСТАНОВКИ НЕ СМЕЕТ ЛЕЖАТЬ ВНУТРИ ВЫКЛАДКИ (todo133). Сравнить каталоги на
# равенство мало: копирование каталога ВНУТРЬ САМОГО СЕБЯ кормит собственный обход —
# Get-ChildItem -Recurse доходит до только что созданной копии и копирует её ещё глубже,
# и так до предела длины пути. Живьём это стоило 4 ГБ мусора и 126 уровней вложенности
# в самой выкладке; следующий install.cmd после этого «висел» по полчаса, потому что
# честно тащил весь этот ком в установку.
# Ловушка не теоретическая: путь приходит из командной строки, и стоит оболочке съесть
# обратные слэши (bash так и делает: «C:\mmf\...\inst» превращается в «C:mmf...inst»),
# как диск-относительный путь раскрывается ОТ ТЕКУЩЕГО КАТАЛОГА — то есть внутрь выкладки.
# Обратный случай (выкладка внутри приёмника) не лучше: уборка $wipe снесёт сам источник
$sourceFull = [System.IO.Path]::GetFullPath($source).TrimEnd('\')
$targetFull = $targetDir.TrimEnd('\')
if ($targetFull -ieq $sourceFull) {
    Write-Host (L 'scr.inst.21') -ForegroundColor Red
    exit 1
}
if ($targetFull.StartsWith($sourceFull + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
    $sourceFull.StartsWith($targetFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Host (L 'scr.inst.75') -ForegroundColor Red
    Write-Host (L 'scr.inst.76') -ForegroundColor Red
    exit 1
}

# то, без чего скопированные файлы не запустятся, проверяем ДО копирования (T-211)
Confirm-Runtime $newInfo

$exists = Test-Path $targetDir
$hasFiles = $exists -and @(Get-ChildItem -Path $targetDir -Force).Count -gt 0
$oldInfo = if ($hasFiles) { Read-Version $targetDir } else { $null }
$oldBuild = Build-Of $oldInfo

if (-not $hasFiles) {
    Write-Host (L 'scr.inst.22') -ForegroundColor Green
}
else {
    if ($null -eq $oldInfo -and -not $Force) {
        Write-Host (L 'scr.inst.23') -ForegroundColor Red
        Write-Host (L 'scr.inst.24' '-Force') -ForegroundColor Red
        exit 1
    }
    Write-Host (L 'scr.inst.25' $oldInfo.version) -ForegroundColor Green
    if ($oldBuild -gt $newBuild -and -not $Force) {
        Write-Host (L 'scr.inst.26' $oldInfo.version $newInfo.version) -ForegroundColor Red
        Write-Host (L 'scr.inst.27') -ForegroundColor Red
        Write-Host (L 'scr.inst.28' '-Force') -ForegroundColor Red
        exit 1
    }
    if ($oldBuild -eq $newBuild) {
        Write-Host (L 'scr.inst.29') -ForegroundColor Yellow
    }

    # СЛУЖБА ЭТОЙ УСТАНОВКИ (T-271): её надо остановить ДО копирования и вернуть в работу
    # после. Отказываться, как от запущенного вручную приложения, здесь нельзя: обновление
    # сервера-службы — обычное дело, а человеку иначе пришлось бы гасить её самому
    $service = Get-ServiceOfTarget $targetDir
    if ($null -ne $service) {
        Write-Host (L 'scr.inst.30' $serviceName $service.State) -ForegroundColor Cyan
        if ($service.State -eq "Running" -and -not $WhatIf) {
            Write-Host (L 'scr.inst.31') -ForegroundColor Cyan
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            try { (Get-Service -Name $serviceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 90)) }
            catch {
                Write-Host (L 'scr.inst.32' $serviceName) -ForegroundColor Red
                exit 1
            }
            $serviceWasRunning = $true
        }
    }
    elseif (Test-ServiceFlag $targetDir) {
        # записка есть, а службы нет: её сняли руками либо ставим на другую машину
        Write-Host (L 'scr.inst.33' $serviceName) -ForegroundColor Yellow
        Write-Host (L 'scr.inst.34' 'makeAsServise.cmd') -ForegroundColor Yellow
    }

    # запущенное приложение держит свои файлы: обновлять его на ходу нельзя
    $running = @(Get-Process -Name "AI2P.Server" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($targetDir, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count -gt 0 -and -not $WhatIf) {
        Write-Host (L 'scr.inst.35' ($running.Id -join ', ')) -ForegroundColor Red
        Write-Host (L 'scr.inst.36') -ForegroundColor Red
        exit 1
    }
}

# --- состав новой выкладки (опись будущей установки) и опись прошлой ---
# Пути относительные, разделитель '/' — чтобы описи Windows и Linux читались одинаково
$payload = @(Get-ChildItem -Path $source -Force -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($source.Length).TrimStart('\', '/')
        if ($skip -contains $rel.Split('\')[0]) { return }
        $rel -replace '\\', '/'
    })
$oldFiles = @()
$inventoryPath = Join-Path $targetDir $inventoryName
if ($hasFiles -and (Test-Path $inventoryPath)) {
    try { $oldFiles = @((Get-Content $inventoryPath -Raw | ConvertFrom-Json).files) } catch { $oldFiles = @() }
}
# устаревшее = было в прошлой описи и нет в новой выкладке. Данные пользователя и сама опись
# в этот счёт не попадают никогда, даже если кто-то впишет их в опись руками
$fresh = New-Object 'System.Collections.Generic.HashSet[string]' (
    [string[]]$payload, [System.StringComparer]::OrdinalIgnoreCase)
$stale = @($oldFiles | Where-Object {
        $_ -and -not $fresh.Contains(($_ -replace '\\', '/')) `
            -and ($keep -notcontains ($_ -replace '\\', '/').Split('/')[0]) `
            -and $_ -ne $inventoryName -and $_ -notlike "config.new.json*"
    } | ForEach-Object { $_ -replace '\\', '/' })

if ($WhatIf) {
    Write-Host ""
    Write-Host (L 'scr.inst.37' '-WhatIf') -ForegroundColor Yellow
    Write-Host (L 'scr.inst.38' $payload.Count ($keep -join ', '))
    if ($hasFiles) {
        if ($oldFiles.Count -eq 0) {
            Write-Host (L 'scr.inst.39' $inventoryName)
        }
        else {
            Write-Host (L 'scr.inst.40' $oldInfo.version $stale.Count)
        }
        if ($null -ne (Get-ServiceOfTarget $targetDir)) {
            Write-Host (L 'scr.inst.41' $serviceName)
        }
    }
    exit 0
}

if (-not $exists) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }

# каталоги дистрибутива переписываются ЦЕЛИКОМ (ТЗ гл. 14, задание todo47): иначе в установке
# остались бы документы и статика, которых в новой версии уже нет. Файлов пользователя там нет
foreach ($dir in $wipe) {
    $dirTarget = Join-Path $targetDir $dir
    if ((Test-Path (Join-Path $source $dir)) -and (Test-Path $dirTarget)) {
        Remove-Item -Path $dirTarget -Recurse -Force
    }
}

# --- копирование ---
$copied = 0
# СПИСОК СНИМАЕТСЯ ЦЕЛИКОМ ДО ПЕРВОГО КОПИРОВАНИЯ (todo133) — вторая страховка к проверке
# «приёмник внутри источника» выше. Конвейерный Get-ChildItem отдаёт файлы ПО ХОДУ обхода,
# и если копия почему-то всё же легла внутрь источника, обход дойдёт до неё и начнёт
# копировать собственный результат. @(...) закрывает эту дверь по построению
@(Get-ChildItem -Path $source -Force -Recurse) | ForEach-Object {
    $rel = $_.FullName.Substring($source.Length).TrimStart('\', '/')
    $top = $rel.Split('\')[0]
    if ($skip -contains $top) { return }
    $dest = Join-Path $targetDir $rel
    if ($_.PSIsContainer) {
        if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        return
    }
    # config.json новой версии кладётся рядом: рабочий не трогаем, сольёт приложение
    if ($rel -eq "config.json" -and (Test-Path (Join-Path $targetDir "config.json"))) {
        $dest = Join-Path $targetDir "config.new.json"
    }
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -Path $_.FullName -Destination $dest -Force
    $copied++
}

# --- уборка того, чего в новой версии больше нет (T-183) ---
$removed = 0
foreach ($rel in $stale) {
    $path = Join-Path $targetDir ($rel -replace '/', '\')
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
        $removed++
        # опустевший каталог убираем вслед за файлом, но не выше корня установки
        $dir = Split-Path $path -Parent
        while ($dir -and $dir.Length -gt $targetDir.Length -and (Test-Path -LiteralPath $dir) `
                -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) {
            Remove-Item -LiteralPath $dir -Force
            $dir = Split-Path $dir -Parent
        }
    }
}

# опись НОВОЙ установки: по ней следующая версия узнает, что тут лежало
[ordered]@{
    version   = "$($newInfo.version)"
    build     = $newBuild
    installed = (Get-Date).ToUniversalTime().ToString("o")
    files     = @($payload | Sort-Object)
} | ConvertTo-Json -Depth 3 | Set-Content -Path $inventoryPath -Encoding UTF8

# --- служба возвращается в работу (T-271) ---
# Путь к программе не менялся (обновление кладётся в тот же каталог), поэтому службу
# достаточно запустить. Не запустилась — это надо сказать: молча оставить сервер лежать
# нельзя, человек ждёт, что он поднимается сам
if ($serviceWasRunning) {
    Write-Host (L 'scr.inst.42' $serviceName) -ForegroundColor Cyan
    try {
        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus('Running', (New-TimeSpan -Seconds 120))
        Write-Host (L 'scr.inst.43' $serviceName) -ForegroundColor Green
    }
    catch {
        Write-Host (L 'scr.inst.44' $serviceName $_.Exception.Message) -ForegroundColor Yellow
        Write-Host (L 'scr.inst.45' "$targetDir\logs\ai2p-*.jsonl") -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host (L 'scr.inst.46' $copied) -ForegroundColor Green
if ($removed -gt 0) {
    Write-Host (L 'scr.inst.47' $removed) -ForegroundColor Cyan
}
elseif ($hasFiles -and $oldFiles.Count -eq 0) {
    Write-Host (L 'scr.inst.48' $inventoryName) -ForegroundColor Cyan
}
Write-Host (L 'scr.inst.49' $newInfo.version)
if ($hasFiles) {
    Write-Host ""
    Write-Host (L 'scr.inst.50' ($keep -join ', ')) -ForegroundColor Cyan
    if (Test-Path (Join-Path $targetDir "config.new.json")) {
        Write-Host (L 'scr.inst.51') -ForegroundColor Cyan
        Write-Host (L 'scr.inst.52') -ForegroundColor Cyan
    }
    Write-Host (L 'scr.inst.53') -ForegroundColor Cyan
}
else {
    Write-Host ""
    Write-Host (L 'scr.inst.54' (Join-Path $targetDir 'AI2P.Server.exe')) -ForegroundColor Cyan
    Write-Host (L 'scr.inst.55') -ForegroundColor Cyan
    Write-Host (L 'scr.inst.56' 'makeAsServise.cmd') -ForegroundColor Cyan
    Write-Host (L 'scr.inst.57' $serviceName) -ForegroundColor Cyan
}
exit 0
