# Story Generation

Two LLM-backed narrative scripts read `world.db` and send a curated timeline to a
local Ollama model:

- `scripts/generate-story.py` — a single character's life, 300–500 words.
- `scripts/generate-civ-story.py` — a civilization's full arc, 900–1500 words,
  with the founder and a sample of its rulers woven in as characters rather than
  a faceless institution.

Both share the same event-formatting convention: raw game stats (damage, health)
are never passed to the model as bare numbers — see "Raw Stats Are Never Cited"
below.

For deterministic (non-LLM) structured reports instead of prose, see
`scripts/character-analysis.py` and `scripts/civ-history.py`.

---

## Prerequisites

- **Ollama** running with at least one model pulled (see model note below)
- Python 3 (stdlib only — no pip installs needed)
- A `world.db` produced by a simulation run

**Model in use:** `gemma4:e4b` (default). Pull it if not already present:
```
ollama pull gemma4:e4b
```

---

## WSL2 Setup (Ollama on Windows)

By default, Ollama on Windows only listens on `127.0.0.1`, which WSL2 cannot reach.
You need to tell Ollama to bind to all interfaces **before** starting it:

1. Open Windows PowerShell (not WSL):
   ```powershell
   $env:OLLAMA_HOST = "0.0.0.0"
   ollama serve
   ```
   Or set it permanently in Windows environment variables (`OLLAMA_HOST = 0.0.0.0`).

That's it — you no longer need to look up or pass `--host` yourself. Both scripts
auto-detect where Ollama is reachable (see "Host Auto-Detection" below) and print
which one they picked, e.g. `Using Ollama at http://172.17.240.1:11434`.

If you ever do need to override it (a non-default port, a remote host, etc.), pass
`--host` explicitly or set the `OLLAMA_HOST` env var — either takes priority over
auto-detection.

---

## Host Auto-Detection

Both scripts try, in order, until one answers `/api/tags`:
1. The `OLLAMA_HOST` environment variable, if set.
2. `http://localhost:11434`.
3. If running under WSL2: the default-route gateway IP (`ip route show default`) —
   this is what Windows-side Ollama binds to once `OLLAMA_HOST=0.0.0.0` is set there.

The gateway IP changes when WSL2 restarts, which is exactly why this is
auto-detected at runtime rather than hardcoded — no more digging it up by hand
every session. If nothing answers, the script still runs with `localhost` as the
default and reports the real connection error when it actually tries to call Ollama.

---

## Basic Usage

```bash
# Generate stories for the 3 most notable characters (host auto-detected)
python3 scripts/generate-story.py publish/win-x64/world.db

# Generate 5 characters, save each to a text file
python3 scripts/generate-story.py world.db --top 5 --out stories/

# Target a specific character by ID (find IDs in world.db or from character-analysis.py)
python3 scripts/generate-story.py world.db --id 7503503

# Preview the prompt that will be sent (no Ollama call, no host lookup needed)
python3 scripts/generate-story.py world.db --prompt-only --top 1
```

---

## Options

| Flag | Default | Description |
|------|---------|-------------|
| `--model MODEL` | `gemma4:e4b` | Ollama model name |
| `--host URL` | auto-detected | Ollama base URL — see "Host Auto-Detection" |
| `--top N` | `3` | Pick the N most event-rich characters |
| `--id CHAR_ID` | — | Target a specific character by numeric ID |
| `--prompt-only` | false | Print the prompt without calling Ollama |
| `--out DIR` | stdout | Write each story to `DIR/<name>_<id>.txt` |
| `--style STYLE` | `biography` | Narrative style (see below) |

### Styles

| Style | Description |
|-------|-------------|
| `biography` | Third-person medieval chronicle, 300–500 words |
| `legend` | Mythologized retelling with elevated poetic language |
| `epic` | Opening passage of an epic poem (Beowulf / Iliad style) |

---

## How Characters Are Selected

`--top N` picks the N characters with the most distinct event types, excluding
goal-spam events. This surfaces rulers, conquerers, and long-lived characters
over minor background figures.

To find specific character IDs, run:
```bash
python3 scripts/character-analysis.py world.db top 10
```
or query the DB directly:
```sql
SELECT ActorId, ActorName, COUNT(*) AS n
FROM Events WHERE ActorId IS NOT NULL AND Type NOT IN (3109,3110)
GROUP BY ActorId ORDER BY n DESC LIMIT 20;
```

---

## What the Prompt Includes

The script filters events down to narrative-relevant moments:
- Birth, death, marriage, exile
- Wars declared and ended (with cause)
- Battles (collapsed into summaries when the same location repeats in one year)
- Conquests (with the losing civ named)
- Successions, appointments, dismissals
- Artworks created
- Flourishing / spiraling wellbeing shifts
- Notable goals: FoundCity, SlayBeast, Avenge (if formed/completed)

It excludes: BuildImprovement cycles, Alliance churn, trader/scholar/diplomat events,
and repetitive goal-form/abandon noise.

Total events are capped at 45 per character to keep the context window manageable.

---

---

## Civilization Stories (`generate-civ-story.py`)

```bash
# The most notable civilization (longest-lived, weighted by event richness)
python3 scripts/generate-civ-story.py world.db

# A specific civ, with up to 6 key figures woven in, saga tone
python3 scripts/generate-civ-story.py world.db --id 7 --characters 6 --style saga

# Preview the prompt (no Ollama call)
python3 scripts/generate-civ-story.py world.db --prompt-only --id 7
```

| Flag | Default | Description |
|------|---------|-------------|
| `--top N` | `1` | Pick the N most notable civilizations |
| `--id CIV_ID` | — | Target a specific civilization |
| `--characters N` | `4` | Max key figures woven into the story |
| `--style STYLE` | `chronicle` | `chronicle` (court-historian prose) or `saga` (mythologized) |

**How key figures are chosen:** the founder always gets a slot. The rest go to
rulers, sampled across the civ's lifespan (first, last, evenly spread between) —
not all of them. A civ that ran for a couple thousand years can rack up 50+
successions; dumping every ruler into the prompt would drown the actual story in
a name-list instead of surfacing which ones mattered. Any slots still free after
that go to the civ's most event-rich non-ruler members.

The civilization timeline itself gets a bigger budget than a single character's
(80 events vs. 45), since a civ's story spans generations.

---

## Raw Stats Are Never Cited

Early versions of these scripts fed raw numeric fields (`Damage: 35`) straight
into the LLM prompt, and the model would parrot them back verbatim — "dealt 35
damage" — with no sense of whether 35 is a scratch or a rout. Character and
settlement `MaxHealth` are both 100 across the sim, so both scripts now run
damage/health values through `damage_severity()` and pass a qualitative phrase
instead (`a glancing blow` → `devastating, near-crippling damage`). The prompt's
TASK section also explicitly instructs the model not to invent or cite raw
numeric stats on its own, as a backstop for any other numeric leakage.

---

## Troubleshooting

**"Could not reach Ollama"**
- Check what the script actually picked — it prints `Using Ollama at <url>` before
  every call. If that's `localhost` and Ollama is on Windows, auto-detection didn't
  find it reachable there.
- Confirm `OLLAMA_HOST=0.0.0.0` is set on the Windows side before `ollama serve`
- Test the gateway directly: `curl http://$(ip route show default | awk '{print $3}'):11434/api/tags`
- As a last resort, override detection: `--host http://<ip>:11434` or `export OLLAMA_HOST=http://<ip>:11434`

**Empty or thin timeline**
- `CharacterSummaries` is populated at session-end. If the game was force-quit, it
  may be empty. The script infers from raw Events instead, so stories still generate.
- Use `--prompt-only` to inspect what the model will see.

**Stories are repetitive or vague**
- Try a larger model: `--model gemma4:27b` or similar
- Try `--style legend` or `--style epic` for less literal retelling
- Run `--prompt-only` and manually trim the event list if it's dominated by one type

**Story cuts off mid-sentence**
- Ollama's `/api/generate` defaults `num_ctx` to a small value (often 2048 tokens)
  unless told otherwise — for the long civ-chronicle prompt in particular, prompt +
  output can blow past that and the response gets silently truncated. Both scripts
  now set `num_ctx`/`num_predict` explicitly in the request (`OLLAMA_NUM_CTX` /
  `OLLAMA_NUM_PREDICT` near the top of each script) and print a `[WARN]` when
  Ollama reports `done_reason: length`. If you see that warning, either raise
  those two constants, or (for civ stories) pass a smaller `--characters` /
  target a shorter-lived civ to shrink the prompt.
