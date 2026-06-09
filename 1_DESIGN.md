You are an experienced senior software architect and Windows desktop application engineer.

We are in the DESIGN PHASE ONLY.
Do not implement code yet.
Do not create files unless explicitly asked.
Your task is to analyze, clarify, and design the application architecture.

## Project Context

Target OS:
- Windows desktop

Preferred technology stack:
- .NET 10
- WPF
- C#
- Local-first application
- Google Chrome may be used during client conversations
- The app should work while the operator is using browser-based communication tools

User:
- My wife works as an online administrator/operator.
- She speaks with clients every day.
- Most conversations follow repeated scripts and contain many repeated phrases.
- She wants to use her own pre-recorded voice phrases during live conversations.

## Product Goal

Design a Windows desktop application that allows the operator to:

1. Record common phrases in her own voice.
2. Store all recordings locally on the computer.
3. Organize phrases into categories/scripts.
4. Quickly play a selected phrase during a live client conversation.
5. Route the played audio so that the other person hears it as if it came from her microphone.
6. Stop/interupt phrase playback immediately when she needs to speak herself.
7. Use the app quickly and safely during real-time conversations.

## Important Functional Requirements

The application should support:

### Phrase Management
- Create, rename, delete phrases.
- Record a new phrase using the microphone.
- Re-record an existing phrase.
- Play preview locally.
- Store phrase metadata:
  - title
  - category/script
  - optional tags
  - duration
  - created/updated date
  - file path

### Playback During Conversation
- Play a phrase instantly.
- Stop playback instantly.
- Only one phrase should play at a time.
- Starting a new phrase should optionally stop the previous one.
- Keyboard shortcuts/hotkeys should be supported.
- UI should be optimized for fast access during calls.

### Audio Routing
The key technical challenge is:
- The phrase audio must be broadcast into the active microphone input used by communication apps.
- The application should analyze possible technical approaches for Windows:
  - virtual audio cable/device
  - Windows audio APIs
  - WASAPI
  - NAudio
  - external dependencies
  - possible limitations with browser-based tools
- The design should clearly explain recommended audio-routing architecture.

### Recording
- Record from the selected microphone.
- Save recordings locally.
- Prefer common audio formats such as WAV or MP3.
- Explain tradeoffs between WAV and compressed formats.
- Include basic volume normalization/noise reduction considerations, but do not over-engineer.

### Local Storage
- All data should be stored locally.
- No cloud account required.
- Consider:
  - local folder structure
  - SQLite or JSON metadata
  - audio file storage
  - backup/export/import

### UI/UX
Design a WPF interface optimized for an operator:
- main phrase board
- categories/scripts
- search/filter
- large buttons for common phrases
- keyboard shortcuts
- recording panel
- playback status indicator
- emergency stop button
- settings page for audio devices and hotkeys

### Non-Functional Requirements
Consider:
- low-latency playback
- reliability during calls
- simple installation
- minimal CPU/memory usage
- offline operation
- safe file handling
- extensibility
- privacy
- maintainability

## Constraints

- First iteration should be a design document only.
- Do not write production code yet.
- Do not scaffold the application yet.
- Prefer practical architecture over theoretical complexity.
- Assume a solo developer or very small team.
- Prefer libraries and approaches that are realistic for .NET/WPF on Windows.
- If an external virtual audio driver is required, explain that clearly.
- If pure .NET cannot fully solve microphone injection, explain the limitation honestly.

## Legal/Ethical/Product Safety Considerations

Include a short section about:
- user consent and transparency during calls
- compliance with workplace/platform rules
- avoiding deception or unauthorized call automation
- local privacy of voice recordings

## Deliverables

Produce a structured design document with the following sections:

1. Executive Summary
2. Product Scope
3. Assumptions
4. Key User Flows
5. Functional Requirements
6. Non-Functional Requirements
7. Main Technical Challenge: Audio Routing to Microphone
8. Recommended Architecture
9. Alternative Architecture Options
10. Technology Choices
11. Data Model
12. Local File/Folder Structure
13. WPF UI Design
14. Audio Engine Design
15. Recording Engine Design
16. Hotkey System Design
17. Error Handling and Edge Cases
18. Security and Privacy
19. Risks and Mitigations
20. MVP Definition
21. Future Enhancements
22. Step-by-Step Implementation Roadmap
23. Open Questions for Me

## Output Style

- Be specific and practical.
- Use bullet points and diagrams where useful.
- Mark uncertain areas clearly.
- Provide recommendations, not just options.
- Do not write full source code.
- Pseudocode is acceptable only if it helps explain architecture.