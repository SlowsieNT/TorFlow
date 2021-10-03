using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TorFlow {
    /// <summary>All events are sync, so call new Threads within events</summary>
    public class TorProcess {
        static string[] m_psStates = "Error,Starting,Running,Ready,Exited,Restarting".Split(',');
        public static string GetStateAsText(int aState) {
            return m_psStates[aState];
        }
        public delegate void TorDelegateDef(TorProcess aTorProc);
        public delegate void TorDelegateInt(TorProcess aTorProc, int aValue);
        public delegate void TorDelegateString(TorProcess aTorProc, string aValue);
        public event TorDelegateDef OnReady;
        public event TorDelegateDef OnClose;
        public event TorDelegateDef OnRestarting;
        public event TorDelegateInt OnState;
        public event TorDelegateString OnLine;
        public event TorDelegateInt OnRapping;
        public int Id;
        public bool Persist = true;
        public bool MakeDirs = true;
        public bool LogErrors = false; // debug
        public string StrErrLog = "";
        public string ExecutablePath = "dir/tor.exe";
        public string TorrcFilePath = "dir/torrc";
        /// <summary>States:
        /// 0 Error, 1 BeforeStart, 2 Running,
        /// 3 Ready, 4 Exited, 5 Restarting
        /// </summary>
        public int State;
        public int RestartDelay = 1025; // [ms]
        public TorConfig Torrc = new TorConfig();
        public Process m_Process;
        /// <summary>Will run as Thread.</summary>
        public void Run() {
            ThreadPool.QueueUserWorkItem(__Run);
        }
        public void Kill(bool abPersist=false) {
            Persist = abPersist;
            m_Process.Kill();
        }
        void __Run(object aState) {
            // Make sure all possible dirs are made
            IOUtils.MakeDirAll(IOUtils.ToDirs(TorrcFilePath));
            IOUtils.MakeDirAll(Torrc.DataDirectory);
            // Attempt to write torrc file
            IOUtils.WriteAllText(TorrcFilePath, "" + Torrc);
            // Check if all paths exist
            if (LogErrors && !File.Exists(TorrcFilePath))
                StrErrLog += "TorrcPath 404:" + TorrcFilePath + "\r\n";
            if (LogErrors && !File.Exists(ExecutablePath))
                StrErrLog += "ExecutablePath 404:" + ExecutablePath + "\r\n";
            if (LogErrors && !Directory.Exists(Torrc.DataDirectory))
                StrErrLog += "Torrc.DataDirectory 404:" + Torrc.DataDirectory + "\r\n";
            m_Process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = ExecutablePath,
                    Arguments = "-f " + '"' + TorrcFilePath + '"',
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            OnState?.Invoke(this, State = 1);
            try {
                m_Process.Start();
                Id = m_Process.Id;
            } catch (Exception vEx) {
                StrErrLog += "\r\n[m_Process.Start]:\r\n" + vEx + "\r\n";
                OnState?.Invoke(this, State = 0);
                return;
            }
            // Report that TOR is alive and running
            OnState?.Invoke(this, 2);
            // While running, and console output incoming...
            while (!m_Process.StandardOutput.EndOfStream) {
                // Read console output line by line
                string vLine = m_Process.StandardOutput.ReadLine();
                // Report each line to OnLine event
                OnLine?.Invoke(this, vLine);
                // TOR enjoys rap
                if (vLine.Contains("rapped ")) {
                    // Get % of how much it rapped
                    string vStrInt = Regex.Split(vLine, "rapped ")[1].Split('%')[0];
                    // Parse % and pass to event OnRapping
                    OnRapping?.Invoke(this, int.Parse(vStrInt));
                    // If TOR rapped enough, report READY
                    if ("100" == vStrInt) {
                        OnState?.Invoke(this, State = 3);
                        OnReady?.Invoke(this);
                    }
                }
            }
            // Report its refusal to rap
            OnState?.Invoke(this, State = 4);
            OnClose?.Invoke(this);
            // Run tor again if escalated
            if (Persist) {
                Thread.Sleep(RestartDelay);
                OnState?.Invoke(this, State = 5);
                OnRestarting?.Invoke(this);
                __Run(aState);
            }
        }
        public class TorConfig {
            public TorConfig() {
                ThreadPool.QueueUserWorkItem(__ListenHostnames);
            }
            void __ListenHostnames(object state) {
                while (true) {
                    try {
                        foreach (HiddenService vHS in TorHiddenServices)
                            vHS.Hostname = ("" + IOUtils.ReadAllText(vHS.Directory + "/Hostname")).Trim();
                    } catch { }
                    Thread.Sleep(480);
                }
            }
            public override string ToString() {
                string vOutput = "";
                if ("auto" == SocksPort)
                    SocksPort = "" + TcpUtils.GetOpenPort();
                if (AvoidDiskWrites) vOutput += "AvoidDiskWrites 1" + "\r\n";
                // GeoIP checks before alloc
                if (File.Exists(GeoIPFile)) vOutput += "GeoIPFile " + GeoIPFile + "\r\n";
                if (File.Exists(GeoIPv6File)) vOutput += "GeoIPv6File " + GeoIPv6File + "\r\n";
                // DataDirectory before alloc
                IOUtils.MakeDirAll(DataDirectory);
                if (Directory.Exists(DataDirectory))
                    vOutput += "DataDirectory " + DataDirectory + "\r\n";
                vOutput += "ControlPort " + ControlPort + " " + ControlPortFlags + "\r\n";
                vOutput += "SocksPort " + SocksPort + " " + SocksPortFlags + "\r\n";
                foreach (HiddenService vHS in TorHiddenServices) {
                    vOutput += "HiddenServiceDir " + vHS.Directory + "\r\n";
                    foreach (HiddenServicePort vPort in vHS.Ports)
                        vOutput += "HiddenServicePort " + vPort.OnionPort + " " + vPort.ServerPort + "\r\n";
                }
                return vOutput;
            }
            public bool AvoidDiskWrites = true; // default=0
            public string DataDirectory;
            public string ControlPort = "auto";
            public string ControlPortFlags = "";
            public string SocksPort = "auto";
            public string SocksPortFlags = "IPv6Traffic PreferIPv6";
            public string GeoIPFile, GeoIPv6File;
            public List<string> CustomLines = new List<string>();
            public List<HiddenService> TorHiddenServices = new List<HiddenService>();
            public bool RemoveCustomAt(int aIndex) {
                if (CustomLines.Count > aIndex) {
                    CustomLines.RemoveAt(aIndex);
                    return true;
                }
                return false;
            }
            public int AddCustom(string aValue, string aValue2) {
                return AddCustom(aValue + " " + aValue2);
            }
            public int AddCustom(string aValue) {
                CustomLines.Add(aValue);
                return CustomLines.Count - 1;
            }
            public HiddenService AddHiddenService(string aDirectory) {
                HiddenService vHS = new HiddenService(aDirectory);
                TorHiddenServices.Add(vHS);
                return vHS;
            }
        }
        /// <summary>No port forwarding required, worry not.</summary>
        public class HiddenServicePort {
            public int OnionPort = 80, ServerPort = 8080;
            public string ServerHost = "";
            public HiddenServicePort(int aOnionPort, int aServerPort, string aServerHost) { OnionPort = aOnionPort; ServerPort = aServerPort; ServerHost = aServerHost; }
            public HiddenServicePort(int aOnionPort, int aServerPort) { OnionPort = aOnionPort; ServerPort = aServerPort; }
            public HiddenServicePort(int aServerPort) { ServerPort = aServerPort; }
            public HiddenServicePort() { }
            public override string ToString() {
                string vStr = ServerHost;
                if (0 < vStr.Length)
                    vStr += ":";
                return OnionPort + " " + vStr + ServerPort;
            }
        }
        public class HiddenService {
            public string Directory;
            /// <summary>Hostname is readonly</summary>
            public string Hostname;
            public List<HiddenServicePort> Ports = new List<HiddenServicePort>();
            public HiddenService(string aDir) {
                Directory = aDir;
            }
            public HiddenService AddPort(int vOnionPort, int vServerPort) {
                Ports.Add(new HiddenServicePort(vOnionPort, vServerPort));
                return this;
            }
            /// <summary>ToString(1) will return onion ports separated by comma</summary>
            public string ToString(int aWhat) {
                if (1 == aWhat) {
                    string vOut = "";
                    for (int vI = 0; vI < Ports.Count; vI++)
                        vOut += Ports[vI].OnionPort + ", ";
                    // Remove trailing comma
                    if (vOut.Length > 3)
                        return vOut.Substring(0, vOut.Length - 2);
                }
                return ToString();
            }
            public override string ToString() {
                string vOutput = "HiddenServiceDir " + Directory + "\r\n";
                for (int vI = 0, vL = Ports.Count; vI < vL; vI++) {
                    vOutput += "HiddenServicePort " + Ports[vI];
                    if (vI + 1 < vL) vOutput += "\r\n";
                }
                return vOutput;
            }
        }
        
    }
    public class IOUtils {
        public static string ToDirs(string aString) {
            string vOutput = "", vTemp = "";
            for (int vI = 0, vL = aString.Length; vI < vL; vI++) {
                if ('\\' == aString[vI] || '/' == aString[vI]) {
                    vOutput += vTemp + "/";
                    vTemp = "";
                } else vTemp += aString[vI];
            }
            return vOutput;
        }
        public static bool MakeDirAll(string aDirname) {
            try {
                Directory.CreateDirectory(aDirname);
                return true;
            } catch { return false; }
        }
        public static bool DeleteFile(string aPath) {
            try {
                File.Delete(aPath);
                return true;
            } catch { return false; }
        }
        public static bool DeleteDir(string aPath) {
            try {
                Directory.Delete(aPath, true);
                return true;
            } catch { return false; }
        }
        public static bool WriteAllText(string aFilename, string aContent) {
            try {
                File.WriteAllText(aFilename, aContent);
                return true;
            } catch { return false; }
        }
        public static string ReadAllText(string aFilename) {
            try {
                return File.ReadAllText(aFilename);
            } catch { return default; }
        }
    }
    public class TcpUtils {
        public static int GetOpenPort(int aPStart = 9000, int aPEnd = 60000) {
            int vUUPort = 0;
            System.Net.NetworkInformation.IPGlobalProperties vProps = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            var vTEP = vProps.GetActiveTcpListeners();
            var vUPorts = (from vP in vTEP select vP.Port).ToList();
            for (int vPort = aPStart; vPort < aPEnd; vPort++)
                if (!vUPorts.Contains(vPort)) {
                    vUUPort = vPort;
                    break;
                }
            return vUUPort;
        }
    }
}
