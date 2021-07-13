using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.EventArgs;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.CommandsNext.Entities;
using Homiebot.Discord.Commands;
using Homiebot.Discord.Voice;
using Homiebot.Models;
using Homiebot.Images;

namespace Homiebot.Discord 
{
    public class DiscordHelper
    {
        public const int CharacterLimit = 1900;
        private readonly ILogger<HomieBot> logger;
        private readonly IConfiguration config;
        private readonly IServiceProvider services;
        private string token;
        private IEnumerable<string> commandMarkers;
        private bool connected;
        private DiscordClient discordClient;
        private CommandsNextExtension commands;
        private VoiceNextExtension voiceNext;
        public bool Connected {
            get => connected;
        }
        public DiscordHelper(string token, IEnumerable<string> commandMarkers, IConfiguration config, ILogger<HomieBot> logger, IServiceProvider services)
        {
            this.token = token;
            this.commandMarkers = commandMarkers;
            this.config = config;
            this.logger = logger;
            this.services = services;
            connected = false;
        }
        private T getService<T>(){
            return (T)services.GetService(typeof(T));
        }
        public async Task Initialize()
        {
            var homiebotConfig = config.GetSection("BotConfig").Get<BotConfig>();
            logger.LogInformation("Starting up main discord client");
            discordClient = new DiscordClient( new DiscordConfiguration(){
                AutoReconnect = true,
                Token = token,
                TokenType = TokenType.Bot,
            });
            commands = discordClient.UseCommandsNext(new CommandsNextConfiguration(){
                CaseSensitive = false,
                EnableDefaultHelp = true,
                StringPrefixes = commandMarkers,
                Services = services,
                EnableDms = false,
            });
            logger.LogInformation("Registering custom parser");
            //commands.RegisterConverter(new StringArrayParamConverter());
            //where the commands are registered!
            logger.LogInformation("Registering Baseline Commands");
            commands.SetHelpFormatter<CustomHelpFormatter>();
            commands.RegisterCommands<HomieCommands>();
            commands.RegisterCommands<ImageMemeCommands>();
            commands.RegisterCommands<DiceCommands>();
            logger.LogInformation("Parsing gimmicks");
            HomieCommands hc = homiebotConfig.UseVoice ? 
            new HomieCommands(getService<Random>(),logger,config,getService<ITextToSpeechHelper>()) :
            new HomieCommands(getService<Random>(),logger,config);
            ImageMemeCommands ic = new ImageMemeCommands(logger,config,getService<IImageStore>(),getService<IImageProcessor>(), getService<Random>());
            //var childgimmicks = config.GetSection("Gimmicks").GetChildren();
            var Gimmicks = config.GetSection("Gimmicks").Get<IEnumerable<Gimmick>>();
            logger.LogInformation("Registering Gimmicks");
            commands.RegisterCommands(hc.GetDynamicGimmickCommands(Gimmicks));
            commands.RegisterCommands(ic.GetDynamicImageCommands());
            discordClient.MessageCreated += async (sender,message) => 
            {
                if(await message.HandleMemorableKeywords(sender, logger))
                {
                    return;
                }
                if(message.MentionedUsers.Contains(discordClient.CurrentUser))
                {
                    await message.Channel.TriggerTypingAsync();
                    logger.LogInformation("Mentioned by name figuring out what to do with that");
                    bool handled = false;
                    handled = await message.HandleHomieMentionCommands(sender,logger);
                    // subsequent handler extension methods go in if clauses below
                    if(!handled){handled = await message.HandleMemoryMentionCommands(logger);}
                    // finally if they're still unhandled do the default
                    if(!handled)
                    {
                        logger.LogInformation("I was pinged but couldn't find a match command, returning help instructions");
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} I don't know what to do with that, but you can use the command {commandMarkers.FirstOrDefault()}help for some help");
                    }
                }
                return;
            };
            logger.LogInformation("Registering reactions");
            discordClient.MessageReactionAdded += hc.ProcessReaction;
            discordClient.MessageReactionAdded += ic.ProcessReaction;
            
            if(homiebotConfig.UseVoice)
            {
                logger.LogInformation("Registering Voice Commands");
                voiceNext = discordClient.UseVoiceNext(
                    new VoiceNextConfiguration{
                        EnableIncoming = false,
                        AudioFormat = new AudioFormat(48000,1,VoiceApplication.Voice)
                    }
                );
                commands.RegisterCommands<VoiceCommands>();
            }
            if(homiebotConfig.UseBrain)
            {
                logger.LogInformation("Registering memory commands");
                commands.RegisterCommands<MemoryCommands>();
            }
            
            logger.LogInformation("Trying Connect");
            try
            {
                await discordClient.ConnectAsync();
                connected = true;
            }
            catch(Exception e)
            {
                logger.LogError(e,"Exception trying to connect: {errorMessage}",e.Message);
            }
        }

        public async Task Disconnect()
        {
            if(connected)
            {
                await discordClient.DisconnectAsync();
                connected = false;
            }
        }

        public async Task<bool> ReConnect()
        {
            try
            {
                await discordClient.ReconnectAsync();
                return true;
            }
            catch(Exception e)
            {
                logger.LogError(e,"Exception trying to reconnect: {errorMessage}", e.Message);
                return false;
            }
            
        }
    }
//where the custom help format is setup
    public class CustomHelpFormatter : DefaultHelpFormatter
    {   
        public CustomHelpFormatter(CommandContext ctx) : base(ctx) { }

        public override CommandHelpMessage Build()
        {   
            EmbedBuilder.Color = DiscordColor.SpringGreen;
            return base.Build();
        }
    }
}