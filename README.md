# CounterstrikeSharp - LuckyDefuse - SainteKhalifa Fork

### Bomb Planting & Defuse Menu Changes by SainteKhalifa

- **Localized wire colors**
  - Wire color names are now fully translated using the language system (`lang/*.json`).
  - Colors displayed in menus and chat messages adapt automatically to the selected language.
  - This allows adding new languages without changing any code.

- **Instant Terrorist elimination on successful defuse**
  - When a Counter-Terrorist cuts the correct wire and defuses the bomb instantly:
    - The bomb is defused immediately.
    - All Terrorist players are instantly killed.
  - This guarantees a clear and decisive round ending.

- **Improved round and state management**
  - Notification timers are properly stopped at the end of each round to prevent duplicate messages.
  - Menus are automatically closed when:
    - The round ends
    - The bomb explodes
    - The bomb is defused
  - Invalid player states are safely ignored to avoid unexpected errors.

This plug-in provides the ability to defuse the bomb by having a 25% chance to cut the correct wire.

## Installation

1. Download and extract the latest release from the [GitHub releases page](https://github.com/Kandru/cs2-lucky-defuse/releases/).
2. Move the "LuckyDefuse" folder to the `/addons/counterstrikesharp/plugins/` directory.
3. (Re)start the server and wait for it to be completely loaded.
4. Restart the server again because it maybe applied some Gamedata entries for the plug-in to work correctly.

Updating is even easier: simply overwrite all plugin files and they will be reloaded automatically. To automate updates please use our [CS2 Update Manager](https://github.com/Kandru/cs2-update-manager/).


## Configuration

This plugin automatically creates a readable JSON configuration file. This configuration file can be found in `/addons/counterstrikesharp/configs/plugins/LuckyDefuse/LuckyDefuse.json`.

```json

```

## Compile Yourself

Clone the project:

```bash
git clone https://github.com/Kandru/cs2-lucky-defuse.git
```

Go to the project directory

```bash
  cd cs2-lucky-defuse
```

Install dependencies

```bash
  dotnet restore
```

Build debug files (to use on a development game server)

```bash
  dotnet build
```

Build release files (to use on a production game server)

```bash
  dotnet publish
```

## FAQ

TBD

## License

Released under [GPLv3](/LICENSE) by [@Kandru](https://github.com/Kandru).

## Authors

- [@jmgraeffe](https://www.github.com/jmgraeffe)
- [@derkalle4](https://www.github.com/derkalle4)
