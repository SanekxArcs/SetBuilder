$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Same public content as https://tunerfest.app/sets. No account or API key.
    $query = '{"affixes":*[_type=="affix" && hidden != true] | order(title asc){title,description,tags,bonusPercentageRangeFrom,bonusPercentageRangeTo,"icon":image.asset->url},"sets":*[_type=="setBonus" && hidden != true] | order(title asc){title,description,tags,bonusPercentageThreeParts,bonusPercentageFull,"icon":image.asset->url},"builds":*[_type=="setAndAffixes" && hidden != true] | order(title asc){title,description,tags,"set":setBonus->title,"affixes":affixes[]->title}}'
    $uri = 'https://2kgev7fd.api.sanity.io/v2025-01-01/data/query/production?query=' + [uri]::EscapeDataString($query)
    $catalog = (Invoke-RestMethod -Uri $uri).result
    if (!$catalog.affixes.Count -or !$catalog.sets.Count -or !$catalog.builds.Count) { throw 'Catalog is empty; existing data was preserved.' }
    # Local user profile remains available after refreshing public site data.
    $catalog.builds = @($catalog.builds | Where-Object { $_.title -ine 'Nitro Push All' }) + @([pscustomobject]@{title='Nitro Push All';description='Pure + Frenetic на NITRO CHEMIST: локальна збірка, якої немає на сайті';tags=@('speed');set='NITRO CHEMIST';affixes=@('Pure','Frenetic')})
    $catalog.builds = @($catalog.builds | Sort-Object title)
    New-Item -ItemType Directory -Force Assets | Out-Null
    foreach ($entry in @($catalog.affixes) + @($catalog.sets)) {
        if (!$entry.icon) { continue }
        $assetUri = [uri]$entry.icon
        if ($assetUri.Host -ne 'cdn.sanity.io') { throw 'Unexpected image host' }
        $name = ($entry.title -replace '[^a-zA-Z0-9]', '').ToLowerInvariant()
        $extension = [IO.Path]::GetExtension($assetUri.AbsolutePath)
        if ($extension -notin '.svg','.png','.jpg','.webp') { throw 'Unexpected asset format' }
        Invoke-WebRequest -Uri $assetUri -OutFile (Join-Path Assets ($name + $extension))
    }
    $catalog | ConvertTo-Json -Depth 15 | Set-Content catalog.json -Encoding utf8
    Write-Host "Updated $($catalog.affixes.Count) affixes, $($catalog.sets.Count) sets, $($catalog.builds.Count) builds. Rebuild the app to include updates."
} finally { Pop-Location }
