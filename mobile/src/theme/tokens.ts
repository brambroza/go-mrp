/** Colour roles of the design system. All text/background pairs have a contrast of at least 4.5:1. */
export interface ThemeColors {
  background: string;
  surface: string;
  surfaceAlt: string;
  border: string;
  text: string;
  textMuted: string;
  primary: string;
  onPrimary: string;
  danger: string;
  onDanger: string;
  dangerSurface: string;
  onDangerSurface: string;
  success: string;
  successSurface: string;
  onSuccessSurface: string;
  warning: string;
  warningSurface: string;
  onWarningSurface: string;
  infoSurface: string;
  onInfoSurface: string;
  focus: string;
  overlay: string;
}

/** Light palette. */
export const lightColors: ThemeColors = {
  background: "#F4F6F8",
  surface: "#FFFFFF",
  surfaceAlt: "#E9EDF1",
  border: "#7A8794",
  text: "#0F1B26",
  textMuted: "#44515E",
  primary: "#0B5CAB",
  onPrimary: "#FFFFFF",
  danger: "#B3261E",
  onDanger: "#FFFFFF",
  dangerSurface: "#FDE7E5",
  onDangerSurface: "#7A1610",
  success: "#1B6E3A",
  successSurface: "#E1F3E7",
  onSuccessSurface: "#0F4A26",
  warning: "#8A5300",
  warningSurface: "#FFF0D1",
  onWarningSurface: "#5C3700",
  infoSurface: "#E0EDFB",
  onInfoSurface: "#083F77",
  focus: "#0B5CAB",
  overlay: "rgba(0, 0, 0, 0.55)",
};

/** Dark palette. */
export const darkColors: ThemeColors = {
  background: "#0D141B",
  surface: "#17212B",
  surfaceAlt: "#223140",
  border: "#8896A5",
  text: "#F2F5F8",
  textMuted: "#B7C3CF",
  primary: "#7DB8F5",
  onPrimary: "#06233F",
  danger: "#FFB4AB",
  onDanger: "#5C0B06",
  dangerSurface: "#5C1A15",
  onDangerSurface: "#FFDAD6",
  success: "#8AD7A4",
  successSurface: "#123D22",
  onSuccessSurface: "#C9F2D6",
  warning: "#FFC766",
  warningSurface: "#4A3200",
  onWarningSurface: "#FFE3AD",
  infoSurface: "#0F3558",
  onInfoSurface: "#D3E7FC",
  focus: "#7DB8F5",
  overlay: "rgba(0, 0, 0, 0.7)",
};

/** Spacing scale in density-independent pixels. */
export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

/** Corner radius. */
export const radius = { sm: 8, md: 12, lg: 16 } as const;

/**
 * Minimum size of anything that can be pressed. Warehouse staff wear gloves, so targets are
 * larger than the usual 44-48 dp.
 */
export const TOUCH_TARGET = 56;

/** Font families loaded at start-up (Sarabun covers Thai and Latin). */
export const fonts = {
  regular: "Sarabun_400Regular",
  medium: "Sarabun_500Medium",
  bold: "Sarabun_700Bold",
} as const;

/** Text styles. Line heights leave room for Thai vowels and tone marks above and below. */
export const typography = {
  title: { fontFamily: fonts.bold, fontSize: 24, lineHeight: 36 },
  heading: { fontFamily: fonts.bold, fontSize: 20, lineHeight: 30 },
  body: { fontFamily: fonts.regular, fontSize: 17, lineHeight: 27 },
  bodyStrong: { fontFamily: fonts.medium, fontSize: 17, lineHeight: 27 },
  label: { fontFamily: fonts.medium, fontSize: 15, lineHeight: 24 },
  caption: { fontFamily: fonts.regular, fontSize: 14, lineHeight: 22 },
  button: { fontFamily: fonts.bold, fontSize: 18, lineHeight: 28 },
  number: { fontFamily: fonts.bold, fontSize: 22, lineHeight: 32 },
} as const;

/** Name of a text style. */
export type TextVariant = keyof typeof typography;

/** Theme given to components. */
export interface Theme {
  dark: boolean;
  colors: ThemeColors;
}
