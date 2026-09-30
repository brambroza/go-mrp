import { useMemo, useState, type ReactNode } from "react";
import { FlatList, Modal, Pressable, StyleSheet, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing, TOUCH_TARGET } from "@/theme/tokens";

import { AppText } from "./AppText";
import { Button } from "./Button";
import { EmptyView, LoadingView } from "./Layout";
import { TextField } from "./TextField";

/** Props of {@link ConfirmDialog}. */
export interface ConfirmDialogProps {
  visible: boolean;
  title: string;
  message?: string;
  confirmLabel: string;
  cancelLabel: string;
  /** Use the danger style for actions that cannot be undone. */
  destructive?: boolean;
  /** Shows a spinner on the confirm button and blocks both buttons. */
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
  /** Extra content, e.g. a reason field. */
  children?: ReactNode;
}

/** Modal that asks the user to confirm an action. */
export function ConfirmDialog({ visible, title, message, confirmLabel, cancelLabel, destructive = false, busy = false, onConfirm, onCancel, children }: ConfirmDialogProps) {
  const { colors } = useTheme();
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={busy ? undefined : onCancel} statusBarTranslucent>
      <View style={[styles.backdrop, { backgroundColor: colors.overlay }]}>
        <View accessibilityViewIsModal style={[styles.dialog, { backgroundColor: colors.surface, borderColor: colors.border }]}>
          <AppText variant="heading" accessibilityRole="header">
            {title}
          </AppText>
          {message ? <AppText style={styles.message}>{message}</AppText> : null}
          {children ? <View style={styles.children}>{children}</View> : null}
          <View style={styles.actions}>
            <Button label={confirmLabel} onPress={onConfirm} variant={destructive ? "danger" : "primary"} loading={busy} />
            <Button label={cancelLabel} onPress={onCancel} variant="secondary" disabled={busy} />
          </View>
        </View>
      </View>
    </Modal>
  );
}

/** Option of {@link PickerModal}. */
export interface PickerOption {
  /** Unique key. */
  id: string;
  /** Main text. */
  title: string;
  /** Second line. */
  subtitle?: string;
}

/** Props of {@link PickerModal}. */
export interface PickerModalProps {
  visible: boolean;
  title: string;
  options: readonly PickerOption[];
  /** Id of the selected option. */
  selectedId?: string | null;
  loading?: boolean;
  /** Error message (already translated) shown instead of the list. */
  error?: string | null;
  onRetry?: () => void;
  /** Placeholder of the search field. */
  searchLabel: string;
  /** Called when the search text changes; without it the list is filtered on the device. */
  onSearch?: (text: string) => void;
  emptyLabel: string;
  loadingLabel: string;
  retryLabel: string;
  closeLabel: string;
  /** Label of the option that clears the selection; hidden when absent. */
  clearLabel?: string;
  onSelect: (option: PickerOption | null) => void;
  onClose: () => void;
}

/** Full-screen list with search for picking one option (warehouse, location, item). */
export function PickerModal(props: PickerModalProps) {
  const { visible, title, options, selectedId, loading = false, error, onRetry, searchLabel, onSearch, emptyLabel, loadingLabel, retryLabel, closeLabel, clearLabel, onSelect, onClose } = props;
  const { colors } = useTheme();
  const insets = useSafeAreaInsets();
  const [search, setSearch] = useState("");

  const shown = useMemo(() => {
    if (onSearch) {
      return options;
    }
    const needle = search.trim().toLowerCase();
    return needle === "" ? options : options.filter((option) => `${option.title} ${option.subtitle ?? ""}`.toLowerCase().includes(needle));
  }, [onSearch, options, search]);

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <View style={[styles.picker, { backgroundColor: colors.background, paddingTop: insets.top + spacing.md, paddingBottom: insets.bottom + spacing.md }]}>
        <AppText variant="title" accessibilityRole="header">
          {title}
        </AppText>
        <TextField
          label={searchLabel}
          value={search}
          autoCapitalize="none"
          autoCorrect={false}
          maxLength={100}
          returnKeyType="search"
          onChangeText={(text) => {
            setSearch(text);
            onSearch?.(text);
          }}
        />
        {loading ? (
          <LoadingView label={loadingLabel} />
        ) : error ? (
          <View style={styles.flex}>
            <AppText color={colors.danger} accessibilityRole="alert" style={styles.message}>
              {error}
            </AppText>
            {onRetry ? <Button label={retryLabel} onPress={onRetry} variant="secondary" /> : null}
          </View>
        ) : (
          <FlatList
            style={styles.flex}
            data={shown}
            keyExtractor={(option) => option.id}
            keyboardShouldPersistTaps="handled"
            ListEmptyComponent={<EmptyView title={emptyLabel} />}
            ListHeaderComponent={
              clearLabel ? (
                <Pressable accessibilityRole="button" onPress={() => onSelect(null)} style={[styles.option, { borderColor: colors.border, backgroundColor: colors.surface }]}>
                  <AppText variant="bodyStrong" muted>
                    {clearLabel}
                  </AppText>
                </Pressable>
              ) : null
            }
            renderItem={({ item }) => {
              const selected = item.id === selectedId;
              return (
                <Pressable
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  onPress={() => onSelect(item)}
                  style={({ pressed }) => [
                    styles.option,
                    { borderColor: selected ? colors.primary : colors.border, borderWidth: selected ? 3 : 1, backgroundColor: colors.surface, opacity: pressed ? 0.75 : 1 },
                  ]}
                >
                  <AppText variant="bodyStrong">{item.title}</AppText>
                  {item.subtitle ? <AppText muted>{item.subtitle}</AppText> : null}
                </Pressable>
              );
            }}
          />
        )}
        <Button label={closeLabel} onPress={onClose} variant="secondary" style={styles.close} />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  backdrop: { flex: 1, justifyContent: "center", padding: spacing.lg },
  dialog: { borderRadius: radius.lg, borderWidth: 1, padding: spacing.xl },
  message: { marginTop: spacing.sm, marginBottom: spacing.sm },
  children: { marginTop: spacing.md },
  actions: { marginTop: spacing.lg, gap: spacing.sm },
  picker: { flex: 1, paddingHorizontal: spacing.lg },
  option: { minHeight: TOUCH_TARGET, borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.sm, justifyContent: "center" },
  close: { marginTop: spacing.sm },
});
