import { Text, type TextProps } from "react-native";

import { useTheme } from "@/theme/ThemeProvider";
import { typography, type TextVariant } from "@/theme/tokens";

/** Props of {@link AppText}. */
export interface AppTextProps extends TextProps {
  /** Text style. Default `body`. */
  variant?: TextVariant;
  /** Use the muted text colour. */
  muted?: boolean;
  /** Explicit colour; overrides `muted`. */
  color?: string;
}

/** Text in the Thai font of the design system. Scales with the device's font size setting. */
export function AppText({ variant = "body", muted = false, color, style, ...rest }: AppTextProps) {
  const { colors } = useTheme();
  return <Text {...rest} maxFontSizeMultiplier={1.6} style={[typography[variant], { color: color ?? (muted ? colors.textMuted : colors.text) }, style]} />;
}
