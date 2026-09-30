"use client";

import type { ReactNode } from "react";

import { ForbiddenState } from "@/components/feedback/states";
import type { PermissionRequirement } from "@/lib/auth/permissions";
import { useCan } from "@/lib/auth/use-can";

/** Props of {@link RequirePermission}. */
export interface RequirePermissionProps {
  /** Permission needed to see the page; an array means "any of these". */
  permission: PermissionRequirement;
  children: ReactNode;
}

/**
 * Page-level guard: shows the forbidden state instead of the page when the user lacks the
 * permission. Hiding is for usability only; the API checks the permission on every call.
 */
export function RequirePermission({ permission, children }: RequirePermissionProps) {
  return useCan(permission) ? <>{children}</> : <ForbiddenState />;
}
