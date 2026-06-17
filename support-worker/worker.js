/*
 * Cloudflare Worker — free AI fallback for the AI Upscaler support bot.
 *
 * Backend priority:
 *   1. GROQ  — if the secret GROQ_API_KEY is set (very fast). Set via:
 *        wrangler secret put GROQ_API_KEY   (or dashboard → Settings → Variables and Secrets → Secret)
 *      Get a FREE key at https://console.groq.com (no credit card).
 *   2. Cloudflare WORKERS AI (env.AI binding) — free, no key, no billing.
 *      Add via dashboard → Settings → Bindings → Add → Workers AI, name: AI.
 *
 * If Groq is configured it's used (fast); otherwise / on Groq error it falls back
 * to Workers AI. The website's support-bot calls this Worker only when its local
 * knowledge base can't answer. No API key ever lives in the repo or the browser.
 */

const ALLOWED_ORIGINS = [
  "https://kuschel-code.github.io",
  "http://localhost:8080", // local preview
];
const CF_MODEL = "@cf/meta/llama-3.3-70b-instruct-fp8-fast"; // free Workers AI model
const GROQ_MODEL = "llama-3.3-70b-versatile";                // Groq model (fast)
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

    if (request.method === "OPTIONS") return new Response(null, { headers: corsHeaders(origin) });
    if (request.method !== "POST") return json({ error: "Method not allowed" }, 405, origin);
    if (!ALLOWED_ORIGINS.includes(origin)) return json({ error: "Forbidden origin" }, 403, origin);

    let body;
    try { body = await request.json(); } catch { return json({ error: "Bad JSON" }, 400, origin); }
    const question = String(body.question || "").slice(0, MAX_QUESTION).trim();
    const context = String(body.context || "").slice(0, MAX_CONTEXT);
    if (!question) return json({ error: "Empty question" }, 400, origin);

    const system = [
      "You are the support assistant for the Jellyfin AI Upscaler Plugin (github.com/Kuschel-code/JellyfinUpscalerPlugin),",
      "a Jellyfin plugin paired with a Dockerised AI upscaling service (port 5000).",
      "Answer ONLY about this plugin and its Docker AI service. Be concise and give numbered, copy-pasteable steps.",
      "Reply in the user's language (German or English). Use the KNOWLEDGE BASE below as your primary source; if it",
      "doesn't cover the question and you're unsure, say so and suggest opening a GitHub issue. Never invent version",
      "numbers, endpoints, or settings. Plain text with light markdown only (no tables).",
      "",
      "KNOWLEDGE BASE:",
      context || "(none provided)",
    ].join("\n");

    // 1) Groq (fast) — only if a key is configured.
    if (env.GROQ_API_KEY) {
      try {
        const r = await fetch("https://api.groq.com/openai/v1/chat/completions", {
          method: "POST",
          headers: { "Authorization": "Bearer " + env.GROQ_API_KEY, "Content-Type": "application/json" },
          body: JSON.stringify({
            model: GROQ_MODEL,
            max_tokens: MAX_TOKENS,
            messages: [{ role: "system", content: system }, { role: "user", content: question }],
          }),
        });
        if (r.ok) {
          const d = await r.json();
          const answer = String((d.choices && d.choices[0] && d.choices[0].message && d.choices[0].message.content) || "").trim();
          if (answer) return json({ answer, via: "groq" }, 200, origin);
        }
        // non-ok or empty -> fall through to Workers AI
      } catch (e) { /* fall through to Workers AI */ }
    }

    // 2) Cloudflare Workers AI (free, no key).
    if (!env.AI) {
      return json({ error: "No AI backend configured (set GROQ_API_KEY secret, or add a Workers AI binding named 'AI')" }, 500, origin);
    }
    let result;
    try {
      result = await env.AI.run(CF_MODEL, {
        max_tokens: MAX_TOKENS,
        messages: [{ role: "system", content: system }, { role: "user", content: question }],
      });
    } catch (e) {
      return json({ error: "AI run failed", detail: String(e && e.message || e).slice(0, 300) }, 502, origin);
    }
    const answer = String((result && (result.response ?? result.text)) || "").trim();
    if (!answer) return json({ error: "Empty AI response" }, 502, origin);
    return json({ answer, via: "workers-ai" }, 200, origin);
  },
};
