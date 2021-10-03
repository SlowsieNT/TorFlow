using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TorFlow;

namespace Tests1 {
    public class TorFlow {
        private string[] m_Args;
        public TorFlow(string[] aArgs) {
            // You don't need this line below
            Process.Start("tskill", "tor /a").WaitForExit();
            // Nor this line below
            m_Args = aArgs;
            // Very simple instancing
            TorProcess vTorProcess = new TorProcess {
                // (Both paths are full filenames)
                ExecutablePath = "etc/tor/tor.exe",
                TorrcFilePath = "etc/Config"
            };
            // Once you call AddHiddenService, it will return HiddenService
            // HiddenService.AddPort will return HiddenService
            // Meaning you can call AddPort over and over again
            vTorProcess.Torrc.AddHiddenService("etc/website01").AddPort(11, 81).AddPort(24, 181);
            vTorProcess.Torrc.AddHiddenService("etc/website02").AddPort(80, 8080);
            // Handle events
            vTorProcess.OnLine += TorProcess1_OnLine;
            vTorProcess.OnReady += TorProcess1_OnReady;
            vTorProcess.OnState += TorProcess1_OnState;
            // Set data directory where the tor is supposed to write files
            vTorProcess.Torrc.DataDirectory = "etc/TorData";
            // Run the Thread
            vTorProcess.Run();
        }

        private void TorProcess1_OnState(TorProcess aTorProc, int aValue) {
            if (0 == aValue) Console.WriteLine(aTorProc.StrErrLog);
            Console.Title = TorProcess.GetStateAsText(aValue);
        }

        private void TorProcess1_OnReady(TorProcess aTorProc) {
            Console.Clear();
            Console.Title = "ONIONs served!!!";
            Console.WriteLine("\r\n[Hidden Websites]");
            foreach (var vHS in aTorProc.Torrc.TorHiddenServices) {
                if (vHS.Hostname.Length > 0)
                    Console.WriteLine(vHS.Hostname + ":" + vHS.ToString(1));
            }
            // KILL THE TOR ASAP
            aTorProc.Kill();
        }

        private void TorProcess1_OnLine(TorProcess aTorProc, string aValue) {
            Console.WriteLine(aValue);
        }
    }
    
}
