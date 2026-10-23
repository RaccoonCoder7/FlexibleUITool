<#
.SYNOPSIS
    FlexibleUITool CLI. Sends one command to Unity and prints the JSON result.

.DESCRIPTION
    The Unity editor must be open on this project; it handles the command through Library/FlexibleUITool/inbox.
    Exit code 0 = ok, 1 = error. See Library/FlexibleUITool/AGENTS.md for commands and the spec format.
    The editor copies this script to Library/FlexibleUITool/flexui.ps1 when the project opens, so it works the same
    whether the tool is in Assets or installed as a package.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Library/FlexibleUITool/flexui.ps1 describe -Prefab FlexibleSample -Profile landscape
#>
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('ping', 'profiles', 'list', 'describe', 'render', 'check', 'export-spec', 'apply-spec', 'set-role', 'duplicate', 'clear-profile', 'scene-place', 'scene-clear', 'game-view', 'review', 'cleanup-tutorial', 'import-samples')]
    [string]$Command,
    [string]$Prefab,
    [string]$Profile,
    [string[]]$Preset,
    [string[]]$Aspect,
    [string]$Spec,
    [string]$Out,
    [string]$Folder,
    [string]$Path,
    [string]$Scene,
    [string]$Role,
    [switch]$DryRun,
    [switch]$All,
    [string]$Project,
    [int]$Timeout = 300,
    [int]$PlayModeWait = 15
)

$ErrorActionPreference = 'Stop'
$Bound = $PSBoundParameters
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Utf8 = New-Object System.Text.UTF8Encoding($false)

# Unity project root: -Project, else the nearest folder above this script (or the current folder) that has Assets and ProjectSettings
function Find-ProjectRoot([string]$Start) {
    $dir = $Start
    while ($dir) {
        if ((Test-Path (Join-Path $dir 'Assets')) -and (Test-Path (Join-Path $dir 'ProjectSettings'))) { return $dir }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
}
if ($Project) { $ProjectRoot = (Resolve-Path $Project).Path }
else {
    $ProjectRoot = Find-ProjectRoot $PSScriptRoot
    if (-not $ProjectRoot) { $ProjectRoot = Find-ProjectRoot (Get-Location).Path }
}
$WorkDir = if ($ProjectRoot) { Join-Path $ProjectRoot 'Library\FlexibleUITool' } else { '.' }
$Inbox = Join-Path $WorkDir 'inbox'
$Outbox = Join-Path $WorkDir 'outbox'
$Id = [guid]::NewGuid().ToString('N')

function Fail([string]$Code, [string]$Message) {
    $result = [ordered]@{ id = $Id; command = $Command; ok = $false; error = [ordered]@{ code = $Code; message = $Message } }
    Write-Output ($result | ConvertTo-Json -Depth 5 -Compress)
    exit 1
}

function Note([string]$Message) {
    [Console]::Error.WriteLine("flexui: $Message")
}

# -Spec/-Out are file paths (made absolute from the current folder); -Prefab/-Folder are asset paths and stay as-is
function Resolve-FilePath([string]$Value) {
    if ([string]::IsNullOrEmpty($Value) -or [IO.Path]::IsPathRooted($Value)) { return $Value }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Value))
}

function New-CommandJson {
    $cmd = [ordered]@{ id = $Id; command = $Command }
    if ($Prefab) { $cmd.prefab = $Prefab }
    if ($Profile) { $cmd.profile = $Profile }
    # 'powershell -File' passes '-Preset 1,0' as one string, so split on commas here
    if ($Preset) { $cmd.presets = @($Preset | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) }
    if ($Aspect) { $cmd.aspects = @($Aspect | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) }
    if ($Spec) { $cmd.spec = Resolve-FilePath $Spec }
    if ($Out) { $cmd.output = Resolve-FilePath $Out }
    if ($Folder) { $cmd.folder = $Folder }
    if ($Path) { $cmd.path = $Path }
    if ($Scene) { $cmd.scene = $Scene }
    if ($Bound.ContainsKey('Role')) { $cmd.role = $Role }
    if ($DryRun) { $cmd.dryRun = $true }
    if ($All) { $cmd.all = $true }
    return ($cmd | ConvertTo-Json -Depth 5 -Compress)
}

# While open, Unity holds Temp/UnityLockfile exclusively
function Test-EditorOpen {
    $lock = Join-Path $ProjectRoot 'Temp\UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try {
        $stream = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    } catch {
        return $true
    }
}

function Get-BridgeState {
    $file = Join-Path $WorkDir 'bridge.json'
    if (-not (Test-Path $file)) { return 'none' }
    try {
        $bridge = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    } catch {
        return 'unknown'
    }
    $age = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - [long]$bridge.unixTime
    if ($age -gt 10) { return 'stale' }
    return [string]$bridge.state
}

function Invoke-Editor([string]$Json) {
    New-Item -ItemType Directory -Force -Path $Inbox | Out-Null
    $temp = Join-Path $Inbox "$Id.tmp"
    $request = Join-Path $Inbox "$Id.json"
    $response = Join-Path $Outbox "$Id.json"
    [IO.File]::WriteAllText($temp, $Json, $Utf8)
    Move-Item -Force $temp $request

    $deadline = (Get-Date).AddSeconds($Timeout)
    $playingSince = $null
    $lastState = ''
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $response) {
            $text = [IO.File]::ReadAllText($response, [Text.Encoding]::UTF8)
            Remove-Item -Force $response
            return $text
        }

        $state = Get-BridgeState
        # ping and game-view also run during Play Mode (the editor handles them while playing)
        $playSafe = @('ping', 'game-view') -contains $Command
        if ($state -ne $lastState -and $state -ne 'ready' -and -not ($playSafe -and $state -eq 'playing')) { Note "waiting for the Unity editor (state: $state)" }
        $lastState = $state

        if ($state -eq 'playing' -and -not $playSafe) {
            if ($null -eq $playingSince) { $playingSince = Get-Date }
            if (((Get-Date) - $playingSince).TotalSeconds -ge $PlayModeWait) {
                Remove-Item -Force -ErrorAction SilentlyContinue $request
                Fail 'EDITOR_PLAYING' 'The Unity editor is in Play Mode. Exit Play Mode and retry.'
            }
        } else {
            $playingSince = $null
        }
        Start-Sleep -Milliseconds 200
    }

    Remove-Item -Force -ErrorAction SilentlyContinue $request
    if ($lastState -eq 'stale' -or $lastState -eq 'none') {
        Fail 'BRIDGE_NOT_RESPONDING' 'The editor is open but the FlexibleUITool bridge is not running (compile errors or editor busy). Check the Unity console.'
    }
    Fail 'TIMEOUT' "The Unity editor did not answer within $Timeout seconds (state: $lastState)."
}

if (-not $ProjectRoot) {
    Fail 'PROJECT_NOT_FOUND' 'Could not find the Unity project. Run this from the project folder or pass -Project <path>.'
}
if (-not (Test-EditorOpen)) {
    Fail 'EDITOR_NOT_RUNNING' 'The Unity editor is not running for this project. Ask the user to open it, then retry.'
}
$text = Invoke-Editor (New-CommandJson)

Write-Output $text
if ($text -match '"ok":\s*true') { exit 0 } else { exit 1 }
