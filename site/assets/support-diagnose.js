/* Support diagnostics - shared by the floating Support Assistant (support-bot.js) and the
 * homepage console (home-chat.js). No DOM access, no network: pure functions over the pasted text.
 *
 * What it reads
 *   - the JSON the AI service prints for /doctor, /status, /gpu-verify and /health
 *   - log excerpts: known failure signatures -> knowledge-base entry, plus facts taken from the
 *     text itself (the render-group ID the service names, the installed version)
 *
 * Why: users paste what they have. The service already computes the fix ("group_add: [\"105\"]",
 * the failing check, the reason the GPU is unused); this module hands THAT text back instead of
 * a generic answer, and says so when the paste shows an outdated version.
 *
 * Usage: window.SupportDiagnose.analyze(text, kb, latest) -> null | { kind, markdown, entries, facts }
 *   kb     the parsed support-kb.json ({entries:[{id,title,...}]}), or null
 *   latest {version:"1.8.3.36"} from the live feed, or null (then no update hint is given)
 */
(function () {
  "use strict";

  var MAX_TEXT = 100000;     // never run regexes over more than this
  var MAX_ITEMS = 6;         // findings shown from a JSON report
  var MAX_ENTRIES = 3;       // KB entries handed back

  // Known failure signatures, most specific first. `kb` must be an id in support-kb.json
  // (tests/support-diagnose.test.cjs checks that). `head` is the one-line summary shown above
  // the KB answer.
  var GPU_HEAD = "The container cannot use the GPU. The usual cause: `group_add: render` names a group whose ID " +
    "inside the container differs from the host's render group. Use the host's numeric ID " +
    "(`stat -c '%g' /dev/dri/renderD128` on the host) in `group_add: [\"<number>\"]`.";
  var ROCM_HEAD = "AMD ROCm is not running on the GPU. docker7-amd images before v1.8.3.36 never used it (issue #98); " +
    "update the image, pass `/dev/kfd` and `/dev/dri` with the host's numeric group IDs, then check `/status` -> " +
    "`gpu_unavailable_reason`.";
  var SIGNATURES = [
    { re: /amd rocm gpu is not usable|hip failure \d+|no rocm-capable device|libamdhip64\.so[^\n]*(not found|cannot open)|libhipblas\.so[^\n]*(not found|cannot open)|ep error[\s\S]{0,400}rocm/i,
      kb: "amd-vulkan", head: ROCM_HEAD },
    { re: /device gpu is not available|ep error[\s\S]{0,400}openvino|falling back to \[?['"]?cpuexecutionprovider|openvino (is )?running on the cpu device|openvino gpu is not usable/i,
      kb: "gpu-device-not-available", head: GPU_HEAD },
    { re: /(may not|cannot|can't) open it|permission denied[^\n]*\/dev\/dri|cannot open[^\n]*renderd\d+|owned by gid|belongs to gid/i,
      kb: "gpu-device-not-available", head: GPU_HEAD },
    { re: /color metadata unavailable|realtime processing cannot be validated/i, kb: "realtime-standby" },
    { re: /HDR requires (PQ|BT\.2020)|HDR realtime and (masking|multi-frame processing) are not supported|unknown transfer functions are not supported|Dynamic HDR \(Dolby Vision/i, kb: "hdr-support" },
    { re: /rate limit exceeded|circuit breaker (open|half-open)|too many concurrent requests|"detail"\s*:\s*"Busy"|ai service is busy|did not produce an upscaled frame|HTTP 503|HTTP 429/i, kb: "service-busy",
      head: "The AI service is busy or cooling down (HTTP 429/503). Since v1.8.3.36 library jobs wait this out automatically (up to 6 times per frame, following Retry-After); older versions fail the whole video on one rejected frame." },
    { re: /no detector loaded/i, kb: "object-masking" },
    { re: /cannot write to .*read-only|could not inject player script|access to the path .*index\.html.*denied/i, kb: "player-button-missing" },
    { re: /sha256 mismatch/i, kb: "model-sha256-mismatch" },
    { re: /checksum (mismatch|failed|did not match)|package .*checksum/i, kb: "install-checksum" },
    { re: /not supported.*abi|targetabi|abi.*mismatch/i, kb: "not-supported-abi" },
    { re: /cuda driver version is insufficient|failed to initialize nvml|could not select device driver|nvidia-smi has failed|libcuda\.so[^\n]*cannot open/i, kb: "nvidia-error" },
    { re: /rocmexecutionprovider (is )?missing|build fail[^\n]*rocm/i, kb: "amd-vulkan" },
    { re: /azureexecutionprovider(?![\s\S]*cudaexecutionprovider)/i, kb: "gpu-on-cpu" },
    { re: /ai service unreachable|connection refused.*:5000|unable to connect.*(docker|:5000)|econnrefused.*5000/i, kb: "docker-unreachable" },
    { re: /missingmethodexception|typeloadexception/i, kb: "jellyfin-12" },
    { re: /invalid_graph|invalidgraph/i, kb: "models-self-host" },
    { re: /reshape.*(fp16|input)|invalid_argument.*onnx/i, kb: "onnx-reshape-fp16" },
    { re: /ffmpeg.*(exited|crash|code 1)|no such file.*ffmpeg/i, kb: "ffmpeg-crash" }
  ];

  // /doctor checks -> the KB entry that explains them
  var CHECK_KB = {
    gpu_device_access: "gpu-device-not-available", gpu_provider_active: "gpu-device-not-available",
    device_passthrough: "gpu-on-cpu", onnx_provider_pkg: "gpu-on-cpu", api_token: "api-token"
  };
  // Order matters: the specific cause first, the generic "driver mismatch" advice last.
  var CHECK_PRIORITY = { gpu_device_access: 0, device_passthrough: 1, onnx_provider_pkg: 2, gpu_provider_active: 8 };

  var GPU_EP = /(CUDA|Tensorrt|OpenVINO|ROCM|MIGraphX|CoreML|Dml)ExecutionProvider/;

  function clip(s, n) { s = String(s == null ? "" : s).replace(/\s+/g, " ").trim(); return s.length > n ? s.slice(0, n - 1) + "…" : s; }
  function byId(kb, id) {
    var list = kb && kb.entries; if (!list) return null;
    for (var i = 0; i < list.length; i++) if (list[i].id === id) return list[i];
    return null;
  }
  function addEntry(out, kb, id) {
    var e = byId(kb, id);
    if (e && out.indexOf(e) === -1 && out.length < MAX_ENTRIES) out.push(e);
  }

  // ---- facts taken from the text itself ---------------------------------------------------
  function gidFrom(text) {
    var m = /belongs to GID\s*\[?\s*(\d+)/i.exec(text) || /owned by GID\s*\[?\s*(\d+)/i.exec(text) ||
            /group_add:\s*\[\s*\\?"(\d+)\\?"/i.exec(text) || /gid=(\d+)[^\n]*accessible=False/i.exec(text);
    return m ? m[1] : null;
  }
  function parseVersion(v) { var p = String(v).split("."), o = []; for (var i = 0; i < p.length; i++) o.push(parseInt(p[i], 10) || 0); return o; }
  function olderThan(a, b) {
    var x = parseVersion(a), y = parseVersion(b);
    for (var i = 0; i < Math.max(x.length, y.length); i++) { var d = (x[i] || 0) - (y[i] || 0); if (d !== 0) return d < 0; }
    return false;
  }
  function installedVersion(text, obj) {
    if (obj && typeof obj.version === "string" && /^1\.\d+\.\d+\.\d+$/.test(obj.version)) return obj.version;
    var all = String(text).match(/\bv?1\.\d+\.\d+\.\d+\b/g) || [], best = null;
    for (var i = 0; i < all.length; i++) { var v = all[i].replace(/^v/, ""); if (!best || olderThan(best, v)) best = v; }
    return best;   // the newest version mentioned = the one actually installed
  }
  function versionHint(version, latest) {
    if (!version || !latest || !latest.version || !olderThan(version, latest.version)) return "";
    return "\n\n**Newer version available:** your paste shows v" + version + ", the latest release is v" + latest.version +
      ". Update first (Jellyfin Dashboard → Plugins → Catalog → AI Upscaler → Update, restart Jellyfin; Docker: pull the newest image and recreate the container), then check again - the problem may already be fixed.";
  }

  // ---- JSON reports -----------------------------------------------------------------------
  function parseJson(text) {
    var t = String(text).trim();
    try { var whole = JSON.parse(t); if (whole && typeof whole === "object") return whole; } catch (e) { /* maybe wrapped in a prompt */ }
    var a = t.indexOf("{"), b = t.lastIndexOf("}");
    if (a !== -1 && b > a) { try { var o = JSON.parse(t.slice(a, b + 1)); if (o && typeof o === "object") return o; } catch (e2) { /* not JSON */ } }
    return null;
  }
  function kindOf(o) {
    if (Array.isArray(o.checks) && o.checks.length && o.checks[0] && o.checks[0].check !== undefined) return "doctor";
    if ("onnx_providers" in o || ("active_providers" in o && "gpu_requested" in o)) return "gpu-verify";
    if (o.status === "running" && ("using_gpu" in o || "available_providers" in o)) return "status";
    if ("circuit_open" in o || ("model_loaded" in o && "status" in o)) return "health";
    return null;
  }

  function fromDoctor(o, kb, out) {
    var bad = o.checks.filter(function (c) { return c && c.status && c.status !== "ok"; });
    var facts = { gid: null };
    var md;
    if (!bad.length) {
      md = "**/doctor: all checks passed** (overall: " + clip(o.overall || "ok", 10) + "). No problems found in this report. " +
        "If something still misbehaves, send the log lines around the error.";
    } else {
      bad.sort(function (x, y) {
        var px = CHECK_PRIORITY[x.check] != null ? CHECK_PRIORITY[x.check] : 5, py = CHECK_PRIORITY[y.check] != null ? CHECK_PRIORITY[y.check] : 5;
        return px - py;
      });
      md = "**/doctor found " + bad.length + (bad.length === 1 ? " problem" : " problems") + " (overall: " + clip(o.overall || "?", 10) + "):**";
      bad.slice(0, MAX_ITEMS).forEach(function (c) {
        md += "\n- **" + (c.status === "fail" ? "fail" : "warn") + " · `" + clip(c.check, 40) + "`** - " + clip(c.detail, 220) +
          (c.fix ? "\n  **Fix:** " + clip(c.fix, 420) : "");
        if (!facts.gid) facts.gid = gidFrom((c.detail || "") + " " + (c.fix || ""));
        if (CHECK_KB[c.check]) addEntry(out, kb, CHECK_KB[c.check]);
      });
      if (bad.length > MAX_ITEMS) md += "\n- … and " + (bad.length - MAX_ITEMS) + " more";
    }
    return { markdown: md, facts: facts };
  }

  function fromStatus(o, kb, out) {
    var facts = { gid: null }, md;
    var providers = (o.available_providers || []).join(", ");
    if (o.using_gpu === false) {
      md = "**The service reports it is NOT using the GPU** (providers: " + clip(providers || "none", 120) + ").";
      if (o.gpu_unavailable_reason) {
        md += "\n**Reason from the service:** " + clip(o.gpu_unavailable_reason, 520);
        facts.gid = gidFrom(o.gpu_unavailable_reason);
        addEntry(out, kb, "gpu-device-not-available");
      } else if (!GPU_EP.test(providers)) {
        md += "\nNo GPU provider is present at all: this is the CPU image or the wrong/old image, not a permission problem.";
        addEntry(out, kb, "gpu-on-cpu");
      } else {
        md += "\nA GPU provider exists but nothing reports why it is unused. Open `/doctor` and paste it here.";
        addEntry(out, kb, "setup-doctor");
      }
    } else if (o.using_gpu === true) {
      md = "**The service reports it is using the GPU** (providers: " + clip(providers, 120) + "). Nothing wrong in this report; if it is slow, check the model (`/models`) and `/gpu-verify`.";
    } else {
      md = "This `/status` report does not say whether the GPU is used. Paste `/doctor` for a full check.";
      addEntry(out, kb, "setup-doctor");
    }
    return { markdown: md, facts: facts };
  }

  function fromGpuVerify(o, kb, out) {
    var facts = { gid: null }, md = "", findings = [];
    var avail = (o.onnx_providers || []).join(", "), active = (o.active_providers || []).join(", ");
    var nodes = Array.isArray(o.render_nodes) ? o.render_nodes : [];
    var locked = nodes.filter(function (n) { return n && n.accessible === false; });
    if (locked.length) {
      facts.gid = locked[0].gid != null ? String(locked[0].gid) : null;
      findings.push("`" + clip(locked[0].path, 60) + "` exists but this container user cannot open it (owner group ID **" + clip(locked[0].gid, 10) + "**). " +
        "**Fix:** use the host's numeric ID - compose `group_add: [\"" + clip(locked[0].gid, 10) + "\"]` (or `--group-add " + clip(locked[0].gid, 10) + "`), then recreate the container. `group_add: render` does not work: the name resolves to a different ID inside the container.");
      addEntry(out, kb, "gpu-device-not-available");
    }
    if (avail && !GPU_EP.test(avail)) {
      findings.push("Only CPU/Azure providers exist (" + clip(avail, 100) + "): this is the CPU image or a wrong/old image, not a permission problem. Pull `docker7-intel` / `docker7-amd` / `docker7` and recreate.");
      addEntry(out, kb, "gpu-on-cpu");
    } else if (avail && o.using_gpu === false && !locked.length) {
      findings.push("A GPU provider exists (" + clip(avail, 100) + ") but is not active (active: " + clip(active || "none", 100) + "). " + (o.gpu_unavailable_reason ? "Reason: " + clip(o.gpu_unavailable_reason, 400) : "The GPU runtime failed to start: check the host driver and `docker logs`, and paste `/doctor`."));
      if (o.gpu_unavailable_reason) facts.gid = facts.gid || gidFrom(o.gpu_unavailable_reason);
      addEntry(out, kb, o.gpu_unavailable_reason ? "gpu-device-not-available" : "gpu-on-cpu");
    }
    if (!findings.length) {
      md = o.using_gpu === true ? "**/gpu-verify: the GPU is in use** (active: " + clip(active, 120) + "). Nothing wrong in this report."
                                : "**/gpu-verify shows no clear fault.** Paste `/doctor` for a full check.";
      if (o.using_gpu !== true) addEntry(out, kb, "setup-doctor");
    } else {
      md = "**/gpu-verify analysis:**" + findings.slice(0, MAX_ITEMS).map(function (f) { return "\n- " + f; }).join("");
    }
    return { markdown: md, facts: facts };
  }

  function fromHealth(o, kb, out) {
    if (o.circuit_open) {
      addEntry(out, kb, "service-busy");
      return { markdown: "**The circuit breaker is open** (status: " + clip(o.status, 20) + "): after repeated failures the service refuses work for a short cooldown (about 10 s) and recovers on its own. " +
        "If it keeps reopening, look at the service log for the error that causes the failures.", facts: { gid: null } };
    }
    return { markdown: "**/health: the service is " + clip(o.status || "ok", 20) + "** (model loaded: " + (o.model_loaded ? "yes" : "no") + "). Nothing wrong in this report.", facts: { gid: null } };
  }

  // ---- log text ---------------------------------------------------------------------------
  function fromLog(text, kb, out) {
    var heads = [], seen = {}, hit = false;
    for (var i = 0; i < SIGNATURES.length; i++) {
      var s = SIGNATURES[i];
      if (!s.re.test(text)) continue;
      hit = true;
      addEntry(out, kb, s.kb);
      if (s.head && !seen[s.kb]) { seen[s.kb] = 1; heads.push(s.head); }
    }
    if (!hit) return null;
    var gid = gidFrom(text), md = "**Known problem found in your text.**";
    if (gid) {
      md += "\n**Your log already names the fix:** the GPU device belongs to group ID **" + gid + "**. In your compose add `group_add: [\"" + gid + "\"]` " +
        "(or `docker run --group-add " + gid + "`), recreate the container, then open `/doctor` to confirm. `group_add: render` does not work: the name resolves to a different ID inside the container.";
    } else {
      heads.slice(0, 2).forEach(function (h) { md += "\n" + h; });
    }
    return { markdown: md, facts: { gid: gid } };
  }

  function analyze(text, kb, latest) {
    if (typeof text !== "string") return null;
    text = text.slice(0, MAX_TEXT);
    if (text.trim().length < 8) return null;
    var out = [], res = null, obj = parseJson(text), kind = obj ? kindOf(obj) : null;
    if (kind === "doctor") res = fromDoctor(obj, kb, out);
    else if (kind === "status") res = fromStatus(obj, kb, out);
    else if (kind === "gpu-verify") res = fromGpuVerify(obj, kb, out);
    else if (kind === "health") res = fromHealth(obj, kb, out);
    else { kind = "log"; res = fromLog(text, kb, out); }
    if (!res) return null;
    var md = res.markdown + versionHint(installedVersion(text, obj), latest);
    return { kind: kind, markdown: md, entries: out, facts: res.facts || { gid: null } };
  }

  function signatureEntryIds() {
    var ids = [], k;
    for (var i = 0; i < SIGNATURES.length; i++) if (ids.indexOf(SIGNATURES[i].kb) === -1) ids.push(SIGNATURES[i].kb);
    for (k in CHECK_KB) if (ids.indexOf(CHECK_KB[k]) === -1) ids.push(CHECK_KB[k]);
    ids.push("setup-doctor");
    return ids;
  }

  window.SupportDiagnose = { analyze: analyze, signatureEntryIds: signatureEntryIds };
})();
