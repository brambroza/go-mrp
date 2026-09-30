"use client";

import { useTranslations } from "next-intl";
import Link from "next/link";

import { useUser } from "@/components/providers/auth-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { usePermissionLabels } from "@/features/settings/roles";
import { ALL_PERMISSIONS } from "@/lib/auth/permissions";

/** Read-only profile of the signed-in user: account, company, roles and permissions. */
export function Profile() {
  const t = useTranslations("account");
  const tc = useTranslations("common");
  const user = useUser();
  const labels = usePermissionLabels();
  const everything = user.permissions.includes(ALL_PERMISSIONS);

  const facts: [string, React.ReactNode][] = [
    [t("profile.displayName"), user.displayName],
    [t("profile.userName"), user.userName],
    [t("profile.email"), user.email ?? "–"],
    [t("profile.language"), tc(`language.${user.language === "en" ? "en" : "th"}`)],
    [t("profile.company"), `${user.tenantName} (${user.tenantSlug})`],
    [t("profile.plan"), user.plan],
    [t("profile.timeZone"), user.timeZone],
    [t("profile.twoFactor"), user.twoFactorEnabled ? tc("on") : tc("off")],
  ];

  return (
    <div className="grid max-w-4xl gap-4 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>{t("profile.account")}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            {facts.map(([label, value]) => (
              <div key={label} className="contents">
                <dt className="text-muted-foreground">{label}</dt>
                <dd className="min-w-0 break-words">{value}</dd>
              </div>
            ))}
          </dl>
          <div>
            <Button variant="outline" asChild>
              <Link href="/account/two-factor">{t("profile.manageTwoFactor")}</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>{t("profile.access")}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4 text-sm">
          <div>
            <p className="mb-1 text-muted-foreground">{t("profile.roles")}</p>
            <div className="flex flex-wrap gap-1">
              {user.roles.map((role) => (
                <Badge key={role} variant="secondary">
                  {role}
                </Badge>
              ))}
            </div>
          </div>
          <div>
            <p className="mb-1 text-muted-foreground">{t("profile.permissions")}</p>
            {everything ? (
              <p>{t("profile.allPermissions")}</p>
            ) : user.permissions.length === 0 ? (
              <p>{t("profile.noPermissions")}</p>
            ) : (
              <ul className="list-disc pl-5">
                {user.permissions.map((code) => (
                  <li key={code}>{labels.permission(code)}</li>
                ))}
              </ul>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
