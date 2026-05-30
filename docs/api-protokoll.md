# API- und Event-Protokoll (MVP)

## HTTP Endpunkte

- `GET /health`
  - Healthcheck fuer Backend-Status.
- `POST /session/realtime`
  - Erstellt ein Realtime Client Secret fuer sichere Session-Initialisierung.
  - Der OpenAI-Key bleibt im Backend.

## WebSocket Endpunkte

- `WS /voice/realtime`
  - Realtime speech-to-speech Pfad.
- `WS /voice/modular`
  - STT -> LLM -> TTS Pfad.

## Client -> Backend Events

- `mode.set`
  - Setzt Zielmodus (`realtime` oder `modular`).
- `ptt.down`
  - Startet einen manuellen Turn.
- `ptt.up`
  - Beendet manuellen Turn.
- `vad.toggle`
  - `enabled: true|false`.
- `audio.chunk`
  - Base64 PCM16 Chunk:
  - `audioBase64`, `format` (`pcm16`), `sampleRateHz`, `channels`.
- `conversation.reset`
  - Loescht lokalen Gesprächskontext der Session.

## Backend -> Client Events

- `state.changed`
  - `state`: `Idle|Listening|Thinking|Speaking`.
  - `mode`: `realtime|modular`.
- `transcript.delta`
  - Laufender Transkript-Text (Streaming).
- `transcript.final`
  - Finales Transkript eines Turns.
- `assistant.text`
  - Modellantwort als Text.
- `assistant.audio.chunk`
  - Base64 Audio-Chunk der Modellantwort.
- `error`
  - Fehlerereignis mit `message` und optional `code`.

## Beispiel: `audio.chunk`

```json
{
  "type": "audio.chunk",
  "audioBase64": "BASE64_PCM16_BYTES",
  "format": "pcm16",
  "sampleRateHz": 24000,
  "channels": 1
}
```

## Beispiel: `state.changed`

```json
{
  "type": "state.changed",
  "state": "Listening",
  "mode": "realtime",
  "timestampUtc": "2026-05-29T12:00:00.0000000+00:00"
}
```
