import { NoPermission } from "@/features/common/components";
import { useCurrentUser } from "@/features/common/hooks";
import { StockMoveForm } from "@/features/documents/StockMoveForm";
import { canOpenTile } from "@/features/home/tiles";
import { Screen } from "@/ui";

/** Transfer between warehouses or between locations of one warehouse. */
export default function TransferScreen() {
  const { permissions } = useCurrentUser();
  if (!canOpenTile(permissions, "transfer")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }
  return <StockMoveForm mode="transfer" />;
}
