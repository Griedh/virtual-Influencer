# Virtual Influencer MVP Scaffold

Stand: 29.05.2026

Dieses Repo enthaelt ein lauffaehiges MVP-Scaffold fuer ein Unity-Team-Maskottchen mit OpenAI-Voice-Backend und OBS/Zoom-Integration.

## Zielbild

- Unity-Avatar als Team-Maskottchen (Overlay-Default).
- Live-Voice-Interaktion:
  - Realtime speech-to-speech (`gpt-realtime-2`)
  - Modularer Pfad (`STT -> LLM -> TTS`)
- Sichere Architektur: OpenAI-Key nur im lokalen Backend-Proxy, nie im Unity-Client.

## Architektur

1. Mikrofon in Unity nimmt Audio auf.
2. Unity sendet Audio-Events per WebSocket an Backend.
3. Backend verarbeitet je nach Modus:
   - `WS /voice/realtime`: Realtime API Bridge
   - `WS /voice/modular`: Transcribe -> Textmodell -> TTS
4. Backend liefert Text + Audio-Events an Unity zurueck.
5. Unity spielt Assistant-Audio ab und steuert Lip-Sync (uLipSync bzw. Fallback).
6. OBS erfasst Unity und gibt die Szene als `OBS Virtual Camera` an Zoom weiter.

## Repo-Struktur

- `backend/` -> .NET 8 Minimal API (HTTP + WebSocket Proxy)
- `Virtual Influencer/` -> Unity-Projekt mit Assets, Packages und ProjectSettings
- `docs/` -> Setup- und Runbook-Dokumentation

## Installations-Tools (Pflicht)

1. Unity Hub + Unity `6.3 LTS`
2. .NET `8 SDK`
3. OBS Studio (mit integrierter Virtual Camera)
4. Zoom Desktop Client
5. Git

## Installations-Tools (empfohlen)

1. Git LFS (fuer groessere Avatar/Binary Assets)
2. Visual Studio 2022 oder JetBrains Rider (C#)
3. Ein virtuelles Audio-Kabel fuer Zoom-Audio-Routing (optional)

## Backend Setup

1. In den Ordner wechseln:
   - `cd backend`
2. Env-Datei anlegen:
   - `.env.example` nach `.env` kopieren
3. Mindestens setzen:
   - `OPENAI_API_KEY=...`
   - `OPENAI_TEXT_MODEL=...` (bewusst kein harter Default)
4. Starten:

```powershell
dotnet restore
dotnet run
```

Verfuegbare Endpunkte:

- `GET /health`
- `POST /session/realtime`
- `WS /voice/realtime`
- `WS /voice/modular`

## Unity Setup

1. Unity-Projekt in `Virtual Influencer/` oeffnen.
2. `Virtual Influencer/Packages/manifest.json` enthaelt `uLipSync` und TextMesh Pro.
3. Scripts aus `Virtual Influencer/Assets/VirtualInfluencer/Scripts` in der Scene verdrahten.
4. Wichtige Referenz:
   - `VoiceWebSocketClient.backendHttpBaseUrl = http://127.0.0.1:5050`

Detaillierte Scene-Anleitung:

- `docs/unity-scene-setup.md`

## Bedienung im Play Mode (Standard-Hotkeys)

- `F1` -> Realtime Modus
- `F2` -> Modularer Modus
- `V` -> VAD an/aus
- `LeftControl` halten -> Push-to-talk (wenn VAD aus)
- `R` -> Conversation reset
- `C` -> Reconnect

## OBS + Zoom

Ausfuehrliches Runbook:

- `docs/obs-zoom-runbook.md`

Kurzfassung Overlay-Default:

1. Webcam + Unity-Fenster in OBS kombinieren.
2. Unity-Hintergrund per Chroma Key entfernen.
3. `Start Virtual Camera`.
4. In Zoom `OBS Virtual Camera` als Kamera auswaehlen.

## Event-Protokoll

Das WebSocket-Protokoll (Client-/Server-Events) ist hier dokumentiert:

- `docs/api-protokoll.md`

## MVP Test-Checkliste

1. `GET /health` liefert `status=ok`.
2. Unity verbindet auf `WS /voice/realtime` und `WS /voice/modular`.
3. Mindestens 3 Voice-Turns in beiden Modi durchlaufen.
4. `state.changed` wechselt sichtbar zwischen Listening/Thinking/Speaking/Idle.
5. `assistant.audio.chunk` wird abgespielt.
6. OBS Overlay funktioniert und erscheint in Zoom via Virtual Camera.

## Quellen

- [OpenAI Realtime Guide](https://developers.openai.com/api/docs/guides/realtime)
- [OpenAI gpt-realtime-2](https://developers.openai.com/api/docs/models/gpt-realtime-2)
- [OpenAI gpt-4o-transcribe](https://developers.openai.com/api/docs/models/gpt-4o-transcribe)
- [OpenAI gpt-4o-mini-tts](https://developers.openai.com/api/docs/models/gpt-4o-mini-tts)
- [OBS Virtual Camera Guide](https://obsproject.com/kb/virtual-camera-guide)
- [uLipSync](https://github.com/hecomi/uLipSync)
