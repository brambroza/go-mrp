import { darkColors, lightColors, TOUCH_TARGET, type ThemeColors } from "./tokens";

/** Relative luminance of a `#RRGGBB` colour (WCAG 2.1). */
function luminance(hex: string): number {
  const channels = [1, 3, 5].map((start) => {
    const value = parseInt(hex.slice(start, start + 2), 16) / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  const [r = 0, g = 0, b = 0] = channels;
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Contrast ratio of two colours. */
function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

const pairs: [keyof ThemeColors, keyof ThemeColors][] = [
  ["text", "background"],
  ["text", "surface"],
  ["text", "surfaceAlt"],
  ["textMuted", "background"],
  ["textMuted", "surface"],
  ["textMuted", "surfaceAlt"],
  ["onPrimary", "primary"],
  ["primary", "surface"],
  ["primary", "background"],
  ["onDanger", "danger"],
  ["danger", "surface"],
  ["onDangerSurface", "dangerSurface"],
  ["onSuccessSurface", "successSurface"],
  ["onWarningSurface", "warningSurface"],
  ["onInfoSurface", "infoSurface"],
  ["success", "surface"],
  ["warning", "surface"],
];

describe.each([
  ["light", lightColors],
  ["dark", darkColors],
])("%s palette", (_name, colors) => {
  it.each(pairs)("%s on %s has a contrast of at least 4.5:1", (foreground, background) => {
    expect(contrast(colors[foreground], colors[background])).toBeGreaterThanOrEqual(4.5);
  });

  it("has input borders with a contrast of at least 3:1", () => {
    expect(contrast(colors.border, colors.surface)).toBeGreaterThanOrEqual(3);
  });
});

it("has touch targets large enough for gloves", () => {
  expect(TOUCH_TARGET).toBeGreaterThanOrEqual(56);
});
