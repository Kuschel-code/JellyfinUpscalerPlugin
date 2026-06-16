# Support-chat Worker (free AI fallback via Cloudflare Workers AI)

The website's support bot (`site/assets/support-bot.js`) answers from the local
knowledge base (`support-kb.json`) and live GitHub-issue search — no key, no
backend, works offline. This Cloudflare Worker is an **optional** add-on: when the
KB can't answer a question, the bot asks an LLM through this Worker.

It uses **Cloudflare Workers AI** (`env.AI`) — **no API key, no external account,
no billing**. Cloudflare's free daily allowance is plenty for a low-traffic support
bot. Model: `@cf/meta/llama-3.1-8b-instruct` (swap in `worker.js` if you like).

## Deploy (dashboard, ~3 min)

1. **dash.cloudflare.com** → **Workers & Pages** → **Create** → **Create Worker** →
   name `upscaler-support-chat` → **Deploy**.
2. **Edit code** → replace the Hello-World code with `worker.js` from this folder
   (raw: `https://raw.githubusercontent.com/Kuschel-code/JellyfinUpscalerPlugin/main/support-worker/worker.js`)
   → **Deploy**.
3. **Settings → Bindings → Add → Workers AI** → variable name exactly **`AI`** →
   Save/Deploy. *(No secret, no key.)*
4. Copy the Worker URL and put it into
   [`site/assets/support-bot.js`](../site/assets/support-bot.js) at `HAIKU_ENDPOINT`,
   then push. (Leave it `""` to keep the bot KB-only.)

## How it behaves

- KB has a confident match → instant local answer (free, offline).
- No KB match **and** `HAIKU_ENDPOINT` set → the bot sends the question + the most
  relevant KB entries as context to the Worker → Workers AI replies (shown with an
  **AI** badge).
- Worker unreachable / not configured → graceful fall back to live GitHub-issue search.

## Notes

- Free tier: Workers AI gives a daily Neuron allowance on all plans (incl. the free
  Workers plan). An 8B-model reply is cheap; only fires on a KB miss.
- Abuse control: the Worker rejects requests whose `Origin` isn't the site
  (`ALLOWED_ORIGINS` in `worker.js`); question capped at 2000 chars. For a public
  site you can also add a Cloudflare rate-limiting rule on the Worker route.

## Files

- `worker.js` — the proxy (Origin check, Workers AI call, CORS).
- `wrangler.toml` — Worker config incl. the `AI` binding (no secrets).
