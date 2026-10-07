import { appIcon } from "@/lib/appIcon";

// iOS uses this for "Add to Home Screen", which is also what lets an iPhone get notifications.
export function GET() {
  return appIcon(180);
}
