# OBS + Zoom Runbook

## Overlay-Modus (Default)

1. Unity-Szene starten (Play Mode oder Standalone Build).
2. OBS Szene erstellen:
   - Quelle 1: `Video Capture Device` (deine Webcam)
   - Quelle 2: `Window Capture` oder `Game Capture` auf Unity-Fenster
3. Fuer Unity-Quelle Chroma Key aktivieren:
   - `Filters` -> `Chroma Key`
   - Farbe passend zur Unity-Hintergrundfarbe
4. Avatar im OBS-Preview unten rechts skalieren/positionieren.
5. OBS -> `Start Virtual Camera`.
6. Zoom -> Kamera: `OBS Virtual Camera`.

## Avatar-only Modus (Alternative)

1. OBS Szene ohne Webcam, nur Unity-Fenster.
2. Optional Hintergrund in OBS rauskeyen oder transparent rendern.
3. `Start Virtual Camera`.
4. Zoom -> Kamera: `OBS Virtual Camera`.

## Audio-Hinweis

- Das hier scaffolded MVP routet Avatar-Audio im Unity-Client.
- Falls Zoom-Teilnehmer den Avatar-Ton ebenfalls hoeren sollen:
  - OBS-Audio-Monitoring aktivieren oder
  - virtuelles Audiokabel (z. B. VB-Cable/Loopback) nutzen.
