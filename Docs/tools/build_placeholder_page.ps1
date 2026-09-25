$Root = "C:\Users\User\Documents\My Games\Terraria\tModLoader\ModSources\SoA"
$Pad  = "C:\Users\User\AppData\Local\Temp\claude\C--Users-User-Documents-My-Games-Terraria-tModLoader-ModSources-SoA\60c8cc79-6df2-40a1-8156-0862bec10b89\scratchpad"

$map = [ordered]@{
    crabknight = 'Content\NPCs\Bosses\KingCrab\CrabKnight.png'
    crabmage   = 'Content\NPCs\Bosses\KingCrab\CrabMage.png'
    leech      = 'Content\NPCs\Enemies\MireLeech.png'
    villager   = 'Content\NPCs\Enemies\DrownedVillager.png'
    glowguppy  = 'Content\Items\Fishing\GlowGuppy.png'
    grouper    = 'Content\Items\Fishing\AbyssGrouper.png'
    tackle     = 'Content\Items\Fishing\SunkenTackle.png'
    rod        = 'Content\Items\Fishing\TidecallerRod.png'
    bobber     = 'Content\Projectiles\Fishing\TideBobber.png'
    tidecrate  = 'Content\Items\Fishing\TideCrate.png'
    abysscrate = 'Content\Items\Fishing\AbyssCrate.png'
    arrowitem  = 'Content\Items\Ammo\AbyssArrow.png'
    arrowproj  = 'Content\Projectiles\AbyssArrow\AbyssArrowProjectile.png'
    boots      = 'Content\Items\Accessories\TideskimmerBoots.png'
    sigil1     = 'Content\Items\Accessories\PressureSigil1.png'
    sigil2     = 'Content\Items\Accessories\PressureSigil2.png'
    sigil3     = 'Content\Items\Accessories\PressureSigil3.png'
    sigil4     = 'Content\Items\Accessories\PressureSigil4.png'
    cratetile  = 'Content\Tiles\Other\TideCrate_tile.png'
    sealtile   = 'Content\Tiles\Other\TideSeal_tile.png'
}

$parts = @()
$total = 0
foreach ($k in $map.Keys) {
    $full = Join-Path $Root $map[$k]
    if (-not (Test-Path $full)) { Write-Host "MISSING $($map[$k])"; continue }
    $bytes = [System.IO.File]::ReadAllBytes($full)
    $total += $bytes.Length
    $b64 = [Convert]::ToBase64String($bytes)
    $parts += ('"{0}":"data:image/png;base64,{1}"' -f $k, $b64)
}
$json = "{" + ($parts -join ",") + "}"

$tpl = [System.IO.File]::ReadAllText((Join-Path $Pad "sprites_template.html"), [System.Text.UTF8Encoding]::new($false))
$out = $tpl.Replace("/*__DATA__*/{}", $json)
[System.IO.File]::WriteAllText((Join-Path $Pad "sprites.html"), $out, [System.Text.UTF8Encoding]::new($false))

Write-Host ("Вшито изображений: {0}, суммарно {1:N0} байт" -f $parts.Count, $total)
Write-Host ("Итоговый HTML: {0:N0} байт" -f (Get-Item (Join-Path $Pad "sprites.html")).Length)
