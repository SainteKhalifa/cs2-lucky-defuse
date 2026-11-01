# CS2 Lucky Defuse — Fork by SainteKhalifa

> Fork de [Kandru/cs2-lucky-defuse](https://github.com/Kandru/cs2-lucky-defuse) — Plugin CounterStrikeSharp pour CS2.

---

## Français

### Qu'est-ce que ce plugin ?

Ce plugin ajoute un mini-jeu de désamorçage de bombe basé sur la chance. Quand la bombe est plantée, le Terroriste choisit un "fil piégé" parmi 4 couleurs. Le CT qui désamorce peut tenter de couper un fil : s'il choisit le bon, la bombe est désamorcée instantanément. S'il se trompe, la bombe explose immédiatement.

### Fonctionnalités

**Système de fils**
- Quand un T plante la bombe, un menu s'affiche pour choisir le fil piégé (1 parmi 4 : Rouge, Vert, Bleu, Jaune)
- Si le T ne choisit pas avant la fermeture du menu (`planter_menu_duration`, 10 secondes par défaut), un fil est tiré aléatoirement
- Les bots n'ont pas de menu : un bot planteur garde un fil tiré au hasard, un bot défuseur désamorce normalement
- Quand un CT commence à désamorcer, un menu s'affiche pour couper un fil
- Bon fil coupé → désamorçage instantané + tous les T encore en vie sont tués
- Mauvais fil coupé → la bombe explose immédiatement
- Un seul fil par bombe : une fois un fil coupé, pas de second essai
- Désamorçage normal (sans choisir de fil) → tous les T encore en vie sont tués

**Menu de sélection**
- Navigation avec les touches Z (haut) et S (bas) en AZERTY, W et S en QWERTY : ce sont les touches pour avancer et reculer. Maintenir la touche fait défiler
- Confirmation avec ESPACE, sur un nouvel appui : une touche déjà enfoncée à l'ouverture du menu ne choisit rien
- Affichage en couleur avec indicateurs visuels ◀ ▶
- Commande console alternative : `ld_choose_wire <1-4>`

**Statistiques persistantes (SQLite)**
- Écritures sur un thread dédié, base en mode WAL : choisir ou couper un fil ne ralentit plus le serveur (jusqu'à 20 ms par écriture auparavant)
- Chaque joueur est suivi par son Steam ID (persistant même en cas de changement de pseudo)
- Les stats sont affichées dans le chat à la fin de chaque round (bombe désamorcée ou explosée)
- Stats des CT : bons fils coupés, mauvais fils coupés, désamorçages normaux
- Stats des T : bombes plantées, fils choisis manuellement, fils tirés aléatoirement
- Fichier de base de données : `addons/counterstrikesharp/configs/plugins/LuckyDefuse/stats.db` (survit aux mises à jour du plugin ; une ancienne base présente dans le dossier du plugin est migrée automatiquement)

**Tendances des fils**
- Toutes les N manches, le chat affiche la répartition en % des fils sur l'ensemble des données du serveur
- Deux distributions : les fils piégés les plus **choisis** par les planteurs, et les fils les plus **coupés** par les défuseurs
- Seuls les choix manuels comptent comme « choisis » (les tirages aléatoires sont uniformes et fausseraient la tendance)
- Compteurs stockés dans une table dédiée de 4 lignes : l'affichage ne lit que 4 lignes, sans agrégation sur tous les joueurs

**Commandes**

| Commande | Description |
|---|---|
| `!ldstats` | Affiche vos statistiques globales (défuseur + planteur) |
| `!ldtop` | Affiche le top 5 des meilleurs défuseurs du serveur |
| `ld_choose_wire <1-4>` | Choisir un fil via la console (alternative au menu) |

**Localisation**
- Support complet du français et de l'anglais
- Couleurs des fils traduites dans les menus et messages chat
- Configurable via `language` dans le fichier de configuration

### Modifications apportées par rapport au projet source

| Fonctionnalité | Projet source | Ce fork |
|---|---|---|
| Tuer les T sur désamorçage par fil | Oui | Oui (désactivable) |
| Tuer les T sur désamorçage normal | Non | Oui (désactivable) |
| Statistiques persistantes SQLite | Non | Oui |
| Statistiques dans MySQL ou MariaDB (au choix) | Non | Oui |
| Base de données hors du thread du jeu (WAL) | Non | Oui |
| Affichage stats en fin de round | Non | Oui (désactivable) |
| Commandes `!ldstats` / `!ldtop` | Non | Oui |
| Traduction des couleurs de fils | Non | Oui |
| Support Linux sans dépendance système | Non | Oui |

### Installation

> **Prérequis :** une version récente de [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) basée sur .NET 10.

1. Télécharger la dernière release depuis la [page des releases GitHub](https://github.com/SainteKhalifa/cs2-lucky-defuse/releases/)
2. Déposer le dossier `LuckyDefuse` dans `/addons/counterstrikesharp/plugins/`
3. (Re)démarrer le serveur

### Configuration

Le fichier de configuration est généré automatiquement dans :
`/addons/counterstrikesharp/configs/plugins/LuckyDefuse/LuckyDefuse.json`

```json
{
  "planter_menu_duration": 10,
  "notification_delay": 30,
  "language": "fr",
  "kill_terrorists_on_defuse": true,
  "show_round_stats": true,
  "wire_trends_every_rounds": 5,
  "database_host": "",
  "database_port": 3306,
  "database_name": "",
  "database_user": "",
  "database_password": ""
}
```

| Paramètre | Description | Défaut |
|---|---|---|
| `planter_menu_duration` | Délai en secondes avant la fermeture automatique du menu du planteur si aucun fil n'est choisi | `10` |
| `notification_delay` | Délai en secondes avant le message d'info en chat après la pose | `30` |
| `language` | Langue des messages (`en` ou `fr`) | `en` |
| `kill_terrorists_on_defuse` | Tuer tous les T encore en vie quand la bombe est désamorcée | `true` |
| `show_round_stats` | Afficher les stats dans le chat en fin de round | `true` |
| `wire_trends_every_rounds` | Afficher les tendances des fils toutes les N manches (`0` = jamais) | `5` |
| `database_host` | Hôte MySQL ou MariaDB ; vide : les stats restent dans `stats.db` (SQLite) | `""` |
| `database_port` | Port de la base | `3306` |
| `database_name`, `database_user`, `database_password` | La base et son compte | `""` |

#### Base de données

Par défaut, les stats sont dans `stats.db` (SQLite), à côté de la config : rien à installer. Avec `database_host`, elles vont dans cette base MySQL ou MariaDB, par exemple pour les afficher sur un site.

- Le plugin y crée ses tables `player_stats` et `wire_stats` : le compte a besoin de `SELECT`, `INSERT`, `UPDATE` et `CREATE` sur la base. Sans `CREATE`, un admin crée les tables avant.
- La connexion se fait hors du thread du jeu : une base injoignable ne fige pas le serveur, et la connexion se rouvre à l'écriture suivante.
- Les stats déjà dans `stats.db` ne sont pas reprises automatiquement.

### Compiler soi-même

```bash
git clone https://github.com/SainteKhalifa/cs2-lucky-defuse.git
cd cs2-lucky-defuse
dotnet restore
dotnet publish -c Release
```

### Auteurs

- Projet original : [@jmgraeffe](https://github.com/jmgraeffe) / [@derkalle4](https://github.com/derkalle4) / [@Kandru](https://github.com/Kandru)
- Fork et modifications : [@SainteKhalifa](https://github.com/SainteKhalifa)

---

## English

### What is this plugin?

This plugin adds a luck-based bomb defuse mini-game. When the bomb is planted, the Terrorist chooses a "hot wire" among 4 colors. The CT who defuses can try to cut a wire: if they pick the right one, the bomb is instantly defused. If they pick the wrong one, the bomb explodes immediately.

### Features

**Wire system**
- When a T plants the bomb, a menu appears to choose the hot wire (1 of 4: Red, Green, Blue, Yellow)
- If the T doesn't choose before the menu closes (`planter_menu_duration`, 10 seconds by default), a wire is selected randomly
- Bots get no menu: a bot planter keeps a random wire, a bot defuser defuses normally
- When a CT starts defusing, a menu appears to cut a wire
- Correct wire cut → instant defuse + all living T players are killed
- Wrong wire cut → bomb explodes immediately
- One wire per bomb: once a wire is cut, there is no second try
- Normal defuse (without choosing a wire) → all living T players are killed

**Selection menu**
- Navigate with W (up) and S (down), Z and S on AZERTY: the forward and back keys. Hold the key to scroll
- Confirm with SPACE, on a new press: a key already held when the menu opens chooses nothing
- Color-coded display with visual indicators ◀ ▶
- Alternative console command: `ld_choose_wire <1-4>`

**Persistent statistics (SQLite)**
- Writes run on a dedicated thread, database in WAL mode: choosing or cutting a wire no longer slows the server down (up to 20 ms per write before)
- Each player is tracked by their Steam ID (persistent even after nickname changes)
- Stats are displayed in chat at the end of each round (bomb defused or exploded)
- CT stats: correct wires cut, wrong wires cut, normal defuses
- T stats: bombs planted, manually chosen wires, randomly chosen wires
- Database file: `addons/counterstrikesharp/configs/plugins/LuckyDefuse/stats.db` (survives plugin updates; an old database found in the plugin folder is migrated automatically)

**Wire trends**
- Every N rounds, chat shows the server-wide wire distribution as percentages
- Two distributions: the hot wires most **chosen** by planters, and the wires most **cut** by defusers
- Only manual picks count as "chosen" (random picks are uniform and would flatten the trend)
- Counters live in a dedicated 4-row table, so displaying them reads 4 rows instead of aggregating over every player

**Commands**

| Command | Description |
|---|---|
| `!ldstats` | Show your all-time stats (defuser + planter) |
| `!ldtop` | Show the server's top 5 defusers |
| `ld_choose_wire <1-4>` | Choose a wire via console (menu alternative) |

**Localization**
- Full French and English support
- Wire color names translated in menus and chat messages
- Configurable via `language` in the config file

### Changes from the original project

| Feature | Original | This fork |
|---|---|---|
| Kill Ts on wire defuse | Yes | Yes (toggleable) |
| Kill Ts on normal defuse | No | Yes (toggleable) |
| Persistent SQLite statistics | No | Yes |
| Statistics in MySQL or MariaDB (optional) | No | Yes |
| Database off the game thread (WAL) | No | Yes |
| Stats display at round end | No | Yes (toggleable) |
| `!ldstats` / `!ldtop` commands | No | Yes |
| Translated wire colors | No | Yes |
| Linux support without system dependency | No | Yes |

### Installation

> **Requirement:** a recent [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) version based on .NET 10.

1. Download the latest release from the [GitHub releases page](https://github.com/SainteKhalifa/cs2-lucky-defuse/releases/)
2. Place the `LuckyDefuse` folder into `/addons/counterstrikesharp/plugins/`
3. (Re)start the server

### Configuration

The config file is auto-generated at:
`/addons/counterstrikesharp/configs/plugins/LuckyDefuse/LuckyDefuse.json`

```json
{
  "planter_menu_duration": 10,
  "notification_delay": 30,
  "language": "en",
  "kill_terrorists_on_defuse": true,
  "show_round_stats": true,
  "wire_trends_every_rounds": 5,
  "database_host": "",
  "database_port": 3306,
  "database_name": "",
  "database_user": "",
  "database_password": ""
}
```

| Parameter | Description | Default |
|---|---|---|
| `planter_menu_duration` | Delay in seconds before the planter menu auto-closes if no wire is chosen | `10` |
| `notification_delay` | Delay in seconds before the info message in chat after bomb plant | `30` |
| `language` | Message language (`en` or `fr`) | `en` |
| `kill_terrorists_on_defuse` | Kill all living Ts when the bomb gets defused | `true` |
| `show_round_stats` | Show stats in chat at round end | `true` |
| `wire_trends_every_rounds` | Show wire trends every N rounds (`0` = never) | `5` |
| `database_host` | MySQL or MariaDB host; empty: the stats stay in `stats.db` (SQLite) | `""` |
| `database_port` | Database port | `3306` |
| `database_name`, `database_user`, `database_password` | The database and its account | `""` |

#### Database

By default the stats live in `stats.db` (SQLite), next to the config: nothing to install. With `database_host`, they go to that MySQL or MariaDB database instead, for example to show them on a website.

- The plugin creates its `player_stats` and `wire_stats` tables there: the account needs `SELECT`, `INSERT`, `UPDATE` and `CREATE` on the database. Without `CREATE`, an admin creates the tables first.
- The connection happens off the game thread: an unreachable database never freezes the server, and the connection reopens on the next write.
- Stats already in `stats.db` are not imported automatically.

### Build from source

```bash
git clone https://github.com/SainteKhalifa/cs2-lucky-defuse.git
cd cs2-lucky-defuse
dotnet restore
dotnet publish -c Release
```

### Authors

- Original project: [@jmgraeffe](https://github.com/jmgraeffe) / [@derkalle4](https://github.com/derkalle4) / [@Kandru](https://github.com/Kandru)
- Fork & modifications: [@SainteKhalifa](https://github.com/SainteKhalifa)

---

Released under [GPLv3](/LICENSE)
