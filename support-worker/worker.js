/*
 * Cloudflare Worker — Claude Haiku fallback for the AI Upscaler support bot.
 *
 * The website (GitHub Pages) is static, so it cannot hold an API key. This tiny
 * Worker is the proxy: it holds the Anthropic key ONLY as the Worker secret
 * ANTHROPIC_API_KEY (set via `wrangler secret put ANTHROPIC_API_KEY`) and forwards
 * the question to Claude Haiku. The key is never in this file, the repo, or the
 * browser. Connect a SEPARATE Anthropic account's key so your main key is untouched.
 *
 * Deploy: see README.md in this folder.
 */

const ALLOWED_ORIGINS = [
  "https://kuschel-code.github.io",
  "http://localhost:8080", // local preview
];
const MODEL = "claude-haiku-4-5";   // latest Haiku
const MAX_QUESTION = 2000;          // chars — caps abuse / cost
const MAX_CONTEXT = 6000;
const MAX_TOKENS = 1024;

function corsHeaders(origin) {
  const allow = ALLOWED_ORIGINS.includes(origin) ? origin : ALLOWED_ORIGINS[0];
  return {
    "Access-Control-Allow-Origin": allow,
    "Access-Control-Allow-Methods": "POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
    "Vary": "Origin",
  };
}
function json(obj, status, origin) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { ...corsHeaders(origin), "Content-Type": "application/json" },
  });
}

export default {
  async fetch(request, env) {
    const origin = request.headers.get("Origin") || "";

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders(origin) });
    }
    if (request.method !== "POST") {
      return json({ error: "Method not allowed" }, 405, origin);
    }
    // Only answer for our own site (cheap abuse gate; pair with Cloudflare rate-limiting).
    if (!ALLOWED_ORIGINS.includes(origin)) {
      return json({ error: "Forbidden origin" }, 403, origin);
    }
    if (!env.ANTHROPIC_API_KEY) {
      return json({ error: "Worker missing ANTHROPIC_API_KEY secret" }, 500, origin);
    }

    let body;
    try { body = await request.json(); } catch { return json({ error: "Bad JSON" }, 400, origin); }
    const question = String(body.question || "").slice(0, MAX_QUESTION).trim();
    const context = String(body.context || "").slice(0, MAX_CONTEXT);
    if (!question) return json({ error: "Empty question" }, 400, origin);

    const system = [
      "You are the support assistant for the Jellyfin AI Upscaler Plugin (github.com/Kuschel-code/JellyfinUpscalerPlugin),",
      "a Jellyfin plugin paired with a Dockerised AI upscaling service (port 5000).",
      "Answer ONLY about this plugin and its Docker AI service. Be concise and give numbered, copy-pasteable steps.",
      "Use the KNOWLEDGE BASE below as your primary source. If the answer is not covered and you are unsure,",
      "say so plainly and suggest opening a GitHub issue. Never invent version numbers, endpoints, or settings.",
      "Plain text with light markdown only (no tables).",
      "",
      "KNOWLEDGE BASE:",
      context || "(none provided)",
    ].join("\n");

    let upstream;
    try {
      upstream = await fetch("https://api.anthropic.com/v1/messages", {
        method: "POST",
        headers: {
          "x-api-key": env.ANTHROPIC_API_KEY,
          "anthropic-version": "2023-06-01",
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          model: MODEL,
          max_tokens: MAX_TOKENS,
          system,
          messages: [{ role: "user", content: question }],
        }),
      });
    } catch (e) {
      return json({ error: "Upstream fetch failed" }, 502, origin);
    }

    if (!upstream.ok) {
      const detail = (await upstream.text()).slice(0, 300);
      return json({ error: "Upstream error", status: upstream.status, detail }, 502, origin);
    }

    const data = await upstream.json();
    const answer = (data.content || []).map((b) => b.text || "").join("").trim();
    return json({ answer }, 200, origin);
  },
};
