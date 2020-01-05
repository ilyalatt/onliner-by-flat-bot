# Onliner By Flat Bot

![Screenshot](https://user-images.githubusercontent.com/20675149/71778898-a9f93b00-2fc4-11ea-8309-db639440ccc7.png)

## Quick Start

* Install .NET Core 3.1 SDK.
* Clone the repo.
* Create a `config.json` in the repo directory like

```JSON
{
  "telegramBotToken": "123456:ABCDE",
  "channels": [
    {
      "name": "boyz",
      "enabled": true,
      "telegramChatId": 12345,
      "onliner": {
        "searchUrl": "https://ak.api.onliner.by/search/apartments?price%5Bmin%5D=500&price%5Bmax%5D=10000&currency=usd&bounds%5Blb%5D%5Blat%5D=53.73206016299958&bounds%5Blb%5D%5Blong%5D=27.39028930664063&bounds%5Brt%5D%5Blat%5D=54.063820915086225&bounds%5Brt%5D%5Blong%5D=27.73361206054688&page=1&v=0.22198904804319453"
      },
      "route": {
        "location": {
          "longitude": 27.55,
          "latitude": 53.93
        }
      }
    }
  ]
}
```

* Execute `dotnet run` in the repo directory.

## Config details

* Telegram Bot Token (ask @BotFather for one).
* Chat Id (invite the bot to your chat and go to <https://api.telegram.org/bot12345:ABCDE/getUpdates> in your browser).
* Longitude and latitude of the place to show routes (find the place in yandex maps and copy the location from the url).
* Onliner Search Url (just go to <https://r.onliner.by/ak/,> copy the query after `?` symbol and prepend `https://ak.api.onliner.by/search/apartments?`).

## Setup process is hard for me

So the process is not quite easy but I doubt anyone except me needs this bot. If you really want this bot but it is hard for you to configure it just create an issue for me. I'll simplify the setup process drastically.

## There are no logs

Yeah, I am too lazy. The same story as with the setup process. You can create an issue for this.
