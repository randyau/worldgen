# Story Generation

`scripts/generate-story.py` extracts character histories from `world.db` and sends
them to a local Ollama model to produce short prose narratives.

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

2. Find the Windows host IP from inside WSL2:
   ```bash
   HOST=$(ip route show default | awk '{print $3}')
   echo $HOST          # e.g. 172.17.240.1
   ```

3. Confirm Ollama is reachable:
   ```bash
   curl http://$HOST:11434/api/tags
   ```

4. Pass `--host` to the script:
   ```bash
   python3 scripts/generate-story.py world.db --host http://$HOST:11434
   ```

The gateway IP (`$HOST`) changes when WSL2 restarts, so use the variable rather than
hardcoding it.

---

## Basic Usage

```bash
# Auto-detect Windows host and generate stories for the 3 most notable characters
HOST=$(ip route show default | awk '{print $3}')
python3 scripts/generate-story.py publish/win-x64/world.db \
    --host http://$HOST:11434

# Generate 5 characters, save each to a text file
python3 scripts/generate-story.py world.db \
    --host http://$HOST:11434 \
    --top 5 \
    --out stories/

# Target a specific character by ID (find IDs in world.db or from character-analysis.py)
python3 scripts/generate-story.py world.db \
    --host http://$HOST:11434 \
    --id 7503503

# Preview the prompt that will be sent (no Ollama call)
python3 scripts/generate-story.py world.db --prompt-only --top 1
```

---

## Options

| Flag | Default | Description |
|------|---------|-------------|
| `--model MODEL` | `gemma4:e4b` | Ollama model name |
| `--host URL` | `http://localhost:11434` | Ollama base URL |
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

## Troubleshooting

**"Could not reach Ollama"**
- Confirm `OLLAMA_HOST=0.0.0.0` is set on the Windows side before `ollama serve`
- Check the gateway IP: `ip route show default | awk '{print $3}'`
- Test: `curl http://<gateway_ip>:11434/api/tags`

**Empty or thin timeline**
- `CharacterSummaries` is populated at session-end. If the game was force-quit, it
  may be empty. The script infers from raw Events instead, so stories still generate.
- Use `--prompt-only` to inspect what the model will see.

**Stories are repetitive or vague**
- Try a larger model: `--model gemma4:27b` or similar
- Try `--style legend` or `--style epic` for less literal retelling
- Run `--prompt-only` and manually trim the event list if it's dominated by one type
