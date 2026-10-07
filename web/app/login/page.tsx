import type { Metadata } from "next";
import { LoginForm } from "./LoginForm";

export const metadata: Metadata = { title: "Sign in" };

export default function LoginPage() {
  return (
    <div className="mx-auto max-w-md pt-10">
      <div className="card p-8">
        <h1 className="text-2xl font-bold tracking-tight">Sign in to Mitsuke</h1>
        <p className="mt-2 text-sm text-muted">
          We&apos;ll email you a link. No password to remember, and we never see one.
        </p>
        <LoginForm />
      </div>
    </div>
  );
}
