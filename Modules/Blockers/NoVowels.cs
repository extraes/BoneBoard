namespace BoneBoard.Modules.Blockers;

internal class NoVowels(BoneBot bot) : ModuleBase(bot)
{
    protected override bool GlobalStopEventPropagation(DiscordEventArgs eventArgs)
    {
        if (eventArgs is MessageCreatedEventArgs msgCreatedArgs)
        {
            return MessageCheck(msgCreatedArgs.Message);
        }

        if (eventArgs is MessageUpdatedEventArgs msgUpdatedArgs)
        {
            return MessageCheck(msgUpdatedArgs.Message);
        }

        if (eventArgs is MessageReactionAddedEventArgs reactionArgs)
        {
            var regionalIndicator = reactionArgs.Emoji.GetDiscordName();
            regionalIndicator = regionalIndicator.Contains("regional_indicator_") ? regionalIndicator.Replace("regional_indicator_", "") : null;
            if (regionalIndicator is null || regionalIndicator.Length != 1)
                return false;

            if (!Config.values.channelsWhereNoVowelsAreAllowed.Contains(reactionArgs.Channel.Id) ||
                !"aeiou".Contains(regionalIndicator[0], StringComparison.InvariantCultureIgnoreCase))
                return false;


            _ = TryDeleteAsync(reactionArgs.Message, reactionArgs.Emoji, reactionArgs.User,
                "'no vowels' includes writing in reactions. woe.");

            return true;
        }

        return false;
    }

    private bool MessageCheck(DiscordMessage msg)
    {
        if (bot.IsMe(msg.Author))
            return false;
        if (msg.Timestamp.AddDays(1) < DateTime.Now)
            return false; // message is old enough to probably not be relevant

        var hasVowelInDisallowedChannel = Config.values.channelsWhereNoVowelsAreAllowed.Contains(msg.ChannelId) &&
                                          msg.Content.Any(c => "aeiou".Contains(c, StringComparison.InvariantCultureIgnoreCase));

        if (hasVowelInDisallowedChannel)
        {
            TryDeleteDontCare(msg, "you must not use vowels in this channel. woe.");

            return true;
        }

        return false;
    }
}