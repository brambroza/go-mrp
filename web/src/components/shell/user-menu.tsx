"use client";

import { LogOutIcon, ShieldCheckIcon, UserIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";

import { useAuth, useUser } from "@/components/providers/auth-provider";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/** First letters of the first two words of a name, for the avatar. */
export function initials(name: string): string {
  const letters = name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((word) => Array.from(word)[0] ?? "")
    .join("");
  return letters.toUpperCase() || "?";
}

/** Avatar menu with profile, two-factor setup and sign-out. */
export function UserMenu() {
  const t = useTranslations("nav");
  const user = useUser();
  const { signOut } = useAuth();
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);

  const leave = async () => {
    setLeaving(true);
    try {
      await signOut();
    } finally {
      router.replace("/login");
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="h-9 gap-2 px-1.5" aria-label={t("user.menu")} data-testid="user-menu">
          <Avatar className="size-7">
            <AvatarFallback>{initials(user.displayName)}</AvatarFallback>
          </Avatar>
          <span className="hidden max-w-40 truncate text-sm md:inline">{user.displayName}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-60">
        <DropdownMenuLabel className="flex flex-col">
          <span className="truncate font-medium text-foreground">{user.displayName}</span>
          <span className="truncate text-xs font-normal">
            {user.userName} · {user.tenantSlug}
          </span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuItem asChild>
            <Link href="/account/profile">
              <UserIcon />
              {t("user.profile")}
            </Link>
          </DropdownMenuItem>
          <DropdownMenuItem asChild>
            <Link href="/account/two-factor">
              <ShieldCheckIcon />
              {t("user.twoFactor")}
            </Link>
          </DropdownMenuItem>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem disabled={leaving} onSelect={() => void leave()} data-testid="sign-out">
          <LogOutIcon />
          {t("user.signOut")}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
