# CS2 Lucky Defuse — Fork by SainteKhalifa

> Fork de [Kandru/cs2-lucky-defuse](https://github.com/Kandru/cs2-lucky-defuse) — Plugin CounterStrikeSharp pour CS2.

---

## Français

### Qu'est-ce que ce plugin ?

Ce plugin ajoute un mini-jeu de désamorçage de bombe basé sur la chance. Quand la bombe est plantée, le Terroriste choisit un "fil piégé" parmi 4 couleurs. Le CT qui désamorce peut tenter de couper un fil : s'il choisit le bon, la bombe est désamorcée instantanément. S'il se trompe, la bombe explose immédiatement.

### Fonctionnalités

**Système de fils**
- Quand un T plante la bombe, un menu s'affiche pour choisir le fil piégé (1 parmi 4 : Rouge, Vert, Bleu, Jaune)
- Si le T ne choisit pas dans les 5 secondes, un fil est tiré aléatoirement
- Quand un CT commence à désamorcer, un menu s'affiche pour couper un fil
- Bon fil coupé → désamorçage instantané + tous les T encore en vie sont tués
- Mauvais fil coupé → la bombe explose immédiatement
- Désamorçage normal (sans choisir de fil) → tous les T encore en vie sont tués

**Menu de sélection**
- Navigation avec les touches W (haut) et S (bas)
- Confirmation avec ESPACE
- Affichage en couleur avec indicateurs visuels ◀ ▶
- Commande console alternative : `ld_choose_wire <1-4>`

**Statistiques persistantes (SQLite)**
- Chaque joueur est suivi par son Steam ID (persistant même en cas de changement de pseudo)
- Les stats sont affichées dans le chat à la fin de chaque round (bombe désamorcée ou explosée)
- Stats des CT : bons fils coupés, mauvais fils coupés, désamorçages normaux
- Stats des T : bombes plantées, fils choisis manuellement, fils tirés aléatoirement
- Fichier de base de données : `addons/counterstrikesharp/plugins/LuckyDefuse/stats.db`

**Localisation**
- Support complet du français et de l'anglais
- Couleurs des fils traduites dans les menus et messages chat
- Configurable via `language` dans le fichier de configuration

### Modifications apportées par rapport au projet source

| Fonctionnalité | Projet source | Ce fork |
|---|---|---|
| Tuer les T sur désamorçage par fil | Oui | Oui |
| Tuer les T sur désamorçage normal | Non | Oui |
| Statistiques persistantes SQLite | Non | Oui |
| Affichage stats en fin de round | Non | Oui |
| Traduction des couleurs de fils | Non | Oui |
| Support Linux sans dépendance système | Non | Oui |

### Installation

1. Télécharger la dernière release depuis la [page des releases GitHub](https://github.com/SainteKhalifa/cs2-lucky-defuse/releases/)
2. Déposer le dossier `LuckyDefuse` dans `/addons/counterstrikesharp/plugins/`
3. (Re)démarrer le serveur

### Configuration

Le fichier de configuration est généré automatiquement dans :
`/addons/counterstrikesharp/configs/plugins/LuckyDefuse/LuckyDefuse.json`

```json
{
  "notification_delay": 30,
  "language": "fr"
}
```

| Paramètre | Description | Défaut |
|---|---|---|
| `notification_delay` | Délai en secondes avant le message d'info en chat après la pose | `30` |
| `language` | Langue des messages (`en` ou `fr`) | `en` |

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
- If the T doesn't choose within 5 seconds, a wire is selected randomly
- When a CT starts defusing, a menu appears to cut a wire
- Correct wire cut → instant defuse + all living T players are killed
- Wrong wire cut → bomb explodes immediately
- Normal defuse (without choosing a wire) → all living T players are killed

**Selection menu**
- Navigate with W (up) and S (down)
- Confirm with SPACE
- Color-coded display with visual indicators ◀ ▶
- Alternative console command: `ld_choose_wire <1-4>`

**Persistent statistics (SQLite)**
- Each player is tracked by their Steam ID (persistent even after nickname changes)
- Stats are displayed in chat at the end of each round (bomb defused or exploded)
- CT stats: correct wires cut, wrong wires cut, normal defuses
- T stats: bombs planted, manually chosen wires, randomly chosen wires
- Database file: `addons/counterstrikesharp/plugins/LuckyDefuse/stats.db`

**Localization**
- Full French and English support
- Wire color names translated in menus and chat messages
- Configurable via `language` in the config file

### Changes from the original project

| Feature | Original | This fork |
|---|---|---|
| Kill Ts on wire defuse | Yes | Yes |
| Kill Ts on normal defuse | No | Yes |
| Persistent SQLite statistics | No | Yes |
| Stats display at round end | No | Yes |
| Translated wire colors | No | Yes |
| Linux support without system dependency | No | Yes |

### Installation

1. Download the latest release from the [GitHub releases page](https://github.com/SainteKhalifa/cs2-lucky-defuse/releases/)
2. Place the `LuckyDefuse` folder into `/addons/counterstrikesharp/plugins/`
3. (Re)start the server

### Configuration

The config file is auto-generated at:
`/addons/counterstrikesharp/configs/plugins/LuckyDefuse/LuckyDefuse.json`

```json
{
  "notification_delay": 30,
  "language": "en"
}
```

| Parameter | Description | Default |
|---|---|---|
| `notification_delay` | Delay in seconds before the info message in chat after bomb plant | `30` |
| `language` | Message language (`en` or `fr`) | `en` |

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
