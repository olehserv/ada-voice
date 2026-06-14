# Phase 0 helper — Chrome AGC / NS / EC test

Throwaway test tooling. Answers roadmap risk **A5/A6**: does Chrome's WebRTC
mic processing (AGC especially) pump our phrases down when room noise enters
the mic? Lets you toggle each processing step and see what Chrome *actually*
applied.

## Why a local server (not just double-clicking the file)

`getUserMedia` (mic access) is blocked on `file://`. You must serve over
`http://localhost`. Pick whichever you have:

```powershell
# Python (most common)
cd C:\p\ada-voice\spike\agc-test
python -m http.server 8080

# OR Node
npx serve -l 8080

# OR .NET global tool (one-time: dotnet tool install --global dotnet-serve)
dotnet serve -p 8080
```

Then open <http://localhost:8080> in Chrome.

## Steps

1. Run the AdaVoice spike so mic + phrases flow into **CABLE Input**.
2. Put on **headphones** (speakers feed back through the cable).
3. In the page: **Grant + list devices** → pick **CABLE Output**.
4. **Start**. Make room noise while a phrase plays. Watch the meter + listen.
5. Toggle **autoGainControl** off vs on and repeat.

## Reading the result

- Phrase level steady with AGC **off**, pumps down with AGC **on** →
  confirmed: the culprit is Chrome AGC, not the app.
- The **Applied** box shows `autoGainControl`/etc. as Chrome really set them.
  If you asked for `false` but it shows `true`, the device/OS overrode you —
  note that, it matters for the Zoho verdict.

## The catch this does NOT solve

This page lets **you** disable AGC. **Zoho sets its own constraints** and
usually forces AGC **on** with no user toggle. So a good result here means
"the phrases survive when AGC is off" — it does **not** prove Zoho will let you
turn AGC off. That still needs a real Zoho call to confirm.
