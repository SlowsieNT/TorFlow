# TorFlow (Updated: 13-Jan-2022)

## License
Unlicense<br>

### Requirements
- Binary files of [TOR (Use official instead!)](https://github.com/SlowsieNT/TorFlow/releases/tag/resources)
- .NET 3.5+

__Example__:
```cs
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
// Handle events (optional, yes)
vTorProcess.OnLine += TorProcess1_OnLine;
vTorProcess.OnReady += TorProcess1_OnReady;
vTorProcess.OnState += TorProcess1_OnState;
// To get hostname of onion, best to use this:
vTorProcess.OnHiddenServiceCreated += TorProcess1_OnHiddenServiceCreated;
// Set data directory where the tor is supposed to write files
vTorProcess.Torrc.DataDirectory = "etc/TorData";
// Run the Thread
vTorProcess.Run();
```
