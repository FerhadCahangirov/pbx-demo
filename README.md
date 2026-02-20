# Native WebRTC + 3CX Softphone Demo

This project has two call paths:

1. Browser-to-browser calls inside this app (pure WebRTC over SignalR).
2. 3CX integration for extension state/call control, plus optional SIP/WebRTC registration.

## Detailed Backend Documentation

For full backend internals (all models, properties, methods, algorithms, and sequence diagrams), see:

- `backend/BACKEND_DOCS.md`

## What Works

1. Login with app users from backend config.
2. Bind each user to exactly one owned 3CX extension.
3. See extension call updates from 3CX Call Control API.
4. Place/answer/reject/end browser-to-browser calls in the app.
5. Attempt SIP/WebRTC registration using SIP.js with server-provided SIP config.

## Important Limitation

Your PBX endpoint behavior decides SIP.js success.

1. If PBX exposes a true SIP-over-WebSocket endpoint, SIP.js registration can work.
2. If PBX `/ws` is webclient-session protocol only (requires `sessionId` and `pass`), SIP.js REGISTER will fail.
3. 3CX Call Control WebSocket (`/callcontrol/ws`) is backend API signaling, not a SIP endpoint for SIP.js.

## Project Structure

1. `backend/softphone.config.json`
Contains PBX base URL, App ID/Secret, SIP/WebRTC endpoint/domain/ICE list.

2. `backend/appsettings.json`
Contains app users and per-user owned extension + optional control DN (RoutePoint) + SIP credentials.

3. `frontend/.env`
Contains frontend API base and optional STUN/TURN overrides for browser-to-browser WebRTC.

## 3CX Configuration (Required)

1. Create an API app in 3CX Admin.
Collect:
- `AppId`
- `AppSecret`
- PBX base URL

2. Ensure each extension used in this app exists and is active.

3. For each extension, collect SIP auth values:
- Extension/username
- Auth ID (often same as extension)
- SIP auth password

4. Configure inbound routing in 3CX so incoming customer calls ring target extension(s).

5. Verify network and certificates:
- PBX HTTPS reachable on `443`
- TLS certificate valid for PBX domain
- If using dedicated SIP WebSocket port, confirm it is reachable from client network

## Backend Configuration

### 1) PBX + SIP global settings

Edit `backend/softphone.config.json`:

```json
{
  "Softphone": {
    "ThreeCx": {
      "PbxBase": "https://your-pbx.example.com",
      "AppId": "YOUR_APP_ID",
      "AppSecret": "YOUR_APP_SECRET"
    },
    "SipWebRtc": {
      "Enabled": true,
      "WebSocketUrl": "wss://your-pbx.example.com/ws",
      "Domain": "your-pbx.example.com",
      "IceServers": [
        "stun:your-pbx.example.com:3478"
      ]
    }
  }
}
```

### 2) App users + extension ownership + SIP credentials

Edit `backend/appsettings.json`:

```json
{
  "Softphone": {
    "Users": [
      {
        "Username": "user-a",
        "Password": "app-login-password",
        "OwnedExtension": "100",
        "ControlDn": "700",
        "Sip": {
          "Username": "100",
          "AuthId": "100",
          "Password": "extension-sip-password",
          "DisplayName": "Extension 100"
        }
      }
    ]
  }
}
```

Notes:

1. `Username`/`Password` here are app login credentials, not 3CX webclient login.
2. `OwnedExtension` is enforced per app user.
3. `ControlDn` is optional and should be set to a 3CX RoutePoint DN if you want Call Control API answering to be reliable for inbound DID calls.
4. SIP credentials are sent to frontend by backend endpoint `/api/softphone/sip/config` for current user.

### 3) Start backend

```bash
cd backend
dotnet restore
dotnet run
```

## Frontend Configuration

Edit `frontend/.env`:

```env
VITE_API_BASE=http://localhost:8080/
VITE_STUN_SERVERS=
VITE_TURN_SERVERS=
VITE_TURN_USERNAME=
VITE_TURN_PASSWORD=
```

Notes:

1. Keep `VITE_API_BASE` empty only if using same-origin/proxy setup.
2. TURN settings are strongly recommended for production browser-to-browser media.

Start frontend:

```bash
cd frontend
npm install
npm run start
```

## How To Use

1. Open app and log in with `Softphone:Users` credentials.
2. Click **Bind My Extension**.
3. Pick **3CX Active Device** (or keep `Web App / server route`).
4. Click **Enable Microphone**.
5. Check SIP status badge:
- `Registered` means SIP.js registration succeeded.
- `Failed` means endpoint/credentials/protocol mismatch.
6. Place calls:
- **Call In-App**: browser-to-browser call (WebRTC over SignalR).
- **Call via 3CX**: call any 3CX extension or external number through 3CX Call Control.
- **Answer/Reject/Hangup** in the `3CX Call Control Audio` panel for PBX calls.

## Required Flow Coverage

The app now exposes each required scenario directly:

1. `Browser softphone -> 3CX softphone`: use **Call via 3CX** to dial extension.
2. `3CX softphone -> browser softphone`: dial the browser user extension from 3CX, then answer in **3CX Call Control Audio**.
3. `Browser softphone -> browser softphone`: use **Call In-App**.
4. `External number -> 3CX -> browser softphone`: route inbound calls to extension/routepoint in 3CX, answer in **3CX Call Control Audio**.
5. `Browser softphone -> external number via 3CX`: use **Call via 3CX** with external number.

## Troubleshooting

### SIP status shows `WebSocket closed`

1. Confirm port is reachable from client machine:
```powershell
Test-NetConnection your-pbx.example.com -Port 443
Test-NetConnection your-pbx.example.com -Port 5001
```

2. If `:5001` fails, do not use `wss://...:5001/ws` in config.

3. If `/ws` returns payload like:
`{"pass":["The pass field is required."],"sessionId":["The sessionId field is required."]}`
then that endpoint is webclient-session protocol, not plain SIP.js REGISTER endpoint.

4. In that case:
- either switch to a PBX endpoint that accepts SIP-over-WebSocket REGISTER
- or keep using Call Control API path without SIP.js media registration

### Calls ring in 3CX client but not as browser SIP call

1. Verify SIP registration state in app is `Registered`.
2. Verify extension SIP credentials in `backend/appsettings.json`.
3. Verify inbound route targets the same extension bound in app.
4. Verify PBX endpoint/protocol actually supports SIP.js for your deployment mode.

### `422 UnprocessableEntity` on `answer`

If alert/log shows a ringing participant with `direct_control=false` and `type=Wextension`, 3CX may reject answer by design.

Recommended fix path:
1. Configure a RoutePoint in 3CX API integration.
2. Route inbound DID to that RoutePoint first.
3. Set user `ControlDn` in `backend/appsettings.json` to that RoutePoint DN.
4. Restart backend and verify ringing call appears with `answerable=true` or RoutePoint DN.

## Security Notes

1. Do not commit real `AppSecret` and SIP passwords to public repositories.
2. Replace demo JWT signing key with a strong secret before production.
3. Prefer secret storage (environment variables or vault) for production deployments.
