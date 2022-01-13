using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TorFlow;

namespace Tests1 {
    public class TorFlow {
        public TorFlow(string[] aArgs) {
            // NOTE: You don't need to kill tor,
            // is usually independent tor instance.
            // Feel free to remove this line:
            Process.Start("tskill", "tor /a").WaitForExit();
            SampleRun1();
        }
        void SampleRun1() {
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
            vTorProcess.OnHiddenServiceCreated += VTorProcess_OnHiddenServiceCreated;
            // Set data directory where the tor is supposed to write files
            vTorProcess.Torrc.DataDirectory = "etc/TorData";
            // Run the Thread
            vTorProcess.Run();
        }

        private void VTorProcess_OnHiddenServiceCreated(TorProcess aTorProc, TorProcess.HiddenService aValue) {
            Console.WriteLine(aValue.Hostname.Trim() + ":" + aValue.ToString(1));
        }

        private void TorProcess1_OnState(TorProcess aTorProc, int aValue) {
            Console.Title = TorProcess.GetStateAsText(aValue);
        }

        private void TorProcess1_OnReady(TorProcess aTorProc) {
            //Console.Clear();
            Console.Title = "ONIONs served!!!";
            // KILL THE TOR ASAP
            aTorProc.Kill();
        }

        private void TorProcess1_OnLine(TorProcess aTorProc, string aValue) {
            Console.WriteLine(aValue);
        }
    }
    
}
