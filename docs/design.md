# Web app design notes

Mitsuke's front end is a **web app** (Next.js, hosted like Kensa-ya), not a native app. It's responsive: the
phone-browser layout is the primary target, and desktop gets a two-column layout (gallery left, price and
detail cards right). Installable as a PWA for push notifications, but nothing requires installing.

## Visual direction (from the agreed mockup, 2026-10-07)

- Dark theme: near-black charcoal background, white type, one accent colour (deep crimson, hanko-seal red).
- Rounded, softly glassy cards with generous spacing. Small Japanese accents (見つけ) used sparingly.
- Premium-but-calm: a luxury watch app crossed with a modern JDM magazine.

## Lot page, top to bottom

1. **Hero: the car's real auction photo** with the gallery strip (thumbnails, `1/12` counter). Not a staged
   render: auction photos are plain, so the page frames them well rather than pretending they're studio shots.
2. Title (`1990 Nissan Skyline GT-R (BNR32)`) and chips: mileage, grade, transmission, steering.
3. **Est. landed (A$)** as the hero number, with the opening bid in ¥ and the auction house underneath.
   Deal score badge alongside.
4. Auction countdown with the date in Japan.
5. **Condition**: car outline diagram with markers + plain-English faults from the decoded sheet; interior grade.
6. **Cost breakdown** accordion: car, auction & export fees, shipping, duty & GST, compliance → total.
7. **History**: earlier auction appearances with a small price line.
8. Attribution + disclaimer footer (always visible, never hidden behind a tap).
9. Sticky action bar: **I want to bid** (primary, crimson), Keep watching, Not for me.

## Wording rules

- Say "Seen at auction 5 times since 4 Sep", not "relisted unsold": we don't know a car didn't sell.
- Japanese prices are **opening bids**; label them that way everywhere.
- Every figure that's an estimate says so.
