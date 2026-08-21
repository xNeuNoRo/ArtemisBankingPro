import { spawnSync } from "node:child_process";
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../wwwroot/js/", import.meta.url));

function collectJavaScript(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory()
      ? collectJavaScript(path)
      : entry.name.endsWith(".js")
        ? [path]
        : [];
  });
}

const files = collectJavaScript(root).sort();
const requiredFiles = [
  "auth.js",
  "feedback.js",
  "forms.js",
  "navigation.js",
  "site.js",
  "theme-bootstrap.js",
  "theme.js",
];

const missingFiles = requiredFiles.filter((name) => !files.includes(join(root, name)));
if (missingFiles.length > 0) {
  console.error(`Required JavaScript modules are missing: ${missingFiles.join(", ")}`);
  process.exit(1);
}

const forbiddenPatterns = [
  /RealEstateApp/i,
  /(?:SweetAlert|window\.Swal|window\.lucide)/i,
  /(?:serviceWorker|navigator\.serviceWorker)/i,
  /(?:innerHTML|outerHTML|insertAdjacentHTML|document\.write)/i,
  /https?:\/\//i,
];

for (const file of files) {
  const source = readFileSync(file, "utf8");
  for (const pattern of forbiddenPatterns) {
    if (pattern.test(source)) {
      console.error(`Forbidden JavaScript pattern in ${file}: ${pattern}`);
      process.exit(1);
    }
  }
}

for (const file of files) {
  const result = spawnSync(process.execPath, ["--check", file], {
    stdio: "inherit",
  });
  if (result.status !== 0) process.exit(result.status ?? 1);
}

process.stdout.write(`JavaScript syntax verified: ${files.length} files.\n`);
