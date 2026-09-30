import { NoPermission } from "@/features/common/components";
import { useCurrentUser } from "@/features/common/hooks";
import { StockMoveForm } from "@/features/documents/StockMoveForm";
import { canOpenTile } from "@/features/home/tiles";
import { Screen } from "@/ui";

/** Goods issue: pick a warehouse, add lines by item search or lot scan, review, submit. */
export default function IssueScreen() {
  const { permissions } = useCurrentUser();
  if (!canOpenTile(permissions, "issue")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }
  return <StockMoveForm mode="issue" />;
}
