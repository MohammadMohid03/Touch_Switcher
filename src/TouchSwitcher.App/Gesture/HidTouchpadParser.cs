using System.Runtime.InteropServices;
using TouchSwitcher.Interop;

namespace TouchSwitcher.Gesture;

internal sealed class HidTouchpadParser : IDisposable
{
    private readonly Dictionary<nint, DeviceCache> _devices = new();

    public TouchFrame? Parse(IntPtr hRawInput)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize) != size)
            {
                return null;
            }

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RIM_TYPEHID)
            {
                return null;
            }

            int hidOffset = Marshal.SizeOf<RAWINPUTHEADER>();
            uint dwSizeHid = (uint)Marshal.ReadInt32(buffer, hidOffset);
            uint dwCount = (uint)Marshal.ReadInt32(buffer, hidOffset + 4);
            int dataLength = (int)(dwSizeHid * dwCount);
            if (dataLength <= 0)
            {
                return null;
            }

            byte[] hidData = new byte[dataLength];
            Marshal.Copy(IntPtr.Add(buffer, (int)size - dataLength), hidData, 0, dataLength);

            DeviceCache? cache = GetOrCreateDevice(header.hDevice);
            if (cache is null)
            {
                return null;
            }

            return ParseReport(cache, hidData);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool PrecisionTouchpadExists()
    {
        uint count = 0;
        uint listSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (NativeMethods.GetRawInputDeviceList(null, ref count, listSize) != 0)
        {
            return false;
        }

        var devices = new RAWINPUTDEVICELIST[count];
        if (NativeMethods.GetRawInputDeviceList(devices, ref count, listSize) == unchecked((uint)-1))
        {
            return false;
        }

        foreach (RAWINPUTDEVICELIST device in devices)
        {
            if (device.dwType != NativeMethods.RIM_TYPEHID)
            {
                continue;
            }

            uint infoSize = 0;
            NativeMethods.GetRawInputDeviceInfo(device.hDevice, NativeMethods.RIDI_DEVICEINFO, IntPtr.Zero, ref infoSize);
            if (infoSize == 0)
            {
                continue;
            }

            IntPtr infoPtr = Marshal.AllocHGlobal((int)infoSize);
            try
            {
                Marshal.WriteInt32(infoPtr, (int)infoSize);
                if (NativeMethods.GetRawInputDeviceInfo(device.hDevice, NativeMethods.RIDI_DEVICEINFO, infoPtr, ref infoSize) == unchecked((uint)-1))
                {
                    continue;
                }

                ushort usagePage = (ushort)Marshal.ReadInt16(infoPtr, 20);
                ushort usage = (ushort)Marshal.ReadInt16(infoPtr, 22);
                if (usagePage == 0x000D && usage == 0x0005)
                {
                    return true;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }
        }

        return false;
    }

    private DeviceCache? GetOrCreateDevice(IntPtr hDevice)
    {
        if (_devices.TryGetValue(hDevice, out DeviceCache? existing))
        {
            return existing;
        }

        uint size = 0;
        NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return null;
        }

        IntPtr preparsed = Marshal.AllocHGlobal((int)size);
        if (NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_PREPARSEDDATA, preparsed, ref size) != size)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        if (NativeMethods.HidP_GetCaps(preparsed, out HIDP_CAPS caps) != NativeMethods.HIDP_STATUS_SUCCESS)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        ushort capCount = caps.NumberInputValueCaps;
        var valueCaps = new HIDP_VALUE_CAPS[capCount];
        if (NativeMethods.HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, valueCaps, ref capCount, preparsed)
            != NativeMethods.HIDP_STATUS_SUCCESS)
        {
            Marshal.FreeHGlobal(preparsed);
            return null;
        }

        var cache = new DeviceCache(preparsed, valueCaps.Take(capCount).ToArray());
        _devices[hDevice] = cache;
        return cache;
    }

    private static TouchFrame? ParseReport(DeviceCache cache, byte[] hidData)
    {
        IntPtr report = Marshal.AllocHGlobal(hidData.Length);
        try
        {
            Marshal.Copy(hidData, 0, report, hidData.Length);

            uint contactCount = 0;
            uint scanTime = 0;
            var slots = new Dictionary<ushort, PartialContact>();

            foreach (HIDP_VALUE_CAPS cap in cache.ValueCaps)
            {
                if (NativeMethods.HidP_GetUsageValue(
                        HIDP_REPORT_TYPE.HidP_Input,
                        cap.UsagePage,
                        cap.LinkCollection,
                        cap.Usage,
                        out uint value,
                        cache.Preparsed,
                        report,
                        (uint)hidData.Length) != NativeMethods.HIDP_STATUS_SUCCESS)
                {
                    continue;
                }

                if (cap.UsagePage == 0x0D && cap.Usage == 0x54) // Contact Count
                {
                    contactCount = value;
                    continue;
                }

                if (cap.UsagePage == 0x0D && cap.Usage == 0x56) // Scan Time
                {
                    scanTime = value;
                    continue;
                }

                if (!slots.TryGetValue(cap.LinkCollection, out var partial))
                {
                    partial = new PartialContact();
                    slots[cap.LinkCollection] = partial;
                }

                switch (cap.UsagePage, cap.Usage)
                {
                    case (0x0D, 0x51): // Contact ID
                        partial.ContactId = (int)value;
                        partial.HasId = true;
                        break;
                    case (0x01, 0x30): // X
                        partial.X = (int)value;
                        partial.HasX = true;
                        break;
                    case (0x01, 0x31): // Y
                        partial.Y = (int)value;
                        partial.HasY = true;
                        break;
                }
            }

            var contacts = new List<TouchContact>();
            foreach (var kvp in slots)
            {
                var partial = kvp.Value;
                if (partial.HasX && partial.HasY)
                {
                    int id = partial.HasId ? partial.ContactId : (int)kvp.Key;
                    contacts.Add(new TouchContact(id, partial.X, partial.Y));
                }
            }

            int reported = contactCount > 0 ? (int)contactCount : contacts.Count;
            return new TouchFrame
            {
                ContactCount = reported,
                Contacts = contacts,
                ScanTime = scanTime
            };
        }
        finally
        {
            Marshal.FreeHGlobal(report);
        }
    }

    public void Dispose()
    {
        foreach (DeviceCache device in _devices.Values)
        {
            device.Dispose();
        }

        _devices.Clear();
    }

    private sealed class DeviceCache : IDisposable
    {
        public DeviceCache(IntPtr preparsed, HIDP_VALUE_CAPS[] valueCaps)
        {
            Preparsed = preparsed;
            ValueCaps = valueCaps;
        }

        public IntPtr Preparsed { get; }
        public HIDP_VALUE_CAPS[] ValueCaps { get; }

        public void Dispose()
        {
            if (Preparsed != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Preparsed);
            }
        }
    }

    private sealed class PartialContact
    {
        public int ContactId;
        public int X;
        public int Y;
        public bool HasId;
        public bool HasX;
        public bool HasY;
    }
}
