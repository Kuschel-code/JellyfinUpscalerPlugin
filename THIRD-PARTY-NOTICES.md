# Third-Party Notices

This product bundles third-party software. Their copyright notices and license
terms are reproduced below as required by their licenses.

---

## Anime4K.js (WebGL Anime4K port)

The plugin embeds a **vendored, tree-shaken** build of `Anime4K.js` inside
`JellyfinUpscalerPlugin.dll` (served at runtime as `Configuration/anime4k.js`)
for the opt-in "Anime4K (anime shader)" real-time tier.

- **Project:** monyone/Anime4K.js — <https://github.com/monyone/Anime4K.js>
- **Version pinned:** npm `anime4k.js` **v1.1.2** (commit `375c03f02f07642dc4a6fa127d9c4bb6123c1109`)
- **Implements:** Anime4K **4.0.1** GLSL shaders (WebGL)
- **Bundle scope:** only the `SIMPLE_S_2X` and `SIMPLE_M_2X` 2× profiles
  (`Clamp_Highlights` + `Restore_CNN_{S,M}` + `Upscale_CNN_x2_{S,M}`) to keep the
  embedded size ~225 KB instead of the full ~9.3 MB upstream bundle.
- **License:** MIT

```
MIT License

Copyright (c) 2023 もにょ～ん

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Anime4K core algorithm

`Anime4K.js` is a port of the original **Anime4K** GLSL shaders.

- **Project:** bloc97/Anime4K — <https://github.com/bloc97/Anime4K>
- **License:** MIT

The original Anime4K project is also MIT-licensed; the shaders embedded here are
derived from it via the monyone/Anime4K.js port. Full upstream text:
<https://github.com/bloc97/Anime4K/blob/master/LICENSE>.

---

*Rebuild provenance:* the embedded bundle is produced by the throwaway
`.a4k-build` harness (`npm install && node build.mjs`), which pins
`anime4k.js@1.1.2` and emits `Configuration/anime4k.js` with the banner header.
