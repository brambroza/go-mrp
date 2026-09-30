"use client";

import type { ReactNode } from "react";

import { useAuth } from "@/components/providers/auth-provider";

import type { PermissionRequirement } from "./permissions";

/**
 * Whether the signed-in user has the permission; an array means "any of these".
 * Use it to hide actions — the API still enforces the permission and may answer 403.
 */
export function useCan(permission: PermissionRequirement | null | undefined): boolean {
  return useAuth().can(permission);
}

/** Props of {@link Can}. */
export interface CanProps {
  /** Required permission; an array means "any of these". */
  permission: PermissionRequirement;
  /** Shown when the user has the permission. */
  children: ReactNode;
  /** Shown otherwise (default nothing). */
  fallback?: ReactNode;
}

/** Renders its children only when the signed-in user has the permission. */
export function Can({ permission, children, fallback = null }: CanProps) {
  return <>{useCan(permission) ? children : fallback}</>;
}
