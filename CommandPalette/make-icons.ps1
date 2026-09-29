# Generates the extension's icons (two overlapping desktops) into CmdPalVirtualDesktops\Assets
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot 'CmdPalVirtualDesktops\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function Draw-Icon([string]$file, [int]$w, [int]$h, [bool]$background) {
	$bmp = New-Object System.Drawing.Bitmap $w, $h
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.SmoothingMode = 'AntiAlias'
	$g.Clear([System.Drawing.Color]::Transparent)
	$s = [Math]::Min($w, $h)
	$ox = ($w - $s) / 2; $oy = ($h - $s) / 2
	if ($background) {
		$path = New-Object System.Drawing.Drawing2D.GraphicsPath
		$r = $s * 0.22; $x = $ox + $s * 0.04; $y = $oy + $s * 0.04; $d = $s * 0.92
		$path.AddArc($x, $y, $r, $r, 180, 90); $path.AddArc($x + $d - $r, $y, $r, $r, 270, 90)
		$path.AddArc($x + $d - $r, $y + $d - $r, $r, $r, 0, 90); $path.AddArc($x, $y + $d - $r, $r, $r, 90, 90); $path.CloseFigure()
		$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))), $path)
	}
	$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, $s * 0.06))
	$pen.LineJoin = 'Round'
	# back desktop
	$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 255, 255, 255))), $ox + $s * 0.34, $oy + $s * 0.24, $s * 0.42, $s * 0.30)
	$g.DrawRectangle($pen, $ox + $s * 0.34, $oy + $s * 0.24, $s * 0.42, $s * 0.30)
	# front desktop
	$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))), $ox + $s * 0.22, $oy + $s * 0.40, $s * 0.42, $s * 0.30)
	$g.DrawRectangle($pen, $ox + $s * 0.22, $oy + $s * 0.40, $s * 0.42, $s * 0.30)
	$g.Dispose()
	$bmp.Save((Join-Path $assets $file), [System.Drawing.Imaging.ImageFormat]::Png)
	$bmp.Dispose()
}

Draw-Icon 'Icon.png' 256 256 $true
Draw-Icon 'StoreLogo.png' 100 100 $true
Draw-Icon 'Square44x44Logo.png' 88 88 $true
Draw-Icon 'Square150x150Logo.png' 300 300 $true
Draw-Icon 'Wide310x150Logo.png' 620 300 $true
Draw-Icon 'SplashScreen.png' 1240 600 $true
Write-Host "Icons written to $assets"
