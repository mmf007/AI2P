# ============================================================
#  AI2P - makeAsServise.ps1  (Windows, T-271)
#  Turning an installation into an OS service. The full description is in the
#  help: makeAsServise.cmd --help  /  makeAsServise.ps1 -Help.
#
#  THE TEXTS LIVE OUTSIDE THE SCRIPT (T-65-S0): the messages in
#  i18n/scripts.<lang>.txt, the help in i18n/help/makeAsServise.<lang>.txt.
#  The default language is en; -Lang xx and AI2P_LANG override it, in that order.
# ============================================================
param(
    [string]$Target = "",
    [switch]$Remove,
    [switch]$NoStart,
    [switch]$Manual,
    [string]$Account = "",
    [string]$Password = "",
    [switch]$UnprotectSecrets,
    [switch]$Force,
    [switch]$WhatIf,
    [string]$Lang = "",
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# ТЕКСТЫ СООБЩЕНИЙ ЛЕЖАТ СНАРУЖИ (T-65-S0); каталога рядом может не оказаться —
# тогда L отдаёт сам ключ, и скрипт всё равно работает
$locFile = Join-Path $PSScriptRoot "i18n\loc.ps1"
if (Test-Path -LiteralPath $locFile) { . $locFile }
else {
    function L { param([string]$Key) return $Key }
    function Set-Ai2pLang([string]$lang) { }
    function Show-Ai2pHelp([string]$name) { Write-Host "No i18n folder next to makeAsServise.ps1" }
}
Set-Ai2pLang $Lang

if ($Help) { Show-Ai2pHelp "makeAsServise"; exit 0 }

# имя службы задано заданием T-271 и одинаково на всех системах: его знают
# ServiceRun.ServiceName в коде, install.ps1, install.sh и пакет установки
$serviceName = "AI2P"
$displayName = "AI2P"
$serviceDescription = (L 'scr.svc.1')

# --- каталог установки --------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Target)) { $Target = $PSScriptRoot }
if (-not [System.IO.Path]::IsPathRooted($Target)) { $Target = Join-Path (Get-Location).Path $Target }
$targetDir = [System.IO.Path]::GetFullPath($Target).TrimEnd('\')
$exe = Join-Path $targetDir "AI2P.Server.exe"

# --- РАБОЧИЙ каталог: он не обязан совпадать с каталогом программы (T-287) -----------
# Установка в каталог программ (C:\Program Files\AI2P) уводит config.json, data и secrets
# в общий каталог данных компьютера, а если и он закрыт — в каталог данных пользователя.
# Правило выбора живёт в приложении (AppHome), и повторять его здесь НЕЛЬЗЯ: два источника
# правды разъедутся молча. Поэтому мы не выбираем каталог, а ИЩЕМ УЖЕ СУЩЕСТВУЮЩИЙ
# config.json там, где приложение могло его завести, — и берём первый найденный.
#
# Каталог ПРОГРАММ при этом пропускается намеренно: пакет установки кладёт config.json и
# туда (правило «положить, если файла ещё нет»), а приложение из каталога программ его НЕ
# читает — рабочие файлы у такой установки в каталоге данных. Взяв тот файл, мы поставили
# бы пометку в мёртвый конфиг. Это единственная часть правила AppHome, повторённая здесь.
function Test-ProgramDir([string]$dir) {
    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($root in $roots) {
        $r = $root.TrimEnd('\')
        if ($dir -ieq $r) { return $true }
        if ($dir.StartsWith($r + '\', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

$homeCandidates = @()
if (-not [string]::IsNullOrWhiteSpace($env:AI2P_HOME)) { $homeCandidates += $env:AI2P_HOME }
if (-not (Test-ProgramDir $targetDir)) { $homeCandidates += $targetDir }
if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
    $homeCandidates += (Join-Path $env:ProgramData "AI2P")
}
if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    $homeCandidates += (Join-Path $env:LOCALAPPDATA "AI2P")
}
$homeCandidates += $targetDir            # ничего не нашлось — работаем по старому правилу

$homeDir = $targetDir
foreach ($candidate in $homeCandidates) {
    if (Test-Path (Join-Path $candidate "config.json")) {
        $homeDir = [System.IO.Path]::GetFullPath($candidate).TrimEnd('\')
        break
    }
}
$relocated = -not ($homeDir.TrimEnd('\') -ieq $targetDir.TrimEnd('\'))
$configPath = Join-Path $homeDir "config.json"
$secretsPath = Join-Path $homeDir "secrets.json"

Write-Host "=== AI2P makeAsServise (Windows) ===" -ForegroundColor Cyan
Write-Host (L 'scr.svc.2' $targetDir)
if ($relocated) { Write-Host (L 'scr.svc.3' $homeDir) -ForegroundColor Yellow }
Write-Host (L 'scr.svc.4' $serviceName)

if (-not $Remove -and -not (Test-Path $exe)) {
    Write-Host (L 'scr.svc.5') -ForegroundColor Red
    Write-Host (L 'scr.svc.6' '-Target') -ForegroundColor Yellow
    exit 1
}

# --- права администратора -----------------------------------------------------------
# Заводить и снимать службы может только администратор. Скрипт НЕ поднимает себя сам:
# перезапуск «с повышением» открыл бы новое окно, и весь вывод (в том числе разбор
# случая с DPAPI) человек бы не увидел.
#
# Проверка стоит НЕ в начале, а перед самой работой со службой: показать план (-WhatIf)
# и снять пометку в своём же config.json можно и без прав, а отказ «нужен администратор»
# там, где администратор не нужен, — это отказ на ровном месте
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host (L 'scr.svc.7' $identity.Name)

function Require-Admin {
    if ($isAdmin) { return }
    Write-Host (L 'scr.svc.8') -ForegroundColor Red
    Write-Host (L 'scr.svc.9') -ForegroundColor Yellow
    Write-Host "        cd `"$targetDir`" ; .\makeAsServise.cmd" -ForegroundColor Yellow
    exit 1
}

# --- существующая служба ------------------------------------------------------------
function Get-Ai2pService {
    try { return Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop }
    catch { return $null }
}

# путь исполняемого файла из PathName службы: он записан в кавычках и может иметь ключи
function Exe-Of([string]$pathName) {
    if ([string]::IsNullOrWhiteSpace($pathName)) { return "" }
    $value = $pathName.Trim()
    if ($value.StartsWith('"')) {
        $end = $value.IndexOf('"', 1)
        if ($end -gt 0) { return $value.Substring(1, $end - 1) }
    }
    return ($value -split ' ')[0]
}

$existing = Get-Ai2pService

# --- записка «эта установка настроена сервисом» (config.json) ------------------------
# Правится ТОЧЕЧНО, регулярным выражением: полный разбор и обратная запись JSON
# переформатировали бы весь файл пользователя ради одного значения
function Set-ServiceFlag([bool]$value) {
    if (-not (Test-Path $configPath)) { return $false }
    $word = if ($value) { "true" } else { "false" }
    $text = [System.IO.File]::ReadAllText($configPath)
    if ($text -match '"serviceMode"\s*:\s*(true|false)') {
        $text = $text -replace '"serviceMode"\s*:\s*(true|false)', ('"serviceMode": ' + $word)
    }
    else {
        # конфиг старой версии — ключа ещё нет; дописываем перед закрывающей скобкой
        $idx = $text.LastIndexOf('}')
        if ($idx -lt 0) { return $false }
        $head = $text.Substring(0, $idx).TrimEnd()
        $head = $head.TrimEnd(',')
        $text = $head + ",`r`n  `"serviceMode`": " + $word + "`r`n}`r`n"
    }
    # без BOM: config.json пишет и приложение (System.Text.Json), и оно пишет его без BOM
    [System.IO.File]::WriteAllText($configPath, $text, (New-Object System.Text.UTF8Encoding($false)))
    return $true
}

# --- снятие службы ------------------------------------------------------------------
if ($Remove) {
    if ($null -eq $existing) {
        Write-Host (L 'scr.svc.10' $serviceName) -ForegroundColor Yellow
    }
    else {
        $svcExe = Exe-Of $existing.PathName
        Write-Host (L 'scr.svc.11' $existing.PathName)
        if ($svcExe -and -not $svcExe.StartsWith($targetDir, [System.StringComparison]::OrdinalIgnoreCase) -and -not $Force) {
            Write-Host (L 'scr.svc.12' $svcExe) -ForegroundColor Red
            Write-Host (L 'scr.svc.13') -ForegroundColor Red
            exit 1
        }
        if ($WhatIf) {
            Write-Host (L 'scr.svc.14' $serviceName) -ForegroundColor Yellow
            Write-Host (L 'scr.svc.15') -ForegroundColor Yellow
            exit 0
        }
        Require-Admin
        if ($existing.State -eq "Running") {
            Write-Host (L 'scr.svc.16') -ForegroundColor Cyan
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            (Get-Service -Name $serviceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 60))
        }
        [void](Invoke-CimMethod -InputObject $existing -MethodName Delete)
        Write-Host (L 'scr.svc.17' $serviceName) -ForegroundColor Green
    }
    if ($WhatIf) {
        Write-Host (L 'scr.svc.15') -ForegroundColor Yellow
        exit 0
    }
    if (Set-ServiceFlag $false) {
        Write-Host (L 'scr.svc.18') -ForegroundColor Cyan
    }
    Write-Host (L 'scr.svc.19')
    exit 0
}

# --- ключи организаций под DPAPI ----------------------------------------------------
# Ключ организации записан областью ТЕКУЩЕГО ПОЛЬЗОВАТЕЛЯ, и служба от LocalSystem
# его не расшифрует. Молча заводить такую службу нельзя: ключи API моделей станут
# нечитаемыми, а выглядеть это будет как «модель есть, а задание не идёт»
$dpapiValues = @()
if (Test-Path $secretsPath) {
    $secretsText = [System.IO.File]::ReadAllText($secretsPath)
    $dpapiValues = @([regex]::Matches($secretsText, '"dpapi:([A-Za-z0-9+/=]+)"') |
        ForEach-Object { $_.Groups[1].Value })
}

if ($UnprotectSecrets -and $dpapiValues.Count -gt 0 -and -not $WhatIf) {
    Add-Type -AssemblyName System.Security
    $done = 0
    foreach ($blob in $dpapiValues) {
        try {
            $bytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
                [Convert]::FromBase64String($blob), $null,
                [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        }
        catch {
            Write-Host (L 'scr.svc.20') -ForegroundColor Red
            Write-Host (L 'scr.svc.21') -ForegroundColor Red
            exit 1
        }
        $secretsText = $secretsText.Replace('"dpapi:' + $blob + '"', '"' + [Convert]::ToBase64String($bytes) + '"')
        $done++
    }
    Copy-Item -LiteralPath $secretsPath -Destination ($secretsPath + ".dpapi.bak") -Force
    [System.IO.File]::WriteAllText($secretsPath, $secretsText, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host (L 'scr.svc.22' $done) -ForegroundColor Green
    Write-Host (L 'scr.svc.23') -ForegroundColor Yellow
    $dpapiValues = @()
}

if ($dpapiValues.Count -gt 0 -and [string]::IsNullOrWhiteSpace($Account) -and -not $Force -and -not $WhatIf) {
    Write-Host ""
    Write-Host (L 'scr.svc.24' $dpapiValues.Count) -ForegroundColor Yellow
    Write-Host (L 'scr.svc.25') -ForegroundColor Yellow
    Write-Host (L 'scr.svc.26') -ForegroundColor Yellow
    Write-Host ""
    Write-Host (L 'scr.svc.27') -ForegroundColor Yellow
    Write-Host ("      .\makeAsServise.ps1 -Account " + $identity.Name + " -Password <password>") -ForegroundColor Yellow
    Write-Host (L 'scr.svc.28')
    Write-Host "      .\makeAsServise.ps1 -UnprotectSecrets" -ForegroundColor Yellow
    Write-Host (L 'scr.svc.29')
    Write-Host "      .\makeAsServise.ps1 -Force" -ForegroundColor Yellow
    Write-Host (L 'scr.svc.30')
    exit 1
}

# --- учётная запись службы ----------------------------------------------------------
$credential = $null
if (-not [string]::IsNullOrWhiteSpace($Account)) {
    if ([string]::IsNullOrWhiteSpace($Password)) {
        Write-Host (L 'scr.svc.31' $Account) -ForegroundColor Cyan
        $secure = Read-Host -AsSecureString
    }
    else {
        $secure = ConvertTo-SecureString $Password -AsPlainText -Force
    }
    $credential = New-Object System.Management.Automation.PSCredential($Account, $secure)
}

$startupType = if ($Manual) { "Manual" } else { "Automatic" }
# путь в кавычках: без них Windows разберёт «C:\Program Files\...» по пробелу
$binaryPath = '"' + $exe + '"'
# РАБОЧИЙ КАТАЛОГ ПРИБИВАЕТСЯ ГВОЗДЯМИ, если он не рядом с программой (T-287). Служба
# работает от другой учётной записи (по умолчанию LocalSystem), а запасной путь выбора —
# каталог данных ПОЛЬЗОВАТЕЛЯ: без явного ключа служба искала бы свои данные в профиле
# системной учётки и завела бы там пустую базу вместо рабочей
if ($relocated) { $binaryPath += ' --config "' + $configPath + '"' }

# --- показать план и ничего не делать (-WhatIf) --------------------------------------
# Прав администратора для этого не нужно: смотреть, что будет, человек вправе и так
if ($WhatIf) {
    Write-Host ""
    Write-Host (L 'scr.svc.32') -ForegroundColor Yellow
    if ($null -eq $existing) {
        Write-Host (L 'scr.svc.33' $serviceName $startupType $binaryPath)
    }
    else {
        Write-Host (L 'scr.svc.34' $serviceName $existing.PathName)
    }
    Write-Host (L 'scr.svc.35' $(if ($credential) { $credential.UserName } else { (L 'scr.svc.36') }))
    Write-Host (L 'scr.svc.37')
    if ($dpapiValues.Count -gt 0) {
        Write-Host (L 'scr.svc.38' $dpapiValues.Count) -ForegroundColor Yellow
    }
    if (-not $isAdmin) {
        Write-Host (L 'scr.svc.39') -ForegroundColor Yellow
    }
    exit 0
}

Require-Admin

if ($null -ne $existing) {
    $svcExe = Exe-Of $existing.PathName
    if ($svcExe -and -not $svcExe.StartsWith($targetDir, [System.StringComparison]::OrdinalIgnoreCase) -and -not $Force) {
        Write-Host (L 'scr.svc.40' $serviceName) -ForegroundColor Red
        Write-Host "        $svcExe" -ForegroundColor Red
        Write-Host (L 'scr.svc.41') -ForegroundColor Red
        exit 1
    }
    Write-Host (L 'scr.svc.42') -ForegroundColor Cyan
    if ($existing.State -eq "Running") {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 60))
    }
    # Change у Win32_Service меняет путь БЕЗ участия оболочки — кавычки в значении
    # доезжают как есть (у sc.exe их пришлось бы экранировать под правила cmd)
    $change = @{ PathName = $binaryPath; DisplayName = $displayName;
                 StartMode = $(if ($Manual) { "Manual" } else { "Auto" }) }
    if ($null -ne $credential) {
        $change["StartName"] = $credential.UserName
        $change["StartPassword"] = $credential.GetNetworkCredential().Password
    }
    $result = Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments $change
    if ($result.ReturnValue -ne 0) {
        Write-Host (L 'scr.svc.43' $result.ReturnValue) -ForegroundColor Red
        exit 1
    }
    Set-Service -Name $serviceName -Description $serviceDescription
}
else {
    Write-Host (L 'scr.svc.44') -ForegroundColor Cyan
    if ($null -ne $credential) {
        New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName $displayName `
            -Description $serviceDescription -StartupType $startupType -Credential $credential | Out-Null
    }
    else {
        New-Service -Name $serviceName -BinaryPathName $binaryPath -DisplayName $displayName `
            -Description $serviceDescription -StartupType $startupType | Out-Null
    }
    Write-Host (L 'scr.svc.45' $serviceName) -ForegroundColor Green
}

# перезапуск после сбоя: сервер держит очередь заданий и репликацию, и молча лежать
# после падения он не должен. Ключи sc.exe без пробелов — экранировать нечего
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

if (Set-ServiceFlag $true) {
    Write-Host (L 'scr.svc.46') -ForegroundColor Cyan
}

# --- запуск -------------------------------------------------------------------------
if ($NoStart) {
    Write-Host (L 'scr.svc.47' $serviceName) -ForegroundColor Yellow
}
else {
    Write-Host (L 'scr.svc.48') -ForegroundColor Cyan
    try {
        Start-Service -Name $serviceName
        (Get-Service -Name $serviceName).WaitForStatus('Running', (New-TimeSpan -Seconds 90))
        Write-Host (L 'scr.svc.49') -ForegroundColor Green
    }
    catch {
        Write-Host (L 'scr.svc.50' $_.Exception.Message) -ForegroundColor Red
        Write-Host (L 'scr.svc.51') -ForegroundColor Yellow
        Write-Host (L 'scr.svc.52') -ForegroundColor Yellow
        Write-Host (L 'scr.svc.53' "$homeDir\logs\ai2p-*.jsonl") -ForegroundColor Yellow
        exit 1
    }
}

# --- куда заходить ------------------------------------------------------------------
$url = ""
if (Test-Path $configPath) {
    try {
        $cfg = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        # адрес для человека (T-315): по HTTPS сертификат выписан на ИМЯ сервера, и «localhost»
        # в нём обычно не назван — браузер отверг бы такую ссылку по несовпадению имени
        $uiHost = 'localhost'
        if ("$($cfg.ui.protocol)" -eq 'https' -and "$($cfg.ui.hostname)".Trim()) {
            $uiHost = "$($cfg.ui.hostname)".Trim()
        }
        $url = "$($cfg.ui.protocol)://${uiHost}:$($cfg.ui.port)$($cfg.ui.basePath)"
    }
    catch { }
}

Write-Host ""
Write-Host (L 'scr.svc.54') -ForegroundColor Green
Write-Host (L 'scr.svc.55' $serviceName $startupType)
Write-Host (L 'scr.svc.56' $exe)
if ($url) { Write-Host (L 'scr.svc.57' $url) }
Write-Host ""
Write-Host (L 'scr.svc.58' $serviceName)
Write-Host (L 'scr.svc.59')
Write-Host (L 'scr.svc.60')
Write-Host (L 'scr.svc.61')
exit 0
