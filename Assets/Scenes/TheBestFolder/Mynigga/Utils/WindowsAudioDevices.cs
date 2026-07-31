using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// เลือกลำโพง/หูฟังจากในเกม (เฉพาะ Windows)
///
/// ทำไมต้องทำแบบนี้:
///   Unity ไม่มี API ให้เลือกอุปกรณ์เสียงออกเลย — มันใช้ตัวที่ Windows ตั้งเป็น default เสมอ
///   (Microphone.devices มีให้เฉพาะขาเข้า) ทางเดียวที่ทำได้โดยไม่ต้องเปลี่ยนไปใช้ FMOD ทั้งระบบ
///   คือสั่ง Windows ย้าย default ให้เรา แล้วรีเซ็ตเครื่องเสียงของ Unity ให้ไปจับตัวใหม่
///
/// ⚠️ ผลของมันคือ "ทั้งเครื่อง" ไม่ใช่แค่เกมนี้ — Discord/เบราว์เซอร์จะย้ายตามด้วย
///    UI ที่เรียกต้องบอกผู้เล่นให้ชัด
///
/// ⚠️ ทำไมไม่ใช้ [ComImport] + interface ปกติ:
///    Mono ที่ Unity ใช้สร้าง COM object แบบนั้นไม่ได้ — ลองแล้วได้ NullReferenceException
///    ตั้งแต่ new (และ Activator.CreateInstance(Type.GetTypeFromCLSID(...)) ก็โยน TargetInvocationException)
///    ไฟล์นี้จึงเรียก CoCreateInstance เอาพอยน์เตอร์ดิบมา แล้วกระโดดเข้า vtable ตรงๆ
///    ซึ่งเป็น P/Invoke ล้วน Mono รองรับเต็มที่
///
/// IPolicyConfig เป็น COM interface ที่ไมโครซอฟท์ไม่ประกาศเป็นสาธารณะ (โปรแกรมสลับเสียงทั่วไป
/// เช่น SoundSwitch/NirCmd ก็ใช้ตัวนี้) จึงห่อ try/catch ไว้ทุกจุด ถ้าวันหนึ่ง Windows เปลี่ยน
/// ให้มันคืนค่าว่างแล้ว UI ซ่อนส่วนนี้ไปเงียบๆ ดีกว่าเกมพัง
/// </summary>
public static class WindowsAudioDevices
{
    public class Device
    {
        public string Id;
        public string Name;
        public bool IsDefault;
    }

    public static bool IsSupported =>
        Application.platform == RuntimePlatform.WindowsPlayer ||
        Application.platform == RuntimePlatform.WindowsEditor;

    /// <summary>เหตุผลล่าสุดที่อ่านอุปกรณ์ไม่ได้ — เอาไปโชว์ตอนดีบักได้</summary>
    public static string LastError { get; private set; } = string.Empty;

    /// <summary>ลำโพง/หูฟังที่ใช้งานได้ทั้งหมด — คืนลิสต์ว่างถ้าไม่ใช่ Windows หรือเรียกไม่สำเร็จ</summary>
    public static List<Device> ListOutputs()
    {
        List<Device> result = new List<Device>();
        if (!IsSupported) { LastError = "ไม่ใช่ Windows"; return result; }

        IntPtr enumerator = IntPtr.Zero;
        IntPtr collection = IntPtr.Zero;

        try
        {
            EnsureComInitialized();

            Guid clsid = ClsidMMDeviceEnumerator;
            Guid iid = IidIMMDeviceEnumerator;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxAll, ref iid, out enumerator);
            if (hr != 0 || enumerator == IntPtr.Zero)
            {
                LastError = $"CoCreateInstance 0x{hr:X8}";
                return result;
            }

            // หา default ไว้ก่อน เพื่อทำเครื่องหมายในลิสต์
            string defaultId = null;
            IntPtr defaultDevice = IntPtr.Zero;
            if (Call<GetDefaultAudioEndpointFn>(enumerator, VtIMMDeviceEnumeratorGetDefault)
                    (enumerator, DataFlowRender, RoleConsole, out defaultDevice) == 0 && defaultDevice != IntPtr.Zero)
            {
                defaultId = ReadDeviceId(defaultDevice);
                Marshal.Release(defaultDevice);
            }

            hr = Call<EnumAudioEndpointsFn>(enumerator, VtIMMDeviceEnumeratorEnum)
                (enumerator, DataFlowRender, DeviceStateActive, out collection);
            if (hr != 0 || collection == IntPtr.Zero)
            {
                LastError = $"EnumAudioEndpoints 0x{hr:X8}";
                return result;
            }

            hr = Call<GetCountFn>(collection, VtIMMDeviceCollectionGetCount)(collection, out int count);
            if (hr != 0)
            {
                LastError = $"GetCount 0x{hr:X8}";
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                IntPtr device = IntPtr.Zero;
                if (Call<ItemFn>(collection, VtIMMDeviceCollectionItem)(collection, i, out device) != 0 ||
                    device == IntPtr.Zero)
                    continue;

                try
                {
                    string id = ReadDeviceId(device);
                    result.Add(new Device
                    {
                        Id = id,
                        Name = ReadFriendlyName(device, id),
                        IsDefault = id != null && id == defaultId
                    });
                }
                finally
                {
                    Marshal.Release(device);
                }
            }

            LastError = result.Count > 0 ? string.Empty : "ไม่พบอุปกรณ์ที่ใช้งานได้";
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name + ": " + e.Message;
            Debug.LogWarning("[AudioDevice] อ่านรายชื่ออุปกรณ์เสียงไม่สำเร็จ: " + LastError);
        }
        finally
        {
            if (collection != IntPtr.Zero) Marshal.Release(collection);
            if (enumerator != IntPtr.Zero) Marshal.Release(enumerator);
        }

        return result;
    }

    /// <summary>ย้าย default ของ Windows ไปที่อุปกรณ์นี้ แล้วรีสตาร์ทเครื่องเสียงของ Unity</summary>
    public static bool SetDefaultOutput(string deviceId)
    {
        if (!IsSupported || string.IsNullOrEmpty(deviceId)) return false;

        IntPtr config = IntPtr.Zero;
        try
        {
            EnsureComInitialized();

            Guid clsid = ClsidPolicyConfigClient;
            Guid iid = IidIPolicyConfig;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxAll, ref iid, out config);
            if (hr != 0 || config == IntPtr.Zero)
            {
                LastError = $"CoCreateInstance(PolicyConfig) 0x{hr:X8}";
                Debug.LogWarning("[AudioDevice] " + LastError);
                return false;
            }

            SetDefaultEndpointFn setDefault = Call<SetDefaultEndpointFn>(config, VtIPolicyConfigSetDefault);

            // ตั้งให้ครบทั้ง 3 role (Console / Multimedia / Communications)
            // ไม่งั้นเสียงเกมกับเสียงคุยอาจไปคนละอุปกรณ์
            for (int role = 0; role <= 2; role++)
            {
                hr = setDefault(config, deviceId, role);
                if (hr != 0)
                {
                    LastError = $"SetDefaultEndpoint role {role} 0x{hr:X8}";
                    Debug.LogWarning("[AudioDevice] " + LastError);
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name + ": " + e.Message;
            Debug.LogWarning("[AudioDevice] เปลี่ยนอุปกรณ์เสียงไม่สำเร็จ: " + LastError);
            return false;
        }
        finally
        {
            if (config != IntPtr.Zero) Marshal.Release(config);
        }

        // Unity จับอุปกรณ์ตอนเริ่มเกม — ต้องสั่ง Reset ให้มันไปเปิดตัวใหม่ ไม่งั้นเสียงยังออกที่เดิม
        try
        {
            AudioSettings.Reset(AudioSettings.GetConfiguration());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AudioDevice] รีเซ็ตระบบเสียงของ Unity ไม่สำเร็จ: " + e.Message);
        }

        return true;
    }

    /// <summary>ไล่เรียกทีละขั้นแล้วคืนผลเป็นข้อความ — ใช้ตอนหาสาเหตุว่าพังตรงไหน</summary>
    public static string Diagnose()
    {
        System.Text.StringBuilder log = new System.Text.StringBuilder();
        log.AppendLine("IsSupported=" + IsSupported);

        List<Device> devices = ListOutputs();
        log.AppendLine($"count={devices.Count} lastError=[{LastError}]");
        foreach (Device device in devices)
            log.AppendLine($"  {(device.IsDefault ? "[D]" : "   ")} {device.Name}  ({device.Id})");

        return log.ToString();
    }

    #region Native plumbing

    private const int DataFlowRender = 0;   // ลำโพง/หูฟัง (1 = ไมค์)
    private const int DeviceStateActive = 1;
    private const int RoleConsole = 0;
    private const int StgmRead = 0;
    private const int ClsctxAll = 0x17;

    // ตำแหน่งเมธอดใน vtable — 3 ช่องแรกเป็นของ IUnknown (QueryInterface/AddRef/Release) เสมอ
    private const int VtIMMDeviceEnumeratorEnum = 3;         // EnumAudioEndpoints
    private const int VtIMMDeviceEnumeratorGetDefault = 4;   // GetDefaultAudioEndpoint
    private const int VtIMMDeviceCollectionGetCount = 3;
    private const int VtIMMDeviceCollectionItem = 4;
    private const int VtIMMDeviceOpenPropertyStore = 4;
    private const int VtIMMDeviceGetId = 5;
    private const int VtIPropertyStoreGetValue = 5;
    private const int VtIPolicyConfigSetDefault = 13;        // 3 (IUnknown) + 10 เมธอดก่อนหน้า

    private static readonly Guid ClsidMMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIMMDeviceEnumerator = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid ClsidPolicyConfigClient = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    private static readonly Guid IidIPolicyConfig = new Guid("f8679f50-850a-41cf-9c72-430f290290c8");

    private static readonly PropertyKey FriendlyNameKey = new PropertyKey
    {
        formatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        propertyId = 14
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid formatId;
        public int propertyId;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAudioEndpointsFn(IntPtr self, int dataFlow, int stateMask, out IntPtr collection);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDefaultAudioEndpointFn(IntPtr self, int dataFlow, int role, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCountFn(IntPtr self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ItemFn(IntPtr self, int index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIdFn(IntPtr self, out IntPtr id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenPropertyStoreFn(IntPtr self, int access, out IntPtr store);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetValueFn(IntPtr self, ref PropertyKey key, IntPtr propVariant);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDefaultEndpointFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string id, int role);

    /// <summary>อ่านพอยน์เตอร์ฟังก์ชันจาก vtable แล้วห่อเป็น delegate — หัวใจของการเลี่ยง COM interop ของ Mono</summary>
    private static T Call<T>(IntPtr comObject, int vtableIndex) where T : class
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        IntPtr function = Marshal.ReadIntPtr(vtable, vtableIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer(function, typeof(T)) as T;
    }

    private static string ReadDeviceId(IntPtr device)
    {
        if (Call<GetIdFn>(device, VtIMMDeviceGetId)(device, out IntPtr idPtr) != 0 || idPtr == IntPtr.Zero)
            return null;

        try { return Marshal.PtrToStringUni(idPtr); }
        finally { Marshal.FreeCoTaskMem(idPtr); }
    }

    private static string ReadFriendlyName(IntPtr device, string fallback)
    {
        IntPtr store = IntPtr.Zero;
        IntPtr variant = IntPtr.Zero;

        try
        {
            if (Call<OpenPropertyStoreFn>(device, VtIMMDeviceOpenPropertyStore)(device, StgmRead, out store) != 0 ||
                store == IntPtr.Zero)
                return fallback;

            variant = Marshal.AllocCoTaskMem(32); // PROPVARIANT กว้าง 24 ไบต์บน 64-bit เผื่อไว้เป็น 32
            for (int i = 0; i < 32; i++) Marshal.WriteByte(variant, i, 0);

            PropertyKey key = FriendlyNameKey;
            if (Call<GetValueFn>(store, VtIPropertyStoreGetValue)(store, ref key, variant) != 0)
                return fallback;

            // PROPVARIANT: vt อยู่ 2 ไบต์แรก, ค่าพอยน์เตอร์อยู่ที่ offset 8 (64-bit)
            IntPtr stringPtr = Marshal.ReadIntPtr(variant, 8);
            string name = stringPtr != IntPtr.Zero ? Marshal.PtrToStringUni(stringPtr) : null;

            PropVariantClear(variant);
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
        catch
        {
            return fallback; // ชื่ออ่านไม่ได้ก็ใช้ id ไปก่อน ไม่ต้องล้มทั้งลิสต์
        }
        finally
        {
            if (variant != IntPtr.Zero) Marshal.FreeCoTaskMem(variant);
            if (store != IntPtr.Zero) Marshal.Release(store);
        }
    }

    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private const int CoinitApartmentThreaded = 0x2;
    private static bool comInitialized;

    /// <summary>Mono ไม่ได้ init COM ให้เธรดหลักเสมอไป — ไม่ init เองแล้ว CoCreateInstance จะล้ม</summary>
    private static void EnsureComInitialized()
    {
        if (comInitialized) return;

        try
        {
            int hr = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);

            // 0x80010106 = เธรดนี้ init ไว้แล้วคนละโหมด ซึ่งก็ใช้งานได้ ไม่ถือว่าพลาด
            if (hr < 0 && hr != RpcEChangedMode)
                Debug.LogWarning($"[AudioDevice] CoInitializeEx คืน 0x{hr:X8}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AudioDevice] เรียก CoInitializeEx ไม่ได้: " + e.Message);
        }

        comInitialized = true;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context,
        ref Guid iid, out IntPtr instance);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(IntPtr propVariant);

    #endregion
}
