# Builds crosshairs\catalog.json: every shape below in every colour, plus the game's own reticle.
# The app downloads this file from GitHub, so editing the shapes and pushing is enough to update everyone.
# Codes are the 19 numbers: gap width height static pipOpacity pipBorder pipBorderGap pipBorderOpacity
#   dotSize dotOpacity dotBorder dotBorderGap dotBorderOpacity r g b outlineR outlineG outlineB
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$inv = [Globalization.CultureInfo]::InvariantCulture

$colors = @(
    @{ key = 'white';  name = 'белый';     rgb = 255, 255, 255 }
    @{ key = 'green';  name = 'зелёный';   rgb = 0, 255, 0 }
    @{ key = 'cyan';   name = 'голубой';   rgb = 0, 255, 255 }
    @{ key = 'yellow'; name = 'жёлтый';    rgb = 255, 255, 0 }
    @{ key = 'pink';   name = 'розовый';   rgb = 255, 0, 255 }
    @{ key = 'red';    name = 'красный';   rgb = 255, 0, 0 }
    @{ key = 'orange'; name = 'оранжевый'; rgb = 255, 140, 0 }
    @{ key = 'blue';   name = 'синий';     rgb = 40, 110, 255 }
)

# Everything is off unless a shape turns it on. ring = the dot's outline drawn as a circle in the reticle colour.
function Shape($key, $name, [hashtable]$s) {
    $d = @{ gap = 0; w = 2; h = 8; static = $true; pip = 0; pb = 1; pbg = 0; pbo = 0.7
            dot = 0; dotOp = 0; db = 0; dbg = 0; dbo = 0; ring = $false }
    foreach ($k in $s.Keys) { $d[$k] = $s[$k] }
    # Hidden pips must not leave their outlines behind.
    if ($d.pip -eq 0) { $d.pb = 0; $d.pbo = 0 }
    $d.key = $key; $d.name = $name
    $d
}

# Pips meet in the centre when gap = height/2 - 11 (the game's base offset for a static reticle).
$shapes = @(
    Shape 'dot-s'      'Точка маленькая'          @{ dot = 3; dotOp = 1; db = 1; dbo = 0.8 }
    Shape 'dot'        'Точка'                    @{ dot = 4; dotOp = 1; db = 1; dbo = 0.8 }
    Shape 'dot-m'      'Точка крупная'            @{ dot = 6; dotOp = 1; db = 1; dbo = 0.8 }
    Shape 'dot-l'      'Точка большая'            @{ dot = 9; dotOp = 1; db = 2; dbo = 0.8 }
    Shape 'dot-clean'  'Точка без обводки'        @{ dot = 4; dotOp = 1 }
    Shape 'dot-bold'   'Точка контрастная'        @{ dot = 5; dotOp = 1; db = 2; dbo = 1 }
    Shape 'ring'       'Кружок'                   @{ dot = 2; db = 2; dbg = 12; dbo = 1; ring = $true }
    Shape 'ring-dot'   'Кружок с точкой'          @{ dot = 3; dotOp = 1; db = 2; dbg = 14; dbo = 1; ring = $true }
    Shape 'ring-l'     'Большой круг с точкой'    @{ dot = 2; dotOp = 1; db = 2; dbg = 28; dbo = 0.9; ring = $true }
    Shape 'cross-xs'   'Крестик мини'             @{ pip = 1; w = 2; h = 4; gap = -5 }
    Shape 'cross'      'Крестик'                  @{ pip = 1; w = 2; h = 8; gap = -4 }
    Shape 'cross-wide' 'Крестик с большим зазором' @{ pip = 1; w = 2; h = 8; gap = 2 }
    Shape 'cross-long' 'Крестик длинный'          @{ pip = 1; w = 2; h = 14; gap = -1 }
    Shape 'cross-thin' 'Крестик тонкий'           @{ pip = 1; w = 1; h = 8; gap = -4 }
    Shape 'cross-bold' 'Крестик жирный'           @{ pip = 1; w = 3; h = 8; gap = -3 }
    Shape 'cross-clean' 'Крестик без обводки'     @{ pip = 1; w = 2; h = 8; gap = -4; pb = 0 }
    Shape 'cross-contrast' 'Крестик контрастный'  @{ pip = 1; w = 2; h = 8; gap = -4; pb = 2; pbo = 1 }
    Shape 'cdot-xs'    'Мини-крестик с точкой'    @{ pip = 1; w = 2; h = 4; gap = -4; dot = 2; dotOp = 1; db = 1; dbo = 0.7 }
    Shape 'cdot'       'Крестик с точкой'         @{ pip = 1; w = 2; h = 8; gap = -3; dot = 2; dotOp = 1; db = 1; dbo = 0.7 }
    Shape 'cdot-wide'  'Широкий крестик с точкой' @{ pip = 1; w = 2; h = 8; gap = 2; dot = 3; dotOp = 1; db = 1; dbo = 0.7 }
    Shape 'cdot-thin'  'Тонкий крестик с точкой'  @{ pip = 1; w = 1; h = 7; gap = -3; dot = 2; dotOp = 1; db = 1; dbo = 0.7 }
    Shape 'cross-ring' 'Крестик в круге'          @{ pip = 1; w = 2; h = 6; gap = -4; dot = 2; db = 2; dbg = 22; dbo = 0.9; ring = $true }
    Shape 'plus-s'     'Плюс маленький'           @{ pip = 1; w = 2; h = 6; gap = -8 }
    Shape 'plus'       'Плюс'                     @{ pip = 1; w = 2; h = 10; gap = -6 }
    Shape 'plus-l'     'Плюс большой'             @{ pip = 1; w = 2; h = 16; gap = -3 }
    Shape 'plus-thin'  'Плюс тонкий'              @{ pip = 1; w = 1; h = 10; gap = -6 }
    Shape 'moving'     'Классика, расходится'     @{ pip = 1; w = 2; h = 8; gap = 0; static = $false }
    Shape 'moving-dot' 'Классика с точкой, расходится' @{ pip = 1; w = 2; h = 8; gap = 0; static = $false; dot = 2; dotOp = 1; db = 1; dbo = 0.7 }
    Shape 'moving-long' 'Длинный, расходится'     @{ pip = 1; w = 2; h = 14; gap = 2; static = $false }
    Shape 'moving-thin' 'Тонкий, расходится'      @{ pip = 1; w = 1; h = 10; gap = 0; static = $false; dot = 2; dotOp = 1 }
)

function Num($x) { ([double]$x).ToString('0.####', $inv) }

function Code($s, $rgb) {
    # A ring is the dot's outline, which the game draws in the outline colour: give it the reticle colour.
    $o = if ($s.ring) { $rgb } else { 0, 0, 0 }
    $v = @($s.gap, $s.w, $s.h, $(if ($s.static) { 'true' } else { 'false' }), $s.pip, $s.pb, $s.pbg, $s.pbo,
           $s.dot, $s.dotOp, $s.db, $s.dbg, $s.dbo, $rgb[0], $rgb[1], $rgb[2], $o[0], $o[1], $o[2])
    ($v | ForEach-Object { if ($_ -is [string]) { $_ } else { Num $_ } }) -join ' '
}

$items = [Collections.Generic.List[object]]::new()
$items.Add([ordered]@{ id = 'game-default'; name = 'Стандартный из игры'; code = '4 2 16 false 0.5 1 0 0.7 4 0.7 2 0 0.7 255 255 255 0 0 0' })
# Colour by colour, so the first screen of the catalog shows every shape rather than one shape in every colour.
foreach ($c in $colors) {
    foreach ($s in $shapes) {
        $items.Add([ordered]@{ id = "$($s.key)-$($c.key)"; name = "$($s.name), $($c.name)"; code = Code $s $c.rgb })
    }
}

New-Item -ItemType Directory -Force crosshairs | Out-Null
$json = [ordered]@{ version = 1; items = $items } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path (Get-Location) 'crosshairs\catalog.json'), $json, [Text.UTF8Encoding]::new($false))
"crosshairs\catalog.json: $($items.Count) crosshairs"
