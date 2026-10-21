# Factory Sound Studio v0.1 — AUDI e-tron GT (2024)

**Windows C#/.NET 8 source prototype. STRICTLY DIAGNOSTIC / READ-ONLY.**

This is NOT a volume unlock, an AVAS retrofit, a flash utility, an SFD bypass, or a firmware editor. The initial goal is to identify the DoIP entity and, when the correct diagnostic address is available and authorised, query an ECU's standard software/hardware identification numbers. Audi identifies J1167 as the base exterior-sound controller and J1177 as an optional additional sound controller. **The DoIP vehicle-announcement logical address is normally for the responding entity/gateway; it does not identify J1167.** No J1167 DoIP address is hard-coded or asserted.

## What is implemented

- Windows Forms dark-mode desktop dashboard, no third-party NuGet packages.
- Enumerate active local IPv4 network adapters, choose cable-connected ENET interface.
- ISO 13400-2 generic DoIP header validation (versions 0x02/0x03), length checks, UDP broadcast vehicle discovery (0x0001 / 0x0004), masked VIN and DoIP logical address.
- TCP DoIP port 13400 connection **bound to the selected local adapter**, default routing activation 0x0005/0x0006, alive check response 0x0008.
- UDS ReadDataByIdentifier 0x22 only, **allowlist**: F187, F188, F189, F18A, F190, F191. Positive and negative responses, timeout, and gateway ACK/NACK handling.
- User-chosen target logical address, because J1167's actual address is not established by shared J1 platform.
- Diagnostic session log held in RAM and optional user-triggered JSON export with VIN redacted.
- Local-only simulator and wire-codec self-checks.

## Setup on Windows 10/11

1. Install the **.NET 8 SDK** (not merely the runtime) from https://dotnet.microsoft.com/download/dotnet/8.0 if you do not have it.
2. Extract the ZIP. Double-click `build-windows.bat`. It builds and starts the program. Alternatively open `src/FactorySoundStudio/FactorySoundStudio.csproj` in Visual Studio 2022 with .NET desktop development support and run it.
3. Connect the **correctly wired** ENET / VAG-compatible DoIP cable to your car's OBD diagnostic port and your laptop's Ethernet interface. Different ENET products are not necessarily compatible with all vehicle diagnostic Ethernet pinouts. Do not assume compatibility solely from the connector shape.
4. Park securely. Have the car in a suitable diagnostic/ignition-on state **without driving**. Maintain sufficient battery state of charge, avoid operating near moving vehicle components, and keep the cable clear of pedals.
5. In the app select the laptop's **Ethernet** adapter corresponding to the ENET cable, not its Wi-Fi. A 169.254.x.x link-local IPv4 address is possible and not necessarily an error.
6. Click **Discover over ENET** (four-second UDP 13400 wait). Select the responding entity, or manually enter a known vehicle DoIP IPv4 address.
7. Use tester logical address `0E00` unless legitimate vehicle-specific diagnostic documentation calls for another valid tester address. Click **Connect (read-only)**. If gateway routing activation is refused due to TLS, authentication or manufacturer restrictions, STOP and report the message. This app never attempts to defeat such restrictions.
8. First read only the discovered DoIP entity/gateway address to verify the transport. **Do not claim it is J1167.** When a legitimate vehicle scan reveals the *correct DoIP logical address* of J1167, enter that address into **ECU target logical address** and read identification DIDs.
9. Share a redacted report. VIN is masked on screen and excluded from exported results; do not publicly share VIN, ECU serial numbers, authentication data or raw network traces.

## To test without a vehicle (optional)

1. Install Python 3.10+. Open Command Prompt in the extracted folder and run `py -3 tools\mock_doip_vehicle.py`.
2. In the application choose the loopback interface (127.0.0.1), manually enter host `127.0.0.1`, tester `0E00`, target `1001`. Click Connect, then Read identification DIDs.
3. **All responses from this simulator are fake**, including the entity address 0x1001. These do not identify an Audi module.
4. Run the unit-level checks from a terminal with `dotnet run --project tests/CodecSelfTest/CodecSelfTest.csproj`.

## Limitations / engineering notes

- Not tested on a real 2024 Audi e-tron GT. **The source has not been compiled in the generation environment (Windows/.NET SDK not installed there)**; compile and offline-test on Windows before any on-car use.
- DoIP can be routed through a vehicle gateway. ENET cable presence does not guarantee activation or ECU access. Audi security policies and diagnostics transport configurations may block requests.
- No DoIP multicast, TLS, OEM variants, secure gateway auth, VIN/EID-specific discovery, DoIP multiplexing, CAN ISO-TP, SFD workflows, ODX datasets, fault memory reads, or ECU address mapping included.
- DoIP routing activation and alive-check are network control exchanges, not diagnostic writes; a literal wire-level 'receive-only' program couldn't establish an active diagnostic session. The only UDS service transmitted is 0x22.
- The tool never sends UDS WriteDataByIdentifier (0x2E), RoutineControl (0x31), SecurityAccess (0x27), DiagnosticSessionControl (0x10), ECUReset (0x11), RequestDownload (0x34), TransferData (0x36), or commands that change gain/firmware.
- Read data requests may be rejected by an ECU in the default session. This is normal: do not try to bypass authentication or switch sessions automatically.
- Sending even identification requests is interaction with the vehicle network. Use only on a vehicle you own/are authorised to service while stationary. Don't continue if diagnostics show instability or unexpected warning messages.
- AVAS is regulated pedestrian-warning equipment. Changing calibration or output can affect legality and safety. No sound-gain adjustment has been implemented or proven safe.

## Reference material

- Audi Technical Service Bulletin / e-tron GT Self-Study, J1167/J1177: https://static.nhtsa.gov/odi/tsbs/2021/MC-10190946-0001.pdf
- DoIP packet structures and payload types: https://scapy.readthedocs.io/en/latest/api/scapy.contrib.automotive.doip.html
- DoIP routing activation overview: https://eclipse-opensovd.github.io/classic-diagnostic-adapter/03_architecture/03_communication/01_doip_communication.html
- ISO 14229 DID table: https://uds.readthedocs.io/en/latest/pages/knowledge_base/did.html

## Next iteration

Have the user run *offline simulator first*, then obtain a real DoIP discovery and read-only identification output. Use the controller part number / software ID to research firmware compatibility and whether OEM-supported parameters exist. Do not infer that Porsche firmware changes work on the Audi.
