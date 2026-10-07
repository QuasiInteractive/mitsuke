import { ImageResponse } from "next/og";

/**
 * Mitsuke's app icon, drawn rather than stored: a red target dot ("found it") on the ink background. Full-bleed so
 * Android can crop it to any shape (maskable); the mark stays inside the safe centre circle.
 */
export function appIcon(size: number) {
  const dot = Math.round(size * 0.44);
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#0b0b0d" }}>
        <div
          style={{
            width: dot,
            height: dot,
            borderRadius: "50%",
            background: "#d7263d",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            color: "#f4f4f5",
            fontSize: Math.round(dot * 0.58),
            fontWeight: 700,
          }}
        >
          M
        </div>
      </div>
    ),
    { width: size, height: size },
  );
}
