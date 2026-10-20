using System;
using OniSim.CycleTrim;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace CycleTrim
{
    public sealed partial class ModInfo
    {
        partial void ConfigureOniSim()
        {
            bool enableOniSim = false;
            try
            {
                PUtil.InitLibrary();
                OniSimOptions options = POptions.ReadSettings<OniSimOptions>();
                if (options != null)
                    enableOniSim = options.EnableOniSim;
            }
            catch (Exception error)
            {
                Console.WriteLine("[CycleTrim][OniSim] PLib options could not be loaded; leaving the experimental core disabled: " + error.Message);
            }
            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, enableOniSim);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);
        }

        partial void RegisterOniSimOptions()
        {
            try
            {
                new POptions().RegisterOptions(this, typeof(OniSimOptions));
            }
            catch (Exception error)
            {
                Console.WriteLine("[CycleTrim][OniSim] PLib options dialog registration failed: " + error.Message);
            }
        }
    }
}
