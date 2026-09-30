import { endSession } from "@/server/auth-handlers";

/** Signs out: revokes the refresh token and removes the cookie. */
export function POST(request: Request): Promise<Response> {
  return endSession(request);
}
