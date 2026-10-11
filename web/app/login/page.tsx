import type { Metadata } from "next";
import { LoginForm } from "./LoginForm";
import { HankoMark } from "@/components/Brand";

export const metadata: Metadata = { title: "Sign in" };

export default function LoginPage() {
  return (
    <div className="mx-auto max-w-md pt-16">
      <div className="card p-8 sm:p-10">
        <HankoMark className="size-12 text-2xl" />
        <h1 className="mt-6 text-3xl font-bold tracking-tight">Sign in to Mitsuke</h1>
        <p className="mt-2 text-sm text-muted">
          We&apos;ll email you a link. No password to remember, and we never see one.
        </p>
        <LoginForm />
      </div>
    </div>
  );
}
