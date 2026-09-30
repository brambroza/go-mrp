import { refreshSession } from "@/server/auth-handlers";

/** Exchanges the refresh cookie for a new access token. */
export function POST(request: Request): Promise<Response> {
  return refreshSession(request);
}
