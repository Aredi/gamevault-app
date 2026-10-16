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

## Web page

Open the service's address in a browser (e.g. https://gamevaultupload.example.com) and sign in with a GameVault
account.

**Install on my PC** (every player): search a game of the server and ask for it; the player's SanctuaryVault
client reads the list every minute while it is open, downloads the game without asking (last install location)
and takes it off the list.

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
| `GET /remote-installs` | the games the signed-in player asked to install on their PC |
| `POST /remote-installs` | asks for one (`{"gameId": 5, "title": "..."}`, at most 20 waiting) |
| `DELETE /remote-installs/{gameId}` | takes it (the client) or drops it |
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
