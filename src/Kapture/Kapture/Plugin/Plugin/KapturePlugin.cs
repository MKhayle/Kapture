using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

using CheapLoc;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Kapture.Kapture.Extensions;
using Kapture.Localization;
using Lumina.Excel;
using Lumina.Extensions;
using Lumina.Excel.Sheets;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.NpcTrade;
using Dalamud.Utility;

// ReSharper disable UseCollectionExpression
// ReSharper disable once ClassNeverInstantiated.Global
#pragma warning disable CS8618, CS8602

namespace Kapture
{
    /// <inheritdoc cref="Kapture.IKapturePlugin" />
    public sealed class KapturePlugin : IKapturePlugin, IDalamudPlugin
    {
        private readonly LegacyLoc localization;
        private readonly object locker = new ();

        /// <summary>
        /// Initializes a new instance of the <see cref="KapturePlugin"/> class.
        /// </summary>
        public KapturePlugin()
        {
            try
            {
                this.localization = new LegacyLoc(PluginInterface, CommandManager);
                this.InitContent();
                this.InitItems();
                this.PluginDataManager = new PluginDataManager(this);
                this.LoadConfig();
                this.FixConfig();
                this.LoadServices();
                this.SetupCommands();
                this.LoadUI();
                this.HandleFreshInstall();
                this.SetupListeners();
                this.IsInitializing = false;
                this.WindowManager?.AddWindows();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to initialize.");
                this.Dispose();
            }
        }

        [PluginService]
        public static IPluginLog PluginLog { get; private set; } = null!;

        /// <summary>
        /// Gets pluginInterface.
        /// </summary>
        [PluginService]
        public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

        /// <summary>
        /// Gets data manager.
        /// </summary>
        [PluginService]
        public static IDataManager DataManager { get; private set; } = null!;

        /// <summary>
        /// Gets command manager.
        /// </summary>
        [PluginService]
        public static ICommandManager CommandManager { get; private set; } = null!;

        /// <summary>
        /// Gets client state.
        /// </summary>
        [PluginService]
        public static IClientState ClientState { get; private set; } = null!;

        /// <summary>
        /// Gets chat gui.
        /// </summary>
        [PluginService]
        public static IChatGui Chat { get; private set; } = null!;

        /// <summary>
        /// Gets condition.
        /// </summary>
        [PluginService]
        public static ICondition Condition { get; private set; } = null!;

        /// <summary>
        /// Gets party list.
        /// </summary>
        [PluginService]
        public static IPartyList PartyList { get; private set; } = null!;

        /// <summary>
        /// Gets framework.
        /// </summary>
        [PluginService]
        public static IFramework Framework { get; private set; } = null!;

        /// <inheritdoc />
        public IPartyMember[] CurrentPartyList { get; set; } = Array.Empty<IPartyMember>();

        /// <inheritdoc />
        public RollMonitor RollMonitor { get; private set; } = null!;

        /// <summary>
        /// Gets loot processor.
        /// </summary>
        public LootProcessor LootProcessor { get; private set; } = null!;

        /// <summary>
        /// Gets or sets loot PluginLog.
        /// </summary>
        public LootLogger LootLogger { get; set; } = null!;

        /// <summary>
        /// Gets or sets HTTP service.
        /// </summary>
        public LootHTTP LootHTTP { get; set; } = null!;

        /// <summary>
        /// Gets or sets discord service.
        /// </summary>
        public LootDiscord LootDiscord { get; set; } = null!;

        /// <summary>
        /// Gets or sets list of item category names.
        /// </summary>
        public string[] ItemCategoryNames { get; set; } = null!;

        /// <summary>
        /// Gets or sets list of items for settings.
        /// </summary>
        public List<KeyValuePair<uint, ItemList>> ItemLists { get; set; } = null!;

        /// <inheritdoc />
        public bool IsInitializing { get; private set; }

        /// <inheritdoc />
        public List<LootEvent> LootEvents { get; } = new ();

        /// <inheritdoc />
        public List<LootRoll> LootRolls { get; } = new ();

        /// <inheritdoc />
        public List<LootRoll>? LootRollsDisplay { get; set; } = new ();

        /// <inheritdoc />
        public PluginDataManager PluginDataManager { get; private set; }

        /// <inheritdoc />
        public KaptureConfig Configuration { get; private set; } = null!;

        /// <summary>
        /// Gets or sets window manager.
        /// </summary>
        public WindowManager WindowManager { get; set; }

        /// <summary>
        /// Gets or sets list of content ids.
        /// </summary>
        public uint[] ContentIds { get; set; } = null!;

        /// <summary>
        /// Gets or sets list of content names.
        /// </summary>
        public string[] ContentNames { get; set; } = null!;

        /// <summary>
        /// Gets or sets list of item ids.
        /// </summary>
        public uint[] ItemIds { get; set; } = null!;

        /// <summary>
        /// Gets or sets list of item names.
        /// </summary>
        public string[] ItemNames { get; set; } = null!;

        /// <summary>
        /// Gets or sets list of item category ids.
        /// </summary>
        public uint[] ItemCategoryIds { get; set; } = null!;

        /// <inheritdoc />
        public bool InContent { get; set; }

        /// <inheritdoc />
        public bool IsRolling { get; set; }

        /// <summary>
        /// Save configuration.
        /// </summary>
        public void SaveConfig()
        {
            PluginInterface.SavePluginConfig(this.Configuration);
        }

        /// <inheritdoc />
        public string GetSeIcon(SeIconChar seIconChar)
        {
            return Convert.ToChar(seIconChar, CultureInfo.InvariantCulture)
                          .ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public string GetLocalPlayerName()
        {
            return ClientState.LocalPlayer?.Name.ToString() ?? string.Empty;
        }

        /// <inheritdoc />
        public string GetLocalPlayerWorld()
        {
            return ClientState.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? string.Empty;
        }

        /// <inheritdoc />
        public ushort ClientLanguage()
        {
            return (ushort)ClientState.ClientLanguage;
        }

        /// <inheritdoc />
        public string FormatPlayerName(int nameFormatCode, string playerName)
        {
            try
            {
                if (string.IsNullOrEmpty(playerName)) return string.Empty;

                if (nameFormatCode == NameFormat.FullName.Code) return playerName;

                if (nameFormatCode == NameFormat.Initials.Code)
                {
                    var splitName = playerName.Split(' ');
                    return splitName[0][..1] + splitName[1][..1];
                }

                if (nameFormatCode == NameFormat.SurnameAbbreviated.Code)
                {
                    var splitName = playerName.Split(' ');
                    return splitName[0] + " " + splitName[1][..1] + ".";
                }

                if (nameFormatCode == NameFormat.ForenameAbbreviated.Code)
                {
                    var splitName = playerName.Split(' ');
                    return splitName[0][..1] + ". " + splitName[1];
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to format name.");
            }

            return string.Empty;
        }

        /// <inheritdoc />
        public bool IsLoggedIn()
        {
            return ClientState.IsLoggedIn;
        }

        /// <summary>
        /// Load test data.
        /// </summary>
        public void LoadTestData()
        {
            TestData.LoadTestData(this);
        }

        /// <summary>
        /// Clear current data.
        /// </summary>
        public void ClearData()
        {
            this.LootEvents.Clear();
            this.LootRolls.Clear();
            this.LootRollsDisplay?.Clear();
            this.IsRolling = false;
        }

        /// <inheritdoc />
        public bool InCombat()
        => Condition[ConditionFlag.InCombat];

        /// <summary>
        /// Dispose plugin.
        /// </summary>
        public void Dispose()
        {
            try
            {
                this.DisposeListeners();
                this.LootLogger.Dispose();
                this.LootHTTP.Dispose();
                this.LootDiscord.Dispose();
                this.RollMonitor.Dispose();
                this.RemoveCommands();
                this.ClearData();
                this.localization.Dispose();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to dispose plugin properly.");
            }
        }

        /// <inheritdoc />
        public IEnumerable<IPartyMember> GetPartyMembers()
        {
            return PartyList.ToArray();
        }

        private void PrintMessage(string message)
        {
            Chat.Print(message);
        }

        private void SetupCommands()
        {
            CommandManager.AddHandler("/loot", new CommandInfo(this.ToggleLootOverlay)
            {
                HelpMessage = "Show loot overlay.",
                ShowInHelp = true,
            });
            CommandManager.AddHandler("/roll", new CommandInfo(this.ToggleRollOverlay)
            {
                HelpMessage = "Show roll monitor overlay.",
                ShowInHelp = true,
            });
            CommandManager.AddHandler("/lootconfig", new CommandInfo(this.ToggleConfig)
            {
                HelpMessage = "Show loot config.",
                ShowInHelp = true,
            });
        }

        private void RemoveCommands()
        {
            CommandManager.RemoveHandler("/loot");
            CommandManager.RemoveHandler("/roll");
            CommandManager.RemoveHandler("/lootconfig");
        }

        private void ToggleLootOverlay(string command, string args)
        {
            PluginLog.Info("Running command {0} with args {1}", command, args);
            this.Configuration.ShowLootOverlay = !this.Configuration.ShowLootOverlay;
            this.WindowManager.LootWindow?.Toggle();
            this.SaveConfig();
        }

        private void ToggleRollOverlay(string command, string args)
        {
            PluginLog.Info("Running command {0} with args {1}", command, args);
            this.Configuration.ShowRollMonitorOverlay = !this.Configuration.ShowRollMonitorOverlay;
            this.WindowManager.RollWindow?.Toggle();
            this.SaveConfig();
        }

        private void ToggleConfig(string command, string args)
        {
            PluginLog.Info("Running command {0} with args {1}", command, args);
            this.WindowManager.SettingsWindow?.Toggle();
        }

        private void FixConfig()
        {
            if (this.Configuration.RollMonitorProcessFrequency == 0)
            {
                this.Configuration.RollMonitorProcessFrequency = 3000;
                this.SaveConfig();
            }

            if (this.Configuration.WriteToLogFrequency == 0)
            {
                this.Configuration.WriteToLogFrequency = 30000;
                this.SaveConfig();
            }

            if (this.Configuration.SendHTTPFrequency == 0)
            {
                this.Configuration.SendHTTPFrequency = 5000;
                this.SaveConfig();
            }

            if (this.Configuration.SendDiscordFrequency == 0)
            {
                this.Configuration.SendDiscordFrequency = 5000;
                this.SaveConfig();
            }
        }

        private void LoadServices()
        {
            // setup services
            this.RollMonitor = new RollMonitor(this);
            var langCode = this.ClientLanguage();
            this.LootProcessor = langCode switch
            {
                // japanese
                0 => new ENLootProcessor(this),

                // english
                1 => new ENLootProcessor(this),

                // german
                2 => new DELootProcessor(this),

                // french
                3 => new ENLootProcessor(this),

                // chinese
                4 => new ZHLootProcessor(this),
                _ => this.LootProcessor,
            };

            this.LootLogger = new LootLogger(this);
            this.LootHTTP = new LootHTTP(this);
            this.LootDiscord = new LootDiscord(this);
        }

        private void LoadUI()
        {
            this.WindowManager = new WindowManager(this);
        }

        private void HandleFreshInstall()
        {
            if (!this.Configuration.FreshInstall) return;
            this.PrintMessage(Loc.Localize("InstallThankYou", "Thank you for installing the Kapture Loot Tracker Plugin!"));
            Thread.Sleep(500);
            this.PrintMessage(Loc.Localize(
                             "Instructions",
                             "Use /loot and /roll for the overlays and /lootconfig for settings."));
            this.Configuration.FreshInstall = false;
            this.SaveConfig();
        }

        private void LoadConfig()
        {
            try
            {
                this.Configuration = PluginInterface.GetPluginConfig() as KaptureConfig ?? new KaptureConfig();
            }
            catch (Exception ex)
            {
                PluginLog.Error("Failed to load config so creating new one.", ex);
                this.Configuration = new KaptureConfig();
                this.SaveConfig();
            }
        }

        private void SetupListeners()
        {
            Chat.CheckMessageHandled += this.ChatMessageHandled;
            ClientState.TerritoryChanged += this.TerritoryChanged;
        }

        private void DisposeListeners()
        {
            Chat.CheckMessageHandled -= this.ChatMessageHandled;
            ClientState.TerritoryChanged -= this.TerritoryChanged;
        }

        private void TerritoryChanged(ushort territoryType)
        {
            try
            {
                foreach (var roll in this.LootRolls.Where(roll => !roll.IsWon))
                {
                    roll.IsWon = true;
                    roll.Winner = Loc.Localize("LeftZone", "Left zone");
                }

                this.IsRolling = false;
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void ChatMessageHandled(
            XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            // check if enabled
            if (!this.Configuration.Enabled) return;

            // log for debugging
            if (this.Configuration.DebugLoggingEnabled) PluginLog.Info("[ChatMessage]" + type + ":" + message);

            // combat check
            if (this.Configuration.RestrictInCombat && this.InCombat()) return;

            // lookup territory and content
            var xivChatType = (ushort)type;
            var territoryTypeId = this.GetTerritoryType();
            var contentId = this.GetContentId();

            // update content
            this.InContent = contentId != 0;

            // restrict by user settings
            if (this.Configuration.RestrictToContent && contentId == 0) return;
            if (this.Configuration.RestrictToHighEndDuty && !this.IsHighEndDuty()) return;
            if (this.Configuration.RestrictToCustomContent && !this.Configuration.PermittedContent.Contains(contentId)) return;

            // filter out bad messages
            if (!Enum.IsDefined(typeof(LootMessageType), xivChatType)) return;
            if (!message.Payloads.Any(payload => payload is ItemPayload)) return;
            var logKind = (LogKind)((uint)type & ~(~0 << 7));
            if (!Enum.IsDefined(typeof(LogKind), logKind)) return;

            // build initial loot message
            var lootMessage = new LootMessage
            {
                XivChatType = xivChatType,
                LogKind = logKind,
                LootMessageType = (LootMessageType)xivChatType,
                Message = message.TextValue,
            };

            // add name fields for logging/display
            lootMessage.LogKindName = Enum.GetName(typeof(LogKind), lootMessage.LogKind) ?? string.Empty;
            lootMessage.LootMessageTypeName = Enum.GetName(typeof(LootMessageType), lootMessage.LootMessageType) ?? string.Empty;

            // add item and message part payloads
            foreach (var payload in message.Payloads)
            {
                switch (payload)
                {
                    case TextPayload textPayload:
                        if (textPayload.Text != null)
                        {
                            lootMessage.MessageParts.Add(textPayload.Text);
                        }

                        break;
                    case ItemPayload itemPayload:
                        if (lootMessage.ItemId != 0) break;
                        lootMessage.ItemId = itemPayload.Item.RowId;

                        if (itemPayload.Item.TryGetValue(out EventItem resolvedEventItem))
                        {
                            break;
                        }
                        else if (itemPayload.Item.TryGetValue(out Lumina.Excel.Sheets.Item resolvedItem))
                        {
                            lootMessage.Item = resolvedItem;
                        }

                        lootMessage.ItemName = lootMessage.Item.Name.ToDalamudString().TextValue;
                        lootMessage.IsHq = itemPayload.IsHQ;
                        break;
                    case PlayerPayload playerPayload:
                        if (lootMessage.Player is not null) break;
                        lootMessage.Player = playerPayload;
                        break;
                }
            }

            // filter out non-permitted item ids
            if (this.Configuration.RestrictToCustomItems && !this.Configuration.PermittedItems.Contains(lootMessage.ItemId)) return;

            // log for debugging
            if (this.Configuration.DebugLoggingEnabled) PluginLog.Info("[LootChatMessage]" + lootMessage);

            // send to loot processor
            var lootEvent = this.LootProcessor.ProcessLoot(lootMessage);

            // kick out if didn't process
            if (lootEvent == null) return;

            // log for debugging
            if (this.Configuration.DebugLoggingEnabled) PluginLog.Info("[LootEvent]" + lootEvent);

            // enrich
            lootEvent.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lootEvent.LootEventId = Guid.NewGuid();
            lootEvent.TerritoryTypeId = territoryTypeId;
            lootEvent.ContentId = contentId;

            // add to list
            if (this.LootProcessor.IsEnabledEvent(lootEvent)) this.LootEvents.Add(lootEvent);

            // process for roll monitor
            this.RollMonitor.LootEvents.Enqueue(lootEvent);

            // output
            if (this.LootProcessor.IsEnabledEvent(lootEvent))
            {
                if (this.Configuration.LoggingEnabled) this.LootLogger.LogLoot(lootEvent);
                if (this.Configuration.SendHTTPEnabled) this.LootHTTP.SendToHTTPQueue(lootEvent);
                if (this.Configuration.SendDiscordEnabled) this.LootDiscord.SendToDiscordQueue(lootEvent);
            }
        }

        private bool IsHighEndDuty()
        {
            return ContentExtension.InHighEndDuty(DataManager, ClientState.TerritoryType);
        }

        private uint GetContentId()
        {
            return ContentExtension.ContentId(DataManager, ClientState.TerritoryType);
        }

        private uint GetTerritoryType()
        {
            return ClientState.TerritoryType;
        }

        private void InitContent()
        {
            try
            {
                var excludedContent = new List<uint> { 69, 70, 71 };
                var contentTypes = new List<uint> { 2, 4, 5, 6, 26, 28, 29 };
                var contentList = DataManager.GetExcelSheet<ContentFinderCondition>() !
                                             .Where(content =>
                                                        contentTypes.Contains(content.ContentType.RowId) && !excludedContent.Contains(content.RowId))
                                             .ToList();
                var contentNames = PluginInterface.Sanitizer.Sanitize(contentList.Select(content => content.Name.ToDalamudString().TextValue.Replace("\u0002\u001F\u0001\u0003", "-"))).ToArray();
                var contentIds = contentList.Select(content => content.RowId).ToArray();
                Array.Sort(contentNames, contentIds);
                this.ContentIds = contentIds;
                this.ContentNames = contentNames;
            }
            catch
            {
                PluginLog.Verbose("Failed to initialize content list.");
            }
        }

        private void InitItems()
        {
            try
            {
                // create item list
                var itemDataList = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>() !.Where(item => !string.IsNullOrEmpty(item.Name.ExtractText())).ToList();

                // add all items
                var itemIds = itemDataList.Select(item => item.RowId).ToArray();
                var itemNames = PluginInterface.Sanitizer.Sanitize(itemDataList.Select(item => item.Name.ToDalamudString().TextValue.Replace("\u0002\u001F\u0001\u0003", "-"))).ToArray();
                this.ItemIds = itemIds;
                this.ItemNames = itemNames;

                // item categories
                var categoryList = DataManager.GetExcelSheet<ItemUICategory>() !
                                              .Where(category => category.RowId != 0).ToList();
                var categoryNames = PluginInterface.Sanitizer.Sanitize(categoryList.Select(category => category.Name.ToDalamudString().TextValue.Replace("\u0002\u001F\u0001\u0003", "-"))).ToArray();
                var categoryIds = categoryList.Select(category => category.RowId).ToArray();
                Array.Sort(categoryNames, categoryIds);
                this.ItemCategoryIds = categoryIds;
                this.ItemCategoryNames = categoryNames;

                // populate item lists by category
                var itemLists = new List<KeyValuePair<uint, ItemList>>();
                foreach (var categoryId in categoryIds)
                {
                    var itemCategoryDataList =
                        itemDataList.Where(item => item.ItemUICategory.RowId == categoryId).ToList();
                    var itemCategoryIds = itemCategoryDataList.Select(item => item.RowId).ToArray();
                    var itemCategoryNames =
                        PluginInterface.Sanitizer.Sanitize(itemCategoryDataList.Select(item => item.Name.ToDalamudString().TextValue.Replace("\u0002\u001F\u0001\u0003", "-"))).ToArray();
                    Array.Sort(itemCategoryNames, itemCategoryIds);
                    var itemList = new ItemList
                    {
                        ItemIds = itemCategoryIds,
                        ItemNames = itemCategoryNames,
                    };
                    itemLists.Add(new KeyValuePair<uint, ItemList>(categoryId, itemList));
                }

                this.ItemLists = itemLists;
            }
            catch
            {
                PluginLog.Verbose("Failed to initialize content list.");
            }
        }
    }
}
