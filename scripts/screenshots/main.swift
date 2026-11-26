// Composes 2880x1800 Mac App Store screenshots from raw window captures.
// Usage: compose-screenshots <raw-dir> <out-dir>   (built by scripts/screenshots.sh)
import AppKit

let args = CommandLine.arguments
let rawDir = URL(fileURLWithPath: args[1], isDirectory: true)
let outDir = URL(fileURLWithPath: args[2], isDirectory: true)
try FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)

let captions: [(file: String, title: String, subtitle: String)] = [
    ("1-improve-in-english", "Better English with one shortcut",
     "Press ⌥⌘E in any app to fix or translate what you typed."),
    ("2-fix-in-your-language", "Fix text in any language",
     "⌥⇧⌘O corrects spelling and grammar but keeps your words."),
    ("3-ai-providers", "Private by default",
     "Apple Intelligence on your Mac, or your ChatGPT, Claude or Gemini account."),
]

let W: CGFloat = 2880, H: CGFloat = 1800

func color(_ hex: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: a)
}

for caption in captions {
    let rawURL = rawDir.appendingPathComponent(caption.file + ".png")
    guard let window = NSImage(contentsOf: rawURL),
          let windowCG = window.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
        print("missing \(rawURL.path)"); continue
    }
    let ctx = CGContext(data: nil, width: Int(W), height: Int(H), bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!

    // Background
    let bg = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                        colors: [color(0x8F6BFF), color(0x5B3DF0), color(0x2F2AB8)] as CFArray,
                        locations: [0, 0.5, 1])!
    ctx.drawLinearGradient(bg, start: CGPoint(x: 0, y: H), end: CGPoint(x: W, y: 0), options: [])
    let glow = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                          colors: [color(0xFFFFFF, 0.22), color(0xFFFFFF, 0)] as CFArray, locations: [0, 1])!
    ctx.drawRadialGradient(glow, startCenter: CGPoint(x: W * 0.3, y: H), startRadius: 0,
                           endCenter: CGPoint(x: W * 0.3, y: H), endRadius: 1600, options: [])

    // Icon
    ctx.saveGState()
    ctx.translateBy(x: W / 2 - 75, y: H - 70 - 150)
    IconArt.drawAppIcon(in: ctx, size: 150)
    ctx.restoreGState()

    // Text (AppKit drawing into the same context)
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
    let center = NSMutableParagraphStyle()
    center.alignment = .center
    let title = NSAttributedString(string: caption.title, attributes: [
        .font: NSFont.systemFont(ofSize: 104, weight: .bold),
        .foregroundColor: NSColor.white, .paragraphStyle: center])
    title.draw(in: NSRect(x: 0, y: H - 360, width: W, height: 130))
    let subtitle = NSAttributedString(string: caption.subtitle, attributes: [
        .font: NSFont.systemFont(ofSize: 54, weight: .medium),
        .foregroundColor: NSColor.white.withAlphaComponent(0.85), .paragraphStyle: center])
    subtitle.draw(in: NSRect(x: 0, y: H - 450, width: W, height: 80))
    NSGraphicsContext.restoreGraphicsState()

    // Window with rounded corners and shadow
    let maxW: CGFloat = 2400, maxH: CGFloat = H - 520 - 80
    let scale = min(maxW / CGFloat(windowCG.width), maxH / CGFloat(windowCG.height))
    let w = CGFloat(windowCG.width) * scale, h = CGFloat(windowCG.height) * scale
    let rect = CGRect(x: (W - w) / 2, y: 80, width: w, height: h)
    let path = CGPath(roundedRect: rect, cornerWidth: 22, cornerHeight: 22, transform: nil)
    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -24), blur: 70, color: color(0x0B0830, 0.55))
    ctx.addPath(path)
    ctx.setFillColor(color(0xFFFFFF))
    ctx.fillPath()
    ctx.restoreGState()
    ctx.saveGState()
    ctx.addPath(path)
    ctx.clip()
    ctx.interpolationQuality = .high
    ctx.draw(windowCG, in: rect)
    ctx.restoreGState()

    let out = outDir.appendingPathComponent(caption.file + ".png")
    let data = NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
    try data.write(to: out)
    print("wrote \(out.path)")
}
