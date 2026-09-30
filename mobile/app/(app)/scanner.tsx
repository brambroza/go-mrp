import { CameraView, useCameraPermissions, type BarcodeScanningResult, type BarcodeType } from "expo-camera";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Linking, StyleSheet, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

import { completeScan } from "@/scanner/scanBus";
import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing } from "@/theme/tokens";
import { AppText, Banner, Button, LoadingView, Screen, TextField } from "@/ui";
import { scannedTextSchema } from "@/validation/schemas";

/** Barcode formats found on lot, pallet and location labels. */
const BARCODE_TYPES: BarcodeType[] = ["qr", "code128", "code39", "code93", "ean13", "ean8", "datamatrix", "pdf417", "itf14", "upc_a"];

/**
 * Scanner screen used by every flow. Returns the text to the screen that opened it (see
 * `useScanner`). Offers typing the code by hand, because labels in a warehouse get damaged.
 */
export default function ScannerScreen() {
  const { t } = useTranslation();
  const { colors, dark } = useTheme();
  const insets = useSafeAreaInsets();
  const router = useRouter();
  const { mode } = useLocalSearchParams<{ mode?: string }>();
  const [permission, requestPermission] = useCameraPermissions();
  const [torch, setTorch] = useState(false);
  const [manual, setManual] = useState(false);
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const delivered = useRef(false);

  const finish = useCallback(
    (value: string | null) => {
      if (delivered.current) {
        return;
      }
      delivered.current = true;
      completeScan(value);
      if (router.canGoBack()) {
        router.back();
      }
    },
    [router],
  );

  // Closing the screen with the system back gesture counts as "cancelled". The check is deferred
  // so that a development re-mount (React strict mode) does not cancel the scan.
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      setTimeout(() => {
        if (!mounted.current && !delivered.current) {
          delivered.current = true;
          completeScan(null);
        }
      }, 0);
    };
  }, []);

  const onScanned = useCallback(
    (result: BarcodeScanningResult) => {
      const parsed = scannedTextSchema.safeParse(result.data ?? "");
      if (parsed.success) {
        finish(parsed.data);
      }
    },
    [finish],
  );

  const submitManual = () => {
    const parsed = scannedTextSchema.safeParse(text);
    if (!parsed.success) {
      setError(t(parsed.error.issues[0]?.message ?? "validation.invalid"));
      return;
    }
    finish(parsed.data);
  };

  const title = mode === "lot" ? t("scanner.titleLot") : mode === "location" ? t("scanner.titleLocation") : t("scanner.title");

  if (manual) {
    return (
      <Screen
        testID="scanner-manual"
        footer={
          <>
            <Button label={t("scanner.useCode")} onPress={submitManual} testID="scanner-manual-submit" />
            <Button label={t("scanner.backToCamera")} variant="secondary" onPress={() => setManual(false)} />
            <Button label={t("common.cancel")} variant="ghost" onPress={() => finish(null)} />
          </>
        }
      >
        <View style={{ paddingTop: insets.top }}>
          <AppText variant="title" accessibilityRole="header">
            {title}
          </AppText>
          <AppText muted style={styles.hint}>
            {t("scanner.manualHint")}
          </AppText>
          <TextField
            label={t("scanner.manualLabel")}
            value={text}
            onChangeText={(value) => {
              setText(value);
              setError(null);
            }}
            error={error}
            autoCapitalize="characters"
            autoCorrect={false}
            autoFocus
            maxLength={200}
            returnKeyType="done"
            onSubmitEditing={submitManual}
            testID="scanner-manual-input"
          />
        </View>
      </Screen>
    );
  }

  if (!permission) {
    return <LoadingView label={t("scanner.preparing")} />;
  }

  if (!permission.granted) {
    return (
      <Screen testID="scanner-permission">
        <View style={{ paddingTop: insets.top }}>
          <AppText variant="title" accessibilityRole="header">
            {title}
          </AppText>
          <Banner tone="info" title={t("scanner.permissionTitle")} message={permission.canAskAgain ? t("scanner.permissionMessage") : t("scanner.permissionBlocked")} />
          {permission.canAskAgain ? (
            <Button label={t("scanner.allowCamera")} onPress={() => void requestPermission()} testID="scanner-allow" />
          ) : (
            <Button label={t("scanner.openSettings")} onPress={() => void Linking.openSettings()} />
          )}
          <Button label={t("scanner.typeCode")} variant="secondary" onPress={() => setManual(true)} style={styles.gap} />
          <Button label={t("common.cancel")} variant="ghost" onPress={() => finish(null)} style={styles.gap} />
        </View>
      </Screen>
    );
  }

  return (
    <View style={[styles.camera, { backgroundColor: "#000000" }]} testID="scanner-camera">
      <CameraView
        style={StyleSheet.absoluteFill}
        facing="back"
        enableTorch={torch}
        barcodeScannerSettings={{ barcodeTypes: BARCODE_TYPES }}
        onBarcodeScanned={onScanned}
        accessibilityLabel={title}
      />
      <View style={[styles.top, { paddingTop: insets.top + spacing.md }]}>
        <AppText variant="heading" color="#FFFFFF" accessibilityRole="header">
          {title}
        </AppText>
        <AppText color="#FFFFFF">{t("scanner.aim")}</AppText>
      </View>
      <View pointerEvents="none" style={styles.frameArea}>
        <View style={[styles.frame, { borderColor: dark ? colors.primary : "#FFFFFF" }]} />
      </View>
      <View style={[styles.bottom, { paddingBottom: insets.bottom + spacing.md, backgroundColor: colors.surface }]}>
        <View style={styles.row}>
          <Button
            label={torch ? t("scanner.torchOff") : t("scanner.torchOn")}
            variant={torch ? "primary" : "secondary"}
            onPress={() => setTorch((on) => !on)}
            style={styles.grow}
            testID="scanner-torch"
          />
          <Button label={t("scanner.typeCode")} variant="secondary" onPress={() => setManual(true)} style={styles.grow} testID="scanner-type" />
        </View>
        <Button label={t("common.cancel")} variant="ghost" onPress={() => finish(null)} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  camera: { flex: 1 },
  top: { paddingHorizontal: spacing.lg, paddingBottom: spacing.md, backgroundColor: "rgba(0, 0, 0, 0.7)" },
  frameArea: { flex: 1, alignItems: "center", justifyContent: "center" },
  frame: { width: "78%", aspectRatio: 1.4, borderWidth: 4, borderRadius: radius.lg },
  bottom: { paddingHorizontal: spacing.lg, paddingTop: spacing.md, gap: spacing.sm },
  row: { flexDirection: "row", gap: spacing.sm },
  grow: { flex: 1 },
  hint: { marginBottom: spacing.lg },
  gap: { marginTop: spacing.sm },
});
