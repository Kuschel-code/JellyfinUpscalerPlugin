// Produces ../Configuration/anime4k.js — the vendored, tree-shaken Anime4K.js
// bundle embedded in the plugin DLL. Run: `node build.mjs`. Not committed.
import { build } from "esbuild";

const SRC_VERSION = "1.1.2";
const SRC_COMMIT = "375c03f02f07642dc4a6fa127d9c4bb6123c1109";

const banner = `/*!
 * anime4k.js — VENDORED, TREE-SHAKEN BUILD (generated; do not edit by hand).
 * Rebuild via the .a4k-build harness (npm i && node build.mjs).
 *
 * Source : monyone/Anime4K.js v${SRC_VERSION} (commit ${SRC_COMMIT})
 *          https://github.com/monyone/Anime4K.js  — MIT License, (c) 2023 monyone
 * Core   : Anime4K 4.0.1 GLSL algorithm by bloc97
 *          https://github.com/bloc97/Anime4K       — MIT License
 *
 * This bundle contains ONLY the SIMPLE S/M 2x profiles
 * (Clamp_Highlights + Restore_CNN_{S,M} + Upscale_CNN_x2_{S,M}) to stay small.
 * Full third-party license text: ../THIRD-PARTY-NOTICES.md
 */`;

await build({
  entryPoints: ["entry.mjs"],
  bundle: true,
  format: "iife",
  globalName: "Anime4KJS",
  minify: true,
  legalComments: "none",
  banner: { js: banner },
  outfile: "../Configuration/anime4k.js",
});
console.log("wrote ../Configuration/anime4k.js");
