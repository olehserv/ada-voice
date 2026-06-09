# TODOS

## Design

- [ ] **Generate Board mockups when an OpenAI API key is available** (added 2026-06-10, design review)
  - **What:** Run the gstack designer — 3 dark-theme Board variants (Full + Docked layouts) against the visual system in /DESIGN.md, review on a comparison board.
  - **Why:** The design review specified the visual system in text; mockups pressure-test token choices (e.g., does `#54D262` LIVE read at arm's length on `#1F1F1F`?) before Phase 3 builds it in XAML.
  - **Pros:** Visual confirmation before any code; taste misses caught for the cost of a few image generations.
  - **Cons:** Needs an OpenAI API key (paid). Mockups are web renders approximating WPF — they guide, not bind.
  - **Context:** Designer binary ready at `~/.claude/skills/gstack/design/dist/design`; the brief is reproducible from /DESIGN.md + docs/design/05-ui-design.md §1. Key goes in `~/.gstack/openai.json` or `OPENAI_API_KEY`.
  - **Depends on:** OpenAI API key only. Best done before Phase 3 (Board UI).
