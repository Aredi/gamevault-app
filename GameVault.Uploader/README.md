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

## Profiles

The client's **Community** page lets each player customize their profile (game showcase, favorite game, texts,
badges, colors), like the modules of a Steam profile. The GameVault server has no place for it, so the profiles
are kept here, one JSON file per player in `PROFILES_DIRECTORY`. Without a writable volume the uploads still work
and the profiles simply stay the default ones.
