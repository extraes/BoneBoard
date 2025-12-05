# BoneBoard

Silly community bot, made for the BONELAB fan-run Discord server

All intended to run in only one server, though.

### Modules
- Stargrid.cs: Starboard clone/quoting
- Casino.cs: Slots & blackjack
- Confessional.cs: User confessions, with optional AI confession system
  - (and voting, so that if someone guesses correctly whether a confession was made by a person/AI, they get points)
- FrogRole.cs: React to/reply to a message to get a role. Leaderboard included.
- ImageRoyale.cs & VideoRoyale.cs: Vote on your favorite image/video, winner gets sent in a configurable channel
- MessageBuffer.cs: Peak of mid, ngl. Buffer messages for a certain amount of time, then send them in a channel.
- Hangman.cs: Hangman!

### Running it yourself

- im ngl i didnt really intend it to be ran by other people, but if you want to, go ahead
- each module will have its own config fields that must be filled out in order to work
  - if you wamt to know what modules use what config fields, open their .cs file (boo! source code jumpscare!) and Ctrl+F for "Config.values.", lol
- if you want Stargrid to work, you'll need to install the '[Twemoji-Mozilla](https://github.com/mozilla/twemoji-colr/releases)' font (for emojis), alongside whatever font you have in the config file (default is 'Comfortaa')
  - also you'll need "gifsicle" installed (and in your OS's PATH so it can be used) in order for animated gif quotes to work