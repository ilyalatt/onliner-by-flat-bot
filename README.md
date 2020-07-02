# Onliner By Flat Bot

## Quick Start

* Obtain Telegram bot token (you can ask `@BotFather` for one).
* Clone the repository (or download only `docker-compose.yaml`).
* Create `data` directory.
* Create `data/config.json`.
* Here is an example of `config.json`.

```JSON
{
  "telegramBotToken": "123456:ABCDE",
  "channels": [
    {
      "name": "custom name here",
      "telegramChatId": -1,
      "onlinerUrl": "https://r.onliner.by/ak/?rent_type%5B%5D=2_rooms&rent_type%5B%5D=3_rooms&rent_type%5B%5D=4_rooms&rent_type%5B%5D=5_rooms&rent_type%5B%5D=6_rooms&only_owner=true&price%5Bmin%5D=640&price%5Bmax%5D=8600&currency=usd&metro%5B%5D=red_line&metro%5B%5D=blue_line#bounds%5Blb%5D%5Blat%5D=53.8937336407655&bounds%5Blb%5D%5Blong%5D=27.518712295391815&bounds%5Brt%5D%5Blat%5D=53.91772192249882&bounds%5Brt%5D%5Blong%5D=27.55391020011789",
      "routeDestinationUrl": "https://yandex.com/maps/157/minsk/stops/station__9880205/?ll=27.541550,53.905134&z=16.88"
    }
  ]
}
```

* Run `docker-compose up`.
* Send a message to the bot.
* Find your chat id in the bot output and place it in the config.
* Restart the bot (Ctrl+C to stop the bot). Use `docker-compose up -d` to run the bot in the background.

## Development

* Install .NET Core 3.1 SDK.
* Clone the repo.
* Setup `data/config.json`.
* Execute `dotnet run --project src` in the repo directory.
