import CoreGraphics
import Foundation

/// Vector artwork for the app icon and the menu bar glyph: a keycap with an AI sparkle.
/// Pure CoreGraphics, shared by the app (menu bar, drawn live at any scale) and
/// scripts/icons (which renders the .icns and PNG/PDF assets).
enum IconArt {

    // MARK: Shapes

    /// Four-point sparkle with concave sides.
    static func sparkle(center c: CGPoint, radius r: CGFloat, waist: CGFloat = 0.26) -> CGPath {
        let tips = [CGPoint(x: c.x, y: c.y + r), CGPoint(x: c.x + r, y: c.y),
                    CGPoint(x: c.x, y: c.y - r), CGPoint(x: c.x - r, y: c.y)]
        let path = CGMutablePath()
        path.move(to: tips[0])
        for i in 0..<4 {
            let a = tips[i], b = tips[(i + 1) % 4]
            let control = CGPoint(x: c.x + (a.x + b.x - 2 * c.x) * waist,
                                  y: c.y + (a.y + b.y - 2 * c.y) * waist)
            path.addQuadCurve(to: b, control: control)
        }
        path.closeSubpath()
        return path
    }

    static func roundedRect(_ rect: CGRect, radius: CGFloat) -> CGPath {
        let r = min(radius, rect.width / 2, rect.height / 2)
        return CGPath(roundedRect: rect, cornerWidth: r, cornerHeight: r, transform: nil)
    }

    /// Superellipse ("squircle") close to the macOS app icon shape.
    static func squircle(in rect: CGRect, exponent n: CGFloat = 5) -> CGPath {
        let path = CGMutablePath()
        let a = rect.width / 2, b = rect.height / 2
        let steps = 720
        for i in 0..<steps {
            let t = CGFloat(i) / CGFloat(steps) * 2 * .pi
            let ct = cos(t), st = sin(t)
            let x = pow(abs(ct), 2 / n) * a * (ct < 0 ? -1 : 1)
            let y = pow(abs(st), 2 / n) * b * (st < 0 ? -1 : 1)
            let p = CGPoint(x: rect.midX + x, y: rect.midY + y)
            i == 0 ? path.move(to: p) : path.addLine(to: p)
        }
        path.closeSubpath()
        return path
    }

    // MARK: Menu bar glyph (monochrome template)

    /// Draws the glyph with the context's current fill color. `rect` is the full canvas;
    /// the y axis points up. `busy` swaps the sparkle for three dots.
    static func drawMenuGlyph(in ctx: CGContext, rect: CGRect, busy: Bool) {
        let s = min(rect.width, rect.height)
        let o = CGPoint(x: rect.midX - s / 2, y: rect.midY - s / 2)
        func R(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> CGRect {
            CGRect(x: o.x + x * s, y: o.y + y * s, width: w * s, height: h * s)
        }
        func P(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: o.x + x * s, y: o.y + y * s) }

        // Keycap outline: thicker bottom wall reads as a key seen slightly from the front.
        let cap = CGMutablePath()
        cap.addPath(roundedRect(R(0.07, 0.07, 0.86, 0.86), radius: 0.24 * s))
        cap.addPath(roundedRect(R(0.155, 0.22, 0.69, 0.625), radius: 0.155 * s))
        ctx.addPath(cap)
        ctx.fillPath(using: .evenOdd)

        if busy {
            for x in [0.34, 0.5, 0.66] as [CGFloat] {
                ctx.addEllipse(in: CGRect(x: o.x + x * s - 0.06 * s, y: o.y + 0.5325 * s - 0.06 * s,
                                          width: 0.12 * s, height: 0.12 * s))
            }
            ctx.fillPath()
        } else {
            ctx.addPath(sparkle(center: P(0.5, 0.5325), radius: 0.235 * s))
            ctx.fillPath()
        }
    }

    // MARK: App icon (color)

    private static func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
        CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
                blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
    }

    private static func gradient(_ colors: [CGColor], _ locations: [CGFloat]) -> CGGradient {
        CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors as CFArray,
                   locations: locations)!
    }

    /// Draws the full-color icon on a square canvas of `size` points (1024-point grid,
    /// body inset per Apple's macOS icon template).
    static func drawAppIcon(in ctx: CGContext, size: CGFloat) {
        ctx.saveGState()
        ctx.scaleBy(x: size / 1024, y: size / 1024)

        // Body
        let body = squircle(in: CGRect(x: 100, y: 100, width: 824, height: 824))
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -10), blur: 24, color: color(0x000000, 0.28))
        ctx.addPath(body)
        ctx.setFillColor(color(0x4A35E0))
        ctx.fillPath()
        ctx.restoreGState()

        ctx.saveGState()
        ctx.addPath(body)
        ctx.clip()
        ctx.drawLinearGradient(gradient([color(0x8F6BFF), color(0x5B3DF0), color(0x2F2AB8)], [0, 0.55, 1]),
                               start: CGPoint(x: 300, y: 924), end: CGPoint(x: 724, y: 100), options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        ctx.drawRadialGradient(gradient([color(0xFFFFFF, 0.28), color(0xFFFFFF, 0)], [0, 1]),
                               startCenter: CGPoint(x: 330, y: 860), startRadius: 0,
                               endCenter: CGPoint(x: 330, y: 860), endRadius: 560, options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        ctx.restoreGState()

        // Keycap base (front and side walls) with a soft shadow
        let base = roundedRect(CGRect(x: 252, y: 226, width: 520, height: 530), radius: 118)
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -30), blur: 50, color: color(0x14104F, 0.45))
        ctx.addPath(base)
        ctx.setFillColor(color(0xB9BDE6))
        ctx.fillPath()
        ctx.restoreGState()
        ctx.saveGState()
        ctx.addPath(base)
        ctx.clip()
        ctx.drawLinearGradient(gradient([color(0xDADDF7), color(0xA4A8DA)], [0, 1]),
                               start: CGPoint(x: 512, y: 756), end: CGPoint(x: 512, y: 226), options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        ctx.restoreGState()

        // Keycap top surface
        let top = roundedRect(CGRect(x: 290, y: 318, width: 444, height: 410), radius: 86)
        ctx.saveGState()
        ctx.addPath(top)
        ctx.clip()
        ctx.drawLinearGradient(gradient([color(0xFFFFFF), color(0xEEF0FC)], [0, 1]),
                               start: CGPoint(x: 512, y: 728), end: CGPoint(x: 512, y: 318), options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        ctx.restoreGState()

        // Sparkles
        let sparkleGradient = gradient([color(0x7C4DFF), color(0xC04CF0), color(0xFF5FA2)], [0, 0.55, 1])
        for (center, radius) in [(CGPoint(x: 498, y: 512), CGFloat(146)), (CGPoint(x: 652, y: 650), CGFloat(50))] {
            ctx.saveGState()
            ctx.addPath(sparkle(center: center, radius: radius))
            ctx.clip()
            ctx.drawLinearGradient(sparkleGradient,
                                   start: CGPoint(x: center.x - radius, y: center.y + radius),
                                   end: CGPoint(x: center.x + radius, y: center.y - radius), options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
            ctx.restoreGState()
        }

        ctx.restoreGState()
    }
}
