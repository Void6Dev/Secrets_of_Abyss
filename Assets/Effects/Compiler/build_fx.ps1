# Сборка шейдера: .fx -> .fxo (fxc, fx_2_0) -> .xnb (обёртка XNA Effect, её грузит tML).
# Результат кладётся рядом с исходником. Пример: .\Assets\Effects\Compiler\build_fx.ps1 .\Assets\Effects\TideUnderwater.fx
param([Parameter(Mandatory)][string]$Fx)
$ErrorActionPreference = 'Stop'
$dir = Split-Path (Resolve-Path $Fx).Path -Parent; $name = [IO.Path]::GetFileNameWithoutExtension($Fx)
$Fx = (Resolve-Path $Fx).Path
$fxc = Join-Path $PSScriptRoot 'fxc.exe'
& $fxc /nologo /T fx_2_0 /Fo "$dir\$name.fxo" $Fx
if ($LASTEXITCODE -ne 0) { throw "fxc failed" }
$code = [IO.File]::ReadAllBytes("$dir\$name.fxo")
$reader = [Text.Encoding]::ASCII.GetBytes('Microsoft.Xna.Framework.Content.EffectReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553')
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
function W7([int]$v){ while($v -ge 0x80){ $w.Write([byte](($v -band 0x7f) -bor 0x80)); $v = $v -shr 7 }; $w.Write([byte]$v) }
W7 1; W7 $reader.Length; $w.Write($reader); $w.Write([int]0); W7 0; W7 1; $w.Write([uint32]$code.Length); $w.Write($code); $w.Flush()
$body = $ms.ToArray()
$out = New-Object IO.MemoryStream; $o = New-Object IO.BinaryWriter($out)
$o.Write([Text.Encoding]::ASCII.GetBytes('XNBw')); $o.Write([byte]5); $o.Write([byte]0); $o.Write([uint32](10 + $body.Length)); $o.Write($body); $o.Flush()
[IO.File]::WriteAllBytes("$dir\$name.xnb", $out.ToArray())
"built $name.xnb ($($out.Length) bytes)"

