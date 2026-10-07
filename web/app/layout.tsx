import type { Metadata, Viewport } from "next";
import { Suspense } from "react";
import Link from "next/link";
import { UserMenu } from "@/components/UserMenu";
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
        <header className="mx-auto flex max-w-6xl items-center justify-between px-4 pt-5 pb-3 sm:px-6">
          <Link href="/" className="group flex flex-col leading-none">
            <span className="text-2xl font-bold tracking-tight">
              Mitsuke <span className="font-jp text-accent">見つけ</span>
            </span>
            <span className="mt-1 text-[9px] font-medium tracking-[0.18em] whitespace-nowrap text-faint sm:text-[10px] sm:tracking-[0.25em]">JAPANESE CARS — AUSTRALIAN ROADS</span>
          </Link>
          <nav className="flex items-center gap-1 text-sm text-muted">
            <Link href="/" className="rounded-full px-3 py-1.5 hover:bg-raised hover:text-text">Matches</Link>
            <Suspense fallback={<span className="w-16" />}>
              <UserMenu />
            </Suspense>
          </nav>
        </header>
        <main className="mx-auto max-w-6xl px-4 pb-32 sm:px-6">{children}</main>
        <footer className="mx-auto max-w-6xl px-4 pb-10 text-xs text-faint sm:px-6">
          Mitsuke by Elyra Software. Estimates only: verify the car and every cost before bidding. Mitsuke never bids or holds money.
        </footer>
      </body>
    </html>
  );
}
