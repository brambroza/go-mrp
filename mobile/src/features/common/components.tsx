import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, View } from "react-native";

import { fetchLocations, fetchWarehouses, searchItems, type Item, type Location, type Warehouse } from "@/api/masters";
import { useTheme } from "@/theme/ThemeProvider";
import { spacing } from "@/theme/tokens";
import { AppText, Banner, Button, PickerModal, type PickerOption } from "@/ui";

import { useCurrentUser, useDebounced, useErrorText, useOnline, useScanner } from "./hooks";

/** Banner shown on every screen while the device has no connection. */
export function OfflineBanner() {
  const { t } = useTranslation();
  const online = useOnline();
  if (online) {
    return null;
  }
  return <Banner tone="warning" title={t("offline.title")} message={t("offline.message")} testID="offline-banner" />;
}

/** Message for a screen that needs a connection. */
export function OnlineOnlyNotice() {
  const { t } = useTranslation();
  return <Banner tone="warning" title={t("offline.title")} message={t("offline.onlineOnly")} testID="online-only" />;
}

/** Message for a screen the user has no permission for. */
export function NoPermission() {
  const { t } = useTranslation();
  return <Banner tone="danger" title={t("permission.deniedTitle")} message={t("permission.deniedMessage")} />;
}

/** Error of a query with a retry button. */
export function QueryError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { t } = useTranslation();
  const errorText = useErrorText();
  return <Banner tone="danger" title={t("common.loadFailed")} message={errorText(error)} actionLabel={onRetry ? t("common.retry") : undefined} onAction={onRetry} />;
}

/** Common labels of {@link PickerModal}. */
function usePickerLabels() {
  const { t } = useTranslation();
  return { loadingLabel: t("common.loading"), retryLabel: t("common.retry"), closeLabel: t("common.close"), emptyLabel: t("common.noResults") };
}

/** Props of the master pickers. */
interface PickerFieldProps<T> {
  label: string;
  value: T | null;
  onChange: (value: T | null) => void;
  /** Error message (already translated). */
  error?: string | null;
  /** Allow clearing the selection. */
  optional?: boolean;
}

/** Row with the current selection and a button that opens the picker. */
function PickerRow({ label, text, error, children }: { label: string; text: string | null; error?: string | null; children: React.ReactNode }) {
  const { t } = useTranslation();
  const { colors } = useTheme();
  return (
    <View style={styles.field}>
      <AppText variant="label">{label}</AppText>
      <AppText variant="bodyStrong" muted={text === null} style={styles.value}>
        {text ?? t("common.notSelected")}
      </AppText>
      <View style={styles.actions}>{children}</View>
      {error ? (
        <AppText variant="caption" color={colors.danger} accessibilityRole="alert" style={styles.error}>
          {error}
        </AppText>
      ) : null}
    </View>
  );
}

/** Picker of an active warehouse. */
export function WarehouseField({ label, value, onChange, error, excludeId }: PickerFieldProps<Warehouse> & { excludeId?: string | null }) {
  const { t } = useTranslation();
  const { scope } = useCurrentUser();
  const errorText = useErrorText();
  const labels = usePickerLabels();
  const [open, setOpen] = useState(false);
  const query = useQuery({ queryKey: ["warehouses", scope?.tenantId], queryFn: fetchWarehouses, enabled: open, staleTime: 10 * 60_000 });
  const options: PickerOption[] = (query.data ?? []).filter((row) => row.id !== excludeId).map((row) => ({ id: row.id, title: row.code, subtitle: row.name }));

  return (
    <PickerRow label={label} text={value ? `${value.code} · ${value.name}` : null} error={error}>
      <Button label={value ? t("common.change") : t("common.choose")} variant="secondary" onPress={() => setOpen(true)} accessibilityLabel={`${t("common.choose")} ${label}`} style={styles.grow} />
      <PickerModal
        {...labels}
        visible={open}
        title={label}
        options={options}
        selectedId={value?.id}
        loading={query.isLoading}
        error={query.error ? errorText(query.error) : null}
        onRetry={() => void query.refetch()}
        searchLabel={t("common.search")}
        onSelect={(option) => {
          onChange(query.data?.find((row) => row.id === option?.id) ?? null);
          setOpen(false);
        }}
        onClose={() => setOpen(false)}
      />
    </PickerRow>
  );
}

/** Picker of a location inside a warehouse: choose from the list or scan the location label. */
export function LocationField({ label, value, onChange, error, warehouseId, optional = true }: PickerFieldProps<Location> & { warehouseId: string | null }) {
  const { t } = useTranslation();
  const errorText = useErrorText();
  const labels = usePickerLabels();
  const scan = useScanner();
  const [open, setOpen] = useState(false);
  const [scanError, setScanError] = useState<string | null>(null);
  const [wanted, setWanted] = useState(false);
  const query = useQuery({
    queryKey: ["locations", warehouseId],
    queryFn: () => fetchLocations(warehouseId ?? ""),
    enabled: warehouseId !== null && (open || wanted),
    staleTime: 10 * 60_000,
  });
  const options: PickerOption[] = (query.data ?? []).map((row) => ({ id: row.id, title: row.code, subtitle: row.name }));

  const scanLocation = async () => {
    setScanError(null);
    setWanted(true);
    const text = await scan("location");
    if (text === null) {
      return;
    }
    try {
      const rows = query.data ?? (await query.refetch({ throwOnError: true })).data ?? [];
      const match = rows.find((row) => row.code.toLowerCase() === text.toLowerCase());
      if (match) {
        onChange(match);
      } else {
        setScanError(t("location.notFound", { code: text }));
      }
    } catch (failure) {
      setScanError(errorText(failure));
    }
  };

  return (
    <PickerRow label={label} text={value ? `${value.code} · ${value.name}` : null} error={error ?? scanError}>
      <Button label={t("common.choose")} variant="secondary" disabled={warehouseId === null} onPress={() => setOpen(true)} accessibilityLabel={`${t("common.choose")} ${label}`} style={styles.grow} />
      <Button label={t("scanner.scan")} variant="secondary" disabled={warehouseId === null} onPress={() => void scanLocation()} accessibilityLabel={`${t("scanner.scan")} ${label}`} style={styles.grow} />
      <PickerModal
        {...labels}
        visible={open}
        title={label}
        options={options}
        selectedId={value?.id}
        loading={query.isLoading}
        error={query.error ? errorText(query.error) : null}
        onRetry={() => void query.refetch()}
        searchLabel={t("common.search")}
        clearLabel={optional ? t("location.none") : undefined}
        onSelect={(option) => {
          setScanError(null);
          onChange(option ? (query.data?.find((row) => row.id === option.id) ?? null) : null);
          setOpen(false);
        }}
        onClose={() => setOpen(false)}
      />
    </PickerRow>
  );
}

/** Button that opens a search for items (code, name or barcode) on the server. */
export function ItemSearchButton({ label, onPick }: { label: string; onPick: (item: Item) => void }) {
  const { t } = useTranslation();
  const errorText = useErrorText();
  const labels = usePickerLabels();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const term = useDebounced(search.trim(), 400);
  const query = useQuery({ queryKey: ["items", term], queryFn: () => searchItems(term), enabled: open, staleTime: 60_000 });
  const options: PickerOption[] = (query.data ?? []).map((row) => ({ id: row.id, title: `${row.code} · ${row.name}`, subtitle: row.stockUnitCode }));

  return (
    <>
      <Button label={label} variant="secondary" onPress={() => setOpen(true)} />
      <PickerModal
        {...labels}
        visible={open}
        title={label}
        options={options}
        loading={query.isLoading}
        error={query.error ? errorText(query.error) : null}
        onRetry={() => void query.refetch()}
        searchLabel={t("item.searchLabel")}
        onSearch={setSearch}
        onSelect={(option) => {
          const item = query.data?.find((row) => row.id === option?.id);
          if (item) {
            onPick(item);
          }
          setOpen(false);
        }}
        onClose={() => setOpen(false)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  field: { marginBottom: spacing.md },
  value: { marginTop: spacing.xs },
  actions: { flexDirection: "row", gap: spacing.sm, marginTop: spacing.sm },
  grow: { flex: 1 },
  error: { marginTop: spacing.xs },
});
