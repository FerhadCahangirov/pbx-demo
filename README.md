# Native WebRTC Browser Softphone (3CX-integrated)

This project provides a browser-only softphone with:

- Native WebRTC media (`RTCPeerConnection`, `getUserMedia`, ICE)
- SignalR/WebSocket signaling
- .NET 8 backend session/authentication layer
- 3CX Call Control API integration for extension/session ownership and sync

No SIP.js, JsSIP, or third-party SIP abstraction library is used.

## Architecture

### Frontend (`frontend/`)

- React + TypeScript
- Captures microphone audio with `getUserMedia`
- Creates peer connections with `RTCPeerConnection`
- Exchanges SDP and ICE over SignalR hub messages
- Renders incoming/outgoing call lifecycle and event timeline
- Supports microphone and speaker device selection

### Backend (`backend/`)

- .NET 8 Web API + SignalR hub (`/hubs/softphone`)
- JWT authentication + per-user owned extension binding
- In-memory WebRTC call/session manager:
  - outgoing call creation
  - incoming call ring/answer/reject/end
  - signal relay (`offer` / `answer` / `ice`)
  - disconnect cleanup and call termination sync
- Existing 3CX Call Control client/session stack remains available

## Signaling Contract

Client invokes on hub:

- `PlaceBrowserCall(destinationExtension)`
- `AnswerBrowserCall(callId)`
- `RejectBrowserCall(callId)`
- `EndBrowserCall(callId)`
- `SendWebRtcSignal({ callId, type, sdp?, candidate?, sdpMid?, sdpMLineIndex? })`
- `MarkCallConnected(callId)`

Server pushes to client:

- `SessionSnapshot(snapshot)`
- `BrowserCallsSnapshot(calls)`
- `BrowserCallUpdated(call)`
- `WebRtcSignal(signal)`
- `SoftphoneEvent(event)`

## Prerequisites

1. .NET 8 SDK
2. Node.js 18+ / npm
3. 3CX API app credentials

## 3CX Setup

1. In 3CX admin, create an API app and save:
   - `AppId`
   - `AppSecret`
2. Configure app users in `backend/appsettings.json`:
   - each user must have an `OwnedExtension`

Example:

```json
{
  "Softphone": {
    "Users": [
      { "Username": "user-a", "Password": "pass-a", "OwnedExtension": "100" },
      { "Username": "user-b", "Password": "pass-b", "OwnedExtension": "101" }
    ]
  }
}
```

## Local Run

### Backend

```bash
cd backend
dotnet restore
dotnet run
```

### Frontend

```bash
cd frontend
npm install
npm run start
```

## Environment (Frontend)

`frontend/.env` supports ICE config:

- `VITE_API_BASE`
- `VITE_STUN_SERVERS` (comma-separated)
- `VITE_TURN_SERVERS` (comma-separated)
- `VITE_TURN_USERNAME`
- `VITE_TURN_PASSWORD`

## Browser-to-Browser Call Flow

1. User A logs in, binds owned extension, enables microphone.
2. User B logs in with different extension and enables microphone.
3. User A dials User B extension.
4. Backend emits ringing call update to both sessions.
5. User A creates SDP offer and sends via hub.
6. User B answers, creates SDP answer, sends via hub.
7. Both sides exchange ICE candidates.
8. Media flows browser-to-browser via WebRTC.
9. End/reject/disconnect updates terminate peer connection on both sides.

## Notes

- This implementation is browser endpoint only for media.
- Calls require both endpoints online in this app/hub.
- For production NAT traversal, configure TURN credentials (not just STUN).
