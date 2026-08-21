import { readFileSync, statSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const rootDirectory = resolve(scriptDirectory, "..");
const outputPath = resolve(rootDirectory, "wwwroot/css/site.css");
const sourcePaths = [
  resolve(rootDirectory, "Styles/app.css"),
  resolve(rootDirectory, "Styles/components.css"),
];
const requiredOutput = [
  "--color-brand-500",
  "--breakpoint-shell",
  "--ui-canvas",
  "--ui-shadow-focus",
  "--ui-table-row-min",
  ".ui-button--primary",
  ".ui-visually-hidden",
  ".ui-table-wrap",
  ".ui-empty-state",
  ".app-shell",
  ".app-sidebar",
  ".auth-shell",
  ".ui-nav-item",
  "prefers-reduced-motion",
  "@view-transition",
  "::view-transition-old(app-main)",
];
const forbiddenSource = [/transition\s*:\s*all/i, /transition-all/i];

let output;
try {
  output = readFileSync(outputPath, "utf8");
} catch (error) {
  console.error(`CSS output could not be read: ${outputPath}`);
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
}

if (statSync(outputPath).size === 0 || output.trim().length === 0) {
  console.error(`CSS output is empty: ${outputPath}`);
  process.exit(1);
}

const missingOutput = requiredOutput.filter((token) => !output.includes(token));
if (missingOutput.length > 0) {
  console.error(`CSS output is missing required contracts: ${missingOutput.join(", ")}`);
  process.exit(1);
}

for (const sourcePath of sourcePaths) {
  const source = readFileSync(sourcePath, "utf8");
  for (const pattern of forbiddenSource) {
    if (pattern.test(source)) {
      console.error(`Forbidden transition pattern in ${sourcePath}: ${pattern}`);
      process.exit(1);
    }
  }
}

console.log(`CSS verified: ${requiredOutput.length} contracts in ${outputPath}.`);
