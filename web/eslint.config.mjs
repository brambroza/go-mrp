import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import jsdoc from "eslint-plugin-jsdoc";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  globalIgnores([".next/**", "out/**", "build/**", "next-env.d.ts", "playwright-report/**", "test-results/**"]),
  {
    files: ["src/**/*.{ts,tsx}"],
    plugins: { jsdoc },
    rules: {
      // Every exported function, component, class, interface and type needs a JSDoc comment.
      "jsdoc/require-jsdoc": [
        "error",
        {
          publicOnly: true,
          require: {
            FunctionDeclaration: true,
            ClassDeclaration: true,
            ArrowFunctionExpression: true,
            FunctionExpression: true,
          },
          contexts: ["TSInterfaceDeclaration", "TSTypeAliasDeclaration"],
          checkConstructors: false,
        },
      ],
      // UI text lives in src/messages; literal text in JSX is a bug.
      "react/jsx-no-literals": ["error", { noStrings: true, ignoreProps: true, allowedStrings: ["*", "·", "–", "…", "404", "/", "(", ")"] }],
    },
  },
  {
    // Test files and generated shadcn/ui primitives carry no UI text of their own.
    files: ["src/**/*.test.{ts,tsx}", "e2e/**"],
    rules: { "jsdoc/require-jsdoc": "off", "react/jsx-no-literals": "off" },
  },
]);

export default eslintConfig;
