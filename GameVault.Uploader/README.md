# GameVault Uploader

A small companion service for a GameVault server. The client's **Admin Console → Publish a Game** sends game
archives to it from any computer; it writes them into the server's games folder (`/files`):

- only **administrators of the GameVault server** can upload: each request carries the client's GameVault
  sign-in, which the uploader checks with `GET /api/users/me` of the server;
- uploads come in chunks of up to 64 MB and **continue where they stopped** after a lost connection;
- the archive is written as `<name>.partial` and renamed when complete, so the server never indexes half a game;
- only plain archive names are accepted (`.zip`, `.7z`, `.rar`, `.iso`, `.tar.gz`, ...), never a path.

## Run

See `docker-compose.example.yml`: mount the games folder of the GameVault server as `/files`, set
`GAMEVAULT_URL` to the server and `user:` to the owner of the games folder. The client uses the service at a fixed
address (`SanctuaryService.Url` in the client, https://gamevaultupload.alexisdominguez.fr): nobody has to enter it.

| Variable | Default | |
|---|---|---|
| `GAMEVAULT_URL` | (required) | GameVault server, e.g. `http://gamevault-backend:8080` |
| `FILES_DIRECTORY` | `/files` | Games folder |
| `PROFILES_DIRECTORY` | `/data/profiles` | Players' profiles (mount `/data` as a volume to keep them) |
| `SAVEFILES_DIRECTORY` | `/savefiles` | The GameVault server's savefiles volume, read-only: older cloud saves can be restored |
| `REMOTE_INSTALLS_DIRECTORY` | `/data/remote` | Games the players asked to install on their PC from the web page |
| `ROMM_COMM_URL` | - | Requests API of the [RomM-ComM](https://github.com/idio-sync/romm-comm) Discord bot (e.g. `http://romm-sync:8095`): game requests are off without it |
| `ROMM_COMM_API_KEY` | - | The bot's `REQUESTS_API_KEY` |
| `ROMM_URL` | - | [RomM](https://github.com/rommapp/romm) (e.g. `http://romm:8080`): the client's Retro page lists its NES, SNES, GBA, DS, GameCube, Wii, PS1, PS2, Neo Geo and 3DS games and plays them with RetroArch; the BIOS come from RomM's Firmware; off without it |
| `ROMM_USERNAME`, `ROMM_PASSWORD` | - | The RomM account the service reads with: a Viewer account is enough |
| `ROMM_PUBLIC_URL` | - | RomM as the players reach it: a player can link their own RomM account in the client, then their retro saves go to RomM (otherwise to this service) |
| `RETRO_SAVES_DIRECTORY` | `/data/retro-saves` | The players' saves of the retro games (one zip per player, and the last 10 versions), so they follow them from PC to PC |
| `RETRO_PLAYERS_DIRECTORY` | `/data/retro-players` | The players' time on the retro games and their favorites (profiles, recent activity) |
| `ZEROTIER_CONTROLLER_URL` | - | The API of a [ZeroTier](https://github.com/zerotier/ZeroTierOne) 1.14.2 network controller (e.g. `http://zerotier-controller:9993`): the client's **LAN rooms** are off without it |
| `ZEROTIER_API_SECRET` | - | The controller's API token (`ZEROTIER_API_SECRET` of its container, or its `authtoken.secret`) |
| `ROOMS_DIRECTORY` | `/data/rooms` | The LAN rooms and the games playable in LAN |
| `REPORTS_DIRECTORY` | `/data/reports` | The problems the players report on games (admin console) |
| `INVITES_DIRECTORY` | `/data/invites` | The invitation codes new accounts need (admin console) |
| `GAMEVAULT_API_KEY` | - | An administrator's API key of the GameVault server: activates the accounts that give an invitation code. The server needs `AUTH_API_KEY_ENABLED=true`, and `SERVER_ACCOUNT_ACTIVATION_DISABLED=false` so that new accounts wait for one |

## LAN rooms

The PC games made for a LAN, played over the Internet: each room is a private network of a ZeroTier controller
(see `docker-compose.example.yml`), with broadcast on so that the games find each other's parties. In the client,
**Community → LAN rooms**: create a room (its game among the ones playable in LAN) and send its code, or join a public room. The service lets each player's PC in the network with an address of
the room's subnet (`10.147.<room>.<player>`); the client joins it with ZeroTier One, installed once on each PC
(Windows: the client runs ZeroTier's installer; Linux: `curl -s https://install.zerotier.com | sudo bash`).
On Windows the client also puts the room's network first and private (one prompt of the administrator's rights).

The games playable in LAN are found on [PCGamingWiki](https://www.pcgamingwiki.com) (its "LAN play" and players),
each game of the library read once in the background, again when its title changes. An administrator marks or
unmarks a game on its page (**Playable in LAN**), and that choice wins.

Use the controller of ZeroTier **1.14.2**: since 1.16 the controller is under a non-commercial license that excludes
services like this one; 1.14.2's license turned into Apache 2.0 on 2026-01-01. ZeroTier One on the PCs (MPL 2.0)
can be the latest.

## Web page

Open the service's address in a browser (e.g. https://gamevaultupload.example.com) and sign in with a GameVault
account.

**Install on my PC** (every player): choose one of your PCs and a game. Each SanctuaryVault client (1.25 or later)
says every minute that it is open, so the page lists the player's PCs (open or not). The chosen PC takes the game,
downloads it without asking (last install location) and says whether it started or why not; the request stays on
the page with that outcome for 3 days.

### Under the GameVault site

The page can also be served under the GameVault web page's own address (`https://gamevault.example.com/sanctuary/`):
it then uses that page's sign-in, Plex or Authentik included, with nothing to type. In Nginx Proxy Manager, on the
proxy host of the GameVault server, tab **Advanced** (replace the address by the one of this service):

```nginx
# The SanctuaryVault page under the GameVault site
location /sanctuary/ {
    proxy_pass http://192.168.1.10:7477;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    client_max_body_size 80m;
}
```

Optionally, an **Install on my PC** button on each game page of the GameVault web page: the proxy adds
`/sanctuary/inject.js` to its pages. The server sends them compressed, which the proxy can not change, hence the
`Accept-Encoding` line (replace the address by the one of the GameVault server):

```nginx
# The GameVault pages (not its API nor its files) with the SanctuaryVault button
location ~ ^/(?!api/|assets/|sanctuary) {
    proxy_pass http://192.168.1.10:7476;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Accept-Encoding "";
    sub_filter '</head>' '<script src="/sanctuary/inject.js"></script></head>';
    sub_filter_once on;
}
```

**Publish a game** without the client: sign in with an administrator account of the GameVault server, drop an archive, check the title, version,
year and type (the games already on the server are offered, to publish a new version of one), then **Publier**.
The file is sent in chunks and an interrupted upload continues where it stopped. The sign-in goes through the service
(`/auth/login`, `/auth/refresh`), so nothing has to be allowed on the GameVault server.

`/invite` is the page to send to a new player: download links for Windows and Linux, and how to sign in. The client's
**Community → Invite a friend** copies its link.

## API

| | |
|---|---|
| `GET /status` | version and free space (public) |
| `GET /uploads/{name}` | bytes received so far, whether the file exists |
| `PUT /uploads/{name}?offset=N&total=T` | appends a chunk at offset N |
| `POST /uploads/{name}/complete?size=S[&overwrite=true]` | publishes the file |
| `DELETE /uploads/{name}` | drops an unfinished upload |
| `GET /profiles/{userId}` | a player's profile (any signed-in player) |
| `PUT /profiles/{userId}` | saves it (the player, or an administrator; a JSON object of at most 64 KB) |
| `POST /auth/login`, `POST /auth/refresh` | GameVault sign-in for the web page (forwarded to the server) |
| `GET /me`, `GET /games?search=` | the signed-in user, the games already on the server |
| `GET /savefiles/{gameId}` | the cloud saves the server keeps for the signed-in player (newest first) |
| `GET /savefiles/{gameId}/{name}` | one of them, to restore an older save |
| `POST /remote/devices` | a client of the signed-in player says it is open (`{"id": "...", "name": "PC-SALON"}`) |
| `GET /remote/devices` | the player's PCs, with `online` (seen in the last 3 minutes) |
| `GET /remote/installs[?device=ID]` | the player's "install on my PC" requests, or the ones that PC still has to take |
| `POST /remote/installs` | asks a PC for a game (`{"gameId": 5, "title": "...", "deviceId": "..."}`, at most 20 waiting) |
| `PUT /remote/installs/{id}` | the PC says what became of it (`{"status": "started" or "failed", "message": "..."}`) |
| `DELETE /remote/installs/{id}` | the player drops it |
| `GET /games/{id}` | one game of the server (the game opened on the GameVault site) |
| `GET /invite` | the page to send to a new player (public) |
| `GET /game-requests` | the signed-in player's game requests and their status (from the bot) |
| `POST /game-requests` | asks for a game (`{"game": "Hades", "details": "..."}`), passed to the bot with the player |

## Game requests

The client's **Library → Ask for a game** (also offered when a search finds nothing) sends the request here; the
service passes it, with the player it checked, to the RomM-ComM Discord bot. Requests made in Discord and in
SanctuaryVault are then in one list (`/request_admin`), duplicates are merged, and the new request is announced in
the bot's channel. The player sees in the client when the request is fulfilled or refused.

## Profiles

The client's **Community** page lets each player customize their profile (game showcase, favorite game, texts,
badges, colors), like the modules of a Steam profile. The GameVault server has no place for it, so the profiles
are kept here, one JSON file per player in `PROFILES_DIRECTORY`. Without a writable volume the uploads still work
and the profiles simply stay the default ones.
