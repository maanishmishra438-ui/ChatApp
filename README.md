# ChatApp — real-time private 2-person chat

This version is intentionally simple to run: the Blazor Web App hosts the UI,
SignalR hub and SQLite persistence under one public origin.

## Features
- Create a private room.
- Copy an invite link.
- Second person opens the link and chooses a display name.
- Real-time messaging with SignalR.
- Messages persist in `chatapp.db`.
- Typing indicator.
- Automatic SignalR reconnect.
- Responsive dark UI.

## Run
From `ChatApp.Web`:

```powershell
dotnet restore
dotnet run --urls "http://0.0.0.0:5280"
```

Open:

`http://localhost:5280`

## Same Wi-Fi sharing
Run:

```powershell
ipconfig
```

Find your IPv4 address, for example `192.168.1.10`.

Another device on the same Wi-Fi can open:

`http://192.168.1.10:5280`

If Windows Firewall asks, allow the app on your Private network.

## Internet sharing
For a temporary public link, point a tunnel such as Cloudflare Tunnel or ngrok
at port 5280. Then send the generated HTTPS URL.

For production, deploy to a proper HTTPS host and add real authentication,
authorization, rate limiting, abuse protection and secure database hosting.

## Important
The solution is deliberately a single runnable web project so that the invite
link, SignalR hub and database all share the same origin. The previously
created `ChatApp.API` project can be kept separately for later API expansion.
