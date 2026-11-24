$ErrorActionPreference = "Stop"

function Resolve-GitExe {
    $cmd = Get-Command git -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = "C:\Program Files\Git\cmd\git.exe"
    if (Test-Path $fallback) { return $fallback }
    throw "git.exe not found."
}

$git = Resolve-GitExe
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Fantasy World Simulator - Multi-Agent Live Status Board" -ForegroundColor Cyan
Write-Host " Repo Root: $repoRoot" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

Write-Host "`n[1] Git Worktrees (Active Working Folders)" -ForegroundColor Yellow
$worktreeRaw = & $git -C $repoRoot worktree list --porcelain
$wtPath = ""
$wtBranch = "(detached)"
$wtHead = ""
foreach ($line in ($worktreeRaw + "")) {
    if ($line -eq "") {
        if ($wtPath -ne "") {
            $dirtyCount = (& $git -C $wtPath status --porcelain | Measure-Object).Count
            $headMsg = & $git -C $wtPath log -1 --format="%h %s" 2>$null
            Write-Host ("  - {0,-45} | Branch: {1,-32} | Dirty: {2,-3} | HEAD: {3}" -f $wtPath, $wtBranch, $dirtyCount, $headMsg)
        }
        $wtPath = ""; $wtBranch = "(detached)"; $wtHead = ""
        continue
    }
    if ($line -match "^worktree (.+)$") { $wtPath = $Matches[1] }
    elseif ($line -match "^branch refs/heads/(.+)$") { $wtBranch = $Matches[1] }
    elseif ($line -match "^HEAD (.+)$") { $wtHead = $Matches[1].Substring(0, 7) }
}

$legacyReview = Join-Path $repoRoot "T001-review"
if (Test-Path (Join-Path $legacyReview ".git")) {
    Write-Host "`n[2] Legacy Nested Checkout Detected (T001-review - migrate to worktree after T002)" -ForegroundColor Magenta
    $revBranch = & $git -C $legacyReview rev-parse --abbrev-ref HEAD
    $revHead = & $git -C $legacyReview log -1 --format="%h %s"
    $revDirty = (& $git -C $legacyReview status --porcelain | Measure-Object).Count
    Write-Host ("  - {0,-45} | Branch: {1,-32} | Dirty: {2,-3} | HEAD: {3}" -f $legacyReview, $revBranch, $revDirty, $revHead)
}

Write-Host "`n[3] All Local Branches (Any feat/Txxx-* here means claimed/in-progress)" -ForegroundColor Yellow
& $git -C $repoRoot branch -vv

$handoffDir = Join-Path $repoRoot "docs\handoffs"
Write-Host "`n[4] Claims (docs/handoffs/*.md on this checkout; task branches carry their own)" -ForegroundColor Yellow
if (Test-Path $handoffDir) {
    Get-ChildItem $handoffDir -Filter *.md | Where-Object Name -ne "README.md" | ForEach-Object {
        $state = (Get-Content -Encoding UTF8 $_.FullName | Select-String -Pattern "IN_PROGRESS|REVIEW|BLOCKED|HANDOFF_READY|DONE" | Select-Object -First 1)
        Write-Host ("  - {0,-20} {1}" -f $_.BaseName, $(if ($state) { $state.Line.Trim() } else { "(no state line)" }))
    }
}

$tasksFile = Join-Path $repoRoot "TASKS.md"
if (Test-Path $tasksFile) {
    Write-Host "`n[5] Task table (TASKS.md)" -ForegroundColor Yellow
    Get-Content -Encoding UTF8 $tasksFile | Where-Object { $_ -match "^\| (T\d+|ID) " } | ForEach-Object { Write-Host "  $_" }
}
