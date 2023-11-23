# WheelBot
A discord bot for spinning a wheel of fortune.
I will not be hosting a public version of this bot.

## Commands

| Command | Description|
|-------------|-----------|
|/add| adds an option to the wheel|
|/rm |removes an option from the wheel|
|/preview | generates a still image of the current wheel|
|/randomize | randomize the order of all the options|
|/spin | generates a gif of a spinning wheel that lands on a random option|
|/reset | clears all options from the wheel |


Renaming a channel will clear the wheel in the current version as it is stored based on the name of the channel rather than the ID. 

## Example output

example with 6 options, randomized

![FullAnimation](https://media.discordapp.net/attachments/1055479473316835468/1134494308121186375/FullAnimation.gif)

# SET UP

## Visual Studio

Download and install [Visual Studio 2022](https://visualstudio.microsoft.com/#vs-section) or [VS Code](https://visualstudio.microsoft.com/#vscode-section) with the [c# extension](https://code.visualstudio.com/docs/languages/csharp)

You can also install these using winget by running

``` winget install --id Microsoft.VisualStudio.2022.Community ```


or


``` winget install --id Microsoft.VisualStudioCode ```

## Storage Emulator

Download and install azurerite by running (requires npm)

``` npm i -g azurite ```

This will install the azure storage emulator globally.
Also make a folder somewhere for the storage, I will be using 'D:/Azurite' as an example.

(Optional) If you want to be able to explore the contents of the storage emulator download 


[Azure Storage Explorer](https://azure.microsoft.com/en-us/products/storage/storage-explorer) (The download is from the Operating System dropdown button) or run

``` winget install --id Microsoft.Azure.StorageExplorer ```

## Discord Developer

Go to the [Discord developer portal (https://discord.com/developers/applications)](https://discord.com/developers/applications) and register a new application.

1. Open the new application and navigate to the "Bot" page
2. Generate a new bot token by clicking the "reset token" button
3. Open the WheelBot.sln from this repo with your preferred IDE.
4. Add the token to userSecrets (You can add an extension for this in VS code):
   - Right click the project file "WheelBotApiApp.csproj" > "Manage User Secrets"
   - Add your generated token to the secrets like this
    ```{"BotToken": "Your_Token_Here"}```
5. Navigate to "OAuth2/General" and add ```https://localhost:7055``` as a redirect url
6. Navigate to "OAuth2/URL Generator", select 'applications.commands.permissions.update' and the redirect url. Copy the url and use it to add the bot to a server.

## Run the app

1. Run the command ``` azurite -s -l D:/Azurite ``` to start the storage emulator.
2. Open the WheelBot.sln from this repo with your preferred IDE.
3. Start the project

With everything set up correctly this should make the bot go online on discord.
