using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Deafen
{
    public static class AudioSessionController
    {
        private static readonly List<SessionNotificationRegistration> SessionNotificationRegistrations =
            new List<SessionNotificationRegistration>();

        public static event Action SessionCreated;

        public static void StartSessionNotifications()
        {
            StopSessionNotifications();

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
                    SessionCreatedNotification notification = null;
                    bool registered = false;
                    try
                    {
                        devices.GetItem(deviceIndex, out device);
                        Guid managerId = typeof(IAudioSessionManager2).GUID;
                        device.Activate(ref managerId, 23, IntPtr.Zero, out managerObject);
                        IAudioSessionManager2 manager = (IAudioSessionManager2)managerObject;
                        notification = new SessionCreatedNotification();
                        manager.RegisterSessionNotification(notification);

                        SessionNotificationRegistrations.Add(new SessionNotificationRegistration
                        {
                            Manager = manager,
                            Notification = notification
                        });
                        registered = true;
                        managerObject = null;
                    }
                    finally
                    {
                        if (!registered)
                            Release(managerObject);
                        Release(device);
                    }
                }

                if (SessionNotificationRegistrations.Count == 0)
                    throw new InvalidOperationException("No active audio output device accepted session notifications.");
            }
            catch
            {
                StopSessionNotifications();
                throw;
            }
            finally
            {
                Release(devices);
                Release(deviceEnumerator);
            }
        }

        public static void StopSessionNotifications()
        {
            Exception firstException = null;
            foreach (SessionNotificationRegistration registration in SessionNotificationRegistrations)
            {
                try
                {
                    registration.Manager.UnregisterSessionNotification(registration.Notification);
                }
                catch (Exception exception)
                {
                    if (firstException == null)
                        firstException = exception;
                }
                finally
                {
                    Release(registration.Manager);
                }
            }

            SessionNotificationRegistrations.Clear();
            if (firstException != null)
                throw firstException;
        }

        private static void NotifySessionCreated()
        {
            Action handler = SessionCreated;
            if (handler != null)
                handler();
        }

        public static void Apply(HashSet<int> mutedProcessIds, HashSet<int> trackedProcessIds)
        {
            if (mutedProcessIds == null)
                throw new ArgumentNullException("mutedProcessIds");
            if (trackedProcessIds == null)
                throw new ArgumentNullException("trackedProcessIds");

            List<AudioSession> sessions = GetSessions();
            Guid eventContext = Guid.Empty;

            try
            {
                foreach (AudioSession session in sessions)
                {
                    int processId = unchecked((int)session.ProcessId);
                    if (trackedProcessIds.Contains(processId))
                    {
                        bool currentMute;
                        session.Volume.GetMute(out currentMute);
                        bool shouldMute = mutedProcessIds.Contains(processId);
                        if (currentMute != shouldMute)
                            session.Volume.SetMute(shouldMute, ref eventContext);
                    }
                }
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

        private sealed class SessionNotificationRegistration
        {
            public IAudioSessionManager2 Manager;
            public SessionCreatedNotification Notification;
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        private sealed class SessionCreatedNotification : IAudioSessionNotification
        {
            public int OnSessionCreated(IAudioSessionControl newSession)
            {
                NotifySessionCreated();
                return 0;
            }
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
            void RegisterSessionNotification(IAudioSessionNotification notification);
            void UnregisterSessionNotification(IAudioSessionNotification notification);
        }

        [ComVisible(true)]
        [Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionNotification
        {
            [PreserveSig]
            int OnSessionCreated([MarshalAs(UnmanagedType.Interface)] IAudioSessionControl newSession);
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