# Draws the Windows VD Helper logo (three stacked desktops) and writes:
#   Assets\WindowsVDHelper.ico   the app icon (16..256 px)
#   Assets\logo.png              256 px, for the README
#   CommandPalette\CmdPalVirtualDesktops\Assets\*.png   the Command Palette extension's icons
# Usage: powershell -ExecutionPolicy Bypass -File Assets\make-logo.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class Logo {
	static GraphicsPath Rounded(RectangleF r, float radius) {
		var p = new GraphicsPath();
		var d = radius * 2;
		p.AddArc(r.X, r.Y, d, d, 180, 90);
		p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
		p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
		p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
		p.CloseFigure();
		return p;
	}

	// One desktop card: a rounded rectangle with a darker "title bar" band
	static void Card(Graphics g, RectangleF r, float radius, Color top, Color bottom, Color bar, bool detail, float outline) {
		using (var path = Rounded(r, radius)) {
			using (var brush = new LinearGradientBrush(new PointF(r.X, r.Y), new PointF(r.X, r.Bottom + 1), top, bottom)) g.FillPath(brush, path);
			if (detail) {
				var state = g.Save();
				g.SetClip(path);
				using (var b = new SolidBrush(bar)) g.FillRectangle(b, r.X, r.Y, r.Width, r.Height * 0.2f);
				g.Restore(state);
			}
			if (outline > 0) using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), outline)) g.DrawPath(pen, path);
		}
	}

	// Draws the logo into a size x size square
	public static void Draw(Graphics g, float x0, float y0, float size) {
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		var s = size;
		var small = s < 28;
		var radius = s * (small ? 0.12f : 0.09f);
		var outline = small ? 0 : Math.Max(1f, s * 0.018f);
		var w = s * (small ? 0.70f : 0.62f);
		var h = s * (small ? 0.54f : 0.47f);
		var step = s * (small ? 0.22f : 0.14f);
		// back to front, stepping down and to the left
		var cards = small ? 2 : 3;
		for (var i = 0; i < cards; i++) {
			var x = x0 + s * (small ? 0.97f : 0.95f) - w - i * step;
			var y = y0 + s * (small ? 0.12f : 0.125f) + i * step; // (vertically centered)
			Color top, bottom, bar;
			if (i == cards - 1) { top = Color.FromArgb(40, 142, 234); bottom = Color.FromArgb(0, 95, 184); bar = Color.FromArgb(0, 70, 140); }
			else if (i == cards - 2) { top = Color.FromArgb(110, 180, 245); bottom = Color.FromArgb(58, 140, 225); bar = Color.FromArgb(40, 110, 200); }
			else { top = Color.FromArgb(185, 220, 252); bottom = Color.FromArgb(140, 196, 245); bar = Color.FromArgb(110, 170, 235); }
			Card(g, new RectangleF(x, y, w, h), radius, top, bottom, bar, !small, outline);
		}
		if (!small) {
			// three dots in the front card's title bar (window buttons)
			var fx = x0 + s * 0.95f - w - (cards - 1) * step;
			var fy = y0 + s * 0.125f + (cards - 1) * step;
			var dot = h * 0.07f;
			using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
				for (var d = 0; d < 3; d++) g.FillEllipse(b, fx + w - (d + 1) * dot * 2.2f - dot * 0.6f, fy + h * 0.1f - dot / 2, dot, dot);
		}
	}

	public static Bitmap Render(int width, int height, float logoSize) {
		var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
		using (var g = Graphics.FromImage(bmp)) {
			g.Clear(Color.Transparent);
			Draw(g, (width - logoSize) / 2f, (height - logoSize) / 2f, logoSize);
		}
		return bmp;
	}

	// A .ico with 32 bit images (PNG for 256 px, BMP for the rest: works everywhere)
	public static void WriteIco(string path, int[] sizes) {
		var images = new List<byte[]>();
		foreach (var size in sizes) {
			using (var bmp = Render(size, size, size)) {
				if (size >= 256) {
					using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
					continue;
				}
				using (var ms = new MemoryStream())
				using (var w = new BinaryWriter(ms)) {
					var maskStride = ((size + 31) / 32) * 4;
					w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
					w.Write(0); w.Write(size * size * 4 + maskStride * size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
					for (var y = size - 1; y >= 0; y--)
						for (var x = 0; x < size; x++) {
							var c = bmp.GetPixel(x, y);
							w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
						}
					w.Write(new byte[maskStride * size]);
					images.Add(ms.ToArray());
				}
			}
		}
		using (var fs = File.Create(path))
		using (var w = new BinaryWriter(fs)) {
			w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
			var offset = 6 + 16 * sizes.Length;
			for (var i = 0; i < sizes.Length; i++) {
				var size = sizes[i];
				w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
				w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
				w.Write(images[i].Length); w.Write(offset);
				offset += images[i].Length;
			}
			foreach (var image in images) w.Write(image);
		}
	}

	public static void SavePng(string path, int width, int height, float logoSize) {
		using (var bmp = Render(width, height, logoSize)) bmp.Save(path, ImageFormat.Png);
	}
}
"@

$assets = $PSScriptRoot
[Logo]::WriteIco((Join-Path $assets 'WindowsVDHelper.ico'), [int[]](16, 20, 24, 32, 40, 48, 64, 128, 256))
[Logo]::SavePng((Join-Path $assets 'logo.png'), 256, 256, 256)

$cmdpal = Join-Path $root 'CommandPalette\CmdPalVirtualDesktops\Assets'
if (Test-Path (Split-Path $cmdpal -Parent)) {
	New-Item -ItemType Directory -Force $cmdpal | Out-Null
	[Logo]::SavePng((Join-Path $cmdpal 'Icon.png'), 256, 256, 256)
	[Logo]::SavePng((Join-Path $cmdpal 'StoreLogo.png'), 100, 100, 100)
	[Logo]::SavePng((Join-Path $cmdpal 'Square44x44Logo.png'), 88, 88, 88)
	[Logo]::SavePng((Join-Path $cmdpal 'Square150x150Logo.png'), 300, 300, 240)
	[Logo]::SavePng((Join-Path $cmdpal 'Wide310x150Logo.png'), 620, 300, 240)
	[Logo]::SavePng((Join-Path $cmdpal 'SplashScreen.png'), 1240, 600, 400)
}
Write-Host "Logo written to $assets (and the Command Palette extension's assets)"
