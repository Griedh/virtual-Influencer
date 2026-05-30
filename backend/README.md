# Backend (Local Proxy)

## Start

1. In `backend/` wechseln.
2. `.env.example` nach `.env` kopieren.
3. `OPENAI_API_KEY` und `OPENAI_TEXT_MODEL` in `.env` setzen.
4. Start:

```powershell
dotnet restore
dotnet run
```

Standard-Port: `http://127.0.0.1:5050` (anpassbar via ASP.NET Core Settings).

## Endpunkte

- `GET /health`
- `POST /session/realtime`
- `WS /voice/realtime`
- `WS /voice/modular`
