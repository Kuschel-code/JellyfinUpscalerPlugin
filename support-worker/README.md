# Support-chat Worker (Claude Haiku fallback)

The website's support bot (`site/assets/support-bot.js`) answers from the local
knowledge base (`support-kb.json`) and live GitHub-issue search — **no key, no
backend, works offline**. This Cloudflare Worker is an **optional** add-on: when
the KB can't answer a question, the bot asks **Claude Haiku** through this Worker.

Why a Worker? GitHub Pages is static, so it can't hold an API key. The Worker is
a tiny proxy that holds the key as an **encrypted secret** and talks to Anthropic.
The key never lives in the repo, this folder, or the browser.

## Deploy (≈5 min)

> Use a **separate Anthropic account's** API key here, so your main key/budget is
> never touched. **Never paste the key into chat, the repo, or `wrangler.toml`** —
> only into `wrangler secret put` (it's stored encrypted).

1. Create a free Cloudflare account: <https://dash.cloudflare.com/sign-up>
2. Install Wrangler and log in:
   ```
   npm install -g wrangler
   wrangler login
   ```
3. From this folder, set the key as a secret (you'll be prompted to paste it):
   ```
   cd support-worker
   wrangler secret put ANTHROPIC_API_KEY
   ```
4. Deploy:
   ```
   wrangler deploy
   ```
   Wrangler prints the URL, e.g. `https://upscaler-support-chat.<your-subdomain>.workers.dev`
5. Put that URL into [`site/assets/support-bot.js`](../site/assets/support-bot.js):
   ```js
   var HAIKU_ENDPOINT = "https://upscaler-support-chat.<your-subdomain>.workers.dev";
   ```
   Commit + push — GitHub Pages redeploys and the Haiku fallback goes live.
   (Leave it `""` to keep the bot KB-only.)

## How it behaves

- KB has a confident match → instant local answer (free, offline).
- No KB match **and** `HAIKU_ENDPOINT` set → the bot sends the question plus the
  most relevant KB entries as context to the Worker → Haiku replies (shown with an
  **AI** badge).
- Worker unreachable / not configured → graceful fall back to live GitHub-issue search.

## Cost & abuse control

- Model: `claude-haiku-4-5`, capped at `max_tokens: 1024`; question capped at 2000
  chars. Only fires on a KB miss, so most chats cost nothing.
- The Worker rejects requests whose `Origin` isn't the site (`ALLOWED_ORIGINS` in
  `worker.js`). For a public site, also enable Cloudflare's **Rate limiting** rule
  on the Worker route (dashboard → Security → Rate limiting), e.g. 20 req/min/IP.
- Watch spend in the Anthropic console of the second account; set a budget alert.

## Files

- `worker.js` — the proxy (Origin check, Anthropic call, CORS).
- `wrangler.toml` — Worker config (no secrets).
