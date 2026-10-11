import type { Metadata, Viewport } from "next";
import { Suspense } from "react";
import Link from "next/link";
import { UserMenu } from "@/components/UserMenu";
import { HankoMark } from "@/components/Brand";
import { Inter, Noto_Sans_JP } from "next/font/google";
import "./globals.css";

const inter = Inter({ variable: "--font-inter", subsets: ["latin"], display: "swap" });
const notoJp = Noto_Sans_JP({ variable: "--font-noto-jp", subsets: ["latin"], weight: ["500", "700"], display: "swap" });

export const metadata: Metadata = {
  title: { default: "Mitsuke 見つけ", template: "%s · Mitsuke" },
  description: "Your wishlist car, found at Japanese auction, with the landed cost in Australia and a plain-English read of the auction sheet.",
  icons: { apple: "/apple-touch-icon.png" },
  appleWebApp: { capable: true, title: "Mitsuke", statusBarStyle: "black-translucent" },
};

export const viewport: Viewport = { themeColor: "#0b0b0d" };

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en-AU" className={`${inter.variable} ${notoJp.variable}`}>
      <body className="min-h-dvh">
        {/* A glass bar that stays put: the brand on the left, the two places people go on the right. */}
        <header className="sticky top-0 z-30 border-b border-hairline bg-ink/70 backdrop-blur-xl">
          <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
            <Link href="/" className="flex items-center gap-3 leading-none">
              <HankoMark />
              <span className="flex flex-col">
                <span className="text-xl font-bold tracking-tight">
                  Mitsuke <span className="font-jp text-accent">見つけ</span>
                </span>
                <span className="mt-1 hidden text-[10px] font-medium tracking-[0.25em] whitespace-nowrap text-faint sm:block">JAPANESE CARS — AUSTRALIAN ROADS</span>
              </span>
            </Link>
            <nav className="flex items-center gap-1 text-sm text-muted">
              <Link href="/" className="rounded-full px-3 py-1.5 transition hover:bg-white/5 hover:text-text">Matches</Link>
              <Suspense fallback={<span className="w-16" />}>
                <UserMenu />
              </Suspense>
            </nav>
          </div>
        </header>
        <main className="mx-auto max-w-6xl px-4 pb-32 sm:px-6">{children}</main>
        <footer className="mt-16 border-t border-hairline">
          <div className="mx-auto flex max-w-6xl flex-col gap-4 px-4 py-10 text-xs text-faint sm:flex-row sm:items-center sm:justify-between sm:px-6">
            <div className="flex items-center gap-3">
              <HankoMark className="size-7 text-sm" />
              <span>Mitsuke by Elyra Software</span>
            </div>
            <p className="max-w-xl sm:text-right">
              Estimates only: verify the car and every cost before bidding. Mitsuke never bids or holds money. Auction data via TheCarApi.
            </p>
          </div>
        </footer>
      </body>
    </html>
  );
}
