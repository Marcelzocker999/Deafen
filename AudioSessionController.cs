using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Deafen
{
    public static class AudioSessionController
    {
        private static readonly Dictionary<string, bool> OriginalMuteStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static void Apply(HashSet<int> minimizedProcessIds)
        {
            if (minimizedProcessIds == null)
                throw new ArgumentNullException("minimizedProcessIds");

            List<AudioSession> sessions = GetSessions();
            HashSet<string> liveSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Guid eventContext = Guid.Empty;

            try
            {
                foreach (AudioSession session in sessions)
                {
                    liveSessionIds.Add(session.Id);
                    bool shouldMute = minimizedProcessIds.Contains(unchecked((int)session.ProcessId));
                    bool originalMute;

                    if (shouldMute)
                    {
                        if (!OriginalMuteStates.TryGetValue(session.Id, out originalMute))
                        {
                            session.Volume.GetMute(out originalMute);
                            OriginalMuteStates.Add(session.Id, originalMute);
                        }

                        session.Volume.SetMute(true, ref eventContext);
                    }
                    else if (OriginalMuteStates.TryGetValue(session.Id, out originalMute))
                    {
                        session.Volume.SetMute(originalMute, ref eventContext);
                        OriginalMuteStates.Remove(session.Id);
                    }
                }

                List<string> endedSessions = new List<string>();
                foreach (string sessionId in OriginalMuteStates.Keys)
                {
                    if (!liveSessionIds.Contains(sessionId))
                        endedSessions.Add(sessionId);
                }

                foreach (string sessionId in endedSessions)
                    OriginalMuteStates.Remove(sessionId);
            }
            finally
            {
                foreach (AudioSession session in sessions)
                    Release(session.Volume);
            }
        }

        private static List<AudioSession> GetSessions()
        {
            List<AudioSession> sessions = new List<AudioSession>();
            IMMDeviceEnumerator deviceEnumerator = null;
            IMMDeviceCollection devices = null;

            try
            {
                deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                devices = deviceEnumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active);
                uint deviceCount;
                devices.GetCount(out deviceCount);

                for (uint deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
                {
                    IMMDevice device = null;
                    object managerObject = null;
                    IAudioSessionEnumerator sessionEnumerator = null;

                    try
                    {
                        devices.GetItem(deviceIndex, out device);
                        string deviceId = device.GetId();
                        Guid managerId = typeof(IAudioSessionManager2).GUID;
                        device.Activate(ref managerId, 23, IntPtr.Zero, out managerObject);
                        IAudioSessionManager2 manager = (IAudioSessionManager2)managerObject;
                        sessionEnumerator = manager.GetSessionEnumerator();

                        int sessionCount;
                        sessionEnumerator.GetCount(out sessionCount);

                        for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                        {
                            IAudioSessionControl session = null;
                            IAudioSessionControl2 session2 = null;
                            ISimpleAudioVolume volume = null;
                            bool transferredVolume = false;

                            try
                            {
                                sessionEnumerator.GetSession(sessionIndex, out session);
                                session2 = (IAudioSessionControl2)session;

                                uint processId;
                                int result = session2.GetProcessId(out processId);
                                if (result < 0 || processId == 0)
                                    continue;

                                string instanceId;
                                session2.GetSessionInstanceIdentifier(out instanceId);
                                volume = (ISimpleAudioVolume)session;

                                sessions.Add(new AudioSession
                                {
                                    Id = deviceId + "|" + instanceId,
                                    ProcessId = processId,
                                    Volume = volume
                                });
                                transferredVolume = true;
                                volume = null;
                            }
                            finally
                            {
                                if (!transferredVolume)
                                    Release(session);
                            }
                        }
                    }
                    finally
                    {
                        Release(sessionEnumerator);
                        Release(managerObject);
                        Release(device);
                    }
                }

                return sessions;
            }
            catch
            {
                foreach (AudioSession session in sessions)
                    Release(session.Volume);
                throw;
            }
            finally
            {
                Release(devices);
                Release(deviceEnumerator);
            }
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.ReleaseComObject(value);
        }

        private sealed class AudioSession
        {
            public string Id;
            public uint ProcessId;
            public ISimpleAudioVolume Volume;
        }

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr windowHandle);

        public static int GetForegroundProcessId()
        {
            uint processId;
            GetWindowThreadProcessId(GetForegroundWindow(), out processId);
            return unchecked((int)processId);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
        public static extern bool DestroyIconHandle(IntPtr iconHandle);

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        [ClassInterface(ClassInterfaceType.None)]
        private class MMDeviceEnumerator
        {
        }

        private enum EDataFlow
        {
            Render = 0
        }

        [Flags]
        private enum DeviceState
        {
            Active = 1
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            IMMDeviceCollection EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            void GetCount(out uint count);
            void GetItem(uint index, out IMMDevice device);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            void Activate(ref Guid interfaceId, uint classContext, IntPtr activationParameters,
                [MarshalAs(UnmanagedType.Interface)] out object instance);
            void OpenPropertyStore(int storageAccess, out IntPtr propertyStore);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetId();
            int GetState();
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            void GetAudioSessionControl(ref Guid sessionGuid, uint streamFlags, out IntPtr sessionControl);
            void GetSimpleAudioVolume(ref Guid sessionGuid, uint streamFlags, out IntPtr audioVolume);
            [return: MarshalAs(UnmanagedType.Interface)]
            IAudioSessionEnumerator GetSessionEnumerator();
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            void GetCount(out int count);
            void GetSession(int index, [MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl session);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            void GetState(out int state);
            void GetDisplayName(out IntPtr displayName);
            void SetDisplayName(IntPtr displayName, ref Guid eventContext);
            void GetIconPath(out IntPtr iconPath);
            void SetIconPath(IntPtr iconPath, ref Guid eventContext);
            void GetGroupingParam(out Guid groupingParam);
            void SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            void RegisterAudioSessionNotification(IntPtr notification);
            void UnregisterAudioSessionNotification(IntPtr notification);
        }

        [ComImport]
        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            void GetState(out int state);
            void GetDisplayName(out IntPtr displayName);
            void SetDisplayName(IntPtr displayName, ref Guid eventContext);
            void GetIconPath(out IntPtr iconPath);
            void SetIconPath(IntPtr iconPath, ref Guid eventContext);
            void GetGroupingParam(out Guid groupingParam);
            void SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            void RegisterAudioSessionNotification(IntPtr notification);
            void UnregisterAudioSessionNotification(IntPtr notification);
            void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);
            void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string instanceId);
            [PreserveSig]
            int GetProcessId(out uint processId);
        }

        [ComImport]
        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            void SetMasterVolume(float level, ref Guid eventContext);
            void GetMasterVolume(out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
    }
}