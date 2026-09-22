# ============================================================
#  AI2P - MakePackage.ps1  (Windows, T-285)
#  An installation package out of a ready release. The full description is in the
#  help: MakePackage.cmd --help  /  MakePackage.ps1 -Help.
#
#  THE TEXTS LIVE OUTSIDE THE SCRIPT (T-65-S0): the messages in
#  i18n/scripts.<lang>.txt, the help in i18n/help/MakePackage.<lang>.txt. The texts
#  of the [Code] section of the installer are taken from the SAME catalogue and go
#  into [CustomMessages], so a new language of the installer is a new catalogue
#  plus one line in [Languages].
#  The default language is en; -Lang xx and AI2P_LANG override it, in that order.
# ============================================================
param(
    [string]$Source = "",
    [string]$Output = "",
    [switch]$KeepStage,
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
    function Get-Ai2pTextIn([string]$lang, [string]$key) { return $key }
    function Show-Ai2pHelp([string]$name) { Write-Host "No i18n folder next to MakePackage.ps1" }
}
Set-Ai2pLang $Lang

if ($Help) { Show-Ai2pHelp "MakePackage"; exit 0 }

# ---------- 1. каталог выкладки ----------
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = $PSScriptRoot }
if (-not [System.IO.Path]::IsPathRooted($Source)) { $Source = Join-Path (Get-Location).Path $Source }
$srcDir = [System.IO.Path]::GetFullPath($Source)

Write-Host "=== AI2P MakePackage (Windows) ===" -ForegroundColor Cyan
Write-Host (L 'scr.pkg.1' $srcDir)

$versionFile = Join-Path $srcDir "version.json"
if (-not (Test-Path $versionFile)) {
    Write-Host (L 'scr.pkg.2') -ForegroundColor Red
    Write-Host (L 'scr.pkg.3') -ForegroundColor Yellow
    Write-Host (L 'scr.pkg.4' '-Source') -ForegroundColor Yellow
    exit 1
}
$info = Get-Content $versionFile -Raw | ConvertFrom-Json
$version = "$($info.version)".Trim()
$build = 0
if ($info.PSObject.Properties.Name -contains "build") { $build = [int]$info.build }
$selfContained = $false
if ($info.PSObject.Properties.Name -contains "selfContained") { $selfContained = [bool]$info.selfContained }
$runtime = ""
if ($info.PSObject.Properties.Name -contains "runtime") { $runtime = "$($info.runtime)".Trim() }
if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host (L 'scr.pkg.5') -ForegroundColor Red
    exit 1
}

# ---------- 2. под какую систему и архитектуру собрана выкладка ----------
# у полной выкладки и система, и архитектура записаны рантаймом (win-x64, linux-arm64,
# osx-arm64), у обычной рантайма нет вовсе — тогда систему выдаёт каталог ОС
# (builds\<ОС>\release), а архитектуру берём у ТЕКУЩЕГО КОМПИЛЯТОРА: выкладка без RID
# собирается под ту машину, на которой её собирали
function Get-OsName([string]$rid) {
    switch -Wildcard ($rid) {
        "win-*"   { return "windows" }
        "osx-*"   { return "macos" }
        "linux-*" { return "linux" }
    }
    return ""
}
# архитектура RID — это его последняя часть: win-x64 -> x64, osx-arm64 -> arm64
function Get-ArchTag([string]$rid) {
    if ($rid -match '-([A-Za-z0-9]+)$') { return $Matches[1].ToLowerInvariant() }
    return ""
}
$osName = Get-OsName $runtime
$archTag = Get-ArchTag $runtime
if (-not $osName) {
    # каталог ОС: <...>\builds\windows\release -> windows
    $osDir = Split-Path (Split-Path $srcDir -Parent) -Leaf
    switch ($osDir.ToLowerInvariant()) {
        "windows" { $osName = "windows" }
        "linux"   { $osName = "linux" }
        "macos"   { $osName = "macos" }
        default   { $osName = "windows" }   # выкладка в своём каталоге: собираем под эту машину
    }
}
if (-not $archTag) {
    # x64, arm64, x86, arm — как их называет сам .NET
    $archTag = ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture).ToString().ToLowerInvariant()
}
if ($osName -ne "windows") {
    Write-Host (L 'scr.pkg.6' $osName) -ForegroundColor Red
    Write-Host (L 'scr.pkg.7') -ForegroundColor Yellow
    exit 1
}

# ---------- 3. имя пакета и каталог результата ----------
# AI2P_v_1_133_full_windows_x64.exe — полная выкладка; AI2P_v_1_133_windows_x64.exe — обычная.
# «1_133» — это версия с точкой, заменённой на подчёркивание: вторая часть и есть билд NN;
# «full» стоит ПОСЛЕ номера версии, дальше система и архитектура (T-234-S0)
$fullTag = if ($selfContained) { "_full" } else { "" }
$baseName = "AI2P_v_" + ($version -replace '\.', '_') + $fullTag + "_" + $osName + "_" + $archTag

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $srcDir "..\..\packages" }
if (-not [System.IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $srcDir $Output }
$outDir = [System.IO.Path]::GetFullPath($Output)
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$kind = if ($selfContained) { (L 'scr.pkg.8' $runtime) } else { (L 'scr.pkg.9') }
Write-Host (L 'scr.pkg.10' $version $build $kind)
Write-Host (L 'scr.pkg.11' "$baseName.exe")
Write-Host (L 'scr.pkg.12' $outDir)

# ---------- 4. Inno Setup ----------
$iscc = $null
$cmdIscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($cmdIscc) { $iscc = $cmdIscc.Source }
if (-not $iscc) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { $iscc = $c; break }
    }
}
if (-not $iscc) {
    Write-Host (L 'scr.pkg.13') -ForegroundColor Red
    Write-Host (L 'scr.pkg.14') -ForegroundColor Yellow
    Write-Host (L 'scr.pkg.15') -ForegroundColor Yellow
    exit 1
}
Write-Host (L 'scr.pkg.16' $iscc)

# ключ x64compatible появился в Inno Setup 6.3; на более старых версиях он не понимается,
# и там ставится прежний x64 — иначе сборка падает на ровном месте
$isccMajor = 6
$isccMinor = 0
try {
    $banner = & $iscc 2>$null | Select-Object -First 3
    $m = [regex]::Match(($banner -join " "), '(\d+)\.(\d+)')
    if ($m.Success) { $isccMajor = [int]$m.Groups[1].Value; $isccMinor = [int]$m.Groups[2].Value }
} catch { }
$arch64 = if ($isccMajor -gt 6 -or ($isccMajor -eq 6 -and $isccMinor -ge 3)) { "x64compatible" } else { "x64" }

# ---------- 5. временный каталог с содержимым пакета ----------
# данные пользователя в дистрибутив не попадают (тот же список, что у install.ps1),
# config.json уносится отдельно: у него в установщике особое правило
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ai2p_pkg_" + $baseName)
if (Test-Path $stageRoot) { Remove-Item -Path $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
$stage = Join-Path $stageRoot "payload"
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$excludeDirs = @("data", "logs", "secrets") | ForEach-Object { Join-Path $srcDir $_ }
$excludeFiles = @("config.json", "secrets.json", "installed.json",
                  "MakePackage.ps1", "MakePackage.cmd", "MakePackage.sh")
$robocopyArgs = @($srcDir, $stage, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NP", "/R:1", "/W:1",
                  "/XD") + $excludeDirs + @("/XF") + $excludeFiles
Write-Host (L 'scr.pkg.17') -ForegroundColor Cyan
& robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Host (L 'scr.pkg.18' $LASTEXITCODE) -ForegroundColor Red
    exit 1
}
$payloadCount = @(Get-ChildItem -Path $stage -Recurse -File).Count
if ($payloadCount -eq 0) {
    Write-Host (L 'scr.pkg.19') -ForegroundColor Red
    exit 1
}
$exeInPackage = Join-Path $stage "AI2P.Server.exe"
if (-not (Test-Path $exeInPackage)) {
    Write-Host (L 'scr.pkg.20') -ForegroundColor Red
    exit 1
}

$configSrc = Join-Path $srcDir "config.json"
$configStaged = Join-Path $stageRoot "config.src.json"
if (Test-Path $configSrc) { Copy-Item -Path $configSrc -Destination $configStaged -Force }
Write-Host (L 'scr.pkg.21' $payloadCount)

# ---------- 6. сценарий Inno Setup ----------
# {{ в AppId — это экранированная фигурная скобка Inno: значение получается {GUID}.
# GUID постоянный: по нему установщик узнаёт СВОЮ прошлую установку и обновляет её,
# а не заводит вторую запись в «Установке и удалении программ»
$iss = @'
; AI2P — сценарий Inno Setup, собран MakePackage.ps1 (T-285). Файл временный.
[Setup]
AppId={{A7C21E64-6B3F-4E7B-9E2C-4F5A2D1B8C90}
AppName=AI2P
AppVersion=@VERSION@
AppVerName=AI2P @VERSION@
AppPublisher=AI2P
DefaultDirName={autopf}\AI2P
DefaultGroupName=AI2P
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=@ARCH64@
OutputDir=@OUTDIR@
OutputBaseFilename=@BASENAME@
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=AI2P @VERSION@
UninstallDisplayIcon={app}\AI2P.Server.exe
@SETUPICON@

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; тексты AI2P для раздела [Code] — из i18n/scripts.<язык>.txt (T-65-S0)
[CustomMessages]
@CUSTOMMESSAGES@

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; каталоги, целиком принадлежащие дистрибутиву, переписываются НАЧИСТО — тот же список,
; что у install.ps1: иначе после обновления через версию в них копится мусор
[InstallDelete]
Type: filesandordirs; Name: "{app}\doc"
Type: filesandordirs; Name: "{app}\wwwroot"
Type: filesandordirs; Name: "{app}\runtimes"

; РАБОЧИЙ КАТАЛОГ ДАННЫХ (T-287). Установка «для всех пользователей» кладёт программу
; в {app} = C:\Program Files\AI2P, а туда обычный пользователь писать не может — поэтому
; config.json, data\, logs\ и secrets\ уходят в C:\ProgramData\AI2P (см. AppHome).
; Каталог заводится здесь и открывается на запись всем пользователям компьютера: установка
; одна, данные общие, и служба (она работает от системы) обязана видеть те же данные, что
; и человек. При установке «только для меня» программа лежит в каталоге пользователя, туда
; писать можно, и общий каталог не нужен вовсе. Удаление его НЕ трогает: это данные
[Dirs]
Name: "{commonappdata}\AI2P"; Permissions: users-modify; Check: IsAdminInstallMode

[Files]
Source: "@STAGE@\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
@CONFIGLINES@

[Icons]
Name: "{group}\AI2P"; Filename: "{app}\AI2P.Server.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,AI2P}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\AI2P"; Filename: "{app}\AI2P.Server.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\AI2P.Server.exe"; Description: "{cm:LaunchProgram,AI2P}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

; данные пользователя (data\, secrets\, secrets.json, config.json) удаление НЕ трогает —
; их сюда не вписываем намеренно
[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: files; Name: "{app}\config.new.json.applied"
@CODE@
'@

# ЗНАЧОК САМОГО УСТАНОВЩИКА (T-6-S0). У программы значок встроен в AI2P.Server.exe
# (ApplicationIcon в AI2P.Server.csproj), и от него же его получают ярлык рабочего стола
# и строка в «Установке и удалении программ» — а вот файлу установщика значок нужен
# отдельным ключом. Берём его из САМОЙ ВЫКЛАДКИ: MakePackage работает из каталога выкладки,
# исходников рядом может не быть. Выкладка старее 1.101 значка не содержит — тогда строки
# просто нет, и пакет собирается как раньше
$iconStaged = Join-Path $stage "ai2p.ico"
$setupIcon = ""
if (Test-Path $iconStaged) {
    $setupIcon = "SetupIconFile=$iconStaged"
    Write-Host (L 'scr.pkg.22')
} else {
    Write-Host (L 'scr.pkg.23') -ForegroundColor Yellow
}

$configLines = ""
if (Test-Path $configStaged) {
    # config.json кладётся, только если его ещё нет (правило install.ps1: рабочий не трогаем),
    # а config.new.json — всегда: приложение сольёт его при первом старте (ConfigMerge).
    #
    # У установки «ДЛЯ ВСЕХ» рабочего config.json рядом с программой не бывает вовсе (T-287):
    # {app} = C:\Program Files\AI2P, рабочие файлы уходят в C:\ProgramData\AI2P, и положенный
    # сюда config.json был бы МЁРТВЫМ — приложение его не читает, правки в нём ничего не
    # меняют, а скрипты установки и службы (T-271) каталог программ и вовсе пропускают при
    # поиске рабочего конфига. Первую конфигурацию каталогу данных даёт config.new.json.
    $configLines = "Source: `"$configStaged`"; DestDir: `"{app}`"; DestName: `"config.json`"; Flags: onlyifdoesntexist; Check: not IsAdminInstallMode" + [Environment]::NewLine +
                   "Source: `"$configStaged`"; DestDir: `"{app}`"; DestName: `"config.new.json`"; Flags: ignoreversion"
}

# РАЗДЕЛ [Code] СОБИРАЕТСЯ ИЗ ДВУХ ЧАСТЕЙ, и он всегда один: несколько [Code] в сценарии
# Inno Setup не складывает, а перечитывает — вторая функция InitializeSetup молча заменила
# бы первую. Первая часть (служба ОС, T-271) нужна всегда, вторая (проверка рантайма,
# T-211) — только обычной выкладке
$code = @'

[Code]
// СЛУЖБА ОС (T-271). Установку можно сделать сервисом (makeAsServise.cmd, имя службы AI2P).
// Тогда её файлы держит работающая служба, и обновление поверх сорвалось бы на копировании.
// Спрашиваем СИСТЕМУ, а не пометку в конфиге: настоящее состояние знает только она.
var
  Ai2pServiceWasRunning: Boolean;

function Ai2pServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  // sc query у несуществующей службы возвращает 1060, у существующей — 0
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query AI2P', '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Служба ведёт ИМЕННО В ЭТУ установку. Ищем путь установки в выводе «sc qc» по всему
// тексту, а не по имени поля: названия полей у sc переведены, и «BINARY_PATH_NAME»
// на русской Windows не встретится вовсе
function Ai2pServiceIsOurs(): Boolean;
var
  ResultCode, I: Integer;
  TmpFile, AppPath: String;
  Lines: TArrayOfString;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\ai2p_service.txt');
  AppPath := Lowercase(ExpandConstant('{app}'));
  if Exec(ExpandConstant('{cmd}'), '/c sc qc AI2P > "' + TmpFile + '" 2>&1', '',
          SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    if LoadStringsFromFile(TmpFile, Lines) then
      for I := 0 to GetArrayLength(Lines) - 1 do
        if Pos(AppPath, Lowercase(Lines[I])) > 0 then
          Result := True;
end;

// net stop, а не sc stop: net ЖДЁТ остановки и отвечает кодом, а разбирать состояние
// из вывода sc нельзя — оно тоже переведено. Служба стояла — код ненулевой, и это
// ровно то, что нам нужно знать: запускать обратно нечего
function Ai2pServiceRun(Command: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c ' + Command, '', SW_HIDE,
                 ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  Ai2pServiceWasRunning := False;
  if Ai2pServiceExists() and Ai2pServiceIsOurs() then
  begin
    if not IsAdminInstallMode() then
    begin
      Result := CustomMessage('Ai2pServiceRunning');
      exit;
    end;
    Ai2pServiceWasRunning := Ai2pServiceRun('net stop AI2P');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // служба возвращается в работу сама: человек её не останавливал и не должен запускать
  if (CurStep = ssPostInstall) and Ai2pServiceWasRunning then
    Ai2pServiceRun('net start AI2P');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  // удаление программы обязано снять и её службу, иначе в системе остаётся запись,
  // ведущая в пустоту. Чужую службу (другая установка) не трогаем
  if (CurUninstallStep = usUninstall) and Ai2pServiceExists() and Ai2pServiceIsOurs() then
  begin
    Ai2pServiceRun('net stop AI2P');
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete AI2P', '', SW_HIDE,
         ewWaitUntilTerminated, ResultCode);
  end;
end;
'@

# ПРОВЕРКА РАНТАЙМА нужна только обычной выкладке (T-211): полной он не нужен вовсе,
# он лежит внутри неё
if (-not $selfContained) {
    $code = $code + @'

// Обычная выкладка собрана БЕЗ рантайма: без ASP.NET Core 8.x программа не запустится.
// Установку не запрещаем (рантайм можно поставить потом), но предупреждаем честно.
function AspNetCore8Present(): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\ai2p_runtimes.txt');
  if Exec(ExpandConstant('{cmd}'), '/c dotnet --list-runtimes > "' + TmpFile + '" 2>&1', '',
          SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(TmpFile, Lines) then
      for I := 0 to GetArrayLength(Lines) - 1 do
        if Pos('Microsoft.AspNetCore.App 8.', Lines[I]) = 1 then
          Result := True;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not AspNetCore8Present() then
  begin
    // SuppressibleMsgBox, а не MsgBox: при тихой установке (/VERYSILENT /SUPPRESSMSGBOXES)
    // обычный MsgBox некому нажать, и установка ЗАВИСАЕТ. Здесь ответ по умолчанию — «да»:
    // рантайм можно поставить и после, а вот повесить тихую установку нельзя
    if SuppressibleMsgBox(CustomMessage('Ai2pNoRuntime'),
                          mbConfirmation, MB_YESNO, IDYES) = IDNO then
      Result := False;
  end;
end;
'@
}

# РАЗДЕЛ [CustomMessages] СОБИРАЕТСЯ ИЗ ТОГО ЖЕ КАТАЛОГА, что и тексты скриптов
# (T-65-S0): язык установщика выбирает человек в его окне, поэтому строки кладутся
# СРАЗУ ВСЕ, по одной на язык Inno Setup. Новый язык установщика — это новый
# scripts.<язык>.txt плюс строка в [Languages] выше
$issLanguages = @{ 'russian' = 'ru'; 'english' = 'en' }
$customMessages = ""
foreach ($innoName in @('russian', 'english')) {
    $langCode = $issLanguages[$innoName]
    $customMessages += $innoName + ".Ai2pServiceRunning=" + (Get-Ai2pTextIn $langCode 'scr.iss.1') + [Environment]::NewLine
    $customMessages += $innoName + ".Ai2pNoRuntime=" + (Get-Ai2pTextIn $langCode 'scr.iss.2') + [Environment]::NewLine
}

$iss = $iss.Replace("@CUSTOMMESSAGES@", $customMessages)
$iss = $iss.Replace("@VERSION@", $version)
$iss = $iss.Replace("@ARCH64@", $arch64)
$iss = $iss.Replace("@OUTDIR@", $outDir)
$iss = $iss.Replace("@BASENAME@", $baseName)
$iss = $iss.Replace("@STAGE@", $stage)
$iss = $iss.Replace("@CONFIGLINES@", $configLines)
$iss = $iss.Replace("@SETUPICON@", $setupIcon)
$iss = $iss.Replace("@CODE@", $code)

$issPath = Join-Path $stageRoot "ai2p.iss"
# Inno Setup читает сценарий в кодировке ANSI, если в нём нет BOM: с BOM он честно
# понимает UTF-8, и русские строки в [Code] доезжают до окна установки целыми
Set-Content -Path $issPath -Value $iss -Encoding UTF8

# ---------- 7. сборка ----------
Write-Host (L 'scr.pkg.24') -ForegroundColor Cyan
$log = Join-Path $stageRoot "iscc.log"
# ErrorActionPreference на время вызова снимаем: Windows PowerShell 5.1 заворачивает КАЖДУЮ
# строку stderr чужой программы в ошибку, и при Stop скрипт падает вместо своего сообщения
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& $iscc "/Q" $issPath 2>&1 | Tee-Object -FilePath $log
$isccExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap
if ($isccExit -ne 0) {
    Write-Host (L 'scr.pkg.25' $isccExit $log) -ForegroundColor Red
    Write-Host (L 'scr.pkg.26' $issPath) -ForegroundColor Yellow
    exit 1
}

$package = Join-Path $outDir ($baseName + ".exe")
if (-not (Test-Path $package)) {
    Write-Host (L 'scr.pkg.27' $package) -ForegroundColor Red
    exit 1
}
$sizeMb = [math]::Round((Get-Item $package).Length / 1MB, 1)

if (-not $KeepStage) { Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue }
else { Write-Host (L 'scr.pkg.28' $stageRoot) -ForegroundColor Yellow }

Write-Host ""
Write-Host "=== MakePackage OK ===" -ForegroundColor Green
Write-Host (L 'scr.pkg.29' $package $sizeMb)
Write-Host (L 'scr.pkg.30')
Write-Host (L 'scr.pkg.31')
Write-Host (L 'scr.pkg.32')
Write-Host (L 'scr.pkg.33')
exit 0
