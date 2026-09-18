<#
Reads the Windows audio-session peak meter for a process on the CURRENT default render endpoint.

This is deliberately outside Unity: every in-engine check (isPlaying, mixer gains, AudioListener.GetOutputData)
samples the engine's own graph and stays true even when the engine opened a device nobody is listening on. A
non-zero peak here means Windows itself sees audio arriving from the process on the endpoint it is currently
playing out of. It still does not prove a human heard it.
#>
param([string]$ProcessName = "RUINRAIL", [int]$Seconds = 20, [string]$OutFile = "")

$source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class AudioSessionProbe
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumerator { }

    internal enum EDataFlow { eRender, eCapture, eAll }
    internal enum ERole { eConsole, eMultimedia, eCommunications }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        int OpenPropertyStore(int access, out IPropertyStore properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid FormatId; public int PropertyId; }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        int GetCount(out int count);
        int GetAt(int index, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        int NotUsed1();
        int NotUsed2();
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        int RegisterSessionNotification(IntPtr notification);
        int UnregisterSessionNotification(IntPtr notification);
        int RegisterDuckNotification(string sessionId, IntPtr notification);
        int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid over, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr newNotifications);
        int UnregisterAudioSessionNotification(IntPtr newNotifications);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid over, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr newNotifications);
        int UnregisterAudioSessionNotification(IntPtr newNotifications);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetProcessId(out uint pid);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid eventContext);
        int GetMasterVolume(out float level);
        int SetMute(bool mute, ref Guid eventContext);
        int GetMute(out bool mute);
    }

    public sealed class Reading
    {
        public uint ProcessId;
        public string Display = "";
        public int State;
        public float Peak;
        public float SessionVolume;
        public bool SessionMuted;
    }

    public static string DefaultRenderDeviceName()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice device;
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) != 0) return "(no default render endpoint)";
        IPropertyStore store;
        if (device.OpenPropertyStore(0, out store) != 0) return "(endpoint, name unavailable)";
        var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 }; // PKEY_Device_FriendlyName
        PropVariant value;
        if (store.GetValue(ref key, out value) != 0 || value.pointerValue == IntPtr.Zero) return "(endpoint, name unavailable)";
        return Marshal.PtrToStringUni(value.pointerValue);
    }

    /// <summary>Every audio session on the current default render endpoint that belongs to the given process ids.</summary>
    public static List<Reading> Read(HashSet<uint> processIds)
    {
        var results = new List<Reading>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice device;
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) != 0) return results;
        var iid = typeof(IAudioSessionManager2).GUID;
        object raw;
        if (device.Activate(ref iid, 1 /* CLSCTX_ALL */, IntPtr.Zero, out raw) != 0) return results;
        var manager = (IAudioSessionManager2)raw;
        IAudioSessionEnumerator sessions;
        if (manager.GetSessionEnumerator(out sessions) != 0) return results;
        int count;
        sessions.GetCount(out count);
        for (var i = 0; i < count; i++)
        {
            IAudioSessionControl control;
            if (sessions.GetSession(i, out control) != 0 || control == null) continue;
            var control2 = control as IAudioSessionControl2;
            if (control2 == null) continue;
            uint pid;
            if (control2.GetProcessId(out pid) != 0) continue;
            if (processIds.Count > 0 && !processIds.Contains(pid)) continue;
            var reading = new Reading { ProcessId = pid };
            int state; control2.GetState(out state); reading.State = state;
            string name; if (control2.GetDisplayName(out name) == 0 && name != null) reading.Display = name;
            var meter = control as IAudioMeterInformation;
            if (meter != null) { float peak; if (meter.GetPeakValue(out peak) == 0) reading.Peak = peak; }
            var volume = control as ISimpleAudioVolume;
            if (volume != null)
            {
                float level; if (volume.GetMasterVolume(out level) == 0) reading.SessionVolume = level;
                bool muted; if (volume.GetMute(out muted) == 0) reading.SessionMuted = muted;
            }

            results.Add(reading);
        }

        return results;
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp | Out-Null

$lines = New-Object System.Collections.Generic.List[string]
function Emit([string]$text) { $lines.Add($text); Write-Output $text }

Emit ("# Windows audio-session probe  " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Emit ("# default render endpoint: " + [AudioSessionProbe]::DefaultRenderDeviceName())
Emit "# a non-zero peak means Windows received audio from the process on the endpoint it is currently playing out of"

$deadline = (Get-Date).AddSeconds($Seconds)
$best = 0.0
$samples = 0
$withSignal = 0
$seenSession = $false
$sessionInfo = ""
while ((Get-Date) -lt $deadline) {
    $procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        $ids = New-Object 'System.Collections.Generic.HashSet[uint32]'
        foreach ($p in $procs) { [void]$ids.Add([uint32]$p.Id) }
        $readings = [AudioSessionProbe]::Read($ids)
        foreach ($r in $readings) {
            $seenSession = $true
            $samples++
            if ($r.Peak -gt $best) { $best = $r.Peak }
            if ($r.Peak -gt 0.0005) { $withSignal++ }
            $sessionInfo = ("pid={0} state={1} sessionVolume={2:N2} muted={3}" -f $r.ProcessId, $r.State, $r.SessionVolume, $r.SessionMuted)
        }
    }
    Start-Sleep -Milliseconds 100
}

if (-not $seenSession) {
    Emit "RESULT: NO AUDIO SESSION - the process opened no render session on the current default endpoint while it ran."
} else {
    Emit ("# session: " + $sessionInfo)
    Emit ("# samples: {0}, samples carrying signal: {1}, peak: {2:N4}" -f $samples, $withSignal, $best)
    if ($withSignal -gt 0) {
        Emit ("RESULT: AUDIO REACHED THE WINDOWS MIXER - peak {0:N4} on the default render endpoint." -f $best)
    } else {
        Emit "RESULT: SESSION PRESENT BUT SILENT - the process held a render session that never carried a sample above the noise floor."
    }
}

if ($OutFile -ne "") {
    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $lines -join "`r`n" | Out-File -FilePath $OutFile -Encoding utf8
}
