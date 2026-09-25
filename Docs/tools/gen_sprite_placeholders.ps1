Add-Type -AssemblyName System.Drawing

$Root = "C:\Users\User\Documents\My Games\Terraria\tModLoader\ModSources\SoA"

# --- палитра -------------------------------------------------------------
function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

$Pal = @{
    tide  = @{ dark = (C 255 26 32 64);  mid = (C 255 63 63 116);  lite = (C 255 91 110 225); glow = (C 255 143 228 242) }
    mage  = @{ dark = (C 255 24 62 74);  mid = (C 255 46 126 140); lite = (C 255 70 176 196); glow = (C 255 143 228 242) }
    crab  = @{ dark = (C 255 74 26 34);  mid = (C 255 140 46 58);  lite = (C 255 196 82 82);  glow = (C 255 240 170 140) }
    wood  = @{ dark = (C 255 58 42 30);  mid = (C 255 108 76 48);  lite = (C 255 156 116 72); glow = (C 255 214 180 120) }
    flesh = @{ dark = (C 255 40 60 52);  mid = (C 255 78 112 92);  lite = (C 255 120 158 128); glow = (C 255 190 214 180) }
}
$Guide  = C 120 255 64 200   # разметка кадров
$Center = C 70 255 255 255   # осевая

# --- примитивы -----------------------------------------------------------
function Px($bmp, [int]$x, [int]$y, $c) {
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { return }
    $bmp.SetPixel($x, $y, $c)
}

function RectOutline($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $c) {
    for ($i = 0; $i -lt $w; $i++) { Px $bmp ($x + $i) $y $c; Px $bmp ($x + $i) ($y + $h - 1) $c }
    for ($j = 0; $j -lt $h; $j++) { Px $bmp $x ($y + $j) $c; Px $bmp ($x + $w - 1) ($y + $j) $c }
}

function RectFill($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $c) {
    for ($j = 0; $j -lt $h; $j++) { for ($i = 0; $i -lt $w; $i++) { Px $bmp ($x + $i) ($y + $j) $c } }
}

function EllipseFill($bmp, [double]$cx, [double]$cy, [double]$rx, [double]$ry, $c) {
    if ($rx -le 0 -or $ry -le 0) { return }
    for ($y = [int][math]::Floor($cy - $ry); $y -le [int][math]::Ceiling($cy + $ry); $y++) {
        for ($x = [int][math]::Floor($cx - $rx); $x -le [int][math]::Ceiling($cx + $rx); $x++) {
            $dx = ($x + 0.5 - $cx) / $rx
            $dy = ($y + 0.5 - $cy) / $ry
            if ($dx * $dx + $dy * $dy -le 1.0) { Px $bmp $x $y $c }
        }
    }
}

function LineThick($bmp, [int]$x0, [int]$y0, [int]$x1, [int]$y1, [int]$t, $c) {
    $steps = [math]::Max([math]::Abs($x1 - $x0), [math]::Abs($y1 - $y0))
    if ($steps -eq 0) { $steps = 1 }
    for ($s = 0; $s -le $steps; $s++) {
        $x = [int][math]::Round($x0 + ($x1 - $x0) * $s / $steps)
        $y = [int][math]::Round($y0 + ($y1 - $y0) * $s / $steps)
        $h = [int][math]::Floor($t / 2)
        for ($j = -$h; $j -le $h; $j++) { for ($i = -$h; $i -le $h; $i++) { Px $bmp ($x + $i) ($y + $j) $c } }
    }
}

# --- крошечный шрифт 3x5 для номеров кадров ------------------------------
$Font3x5 = @{
    '0' = @('111', '101', '101', '101', '111')
    '1' = @('010', '110', '010', '010', '111')
    '2' = @('111', '001', '111', '100', '111')
    '3' = @('111', '001', '111', '001', '111')
    '4' = @('101', '101', '111', '001', '001')
    '5' = @('111', '100', '111', '001', '111')
    '6' = @('111', '100', '111', '101', '111')
    '7' = @('111', '001', '001', '001', '001')
    '8' = @('111', '101', '111', '101', '111')
    '9' = @('111', '101', '111', '001', '111')
}

function DrawNumber($bmp, [int]$x, [int]$y, [string]$text, $c) {
    $cx = $x
    foreach ($ch in $text.ToCharArray()) {
        $rows = $Font3x5[[string]$ch]
        if ($null -ne $rows) {
            for ($j = 0; $j -lt 5; $j++) {
                for ($i = 0; $i -lt 3; $i++) {
                    if ($rows[$j][$i] -eq '1') { Px $bmp ($cx + $i) ($y + $j) $c }
                }
            }
        }
        $cx += 4
    }
}

# --- болванки силуэтов ---------------------------------------------------
function BlockNpc($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p, [int]$idx) {
    # корпус стоит на нижней грани кадра, лёгкий покачив по номеру кадра
    $bob = [int]($idx % 2)
    $bodyW = [int]($w * 0.55)
    $bodyH = [int]($h * 0.45)
    $cx = $x + $w / 2.0
    $baseY = $y + $h - 2 - $bob
    EllipseFill $bmp $cx ($baseY - $bodyH / 2.0) ($bodyW / 2.0) ($bodyH / 2.0) $p.mid
    EllipseFill $bmp $cx ($baseY - $bodyH * 0.75) ($bodyW / 2.6) ($bodyH / 3.2) $p.lite
    # ноги
    $legY = $baseY
    for ($k = -1; $k -le 1; $k += 2) {
        $lx = [int]($cx + $k * $bodyW * 0.45)
        LineThick $bmp $lx ($legY - $bodyH / 2) ($lx + $k * 3) $legY 1 $p.dark
    }
    # клешня справа
    EllipseFill $bmp ($cx + $bodyW * 0.65) ($baseY - $bodyH * 0.7) 3 2.5 $p.glow
}

function BlockFish($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p) {
    $cx = $x + $w * 0.55
    $cy = $y + $h / 2.0
    EllipseFill $bmp $cx $cy ($w * 0.3) ($h * 0.3) $p.mid
    EllipseFill $bmp ($cx + $w * 0.1) $cy ($w * 0.16) ($h * 0.18) $p.lite
    LineThick $bmp ([int]($x + $w * 0.16)) ([int]($cy - $h * 0.25)) ([int]($x + $w * 0.25)) ([int]$cy) 1 $p.dark
    LineThick $bmp ([int]($x + $w * 0.16)) ([int]($cy + $h * 0.25)) ([int]($x + $w * 0.25)) ([int]$cy) 1 $p.dark
    LineThick $bmp ([int]($x + $w * 0.16)) ([int]($cy - $h * 0.25)) ([int]($x + $w * 0.16)) ([int]($cy + $h * 0.25)) 1 $p.dark
    Px $bmp ([int]($x + $w * 0.78)) ([int]($cy - 1)) $p.glow
}

function BlockBlob($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p) {
    EllipseFill $bmp ($x + $w / 2.0) ($y + $h / 2.0) ($w / 2.0 - 2) ($h / 2.0 - 2) $p.mid
    EllipseFill $bmp ($x + $w / 2.0) ($y + $h * 0.4) ($w / 3.5) ($h / 4.5) $p.lite
}

function BlockCrate($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p) {
    RectFill $bmp ($x + 2) ($y + 3) ($w - 4) ($h - 5) $p.mid
    RectOutline $bmp ($x + 2) ($y + 3) ($w - 4) ($h - 5) $p.dark
    RectFill $bmp ($x + 2) ($y + 3) ($w - 4) 3 $p.lite
    LineThick $bmp ($x + 2) ($y + [int]($h * 0.55)) ($x + $w - 3) ($y + [int]($h * 0.55)) 1 $p.dark
}

function BlockDiag($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p) {
    LineThick $bmp ($x + 3) ($y + $h - 3) ($x + $w - 3) ($y + 3) 3 $p.mid
    LineThick $bmp ($x + 2) ($y + $h - 2) ($x + 6) ($y + $h - 6) 3 $p.dark
    Px $bmp ($x + $w - 3) ($y + 2) $p.glow
}

function BlockArrow($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p) {
    $cx = [int]($x + $w / 2)
    LineThick $bmp $cx ($y + 5) $cx ($y + $h - 4) 1 $p.mid
    # наконечник вверх
    for ($j = 0; $j -lt 6; $j++) {
        $half = [int]([math]::Round($j * 0.5))
        for ($i = -$half; $i -le $half; $i++) { Px $bmp ($cx + $i) ($y + 1 + $j) $p.lite }
    }
    # оперение
    for ($j = 0; $j -lt 5; $j++) {
        Px $bmp ($cx - 2) ($y + $h - 2 - $j) $p.dark
        Px $bmp ($cx + 2) ($y + $h - 2 - $j) $p.dark
    }
}

function BlockBoot($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p) {
    RectFill $bmp ($x + 4) ($y + 3) ([int]($w * 0.4)) ($h - 8) $p.mid
    RectFill $bmp ($x + 4) ($y + $h - 7) ($w - 8) 5 $p.mid
    RectFill $bmp ($x + 3) ($y + $h - 3) ($w - 6) 2 $p.dark
    RectFill $bmp ($x + 4) ($y + 3) ([int]($w * 0.4)) 2 $p.lite
}

function BlockSigil($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p, [int]$tier) {
    $cx = $x + $w / 2.0
    $cy = $y + $h / 2.0
    EllipseFill $bmp $cx $cy ($w / 2.0 - 3) ($h / 2.0 - 3) $p.dark
    EllipseFill $bmp $cx $cy ($w / 2.0 - 5) ($h / 2.0 - 5) $p.mid
    # вставка-ромб, растёт со ступенью
    $r = 3 + $tier
    for ($j = -$r; $j -le $r; $j++) {
        $span = $r - [math]::Abs($j)
        for ($i = -$span; $i -le $span; $i++) { Px $bmp ([int]($cx + $i)) ([int]($cy + $j)) $p.lite }
    }
    # кристаллы по кругу — по одному на ступень
    for ($k = 0; $k -lt $tier; $k++) {
        $a = [math]::PI * 2 * $k / $tier - [math]::PI / 2
        $px = [int]($cx + [math]::Cos($a) * ($w / 2.0 - 4))
        $py = [int]($cy + [math]::Sin($a) * ($h / 2.0 - 4))
        Px $bmp $px $py $p.glow
        Px $bmp ($px + 1) $py $p.glow
        Px $bmp $px ($py + 1) $p.glow
    }
}

function BlockTileCell($bmp, [int]$x, [int]$y, [int]$cell, $p, [switch]$Open) {
    RectFill $bmp $x $y $cell $cell $p.mid
    RectOutline $bmp $x $y $cell $cell $p.dark
    if ($Open) { RectFill $bmp ($x + 5) ($y + 5) ($cell - 10) ($cell - 10) $p.glow }
    else { RectFill $bmp ($x + 6) ($y + 6) ($cell - 12) ($cell - 12) $p.lite }
}

# --- приливная флора -----------------------------------------------------
# Клетка листа шире тайла втрое: тайл — центральные 16 px, лапы свисают на
# соседей. Разметка показывает и границу клетки, и границу тайла, иначе при
# отрисовке легко потерять, где проходит колонка.
function FloraLap($bmp, [int]$cx, [int]$y, [int]$h, [int]$dir, [int]$len, $p) {
    $top = $y + 3
    for ($k = 0; $k -lt 4; $k++) {
        $row = $top + $k * 3
        if ($row -ge ($y + $h - 1)) { break }
        $l = [int]($len * (1.0 - $k * 0.22))
        $c = if ($k -eq 0) { $p.lite } else { $p.mid }
        LineThick $bmp $cx $row ($cx + $dir * $l) ([math]::Min($row + 2, $y + $h - 2)) 1 $c
    }
    Px $bmp ($cx + $dir * $len) ($top + 1) $p.glow
}

function BlockFlora($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $p, [int]$variant) {
    $cx = [int]($x + $w / 2)   # ось стебля = центр тайла
    $stemHalf = 3
    $lapLong = 20
    $lapShort = 13

    # стебель сквозной по всей высоте клетки — сегменты обязаны стыковаться
    if ($variant -ne 7) { RectFill $bmp ($cx - $stemHalf) $y ($stemHalf * 2) $h $p.mid }

    switch ($variant) {
        0 { EllipseFill $bmp $cx ($y + $h - 1) 7 4 $p.dark }                      # комель
        1 { }                                                                      # голый стебель
        2 { FloraLap $bmp $cx $y $h -1 $lapLong  $p }
        3 { FloraLap $bmp $cx $y $h -1 $lapShort $p }
        4 { FloraLap $bmp $cx $y $h  1 $lapLong  $p }
        5 { FloraLap $bmp $cx $y $h  1 $lapShort $p }
        6 { FloraLap $bmp $cx $y $h -1 $lapLong $p; FloraLap $bmp $cx $y $h 1 $lapShort $p }
        7 {
            RectFill $bmp ($cx - 2) ($y + $h - 7) 4 7 $p.mid                       # верхушка
            EllipseFill $bmp $cx ($y + $h - 8) 5 5 $p.lite
            Px $bmp $cx ($y + 2) $p.glow
        }
    }

    # разметка поверх силуэта: рамка клетки, границы тайла, осевая
    RectOutline $bmp $x $y $w $h $Guide
    for ($j = $y; $j -lt ($y + $h); $j += 2) {
        Px $bmp ($cx - 8) $j $Guide
        Px $bmp ($cx + 7) $j $Guide
        Px $bmp $cx $j $Center
    }
    DrawNumber $bmp ($x + 2) ($y + 2) ([string]$variant) $Guide
}

# --- манифест ------------------------------------------------------------
# kind: npc | fish | blob | crate | diag | arrow | boot | sigil | tilecrate | tileseal | flora
$Manifest = @(
    @{ id = 'tideflora';  path = 'Content\Tiles\Nature\Tidekelp_tile.png';                    w = 398; h = 52;  kind = 'flora'; pal = 'tide' }
    @{ id = 'crabmage';   path = 'Content\NPCs\Bosses\KingCrab\CrabMage.png';                  w = 66;  h = 912; fw = 66; fh = 48; kind = 'npc';   pal = 'mage' }
    @{ id = 'crabknight'; path = 'Content\NPCs\Bosses\KingCrab\CrabKnight_24frames.png';       w = 66;  h = 1152; fw = 66; fh = 48; kind = 'npc';  pal = 'crab'; baseFrom = 'Content\NPCs\Bosses\KingCrab\CrabKnight.png' }
    @{ id = 'glowguppy';  path = 'Content\Items\Fishing\GlowGuppy.png';                        w = 20;  h = 16;  kind = 'fish';  pal = 'tide' }
    @{ id = 'tackle';     path = 'Content\Items\Fishing\SunkenTackle.png';                     w = 18;  h = 18;  kind = 'blob';  pal = 'wood' }
    @{ id = 'grouper';    path = 'Content\Items\Fishing\AbyssGrouper.png';                     w = 26;  h = 20;  kind = 'fish';  pal = 'mage' }
    @{ id = 'tidecrate';  path = 'Content\Items\Fishing\TideCrate.png';                        w = 26;  h = 26;  kind = 'crate'; pal = 'wood' }
    @{ id = 'abysscrate'; path = 'Content\Items\Fishing\AbyssCrate.png';                       w = 26;  h = 26;  kind = 'crate'; pal = 'mage' }
    @{ id = 'cratetile';  path = 'Content\Tiles\Other\TideCrate_tile.png';                     w = 72;  h = 36;  kind = 'tilecrate'; pal = 'wood' }
    @{ id = 'sealtile';   path = 'Content\Tiles\Other\TideSeal_tile.png';                      w = 106; h = 52;  kind = 'tileseal';  pal = 'tide' }
    @{ id = 'leech';      path = 'Content\NPCs\Enemies\MireLeech.png';                         w = 24;  h = 64;  fw = 24; fh = 16; kind = 'fish'; pal = 'flesh' }
    @{ id = 'villager';   path = 'Content\NPCs\Enemies\DrownedVillager.png';                   w = 18;  h = 240; fw = 18; fh = 40; kind = 'npc';  pal = 'flesh' }
    @{ id = 'rod';        path = 'Content\Items\Fishing\TidecallerRod.png';                    w = 24;  h = 28;  kind = 'diag';  pal = 'tide' }
    @{ id = 'bobber';     path = 'Content\Projectiles\Fishing\TideBobber.png';                 w = 16;  h = 16;  kind = 'blob';  pal = 'tide' }
    @{ id = 'arrowitem';  path = 'Content\Items\Ammo\AbyssArrow.png';                          w = 14;  h = 32;  kind = 'arrow'; pal = 'mage' }
    @{ id = 'arrowproj';  path = 'Content\Projectiles\AbyssArrow\AbyssArrowProjectile.png';    w = 12;  h = 32;  kind = 'arrow'; pal = 'mage'; axis = $true }
    @{ id = 'boots';      path = 'Content\Items\Accessories\TideskimmerBoots.png';             w = 28;  h = 24;  kind = 'boot';  pal = 'tide' }
    @{ id = 'sigil1';     path = 'Content\Items\Accessories\PressureSigil1.png';               w = 32;  h = 32;  kind = 'sigil'; pal = 'tide'; tier = 1 }
    @{ id = 'sigil2';     path = 'Content\Items\Accessories\PressureSigil2.png';               w = 32;  h = 32;  kind = 'sigil'; pal = 'tide'; tier = 2 }
    @{ id = 'sigil3';     path = 'Content\Items\Accessories\PressureSigil3.png';               w = 32;  h = 32;  kind = 'sigil'; pal = 'tide'; tier = 3 }
    @{ id = 'sigil4';     path = 'Content\Items\Accessories\PressureSigil4.png';               w = 32;  h = 32;  kind = 'sigil'; pal = 'tide'; tier = 4 }
)

$written = @()

foreach ($m in $Manifest) {
    $full = Join-Path $Root $m.path
    $dir = Split-Path $full -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    if (Test-Path $full) { Write-Host "SKIP (уже есть): $($m.path)"; continue }

    $bmp = New-Object System.Drawing.Bitmap($m.w, $m.h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $p = $Pal[$m.pal]

    $fw = if ($m.ContainsKey('fw')) { $m.fw } else { $m.w }
    $fh = if ($m.ContainsKey('fh')) { $m.fh } else { $m.h }
    $frames = [int]($m.h / $fh)

    switch ($m.kind) {
        'tilecrate' {
            # 4 столбца x 2 ряда, клетка 16, шаг 18: слева приливный, справа ящик бездны
            for ($row = 0; $row -lt 2; $row++) {
                for ($col = 0; $col -lt 4; $col++) {
                    $pp = if ($col -lt 2) { $Pal['wood'] } else { $Pal['mage'] }
                    BlockTileCell $bmp ($col * 18) ($row * 18) 16 $pp
                    DrawNumber $bmp ($col * 18 + 1) ($row * 18 + 1) ([string]($row * 4 + $col)) $Guide
                }
            }
        }
        'flora' {
            # 8 столбцов x 3 ряда, клетка 48x16, шаг 50x18:
            # ряд — вид (ель / папоротник / рыжий куст), столбец — форма сегмента
            $rowPal = @('tide', 'mage', 'crab')
            for ($row = 0; $row -lt 3; $row++) {
                for ($col = 0; $col -lt 8; $col++) {
                    BlockFlora $bmp ($col * 50) ($row * 18) 48 16 $Pal[$rowPal[$row]] $col
                }
            }
        }
        'tileseal' {
            # 6 столбцов x 3 ряда, клетка 16, шаг 18: 0-2 запечатана, 3-5 открыта
            for ($row = 0; $row -lt 3; $row++) {
                for ($col = 0; $col -lt 6; $col++) {
                    if ($col -ge 3) { BlockTileCell $bmp ($col * 18) ($row * 18) 16 $p -Open }
                    else { BlockTileCell $bmp ($col * 18) ($row * 18) 16 $p }
                }
            }
        }
        default {
            for ($f = 0; $f -lt $frames; $f++) {
                $y0 = $f * $fh
                switch ($m.kind) {
                    'npc'   { BlockNpc  $bmp 0 $y0 $fw $fh $p $f }
                    'fish'  { BlockFish $bmp 0 $y0 $fw $fh $p }
                    'blob'  { BlockBlob $bmp 0 $y0 $fw $fh $p }
                    'crate' { BlockCrate $bmp 0 $y0 $fw $fh $p }
                    'diag'  { BlockDiag $bmp 0 $y0 $fw $fh $p }
                    'arrow' { BlockArrow $bmp 0 $y0 $fw $fh $p }
                    'boot'  { BlockBoot $bmp 0 $y0 $fw $fh $p }
                    'sigil' { BlockSigil $bmp 0 $y0 $fw $fh $p $m.tier }
                }
                if ($frames -gt 1) {
                    RectOutline $bmp 0 $y0 $fw $fh $Guide
                    if ($fh -ge 20 -and $fw -ge 14) { DrawNumber $bmp 2 ($y0 + 2) ([string]$f) $Guide }
                }
            }
            if ($m.ContainsKey('axis') -and $m.axis) {
                $cx = [int]($m.w / 2)
                for ($y = 0; $y -lt $m.h; $y += 2) { Px $bmp $cx $y $Center }
            }
        }
    }

    # уже нарисованные кадры переносим поверх болванки
    if ($m.ContainsKey('baseFrom')) {
        $srcPath = Join-Path $Root $m.baseFrom
        if (Test-Path $srcPath) {
            $src = [System.Drawing.Image]::FromFile($srcPath)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $g.DrawImage($src, 0, 0, $src.Width, $src.Height)
            $g.Dispose()
            $src.Dispose()
            Write-Host "  (перенёс $($src.Width)x$($src.Height) из $($m.baseFrom))"
        }
    }

    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $written += $m.path
    Write-Host "OK $($m.path)  $($m.w)x$($m.h)"
}

Write-Host ""
Write-Host "Создано файлов: $($written.Count)"
