param([string]$LogsRoot = (Join-Path $PSScriptRoot 'dist-v1.0.3\Logs'))
$ErrorActionPreference = 'Stop'
# Derived files only. Original event logs, screenshots and summaries remain untouched.
$reports = @()
foreach ($folder in Get-ChildItem -LiteralPath $LogsRoot -Directory) {
    $metadataPath = Join-Path $folder.FullName 'session.json'
    $summaryPath = Join-Path $folder.FullName 'summary.json'
    if (!(Test-Path $metadataPath) -or !(Test-Path $summaryPath)) { continue }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ($metadata.mode -ne 'automation' -or $metadata.schemaVersion -ge 2) { continue }
    $rows = [Collections.Generic.List[object]]::new()
    $current = $null
    foreach ($line in [IO.File]::ReadLines((Join-Path $folder.FullName 'events.jsonl'))) {
        try { $event = $line | ConvertFrom-Json } catch { continue }
        if ($event.kind -eq 'message' -and $event.data.message -match 'слот (\d+) · кидок (\d+): шукаю (.+)$') {
            if ($null -ne $current) { $rows.Add($current) }
            $current = [ordered]@{ roll_id=[int]$Matches[2]; time=$event.time; slot=[int]$Matches[1]; wanted=$Matches[3]; phase='unknown'; paid_confirmed=$false; fresh_name=''; current=''; reroll=''; choice=''; expected=''; bucks=0; spare_parts=0; source='legacy-reconstructed' }
        }
        elseif ($event.kind -eq 'decision' -and $null -ne $current) {
            $current.paid_confirmed = $true
            $current.fresh_name = $event.data.freshName
            $current.current = $event.data.current
            $current.phase = if ($event.data.current -like ($current.wanted+' *')) { 'Maximize' } else { 'Search' }
            $current.reroll = $event.data.reroll
            $current.choice = $event.data.choice
            $current.expected = $event.data.expected
            $current.bucks = 1500
            $current.spare_parts = 150
        }
    }
    if ($null -ne $current) { $rows.Add($current) }
    $objects = @($rows | ForEach-Object { [pscustomobject]$_ })
    if ($objects.Count) { $objects | Export-Csv -LiteralPath (Join-Path $folder.FullName 'legacy-rolls.csv') -NoTypeInformation -Encoding utf8 }
    $paid = @($objects | Where-Object paid_confirmed)
    $pending = @($objects | Where-Object { !$_.paid_confirmed })
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $report = [ordered]@{ source='legacy-reconstructed'; session=$folder.Name; version=$metadata.version; outcome=$summary.outcome; confirmed_rolls=$paid.Count; pending_unconfirmed=$pending.Count; bucks_spent=$paid.Count*1500; spare_parts_spent=$paid.Count*150; unit_bucks=1500; unit_spare_parts=150; fresh_offers=@($paid | Group-Object fresh_name | Sort-Object Count -Descending | Select-Object Name,Count); notes='Paid when at least one valid popup decision was recorded between roll markers. Repeated OCR decisions collapse to one rotation. Manual rolls outside the app are not included.' }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $folder.FullName 'legacy-spending.json') -Encoding utf8
    $reports += [pscustomobject]$report
}
$reports | Select-Object session,outcome,confirmed_rolls,pending_unconfirmed,bucks_spent,spare_parts_spent | Format-Table -AutoSize

