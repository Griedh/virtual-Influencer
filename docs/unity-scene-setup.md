# Unity Scene Setup (Overlay MVP)

## 1. Projektbasis

1. Unity Hub -> neues 3D (URP oder Built-in) Projekt in `unity/` erstellen oder vorhandenes Projekt auf diesen Ordner zeigen.
2. `unity/Packages/manifest.json` nutzen (enthaelt uLipSync-Dependency).
3. `unity/Assets/VirtualInfluencer/Scripts` als Source-Ordner importieren.

## 2. Hierarchie-Empfehlung

- `AppRoot`
  - `VirtualInfluencerApp`
  - `VoiceWebSocketClient`
  - `MicrophoneStreamController`
  - `OverlayUiController`
  - `AssistantAudioPlayer` (+ `AudioSource`)
- `AvatarRoot`
  - Dein VRM-Avatar (BYO VRM)
  - `AvatarLipSyncController`
  - optional `ULipSyncSetupHelper`

## 3. Pflichtzuweisungen im Inspector

- `VoiceWebSocketClient`
  - `backendHttpBaseUrl`: `http://127.0.0.1:5050`
  - `startMode`: `Realtime` oder `Modular`
- `MicrophoneStreamController`
  - `webSocketClient` referenzieren
  - `sampleRateHz`: `24000`
- `AssistantAudioPlayer`
  - `webSocketClient` referenzieren
  - `outputSource` setzen
- `OverlayUiController`
  - `statusLabel`, `transcriptLabel`, `assistantLabel`, `connectionLabel` setzen

## 4. uLipSync (phonembasiert) anbinden

1. `uLipSync` Komponente auf ein Objekt mit Assistant-Audioquelle legen.
2. `uLipSyncBlendShape` (oder `uLipSyncBlendShapeVRM`) auf Avatar-Root/Face legen.
3. In `uLipSyncBlendShape` die Vokale/Visemes den BlendShapes zuordnen.
4. Event-Hook setzen:
   - `uLipSync` -> `On Lip Sync Update` -> `uLipSyncBlendShape.OnLipSyncUpdate`.
5. Beim Start pruefen:
   - `ULipSyncSetupHelper` warnt im Console Log bei fehlenden Komponenten.

## 5. Overlay-freundliche Darstellung

- Kamera-Hintergrund auf klaren Chroma-Key (z. B. #00FF00) setzen.
- Avatar unten rechts platzieren, damit OBS das leicht als Overlay erfassen kann.
- Optional Namensschild/Statustext als Canvas unten rechts.
