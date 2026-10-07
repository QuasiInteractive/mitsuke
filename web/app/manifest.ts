import type { MetadataRoute } from "next";

// Installable web app: on Android/desktop Chrome offers "Install"; on iPhone, "Add to Home Screen" is required
// before Safari allows notifications at all.
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Mitsuke 見つけ",
    short_name: "Mitsuke",
    description: "Your wishlist car, found at Japanese auction, with the landed cost and a plain-English read of the auction sheet.",
    start_url: "/",
    scope: "/",
    display: "standalone",
    background_color: "#0b0b0d",
    theme_color: "#0b0b0d",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
