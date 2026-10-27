using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using TargetHighlight.Services;
using TargetHighlight.Windows;

namespace TargetHighlight;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/targethighlight";
    private const string LegacyCommandName = "/notargetflash";
    private const string LegacyPluginName = "NoTargetFlash";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }
    public FlashRecorder Recorder { get; }
    public TargetFlashController Controller { get; }

    private readonly WindowSystem windowSystem = new("TargetHighlight");
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? ImportLegacyConfig() ?? new Configuration();
        Configuration.Migrate();
        Controller = new TargetFlashController(Configuration);
        Recorder = new FlashRecorder(PluginInterface.GetPluginConfigDirectory());

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open settings. \"/targethighlight none|default|custom\" switches mode; \"preview\" flashes your target.",
        });
        CommandManager.AddHandler(LegacyCommandName, new CommandInfo(OnCommand) { ShowInHelp = false });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
    }

    public bool HookActive => Controller.HookActive;

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(LegacyCommandName);

        windowSystem.RemoveAllWindows();
        Recorder.Dispose();
        Controller.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":
            case "none":
                SetMode(FlashMode.Disabled);
                break;
            case "off":
            case "default":
                SetMode(FlashMode.GameDefault);
                break;
            case "custom":
                SetMode(FlashMode.Custom);
                break;
            case "preview":
                Controller.RequestPreview();
                break;
            case "record":
                Recorder.Armed = !Recorder.Armed;
                break;
            default:
                ToggleConfigUi();
                break;
        }
    }

    /// <summary>Carries settings over from when this plugin was called NoTargetFlash.</summary>
    private static Configuration? ImportLegacyConfig()
    {
        var legacyFile = Path.Combine(PluginInterface.ConfigFile.DirectoryName!, $"{LegacyPluginName}.json");
        if (!File.Exists(legacyFile))
            return null;

        try
        {
            // Default settings ignore the old "$type" names, which point at the NoTargetFlash namespace.
            var config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(legacyFile));
            if (config == null)
                return null;

            config.Save();
            Log.Information($"Imported settings from {legacyFile}");
            return config;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not import settings from {legacyFile}");
            return null;
        }
    }

    private void SetMode(FlashMode mode)
    {
        Configuration.Mode = mode;
        Configuration.Save();
    }

    private void ToggleConfigUi() => configWindow.Toggle();
}
