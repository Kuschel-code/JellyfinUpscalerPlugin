// Build entry for the vendored, tree-shaken Anime4K.js bundle (WS2).
// Pulls ONLY the SIMPLE S/M 2x profiles so the embedded bundle stays small
// (~200KB) instead of shipping the upstream 9.3MB all-in-one dist/anime4k.js.
//
// Profiles replicate upstream presets.mjs exactly:
//   ANIME4KJS_SIMPLE_S_2X = [Clamp_Highlights, Restore_CNN_S, Upscale_CNN_x2_S]
//   ANIME4KJS_SIMPLE_M_2X = [Clamp_Highlights, Restore_CNN_M, Upscale_CNN_x2_M]
import {
  VideoUpscaler,
  ImageUpscaler,
  Anime4K_Clamp_Highlights,
  Anime4K_Restore_CNN_S,
  Anime4K_Restore_CNN_M,
  Anime4K_Upscale_CNN_x2_S,
  Anime4K_Upscale_CNN_x2_M,
} from "anime4k.js";

const ANIME4KJS_SIMPLE_S_2X = [Anime4K_Clamp_Highlights, Anime4K_Restore_CNN_S, Anime4K_Upscale_CNN_x2_S];
const ANIME4KJS_SIMPLE_M_2X = [Anime4K_Clamp_Highlights, Anime4K_Restore_CNN_M, Anime4K_Upscale_CNN_x2_M];

export { VideoUpscaler, ImageUpscaler, ANIME4KJS_SIMPLE_S_2X, ANIME4KJS_SIMPLE_M_2X };
