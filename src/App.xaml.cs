using System.Windows;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        App()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Translator.Setting?.Save();

            var genaiConfig = Translator.Setting?["GenAI"] as GenAIConfig;
            if (genaiConfig != null)
            {
                CookieBridge.Start(genaiConfig.UseCookieBridge ? genaiConfig.BridgePort : null);
                CookieBridge.CookiesUpdated += header =>
                {
                    if (genaiConfig.UseCookieBridge)
                        genaiConfig.CookieHeader = header;
                };
            }

            Task.Run(() => Translator.SyncLoop());
            Task.Run(() => Translator.TranslateLoop());
            Task.Run(() => Translator.DisplayLoop());
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            if (Translator.Window != null)
            {
                LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);
                LiveCaptionsHandler.KillLiveCaptions(Translator.Window);
            }

            CookieBridge.Stop();
        }
    }
}
