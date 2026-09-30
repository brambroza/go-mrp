import type { ReactNode } from "react";

/** Props of {@link PageHeader}. */
export interface PageHeaderProps {
  /** Page title (rendered as `h1`). */
  title: ReactNode;
  /** One line under the title. */
  description?: ReactNode;
  /** Buttons on the right. */
  actions?: ReactNode;
}

/** Title row at the top of every page. */
export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {description ? <p className="mt-1 text-sm text-muted-foreground">{description}</p> : null}
      </div>
      {actions ? <div className="flex shrink-0 items-center gap-2">{actions}</div> : null}
    </div>
  );
}
