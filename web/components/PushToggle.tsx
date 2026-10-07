"use client";

import { useEffect, useState } from "react";
import { removePushSubscription, savePushSubscription, sendTestPush, type BrowserSubscription } from "@/app/actions";

const PUBLIC_KEY = process.env.NEXT_PUBLIC_VAPID_PUBLIC_KEY ?? "";

type State = "loading" | "unsupported" | "ios-install" | "blocked" | "off" | "on" | "working";

function keyBytes(base64url: string) {
  const base64 = (base64url + "=".repeat((4 - (base64url.length % 4)) % 4)).replace(/-/g, "+").replace(/_/g, "/");
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

async function registration() {
  return navigator.serviceWorker.register("/sw.js", { scope: "/", updateViaCache: "none" });
}

/** "Notifications on this device": one switch per phone/laptop, saved against the signed-in person. */
export function PushToggle() {
  const [state, setState] = useState<State>("loading");
  const [note, setNote] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const supported = PUBLIC_KEY && "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
      let next: State;
      if (!supported) {
        // iPhones only allow web notifications once the site is added to the home screen.
        const ios = /iphone|ipad|ipod/i.test(navigator.userAgent);
        const standalone = window.matchMedia("(display-mode: standalone)").matches;
        next = PUBLIC_KEY && ios && !standalone ? "ios-install" : "unsupported";
      } else if (Notification.permission === "denied") {
        next = "blocked";
      } else {
        const sub = await (await registration()).pushManager.getSubscription();
        if (sub) await savePushSubscription(sub.toJSON() as BrowserSubscription); // keep the server in step
        next = sub ? "on" : "off";
      }
      if (!cancelled) setState(next);
    })().catch(() => !cancelled && setState("unsupported"));
    return () => {
      cancelled = true;
    };
  }, []);

  async function turnOn() {
    setState("working");
    setNote(null);
    try {
      if ((await Notification.requestPermission()) !== "granted") {
        setState("blocked");
        return;
      }
      const reg = await registration();
      const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(PUBLIC_KEY) });
      if (!(await savePushSubscription(sub.toJSON() as BrowserSubscription))) {
        await sub.unsubscribe();
        throw new Error("save failed");
      }
      setState("on");
    } catch {
      setState("off");
      setNote("Couldn't turn notifications on. Try again.");
    }
  }

  async function turnOff() {
    setState("working");
    setNote(null);
    const sub = await (await registration()).pushManager.getSubscription();
    if (sub) {
      await removePushSubscription(sub.endpoint);
      await sub.unsubscribe();
    }
    setState("off");
  }

  async function test() {
    setNote("Sending…");
    const result = await sendTestPush();
    setNote("error" in result ? result.error : result.delivered > 0 ? "Sent. It should pop up in a few seconds." : "No devices to send to. Turn it off and on again.");
  }

  if (state === "loading" || state === "unsupported") return null;

  return (
    <div className="card flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <p className="font-semibold">Notifications on this device</p>
        <p className="text-sm text-muted">
          {state === "on" && "On. Mitsuke pings this device the moment a car matches, alongside the email."}
          {(state === "off" || state === "working") && "Get a ping the moment a car matches. Lots close fast, so this beats checking your email."}
          {state === "blocked" && "Blocked in your browser settings. Allow notifications for this site, then reload."}
          {state === "ios-install" && "On iPhone: tap Share, then “Add to Home Screen”, and open Mitsuke from there to turn notifications on."}
        </p>
        {note && <p className="mt-1 text-sm text-amber">{note}</p>}
      </div>
      <div className="flex shrink-0 gap-2">
        {state === "on" && (
          <>
            <button onClick={test} className="rounded-xl border border-line px-3 py-2 text-sm hover:border-text">Send a test</button>
            <button onClick={turnOff} className="rounded-xl border border-line px-3 py-2 text-sm text-muted hover:border-accent hover:text-accent">Turn off</button>
          </>
        )}
        {(state === "off" || state === "working") && (
          <button onClick={turnOn} disabled={state === "working"} className="rounded-xl bg-accent px-4 py-2 text-sm font-semibold hover:bg-accent-strong disabled:opacity-60">
            {state === "working" ? "Turning on…" : "Turn on"}
          </button>
        )}
      </div>
    </div>
  );
}
