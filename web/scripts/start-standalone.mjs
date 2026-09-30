// Starts the production build the way the container does: the standalone server with the static
// assets next to it. Usage: `pnpm build && node scripts/start-standalone.mjs` (PORT, HOSTNAME from env).
import { cp, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const standalone = path.join(root, ".next", "standalone", "web");
const server = path.join(standalone, "server.js");

try {
  await stat(server);
} catch {
  console.error("No standalone build found. Run `pnpm --filter @mrp/web build` first.");
  process.exit(1);
}

await cp(path.join(root, ".next", "static"), path.join(standalone, ".next", "static"), { recursive: true });
await cp(path.join(root, "public"), path.join(standalone, "public"), { recursive: true }).catch(() => undefined);

process.env.PORT ??= "3000";
process.env.HOSTNAME ??= "127.0.0.1";
process.chdir(standalone);
await import(pathToFileURL(server).href);
