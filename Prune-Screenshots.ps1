param(
    [string]$LogsRoot = (Join-Path $PSScriptRoot 'Logs'),
    # Sessions that started before this date lose their PNGs. Default: everything older than 7 days.
    [datetime]$Before = (Get-Date).AddDays(-7),
    # Without -Apply only reports what would be deleted.
    [switch]$Apply
)
# Deletes screenshots/*.png inside old session folders. Text files (events.jsonl, rolls.csv, session.log,
# summary.json, settings.json) stay, so analytics and the text logs keep working. Nothing else is touched.
$ErrorActionPreference = 'Stop'
$files = 0; $bytes = 0L; $sessions = 0
foreach ($folder in Get-ChildItem -LiteralPath $LogsRoot -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}_' }) {
    $started = [datetime]::ParseExact($folder.Name.Substring(0, 19), 'yyyy-MM-dd_HH-mm-ss', $null)
    if ($started -ge $Before) { continue }
    $shots = Join-Path $folder.FullName 'screenshots'
    if (-not (Test-Path -LiteralPath $shots)) { continue }
    $pngs = @(Get-ChildItem -LiteralPath $shots -Filter '*.png' -File)
    if (-not $pngs.Count) { continue }
    $sessions++; $files += $pngs.Count; $bytes += ($pngs | Measure-Object Length -Sum).Sum
    if ($Apply) { $pngs | Remove-Item -Force -Confirm:$false }
}
$verb = if ($Apply) { 'Deleted' } else { 'Would delete (add -Apply)' }
Write-Host ("{0} {1} PNG files, {2:N0} MB, in {3} sessions started before {4:yyyy-MM-dd}." -f $verb, $files, ($bytes / 1MB), $sessions, $Before)
