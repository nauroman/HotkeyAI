// Frames the real window captures from the CI self-test (HotkeyAI-tests artifact) on 1920×1080
// canvases for the Microsoft Store listing. Run windows/store/make-screenshots.sh on a Mac.
import AppKit

let input = URL(fileURLWithPath: CommandLine.arguments[1])   // folder with settings-*.png, welcome.png
let output = URL(fileURLWithPath: CommandLine.arguments[2])
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

let shots: [(file: String, caption: String, name: String)] = [
    ("welcome.png", "Press Win+Alt+E in any text field", "1-welcome.png"),
    ("settings-actions.png", "Your own shortcuts and prompts", "2-actions.png"),
    ("settings-providers.png", "ChatGPT, OpenAI, Claude or Gemini", "3-providers.png"),
]

func color(_ hex: UInt32) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
}

for shot in shots {
    guard let image = NSImage(contentsOf: input.appendingPathComponent(shot.file)),
          let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
        print("missing \(shot.file)"); continue
    }
    let W = 1920, H = 1080
    let ctx = CGContext(data: nil, width: W, height: H, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    // Background: the app icon's violet gradient.
    let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB),
                              colors: [color(0x8F6BFF), color(0x5B3DF0), color(0x2F2AB8)] as CFArray,
                              locations: [0, 0.55, 1])!
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: H), end: CGPoint(x: W, y: 0), options: [])

    // Caption at the top.
    let ns = NSGraphicsContext(cgContext: ctx, flipped: false)
    NSGraphicsContext.current = ns
    let style = NSMutableParagraphStyle()
    style.alignment = .center
    let attributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: 64, weight: .semibold),
        .foregroundColor: NSColor.white,
        .paragraphStyle: style,
    ]
    (shot.caption as NSString).draw(in: CGRect(x: 0, y: CGFloat(H) - 150, width: CGFloat(W), height: 90),
                                    withAttributes: attributes)
    NSGraphicsContext.current = nil

    // The window capture, scaled to fit below the caption, on a white card with a shadow.
    let maxW = CGFloat(W) - 240, maxH = CGFloat(H) - 230
    let scale = min(maxW / CGFloat(cg.width), maxH / CGFloat(cg.height), 1)
    let w = CGFloat(cg.width) * scale, h = CGFloat(cg.height) * scale
    let rect = CGRect(x: (CGFloat(W) - w) / 2, y: 60, width: w, height: h)
    let card = CGPath(roundedRect: rect.insetBy(dx: -2, dy: -2), cornerWidth: 18, cornerHeight: 18, transform: nil)
    ctx.saveGState()
    ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 40, color: CGColor(gray: 0, alpha: 0.35))
    ctx.addPath(card)
    ctx.setFillColor(color(0xF9F9F9))
    ctx.fillPath()
    ctx.restoreGState()
    ctx.saveGState()
    ctx.addPath(CGPath(roundedRect: rect, cornerWidth: 16, cornerHeight: 16, transform: nil))
    ctx.clip()
    ctx.interpolationQuality = .high
    ctx.draw(cg, in: rect)
    ctx.restoreGState()

    let data = NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
    try data.write(to: output.appendingPathComponent(shot.name))
    print("wrote \(shot.name)")
}
