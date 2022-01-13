using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TorFlow {

    /// <summary>Most events are background threads, remember: use try-catch.</summary>
    public class TorProcess {
        static string[] m_psStates = "Error,Starting,Running,Ready,Exited,Restarting".Split(',');
        public static string GetStateAsText(int aState) {
            return m_psStates[aState];
        }
        /// <summary>Return state, use int/null to avoid errors.
        /// <br>null returns current state as text.</br></summary>
        public string this[object aState] {
            get {
                if (null == aState)
                    return m_psStates[State];
                // Disable out of bounds
                if (m_psStates.Length > (ushort)aState)
                    return m_psStates[(int)aState];
                return this[null];
            }
        }
        public delegate void TorDelegateDef(TorProcess aTorProc);
        public delegate void TorDelegateInt(TorProcess aTorProc, int aValue);
        public delegate void TorDelegateString(TorProcess aTorProc, string aValue);
        public delegate void TorDelegateEx(TorProcess aTorProc, object aValue, int aDataType);
        public delegate void TorDelegateHS(TorProcess aTorProc, HiddenService aValue);
        public delegate void TorDelegateIntArray(TorProcess aTorProc, int[] aValue);
        /// <summary>Called when tor finishes bootstrapping.</summary>
        public event TorDelegateDef OnReady;
        public event TorDelegateDef OnClose;
        public event TorDelegateDef OnRestarting;
        public event TorDelegateInt OnState;
        public event TorDelegateHS OnHiddenServiceCreated;
        /// <summary>Silence is golden.</summary>
        public event TorDelegateEx OnError;
        /// <summary>This event is not background thread.
        /// <br>Useful when more ports are needed.</br></summary>
        public event TorDelegateIntArray OnTorrcPreparedPorts;
        /// <summary>Called every ReadLine of tor output</summary>
        public event TorDelegateString OnLine;
        /// <summary>On Bootstrapping</summary>
        public event TorDelegateInt OnRapping;
        /// <summary>Process ID</summary>
        public int Id;
        /// <summary>Used if tor is not intended to be terminated at all</summary>
        public bool Persist = true;
        /// <summary>Doesn't work for hidden service class</summary>
        public bool MakeDirs = true;
        /// <summary>Full File path to tor executable</summary>
        public string ExecutablePath = "dir/tor.exe";
        /// <summary>Full File path to torrc</summary>
        public string TorrcFilePath = "dir/torrc";
        /// <summary>States:
        /// 0 Error, 1 BeforeStart, 2 Running,
        /// 3 Ready, 4 Exited, 5 Restarting
        /// </summary>
        public int State;
        /// <summary>Time to wait [ms] before reinitiating tor process.</summary>
        public int RestartDelay = 1025;
        public TorConfig Torrc;
        public Process m_Process;
        public TorProcess() {
            Torrc = new TorConfig(this);
        }
        /// <summary>Run the background Thread.</summary>
        public void Run() {
            ThreadPool.QueueUserWorkItem(UserWorkItemRun);
        }
        /// <summary>Terminate Process, will reinitiate if abPersist is false.</summary>
        public void Kill(bool abPersist=false) {
            Persist = abPersist;
            m_Process.Kill();
        }
        void FakeAsync(WaitCallback aAction) {
            ThreadPool.QueueUserWorkItem(aAction);
        }
        void InvokeOnHiddenServiceCreated(HiddenService aHiddenService) {
            FakeAsync(delegate (object aState) {
                OnHiddenServiceCreated?.Invoke(this, aHiddenService);
            });
        }
        void InvokeOnError(object aValue, int aDataType) {
            FakeAsync(delegate (object aState) {
                OnError?.Invoke(this, aValue, aDataType);
            });
        }
        void InvokeOnState(int aValue) {
            FakeAsync(delegate (object aState) {
                OnState?.Invoke(this, aValue);
            });
        }
        void InvokeOnRapping(int aValue) {
            FakeAsync(delegate (object aState) {
                OnRapping?.Invoke(this, aValue);
            });
        }
        void InvokeOnReady() {
            FakeAsync(delegate (object aState) {
                OnReady?.Invoke(this);
            });
        }
        void InvokeOnLine(string aValue) {
            FakeAsync(delegate (object aState) {
                OnLine?.Invoke(this, aValue);
            });
        }
        private void InvokeOnClose() {
            FakeAsync(delegate (object aState) {
                OnClose?.Invoke(this);
            });
        }
        private void InvokeOnRestarting() {
            FakeAsync(delegate (object aState) {
                OnRestarting?.Invoke(this);
            });
        }
        void UserWorkItemRun(object aState) {
            // Make sure all possible dirs are made
            if (MakeDirs) {
                IOUtils.MakeDirAll(IOUtils.ToDirs(TorrcFilePath));
                IOUtils.MakeDirAll(Torrc.DataDirectory);
            }
            // Attempt to write torrc file
            IOUtils.WriteAllText(TorrcFilePath, "" + Torrc);
            // Check if all paths exist
            if (!File.Exists(TorrcFilePath))
                InvokeOnError(TorrcFilePath + "\r\n", 0);
            if (!File.Exists(ExecutablePath))
                InvokeOnError(ExecutablePath + "\r\n", 0);
            if (!Directory.Exists(Torrc.DataDirectory))
                InvokeOnError(Torrc.DataDirectory + "\r\n", 0);
            m_Process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = ExecutablePath,
                    Arguments = "-f " + '"' + TorrcFilePath + '"',
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            InvokeOnState(State = 1);
            try {
                m_Process.Start();
                Id = m_Process.Id;
            } catch (Exception vEx) {
                InvokeOnState(State = 0);
                InvokeOnError(vEx, 1);
                return;
            }
            // Report that TOR is alive and running
            InvokeOnState(State = 2);
            // While running, and console output incoming...
            while (!m_Process.StandardOutput.EndOfStream) {
                // Read console output line by line
                string vLine = m_Process.StandardOutput.ReadLine();
                // Report each line to OnLine event
                InvokeOnLine(vLine);
                // TOR enjoys rap
                if (vLine.Contains("rapped ")) {
                    // Get % of how much it rapped
                    string vStrInt = Regex.Split(vLine, "rapped ")[1].Split('%')[0];
                    // Parse % and pass to event OnRapping
                    InvokeOnRapping(int.Parse(vStrInt));
                    // If TOR rapped enough, report READY
                    if ("100" == vStrInt) {
                        InvokeOnState(State = 3);
                        InvokeOnReady();
                    }
                }
            }
            // Report its refusal to rap
            InvokeOnState(State = 4);
            InvokeOnClose();
            // Run tor again if escalated
            if (Persist) {
                Thread.Sleep(RestartDelay);
                InvokeOnState(State = 5);
                InvokeOnRestarting();
                UserWorkItemRun(aState);
            }
        }

        public class TorConfig {
            TorProcess m_TProc;
            public TorConfig(TorProcess aTorProc) {
                m_TProc = aTorProc;
                ThreadPool.QueueUserWorkItem(UserWorkItemListenHostnames);
            }
            public int PreparePortCount = 0;
            public int[] PreparedPorts;
            void UserWorkItemListenHostnames(object state) {
                while (true) {
                    for (int vI = 0; vI < TorHiddenServices.Count; vI++) {
                        var vHS = TorHiddenServices[vI];
                        if (vHS != null && default == vHS.Hostname) {
                            var vFileName = vHS.Directory + "/hostname";
                            // Attempt to read hostname.
                            vHS.Hostname = IOUtils.ReadAllText(vFileName);
                            if (default != vHS.Hostname)
                                m_TProc.InvokeOnHiddenServiceCreated(vHS);
                        }
                    }
                    Thread.Sleep(1000/50); // 50 Op/s
                }
            }
            public void PreparePorts() {
                List<int> vPorts = new List<int>(new int[] { TcpUtils.GetOpenPort() });
                for (int vI = 0; vI < PreparePortCount; vI++)
                    vPorts.Add(TcpUtils.GetOpenPort(1 + vPorts[vI]));
                PreparedPorts = vPorts.ToArray();
            }
            public override string ToString() {
                if (null == PreparedPorts) {
                    PreparePorts();
                    m_TProc.OnTorrcPreparedPorts?.Invoke(m_TProc, PreparedPorts);
                }
                if (0 == PreparedPorts.Length) {
                    PreparePorts();
                    m_TProc.OnTorrcPreparedPorts?.Invoke(m_TProc, PreparedPorts);
                }
                string vOutput = "";
                if ("auto" == SocksPort)
                    SocksPort = "" + PreparedPorts[0];
                if (AvoidDiskWrites) vOutput += "AvoidDiskWrites 1" + "\r\n";
                // GeoIP checks before alloc
                if (File.Exists(GeoIPFile)) vOutput += "GeoIPFile " + GeoIPFile + "\r\n";
                if (File.Exists(GeoIPv6File)) vOutput += "GeoIPv6File " + GeoIPv6File + "\r\n";
                // DataDirectory before alloc
                if (m_TProc.MakeDirs)
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
            /// <summary>If not found, will attempt to create automatically.</summary>
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
            /// <summary>
            /// Used if hosting onion service, no port forwarding required. 
            /// </summary>
            /// <param name="aDirectory">If not found, will attempt to create automatically.</param>
            /// <returns>HiddenService always.</returns>
            public HiddenService AddHiddenService(string aDirectory) {
                HiddenService vHS = new HiddenService(aDirectory);
                TorHiddenServices.Add(vHS);
                return vHS;
            }
        }
        /// <summary>No port forwarding required.</summary>
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
                IOUtils.MakeDirAll(aDir);
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
                if (!Directory.Exists(aDirname))
                    Directory.CreateDirectory(aDirname);
                return true;
            } catch { return false; }
        }
        public static bool DeleteFile(string aPath) {
            try {
                if (File.Exists(aPath))
                    File.Delete(aPath);
                return true;
            } catch { return false; }
        }
        public static bool DeleteDir(string aPath) {
            try {
                if (Directory.Exists(aPath))
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
                if (!File.Exists(aFilename)) return default;
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
