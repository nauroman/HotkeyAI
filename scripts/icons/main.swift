// Renders the app icon and menu bar glyph from Sources/HotkeyAI/IconArt.swift.
// Run via scripts/make-icons.sh.
import AppKit

let out = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "build/icons")
let fm = FileManager.default
try? fm.removeItem(at: out)
try fm.createDirectory(at: out.appendingPathComponent("AppIcon.iconset"), withIntermediateDirectories: true)

func bitmap(width: Int, height: Int, draw: (CGContext) -> Void) -> Data {
    let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    draw(ctx)
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}

func write(_ data: Data, _ name: String) throws {
    try data.write(to: out.appendingPathComponent(name))
}

// App icon at every size the .icns needs (drawn from vectors at each size, not downscaled).
for base in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let px = base * scale
        let name = "AppIcon.iconset/icon_\(base)x\(base)\(scale == 2 ? "@2x" : "").png"
        try write(bitmap(width: px, height: px) { IconArt.drawAppIcon(in: $0, size: CGFloat(px)) }, name)
    }
}
try write(bitmap(width: 1024, height: 1024) { IconArt.drawAppIcon(in: $0, size: 1024) }, "AppIcon-1024.png")

// Monochrome menu bar glyph: black on transparent, used as a template image (18 pt).
for (scale, suffix) in [(1, ""), (2, "@2x"), (3, "@3x")] {
    let px = 18 * scale
    try write(bitmap(width: px, height: px) { ctx in
        ctx.setFillColor(CGColor(gray: 0, alpha: 1))
        IconArt.drawMenuGlyph(in: ctx, rect: CGRect(x: 0, y: 0, width: px, height: px), busy: false)
    }, "MenuBarIconTemplate\(suffix).png")
}
// Vector version for designers / an Xcode asset catalog ("Render As: Template Image").
var box = CGRect(x: 0, y: 0, width: 18, height: 18)
let pdf = NSMutableData()
let pdfCtx = CGContext(consumer: CGDataConsumer(data: pdf as CFMutableData)!, mediaBox: &box, nil)!
pdfCtx.beginPDFPage(nil)
pdfCtx.setFillColor(CGColor(gray: 0, alpha: 1))
IconArt.drawMenuGlyph(in: pdfCtx, rect: box, busy: false)
pdfCtx.endPDFPage()
pdfCtx.closePDF()
try (pdf as Data).write(to: out.appendingPathComponent("MenuBarIconTemplate.pdf"))

// Preview sheet: the icon, plus the glyph on light and dark menu bars (idle and busy).
let preview = bitmap(width: 1200, height: 640) { ctx in
    ctx.setFillColor(CGColor(srgbRed: 0.95, green: 0.95, blue: 0.97, alpha: 1))
    ctx.fill(CGRect(x: 0, y: 0, width: 1200, height: 640))
    ctx.saveGState()
    ctx.translateBy(x: 40, y: 60)
    IconArt.drawAppIcon(in: ctx, size: 520)
    ctx.restoreGState()
    // Small sizes, as in Finder lists and the Dock at minimum size
    var x: CGFloat = 600
    for size in [128, 64, 32, 16] as [CGFloat] {
        ctx.saveGState()
        ctx.translateBy(x: x, y: 470 - size / 2)
        IconArt.drawAppIcon(in: ctx, size: size)
        ctx.restoreGState()
        x += size + 40
    }
    // Menu bars: light and dark, glyph at 1x scale ×3 for visibility
    for (i, dark) in [false, true].enumerated() {
        let y: CGFloat = 230 - CGFloat(i) * 150
        ctx.setFillColor(dark ? CGColor(srgbRed: 0.13, green: 0.13, blue: 0.15, alpha: 1)
                              : CGColor(srgbRed: 0.98, green: 0.98, blue: 0.99, alpha: 1))
        ctx.fill(CGRect(x: 600, y: y, width: 560, height: 96))
        ctx.setFillColor(dark ? CGColor(gray: 1, alpha: 0.92) : CGColor(gray: 0, alpha: 0.85))
        IconArt.drawMenuGlyph(in: ctx, rect: CGRect(x: 640, y: y + 21, width: 54, height: 54), busy: false)
        IconArt.drawMenuGlyph(in: ctx, rect: CGRect(x: 740, y: y + 21, width: 54, height: 54), busy: true)
        // Actual-size glyphs
        IconArt.drawMenuGlyph(in: ctx, rect: CGRect(x: 860, y: y + 39, width: 18, height: 18), busy: false)
        IconArt.drawMenuGlyph(in: ctx, rect: CGRect(x: 900, y: y + 39, width: 18, height: 18), busy: true)
    }
}
try write(preview, "preview.png")
print("Wrote icons to \(out.path)")
