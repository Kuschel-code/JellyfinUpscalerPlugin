/*
 * Cloudflare Worker — free AI fallback for the AI Upscaler support bot.
 *
 * Uses **Cloudflare Workers AI** (env.AI binding) — no API key, no external
 * account, no billing. Cloudflare's free daily allowance is plenty for a
 * low-traffic support bot. The website's support-bot.js calls this Worker only
 * when its local knowledge base can't answer a question.
 *
 * Setup (dashboard): Worker -> Settings -> Bindings -> Add -> Workers AI,
 *   variable name: AI   (that's it — no secret needed).
 * Deploy: see README.md in this folder.
 */

const ALLOWED_ORIGINS = [
  "https://kuschel-code.github.io",
  "http://localhost:8080", // local preview
];
const MODEL = "@cf/meta/llama-3.3-70b-instruct-fp8-fast"; // free Workers AI text model
const MAX_QUESTION = 2000;
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
    if (!ALLOWED_ORIGINS.includes(origin)) {
      return json({ error: "Forbidden origin" }, 403, origin);
    }
    if (!env.AI) {
      return json({ error: "Worker missing AI binding (add a Workers AI binding named 'AI')" }, 500, origin);
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

    let result;
    try {
      result = await env.AI.run(MODEL, {
        max_tokens: MAX_TOKENS,
        messages: [
          { role: "system", content: system },
          { role: "user", content: question },
        ],
      });
    } catch (e) {
      return json({ error: "AI run failed", detail: String(e && e.message || e).slice(0, 300) }, 502, origin);
    }

    const answer = String((result && (result.response ?? result.text)) || "").trim();
    if (!answer) return json({ error: "Empty AI response" }, 502, origin);
    return json({ answer }, 200, origin);
  },
};
