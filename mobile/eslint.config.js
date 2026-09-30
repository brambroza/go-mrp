// ESLint flat configuration (Expo preset + project rules).
const { defineConfig } = require("eslint/config");
const expoConfig = require("eslint-config-expo/flat");

module.exports = defineConfig([
  expoConfig,
  {
    ignores: ["dist/*", "coverage/*", ".expo/*", "node_modules/*", "expo-env.d.ts"],
  },
  {
    rules: {
      // Tokens and personal data must never reach the device log.
      "no-console": "error",
      eqeqeq: ["error", "always"],
    },
  },
]);
