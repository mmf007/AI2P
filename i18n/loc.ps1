# ============================================================
#  AI2P - i18n/loc.ps1  (T-65-S0)
#  Message catalogue of the build and install scripts for PowerShell.
#  Dot-source it and call L:
#       . "$PSScriptRoot\i18n\loc.ps1"
#       Set-Ai2pLang $Lang           # $Lang may be empty - see the ladder below
#       Write-Host (L 'scr.inst.20' $targetDir)
#
#  THE CATALOGUE IS OUTSIDE THE SCRIPTS ON PURPOSE: a script is never duplicated
#  per language, only the texts are. A new language is a copy of scripts.en.txt
#  named scripts.<lang>.txt with the values translated - no code is touched.
#
#  LANGUAGE LADDER, the first non-empty wins:
#      1) the -Lang switch of the script;
#      2) the AI2P_LANG environment variable;
#      3) en - THE DEFAULT AND THE BASE LANGUAGE (T-65-S0).
#  The "language" of config.json is DELIBERATELY NOT asked: the language of the
#  application interface and the language of an installation console are different
#  things, and the scripts answer in English on any machine until they are told
#  otherwise. Whoever wants another one says so: -Lang or AI2P_LANG.
#  A key missing from the chosen catalogue is taken from scripts.en.txt, and a key
#  missing from both is printed as the key itself: a script never dies over a text.
#
#  THIS FILE IS ASCII ONLY. Windows PowerShell 5.1 reads a BOM-less file in ANSI,
#  and the catalogues themselves are read with an explicit -Encoding UTF8 below.
# ============================================================

$script:Ai2pLocRoot = $PSScriptRoot
$script:Ai2pLocLang = ''
$script:Ai2pLocMap  = @{}
$script:Ai2pLocBase = @{}

function Read-Ai2pLocFile([string]$path) {
    $map = @{}
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) { return $map }
    foreach ($line in (Get-Content -LiteralPath $path -Encoding UTF8)) {
        if ($line -match '^\s*(#|$)') { continue }
        $i = $line.IndexOf('=')
        if ($i -lt 1) { continue }
        $map[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1)
    }
    return $map
}

function Set-Ai2pLang([string]$lang) {
    if ([string]::IsNullOrWhiteSpace($lang)) { $lang = $env:AI2P_LANG }
    if ([string]::IsNullOrWhiteSpace($lang)) { $lang = 'en' }
    $lang = $lang.Trim().ToLowerInvariant()
    $script:Ai2pLocLang = $lang
    $script:Ai2pLocBase = Read-Ai2pLocFile (Join-Path $script:Ai2pLocRoot 'scripts.en.txt')
    if ($lang -eq 'en') { $script:Ai2pLocMap = $script:Ai2pLocBase }
    else { $script:Ai2pLocMap = Read-Ai2pLocFile (Join-Path $script:Ai2pLocRoot ('scripts.' + $lang + '.txt')) }
}

function Get-Ai2pLang { return $script:Ai2pLocLang }

# the text of a key in an ARBITRARY language: MakePackage.ps1 builds the
# [CustomMessages] section of the Inno Setup script out of it
function Get-Ai2pTextIn([string]$lang, [string]$key) {
    $map = Read-Ai2pLocFile (Join-Path $script:Ai2pLocRoot ('scripts.' + $lang + '.txt'))
    if ($map.ContainsKey($key)) { return $map[$key] }
    if ($script:Ai2pLocBase.ContainsKey($key)) { return $script:Ai2pLocBase[$key] }
    return $key
}

function L {
    param([string]$Key)
    $text = $null
    if ($script:Ai2pLocMap.ContainsKey($Key)) { $text = $script:Ai2pLocMap[$Key] }
    elseif ($script:Ai2pLocBase.ContainsKey($Key)) { $text = $script:Ai2pLocBase[$Key] }
    else { return $Key }
    for ($i = 0; $i -lt $args.Count; $i++) {
        $text = $text.Replace('{' + $i + '}', [string]$args[$i])
    }
    return $text
}

# --help of a script: a WHOLE FILE i18n/help/<script>.<lang>.txt, not per-line keys.
# Help is edited in paragraphs, and splitting it into keys guarantees drift.
function Show-Ai2pHelp([string]$name) {
    $file = Join-Path $script:Ai2pLocRoot ('help\' + $name + '.' + $script:Ai2pLocLang + '.txt')
    if (-not (Test-Path -LiteralPath $file)) {
        $file = Join-Path $script:Ai2pLocRoot ('help\' + $name + '.en.txt')
    }
    if (Test-Path -LiteralPath $file) {
        foreach ($line in (Get-Content -LiteralPath $file -Encoding UTF8)) { Write-Host $line }
    }
    else {
        Write-Host ("No help file for " + $name)
    }
}

Set-Ai2pLang ''
