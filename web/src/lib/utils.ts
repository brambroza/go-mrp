import { cn as merge } from "cn";

/** Joins class names and resolves conflicting Tailwind classes (later wins). */
export function cn(...inputs: Parameters<typeof merge>): string {
  return merge(...inputs);
}
