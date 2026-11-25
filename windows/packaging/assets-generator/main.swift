// Renders the Windows icon (.ico) and the MSIX logo images from Sources/HotkeyAI/IconArt.swift,
// so the Windows and Mac apps share one drawing. Run windows/packaging/make-assets.sh on a Mac.
import AppKit

let args = CommandLine.arguments
let icoPath = URL(fileURLWithPath: args[1])
let msixDir = URL(fileURLWithPath: args[2])
let storeDir = URL(fileURLWithPath: args[3])
let fm = FileManager.default
try? fm.removeItem(at: msixDir)
try fm.createDirectory(at: msixDir, withIntermediateDirectories: true)
try fm.createDirectory(at: storeDir, withIntermediateDirectories: true)

/// The Mac icon keeps Apple's margin around its body (80% of the canvas). Windows icons use
/// almost the whole square, so the same drawing is enlarged to fill `fill` of it.
func drawWindowsIcon(_ ctx: CGContext, size: CGFloat, fill: CGFloat = 0.92) {
    let scale = fill * 1024 / 824
    ctx.saveGState()
    ctx.translateBy(x: size / 2, y: size / 2)
    ctx.scaleBy(x: scale, y: scale)
    ctx.translateBy(x: -size / 2, y: -size / 2)
    IconArt.drawAppIcon(in: ctx, size: size)
    ctx.restoreGState()
}

/// PNG of `width`×`height` pixels with the icon centered at `iconSize` pixels; transparent elsewhere.
func png(width: Int, height: Int, iconSize: Int) -> Data {
    let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.translateBy(x: CGFloat(width - iconSize) / 2, y: CGFloat(height - iconSize) / 2)
    drawWindowsIcon(ctx, size: CGFloat(iconSize))
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}

func write(_ name: String, _ width: Int, _ height: Int, icon: Int) throws {
    try png(width: width, height: height, iconSize: icon).write(to: msixDir.appendingPathComponent(name))
}

// Application icon: PNG-compressed entries (supported since Windows Vista).
let icoSizes = [16, 20, 24, 32, 40, 48, 64, 256]
let images = icoSizes.map { png(width: $0, height: $0, iconSize: $0) }
var ico = Data([0, 0, 1, 0, UInt8(icoSizes.count), 0])
var offset = 6 + 16 * icoSizes.count
func le16(_ v: Int) -> [UInt8] { [UInt8(v & 0xFF), UInt8((v >> 8) & 0xFF)] }
func le32(_ v: Int) -> [UInt8] { le16(v & 0xFFFF) + le16(v >> 16) }
for (size, image) in zip(icoSizes, images) {
    let dim = UInt8(size >= 256 ? 0 : size)
    ico.append(contentsOf: [dim, dim, 0, 0] + le16(1) + le16(32) + le32(image.count) + le32(offset))
    offset += image.count
}
for image in images { ico.append(image) }
try ico.write(to: icoPath)

// MSIX logos. Scale variants let Windows pick a sharp image for each display scale;
// targetsize variants are used for the taskbar, Start and Explorer.
let scales = [100, 125, 150, 200, 400]
for s in scales {
    let px = { (base: Int) in Int((Double(base) * Double(s) / 100).rounded()) }
    try write("Square44x44Logo.scale-\(s).png", px(44), px(44), icon: px(44))
    try write("Square150x150Logo.scale-\(s).png", px(150), px(150), icon: px(100))
    try write("Wide310x150Logo.scale-\(s).png", px(310), px(150), icon: px(100))
    try write("Square310x310Logo.scale-\(s).png", px(310), px(310), icon: px(200))
    try write("StoreLogo.scale-\(s).png", px(50), px(50), icon: px(50))
}
for t in [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256] {
    try write("Square44x44Logo.targetsize-\(t).png", t, t, icon: t)
    try write("Square44x44Logo.targetsize-\(t)_altform-unplated.png", t, t, icon: t)
    try write("Square44x44Logo.targetsize-\(t)_altform-lightunplated.png", t, t, icon: t)
}

// Partner Center listing images.
try png(width: 300, height: 300, iconSize: 300).write(to: storeDir.appendingPathComponent("StoreLogo-300x300.png"))
try png(width: 1080, height: 1080, iconSize: 1080).write(to: storeDir.appendingPathComponent("BoxArt-1080x1080.png"))

// 9:16 poster art (the main Store logo on Windows 10/11): the icon and the name on the icon's gradient.
func poster(width W: Int, height H: Int) -> Data {
    let ctx = CGContext(data: nil, width: W, height: H, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    func c(_ hex: UInt32) -> CGColor {
        CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
                blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
    }
    let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                              colors: [c(0x8F6BFF), c(0x5B3DF0), c(0x2F2AB8)] as CFArray, locations: [0, 0.55, 1])!
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: H), end: CGPoint(x: W, y: 0), options: [])
    let icon = CGFloat(W) * 0.62
    ctx.saveGState()
    ctx.translateBy(x: (CGFloat(W) - icon) / 2, y: CGFloat(H) * 0.42)
    drawWindowsIcon(ctx, size: icon)
    ctx.restoreGState()
    NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
    let style = NSMutableParagraphStyle()
    style.alignment = .center
    ("HotkeyAI" as NSString).draw(in: CGRect(x: 0, y: CGFloat(H) * 0.25, width: CGFloat(W), height: CGFloat(W) * 0.2),
        withAttributes: [.font: NSFont.systemFont(ofSize: CGFloat(W) * 0.14, weight: .bold),
                         .foregroundColor: NSColor.white, .paragraphStyle: style])
    ("Rewrite text with one shortcut" as NSString).draw(
        in: CGRect(x: 0, y: CGFloat(H) * 0.19, width: CGFloat(W), height: CGFloat(W) * 0.1),
        withAttributes: [.font: NSFont.systemFont(ofSize: CGFloat(W) * 0.055, weight: .medium),
                         .foregroundColor: NSColor(white: 1, alpha: 0.85), .paragraphStyle: style])
    NSGraphicsContext.current = nil
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}
try poster(width: 1440, height: 2160).write(to: storeDir.appendingPathComponent("PosterArt-1440x2160.png"))
print("Wrote \(icoPath.path), \(msixDir.path) and \(storeDir.path)")
