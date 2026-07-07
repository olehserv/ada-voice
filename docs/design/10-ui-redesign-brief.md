# 10 — UI Redesign Brief (2026-07-06)

Owner-approved rules for the professional UI redesign. Companion to
[09-design-system.md](09-design-system.md) (tokens, canonical) and
[05-ui-design.md](05-ui-design.md) (screens). When this brief and 09 conflict, the
resolution lands in 09 — that file stays the single source of truth.

## Goal

Make the app feel calm, focused, modern, premium, and enterprise-grade — suitable for
long daily use during live calls. Quality bar: Linear / Raycast / Superhuman / Fluent 2
class. Extract principles, never copy: strong hierarchy, refined spacing, excellent
typography, subtle depth, restrained color, polished dark UI, clear interaction states,
keyboard-first feel.

## Scope adaptation (reality check)

The original brief assumed an AI copilot (live transcript, AI suggestions, sentiment).
AdaVoice today is a phrase soundboard — no transcript, no AI. The redesign applies the
same quality bar to the real screens: Board, recorder area, setup wizard, Settings, and
the four dialogs. The approved Conversations feature (ordered scripts,
[spec](../superpowers/specs/2026-07-06-conversations-design.md)) is the product's real
"script flow" and the layout must leave room for it. AI panels stay future-facing only.

## Hard rules

- No default-WPF-looking controls: buttons, inputs, combo boxes, list rows all styled.
- No cheap gradients, glassmorphism, neon, decorative blobs, emoji-as-design (09 rule).
- Every color, spacing, font size, and radius comes from a token — no hard-coded values
  in view XAML.
- Clear interaction states everywhere: default / hover / pressed / focused / selected /
  disabled (and loading where async).
- Call-state (LIVE / OFF AIR / DEGRADED / STOPPED) readable at a glance from across the
  room; STOP stays the most reachable action.
- Preserve business logic, bindings, and ViewModels; UI-only changes.
- Keep the fixed dark theme and WPF-UI Fluent base (09 decisions).
- All windows use consistent Fluent chrome (dark title bars everywhere).
- Keyboard-first: ship the planned in-app keys (`/` search, arrows, `Enter`, `Esc`).
- Empty / loading / error states are designed, not leftover.

## Workflow (agreed)

1. Analyze current UI and list problems before touching files.
2. Consult available design resources (ui-ux-pro-max as a checklist; web component
   generators don't apply to WPF).
3. Propose 3 visual directions, recommend one.
4. Extend the design system (tokens, type ramp, spacing, radii, elevation, component
   states) before implementation.
5. Define the layout/shell before implementation.
6. Implementation plan with risk list, then implement: tokens → base styles →
   components → shell → polish.
7. Self-critique against a scored rubric; polish pass; final report.
