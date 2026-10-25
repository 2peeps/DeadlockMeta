# Builds crosshairs\catalog.json: about a hundred shapes, each in every colour (the app shows a shape once, with its
# colours as dots on the card), plus a few two-colour shapes and the game's own reticle.
# The app downloads this file from GitHub, so editing the shapes and pushing is enough to update everyone.
#
# Codes are the 19 numbers: gap width height static pipOpacity pipBorder pipBorderGap pipBorderOpacity
#   dotSize dotOpacity dotBorder dotBorderGap dotBorderOpacity r g b outlineR outlineG outlineB
#
# Shapes are written in screen pixels at 1080p rather than in the game's raw settings:
#   h, w   pip length and thickness          in   distance from the centre to the inner end of each pip
#   op     pip opacity (1)                    mv   pips spread while shooting (in is then at rest)
#   ob     pip outline thickness (1), obo its opacity (0.7), hollow = only the outline, in the reticle colour
#   dot    dot size, dotOp its opacity (1), dob its dark outline (1), dobo that outline's opacity (0.8)
#   r      ring radius, rb its thickness (2), rbo its opacity (1); the ring is drawn in the reticle colour
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$inv = [Globalization.CultureInfo]::InvariantCulture

$colors = @(
    @{ key = 'white';  name = 'белый';      rgb = 255, 255, 255 }
    @{ key = 'green';  name = 'зелёный';    rgb = 0, 255, 0 }
    @{ key = 'lime';   name = 'лаймовый';   rgb = 190, 255, 0 }
    @{ key = 'cyan';   name = 'голубой';    rgb = 0, 255, 255 }
    @{ key = 'blue';   name = 'синий';      rgb = 40, 110, 255 }
    @{ key = 'purple'; name = 'фиолетовый'; rgb = 170, 90, 255 }
    @{ key = 'pink';   name = 'розовый';    rgb = 255, 0, 255 }
    @{ key = 'red';    name = 'красный';    rgb = 255, 0, 0 }
    @{ key = 'orange'; name = 'оранжевый';  rgb = 255, 140, 0 }
    @{ key = 'yellow'; name = 'жёлтый';     rgb = 255, 255, 0 }
    @{ key = 'black';  name = 'чёрный';     rgb = 0, 0, 0 }
)

# Families are listed separately and then interleaved, so the first screen of the catalog shows a bit of everything.
$families = [ordered]@{
    dots = @(
        ,@('dot-2', 'Точка-пиксель', @{ dot = 2 })
        ,@('dot-3', 'Точка маленькая', @{ dot = 3 })
        ,@('dot-4', 'Точка', @{ dot = 4 })
        ,@('dot-5', 'Точка средняя', @{ dot = 5 })
        ,@('dot-6', 'Точка крупная', @{ dot = 6 })
        ,@('dot-8', 'Точка большая', @{ dot = 8 })
        ,@('dot-11', 'Шар', @{ dot = 11; dob = 2 })
        ,@('dot-clean-3', 'Маленькая точка без обводки', @{ dot = 3; dob = 0 })
        ,@('dot-clean-5', 'Точка без обводки', @{ dot = 5; dob = 0 })
        ,@('dot-halo', 'Точка с ореолом', @{ dot = 4; r = 5; rb = 1; rbo = 0.45 })
        ,@('dot-glass', 'Полупрозрачная точка', @{ dot = 7; dotOp = 0.5; dob = 0 })
        ,@('dot-heavy', 'Точка в толстой обводке', @{ dot = 4; dob = 2; dobo = 1 })
    )
    rings = @(
        ,@('ring-xs', 'Колечко', @{ r = 4; rb = 1 })
        ,@('ring-s', 'Кружок маленький', @{ r = 6; rb = 1 })
        ,@('ring-m', 'Кружок', @{ r = 10 })
        ,@('ring-l', 'Круг', @{ r = 16 })
        ,@('ring-xl', 'Большой круг', @{ r = 26 })
        ,@('ring-thin', 'Тонкий круг', @{ r = 20; rb = 1 })
        ,@('ring-bold', 'Жирное кольцо', @{ r = 9; rb = 3 })
        ,@('ring-dot-s', 'Колечко с точкой', @{ r = 5; rb = 1; dot = 2; dob = 0 })
        ,@('ring-dot', 'Кружок с точкой', @{ r = 10; dot = 3; dob = 0 })
        ,@('ring-dot-l', 'Круг с точкой', @{ r = 18; dot = 3; dob = 0 })
        ,@('ring-dot-xl', 'Большой круг с точкой', @{ r = 28; rb = 1; dot = 2; dob = 0 })
        ,@('target', 'Мишень', @{ r = 8; dot = 6; dob = 0 })
        ,@('target-l', 'Мишень большая', @{ r = 13; rb = 3; dot = 8; dob = 0 })
        ,@('ring-glass', 'Полупрозрачный круг', @{ r = 14; rbo = 0.5; dot = 2; dob = 0 })
    )
    crosses = @(
        ,@('cross-micro', 'Крестик-микро', @{ h = 2; w = 1; in = 2 })
        ,@('cross-xs', 'Крестик мини', @{ h = 3; w = 2; in = 3 })
        ,@('cross-s', 'Крестик маленький', @{ h = 5; w = 2; in = 3 })
        ,@('cross', 'Крестик', @{ h = 7; w = 2; in = 4 })
        ,@('cross-m', 'Крестик средний', @{ h = 9; w = 2; in = 5 })
        ,@('cross-l', 'Крестик большой', @{ h = 12; w = 2; in = 6 })
        ,@('cross-tight', 'Крестик с маленьким зазором', @{ h = 6; w = 2; in = 1 })
        ,@('cross-wide', 'Крестик с большим зазором', @{ h = 7; w = 2; in = 9 })
        ,@('cross-brackets', 'Скобки', @{ h = 5; w = 2; in = 14 })
        ,@('cross-thin', 'Тонкий крестик', @{ h = 8; w = 1; in = 4 })
        ,@('cross-thin-l', 'Тонкий длинный крестик', @{ h = 14; w = 1; in = 5 })
        ,@('cross-bold', 'Жирный крестик', @{ h = 7; w = 3; in = 4 })
        ,@('cross-heavy', 'Массивный крестик', @{ h = 9; w = 4; in = 5 })
        ,@('cross-clean', 'Крестик без обводки', @{ h = 7; w = 2; in = 4; ob = 0 })
        ,@('cross-contrast', 'Контрастный крестик', @{ h = 7; w = 2; in = 4; ob = 2; obo = 1 })
        ,@('cross-glass', 'Полупрозрачный крестик', @{ h = 8; w = 2; in = 4; op = 0.55 })
        ,@('cross-stub', 'Короткие толстые засечки', @{ h = 3; w = 3; in = 5 })
        ,@('cross-needle', 'Иглы', @{ h = 16; w = 1; in = 8; ob = 0 })
    )
    crossdots = @(
        ,@('cdot-micro', 'Микро-крестик с точкой', @{ h = 2; w = 1; in = 3; dot = 2; dob = 0 })
        ,@('cdot-xs', 'Мини-крестик с точкой', @{ h = 3; w = 2; in = 4; dot = 2 })
        ,@('cdot', 'Крестик с точкой', @{ h = 6; w = 2; in = 4; dot = 2 })
        ,@('cdot-m', 'Средний крестик с точкой', @{ h = 8; w = 2; in = 6; dot = 3 })
        ,@('cdot-wide', 'Широкий крестик с точкой', @{ h = 6; w = 2; in = 10; dot = 3 })
        ,@('cdot-thin', 'Тонкий крестик с точкой', @{ h = 8; w = 1; in = 4; dot = 2; dob = 0 })
        ,@('cdot-bold', 'Жирный крестик с точкой', @{ h = 7; w = 3; in = 5; dot = 3 })
        ,@('cdot-bigdot', 'Крестик с крупной точкой', @{ h = 5; w = 2; in = 6; dot = 5 })
        ,@('cdot-clean', 'Крестик с точкой без обводки', @{ h = 6; w = 2; in = 4; ob = 0; dot = 2; dob = 0 })
        ,@('cdot-needle', 'Иглы с точкой', @{ h = 14; w = 1; in = 7; ob = 0; dot = 2; dob = 0 })
    )
    pluses = @(
        ,@('plus-xs', 'Плюс-крошка', @{ h = 3; w = 2; in = 0 })
        ,@('plus-s', 'Плюс маленький', @{ h = 5; w = 2; in = 0 })
        ,@('plus', 'Плюс', @{ h = 7; w = 2; in = 0 })
        ,@('plus-l', 'Плюс большой', @{ h = 11; w = 2; in = 0 })
        ,@('plus-xl', 'Плюс огромный', @{ h = 18; w = 2; in = 0 })
        ,@('plus-thin', 'Тонкий плюс', @{ h = 8; w = 1; in = 0 })
        ,@('plus-bold', 'Жирный плюс', @{ h = 7; w = 3; in = 0 })
        ,@('plus-clean', 'Плюс без обводки', @{ h = 7; w = 2; in = 0; ob = 0 })
        ,@('plus-hair', 'Перекрестие', @{ h = 40; w = 1; in = 0; ob = 0; op = 0.8 })
        ,@('plus-hair-gap', 'Перекрестие с окном', @{ h = 34; w = 1; in = 6; ob = 0; op = 0.8 })
        ,@('plus-dot', 'Плюс с точкой', @{ h = 7; w = 2; in = 0; dot = 4 })
    )
    squares = @(
        ,@('sq-s', 'Четыре квадратика', @{ h = 2; w = 2; in = 4 })
        ,@('sq', 'Квадратики', @{ h = 3; w = 3; in = 5 })
        ,@('sq-wide', 'Квадратики вразлёт', @{ h = 3; w = 3; in = 10 })
        ,@('sq-dot', 'Квадратики с точкой', @{ h = 2; w = 2; in = 5; dot = 2 })
        ,@('sq-big', 'Крупные квадраты', @{ h = 4; w = 4; in = 6 })
        ,@('sq-glass', 'Полупрозрачные квадраты', @{ h = 4; w = 4; in = 5; op = 0.5; ob = 0 })
    )
    hollow = @(
        ,@('hol', 'Контурный крестик', @{ h = 8; w = 3; in = 5; hollow = $true })
        ,@('hol-l', 'Контурный большой', @{ h = 12; w = 4; in = 6; hollow = $true })
        ,@('hol-dot', 'Контурный с точкой', @{ h = 8; w = 3; in = 5; hollow = $true; dot = 2; dob = 0 })
        ,@('hol-plus', 'Контурный плюс', @{ h = 8; w = 3; in = 1; hollow = $true })
        ,@('hol-sq', 'Контурные квадраты', @{ h = 4; w = 4; in = 6; hollow = $true })
        ,@('hol-brackets', 'Контурные скобки', @{ h = 6; w = 3; in = 12; hollow = $true })
    )
    combos = @(
        ,@('combo-in', 'Крестик в круге', @{ h = 5; w = 2; in = 3; ob = 0; r = 14; rb = 1 })
        ,@('combo-sniper', 'Прицел снайпера', @{ h = 8; w = 1; in = 16; ob = 0; r = 12; rb = 1; dot = 2; dob = 0 })
        ,@('combo-cross', 'Круг с перекрестием', @{ h = 14; w = 1; in = 0; ob = 0; r = 10; rb = 1 })
        ,@('combo-target', 'Мишень с засечками', @{ h = 4; w = 2; in = 10; ob = 0; r = 7; dot = 3; dob = 0 })
        ,@('combo-ticks', 'Круг с засечками', @{ h = 4; w = 2; in = 12; ob = 0; r = 12; rb = 1 })
        ,@('combo-big', 'Большой круг с крестиком', @{ h = 6; w = 2; in = 4; ob = 0; r = 22; dot = 2; dob = 0 })
        ,@('combo-plus', 'Плюс в кольце', @{ h = 5; w = 2; in = 0; ob = 0; r = 9; rb = 1 })
    )
    moving = @(
        ,@('mv', 'Классика, расходится', @{ h = 8; w = 2; in = 5; mv = $true })
        ,@('mv-dot', 'Классика с точкой, расходится', @{ h = 8; w = 2; in = 5; mv = $true; dot = 2 })
        ,@('mv-long', 'Длинный, расходится', @{ h = 14; w = 2; in = 6; mv = $true })
        ,@('mv-thin', 'Тонкий, расходится', @{ h = 10; w = 1; in = 4; mv = $true; dot = 2; dob = 0 })
        ,@('mv-short', 'Короткий, расходится', @{ h = 4; w = 2; in = 4; mv = $true })
        ,@('mv-bold', 'Жирный, расходится', @{ h = 7; w = 3; in = 6; mv = $true })
        ,@('mv-wide', 'Широкий, расходится', @{ h = 6; w = 2; in = 12; mv = $true })
        ,@('mv-ring', 'Расходится, с кругом', @{ h = 6; w = 2; in = 9; mv = $true; ob = 0; r = 5; rb = 1 })
    )
}

# Two-colour shapes: the outline (or ring) is a colour of its own. Each pair is one colour choice on the card.
$pairs = @(
    @{ key = 'white-red';     name = 'белый с красным';      main = 255, 255, 255; out = 255, 0, 0 }
    @{ key = 'black-white';   name = 'чёрный с белым';       main = 0, 0, 0;       out = 255, 255, 255 }
    @{ key = 'cyan-pink';     name = 'голубой с розовым';    main = 0, 255, 255;   out = 255, 0, 255 }
    @{ key = 'yellow-purple'; name = 'жёлтый с фиолетовым';  main = 255, 255, 0;   out = 170, 90, 255 }
    @{ key = 'green-black';   name = 'зелёный с чёрным';     main = 0, 255, 0;     out = 0, 0, 0 }
    @{ key = 'red-white';     name = 'красный с белым';      main = 255, 0, 0;     out = 255, 255, 255 }
    @{ key = 'white-blue';    name = 'белый с синим';        main = 255, 255, 255; out = 40, 110, 255 }
    @{ key = 'orange-cyan';   name = 'оранжевый с голубым';  main = 255, 140, 0;   out = 0, 255, 255 }
)
$twoTone = @(
    ,@('duo-cross', 'Двухцветный крестик', @{ h = 7; w = 2; in = 4; ob = 2; obo = 1 })
    ,@('duo-dot', 'Двухцветная точка', @{ dot = 5; dob = 2; dobo = 1 })
    ,@('duo-target', 'Двухцветная мишень', @{ r = 9; dot = 5; dob = 0 })
    ,@('duo-cdot', 'Двухцветный крестик с точкой', @{ h = 6; w = 2; in = 5; ob = 1; obo = 1; dot = 3; dob = 1; dobo = 1 })
)

function Num($x) { ([double]$x).ToString('0.####', $inv) }

# Turns a shape in screen terms into the game's 19 settings for one colour (and, for two-colour shapes, one outline).
function Code([hashtable]$o, $rgb, $outline) {
    $g = @{}; foreach ($k in $o.Keys) { $g[$k] = $o[$k] }
    $static = -not $g.mv
    $v = [ordered]@{ gap = 0; w = 2; h = 8; static = $static; pip = 0; pb = 0; pbg = 0; pbo = 0
                     dot = 2; dotOp = 0; db = 0; dbg = 0; dbo = 0 }
    $outlineColour = if ($outline) { $outline } elseif ($rgb[0] + $rgb[1] + $rgb[2] -lt 60) { 255, 255, 255 } else { 0, 0, 0 }
    if ($g.ContainsKey('h')) {
        $v.h = $g.h; $v.w = $g.w
        $mid = $g.in + $g.h / 2
        # The game puts a static pip's centre 11 px from the middle at gap 0, a spreading one 17 px plus 2 per gap.
        $v.gap = if ($static) { $mid - 11 } else { ($mid - 17) / 2 }
        if ($g.hollow) {
            $v.pip = 0; $v.pb = $(if ($g.ContainsKey('ob')) { $g.ob } else { 1 }); $v.pbo = 1
            if (-not $outline) { $outlineColour = $rgb }
        } else {
            $v.pip = $(if ($g.ContainsKey('op')) { $g.op } else { 1 })
            $v.pb = $(if ($g.ContainsKey('ob')) { $g.ob } else { 1 })
            $v.pbo = $(if ($g.ContainsKey('obo')) { $g.obo } else { 0.7 })
            if ($v.pb -eq 0) { $v.pbo = 0 }
        }
    }
    if ($g.ContainsKey('dot')) {
        $v.dot = $g.dot; $v.dotOp = $(if ($g.ContainsKey('dotOp')) { $g.dotOp } else { 1 })
    }
    if ($g.ContainsKey('r')) {
        # The ring is the dot's outline: radius = dot/2 + gap/2 + thickness/2, drawn in the outline colour.
        $rb = $(if ($g.ContainsKey('rb')) { $g.rb } else { 2 })
        $v.db = $rb; $v.dbg = 2 * ($g.r - $v.dot / 2 - $rb / 2); $v.dbo = $(if ($g.ContainsKey('rbo')) { $g.rbo } else { 1 })
        if (-not $outline) { $outlineColour = $rgb }
    } elseif ($g.ContainsKey('dot')) {
        $v.db = $(if ($g.ContainsKey('dob')) { $g.dob } else { 1 }); $v.dbg = 0
        $v.dbo = $(if ($v.db -gt 0) { $(if ($g.ContainsKey('dobo')) { $g.dobo } else { 0.8 }) } else { 0 })
    }
    $parts = @($v.gap, $v.w, $v.h, $(if ($v.static) { 'true' } else { 'false' }), $v.pip, $v.pb, $v.pbg, $v.pbo,
               $v.dot, $v.dotOp, $v.db, $v.dbg, $v.dbo) + $rgb + $outlineColour
    ($parts | ForEach-Object { if ($_ -is [string]) { $_ } else { Num $_ } }) -join ' '
}

# The first screen: one clear, mid-sized example of each family, then everything else interleaved.
$featured = 'cdot', 'ring-dot', 'dot-4', 'cross', 'combo-sniper', 'plus', 'hol', 'target', 'mv-dot', 'sq', 'cross-wide', 'combo-in'
$groups = [Collections.Generic.List[object]]::new()
foreach ($k in $featured) {
    $s = $families.Values | ForEach-Object { $_ } | Where-Object { $_[0] -eq $k }
    if (-not $s) { throw "featured shape $k not found" }
    $groups.Add($s)
}
$queues = @($families.Values | ForEach-Object { , [Collections.Generic.Queue[object]]::new([object[]]@($_ | Where-Object { $featured -notcontains $_[0] })) })
while (($queues | Where-Object { $_.Count -gt 0 }).Count -gt 0) {
    foreach ($q in $queues) { if ($q.Count -gt 0) { $groups.Add($q.Dequeue()) } }
}

$items = [Collections.Generic.List[object]]::new()
$items.Add([ordered]@{ id = 'game-default'; group = 'game-default'; name = 'Стандартный из игры'; code = '4 2 16 false 0.5 1 0 0.7 4 0.7 2 0 0.7 255 255 255 0 0 0' })
$i = 0
foreach ($s in $groups) {
    foreach ($c in $colors) {
        $items.Add([ordered]@{ id = "$($s[0])-$($c.key)"; group = $s[0]; name = "$($s[1]), $($c.name)"; code = Code $s[2] $c.rgb $null })
    }
    # A two-colour shape after every ~20 shapes keeps them spread through the list.
    if (++$i % 20 -eq 0 -and $i / 20 -le $twoTone.Count) {
        $t = $twoTone[$i / 20 - 1]
        foreach ($p in $pairs) {
            $items.Add([ordered]@{ id = "$($t[0])-$($p.key)"; group = $t[0]; name = "$($t[1]), $($p.name)"; code = Code $t[2] $p.main $p.out })
        }
    }
}

New-Item -ItemType Directory -Force crosshairs | Out-Null
$json = [ordered]@{ version = 2; items = $items } | ConvertTo-Json -Depth 4 -Compress
[IO.File]::WriteAllText((Join-Path (Get-Location) 'crosshairs\catalog.json'), $json, [Text.UTF8Encoding]::new($false))
$shapes = ($items | ForEach-Object { $_.group } | Select-Object -Unique).Count
"crosshairs\catalog.json: $shapes shapes, $($items.Count) crosshairs"
